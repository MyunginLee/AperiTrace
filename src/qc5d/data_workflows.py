"""Protected format conversion/export and read-only validation CLI workflows."""

import platform
from datetime import UTC, datetime
from pathlib import Path

import numpy as np

from qc5d.authoritative import authoritative_payloads, load_authoritative, scientific_data
from qc5d.checksums import sha256_bytes, sha256_file
from qc5d.data_schema import ExportConfig, ScientificResultManifest
from qc5d.manifest import OutputRecord, PlatformRecord, ResultManifest
from qc5d.model_config import read_settings
from qc5d.reference import _git_revision, canonical_json, source_inventory
from qc5d.runtime import encode_runtime, parse_runtime, preview_state
from qc5d.safe_npz import read_npz
from qc5d.science import load_candidates


def protect(paths, root: Path, overwrite: bool):
    for path in paths:
        relative = path.resolve().relative_to(root.resolve())
        if not (
            relative.parts[:1] == ("results",) or relative.parts[:2] == ("datasets", "derived")
        ):
            raise ValueError("format outputs must be under results/ or datasets/derived/")
        if path.exists() and not overwrite:
            raise FileExistsError("format output already exists; use --overwrite")


def manifest_bytes(artifacts: dict[Path, bytes], root, command, config_bytes):
    inventory = source_inventory()
    provenance = ResultManifest(
        schema_version="1.0",
        command=command,
        config_hash=sha256_bytes(config_bytes),
        source_hash=sha256_bytes(canonical_json({"files": inventory})),
        code_revision=_git_revision(root),
        timestamp=datetime.now(UTC),
        platform=PlatformRecord(
            system=platform.system(),
            release=platform.release(),
            machine=platform.machine(),
            python_version=platform.python_version(),
        ),
        outputs=[
            OutputRecord(
                path=path.resolve().relative_to(root).as_posix(),
                sha256=sha256_bytes(payload),
                size_bytes=len(payload),
            )
            for path, payload in sorted(artifacts.items())
        ],
    )
    return canonical_json(ScientificResultManifest(provenance=provenance).model_dump(mode="json"))


def make_authoritative(
    dataset, output, *, diffraction=None, root=None, overwrite=False, command=None
):
    root = Path.cwd().resolve() if root is None else root.resolve()
    source_directory = dataset.resolve() if dataset.is_dir() else dataset.resolve().parent
    if output.resolve() == source_directory:
        raise ValueError(
            "conversion must use a distinct output directory; preserve the legacy input"
        )
    names = [output / name for name in ("authoritative.npz", "metadata.json", "manifest.json")]
    protect(names, root, overwrite)
    candidates, provenance = load_candidates(dataset, project_root=root)
    q, checksums = None, {"stage2_modelset": provenance["dataset_sha256"]}
    if diffraction is not None:
        # Validate the existing result manifest and the exact selected-ID/tuple relation.
        manifest = ResultManifest.model_validate_json(
            (diffraction.parent / "manifest.json").read_bytes()
        )
        present = False
        for record in manifest.outputs:
            file = (root / record.path).resolve()
            if (
                not file.is_relative_to(root)
                or file.stat().st_size != record.size_bytes
                or sha256_file(file) != record.sha256
            ):
                raise ValueError("diffraction input manifest checksum/size mismatch")
            present |= file == diffraction.resolve()
        if not present:
            raise ValueError("diffraction input is not recorded by its manifest")
        arrays = read_npz(diffraction)
        state_phi = provenance["dataset_phi"]
        from qc5d.modelset import evaluate_membership

        state = evaluate_membership(candidates, state_phi)
        if (
            not np.array_equal(arrays["selected_ids"], state.selected_ids)
            or not np.array_equal(
                arrays["selected_lattice_n_int32"], candidates.lattice_n[state.selected]
            )
            or not np.array_equal(arrays["selected_physical_xyz_float64"], state.selected_physical)
        ):
            raise ValueError(
                "diffraction input does not use the same selected tuple IDs/coordinates"
            )
        q = arrays["q_xyz"]
        checksums["stage3_diffraction"] = sha256_file(diffraction)
    data = scientific_data(
        candidates, phi=provenance["dataset_phi"], q=q, input_checksums=checksums, root=root
    )
    if diffraction is not None:
        for name in ("F_complex128", "intensity_raw", "intensity_normalized"):
            if not np.array_equal(data.arrays["diffraction/" + name], arrays[name]):
                raise ValueError(
                    f"diffraction input {name} disagrees with independent recomputation"
                )
    archive, metadata = authoritative_payloads(data)
    artifacts = {output / "authoritative.npz": archive, output / "metadata.json": metadata}
    manifest = manifest_bytes(
        artifacts,
        root,
        command or ["qc5d", "make-authoritative"],
        canonical_json(data.metadata.model.parameters.model_dump(mode="json")),
    )
    output.mkdir(parents=True, exist_ok=True)
    for path, payload in artifacts.items():
        path.write_bytes(payload)
    (output / "manifest.json").write_bytes(manifest)
    _, result = load_authoritative(output)
    return result


def export_unity(dataset, output, *, config_file=None, root=None, overwrite=False, command=None):
    root = Path.cwd().resolve() if root is None else root.resolve()
    if output.suffix != ".qc5d":
        raise ValueError("runtime output must have .qc5d extension")
    loss_file = output.with_suffix(".loss.json")
    config_output = output.with_suffix(".config.json")
    manifest_file = output.with_suffix(".manifest.json")
    protect([output, loss_file, config_output, manifest_file], root, overwrite)
    authority = dataset / "authoritative.npz" if dataset.is_dir() else dataset
    if output.resolve() == authority.resolve():
        raise ValueError("runtime output must not overwrite the authoritative input")
    data, validation = load_authoritative(dataset)
    config = (
        ExportConfig()
        if config_file is None
        else ExportConfig.model_validate(read_settings(config_file))
    )
    config_bytes = (
        canonical_json(config.model_dump(mode="json"))
        if config_file is None
        else config_file.read_bytes()
    )
    binary, report = encode_runtime(data, validation["authoritative_sha256"], config)
    artifacts = {output: binary, loss_file: canonical_json(report), config_output: config_bytes}
    manifest = manifest_bytes(artifacts, root, command or ["qc5d", "export-unity"], config_bytes)
    output.parent.mkdir(parents=True, exist_ok=True)
    for path, payload in artifacts.items():
        path.write_bytes(payload)
    manifest_file.write_bytes(manifest)
    return report


def inspect_runtime(file):
    parsed = parse_runtime(file)
    state = preview_state(parsed)
    disagreement = parsed.selected != state.selected
    return {
        "passed": True,
        **parsed.header,
        "selected_count": int(np.count_nonzero(parsed.selected)),
        "boundary_count": int(np.count_nonzero(parsed.boundary)),
        "selected_ids": parsed.ids[parsed.selected].tolist(),
        "preview_selected_disagreement_ids": parsed.ids[disagreement].tolist(),
        "preview_semantics": "quantized operands/Python float64 classifier, not GPU parity",
    }


def validate_dataset(file):
    if file.suffix == ".qc5d":
        return inspect_runtime(file)
    _, result = load_authoritative(file)
    return result
