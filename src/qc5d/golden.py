"""Deliberately regenerated, byte-locked canonical cross-language fixtures."""

from pathlib import Path

import numpy as np

from qc5d.authoritative import authoritative_payloads, scientific_data
from qc5d.checksums import sha256_bytes, sha256_file
from qc5d.data_schema import FixtureManifest, GoldenFixtureConfig
from qc5d.manifest import OutputRecord
from qc5d.model_config import read_settings
from qc5d.modelset import generate_candidates
from qc5d.reference import canonical_json
from qc5d.runtime import encode_runtime

FIXTURE_RELATIVE = Path("tests/fixtures/canonical_small_v1")


def fixture_payloads(config_file: Path, root: Path) -> dict[str, bytes]:
    settings = GoldenFixtureConfig.model_validate(read_settings(config_file))
    candidates = generate_candidates(settings.model.parameters)
    data = scientific_data(
        candidates,
        phasons=settings.phasons,
        q=np.asarray(settings.q_vectors),
        root=root,
        input_checksums={"fixture_config": sha256_file(config_file)},
    )
    archive, metadata = authoritative_payloads(data)
    binary, loss = encode_runtime(data, sha256_bytes(archive), settings.runtime)
    artifacts = {
        "authoritative.npz": archive,
        "metadata.json": metadata,
        "canonical.qc5d": binary,
        "loss_report.json": canonical_json(loss),
        "config_input.json": config_file.read_bytes(),
        "basis.json": canonical_json(data.metadata.basis.model_dump(mode="json")),
        "window.json": canonical_json(data.metadata.window.model_dump(mode="json")),
        "expected_phasons.json": canonical_json(
            {
                "schema_version": "1.0.0",
                "frames": [frame.model_dump(mode="json") for frame in data.metadata.phason_frames],
            }
        ),
        "graph.json": canonical_json(
            {
                "schema_version": "1.0.0",
                "endpoints_candidate_indices": data.arrays["graph/endpoints_uint32"].tolist(),
                "generator": data.arrays["graph/generator"].tolist(),
                "sign": data.arrays["graph/sign"].tolist(),
            }
        ),
        "diffraction.json": canonical_json(
            {
                "schema_version": "1.0.0",
                "parameters": data.metadata.diffraction.model_dump(mode="json"),
                "q_xyz": data.arrays["diffraction/q_xyz"].tolist(),
                "F_real": data.arrays["diffraction/F_complex128"].real.tolist(),
                "F_imag": data.arrays["diffraction/F_complex128"].imag.tolist(),
                "intensity_raw": data.arrays["diffraction/intensity_raw"].tolist(),
                "intensity_normalized": data.arrays["diffraction/intensity_normalized"].tolist(),
            }
        ),
    }
    manifest = FixtureManifest(
        files=tuple(
            OutputRecord(path=name, sha256=sha256_bytes(payload), size_bytes=len(payload))
            for name, payload in sorted(artifacts.items())
        )
    )
    artifacts["sha256_manifest.json"] = canonical_json(manifest.model_dump(mode="json"))
    return artifacts


def verify_fixture(directory: Path) -> dict:
    manifest_file = directory / "sha256_manifest.json"
    manifest = FixtureManifest.model_validate_json(manifest_file.read_bytes())
    names = [file.path for file in manifest.files]
    if names != sorted(set(names)) or not names:
        raise ValueError("golden fixture manifest paths must be unique and sorted")
    for record in manifest.files:
        file = (directory / record.path).resolve()
        if (
            not file.is_relative_to(directory.resolve())
            or file.stat().st_size != record.size_bytes
            or sha256_file(file) != record.sha256
        ):
            raise ValueError(f"golden fixture payload checksum mismatch: {record.path}")
    return {
        "passed": True,
        "file_count": len(manifest.files),
        "manifest_sha256": sha256_file(manifest_file),
    }
