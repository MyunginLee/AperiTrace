"""Canonical reference datasets, figures, and reproducibility manifests."""

import io
import platform
from datetime import UTC, datetime
from importlib.metadata import version
from pathlib import Path

import numpy as np
from matplotlib.backends.backend_agg import FigureCanvasAgg
from matplotlib.figure import Figure

from qc5d.basis import SuperspaceBasis
from qc5d.checksums import sha256_bytes, sha256_file
from qc5d.graph import ParentGraph, build_graph, check_graph
from qc5d.identity import identity_diagnostics, lookup_hashes, stable_ids
from qc5d.manifest import OutputRecord, PlatformRecord, ResultManifest
from qc5d.model_config import ModelConfig
from qc5d.modelset import (
    CandidateSet,
    crop_mask,
    evaluate_membership,
    generate_candidates,
    project_tuples,
)
from qc5d.phason import PhasonPath, phason_sweep
from qc5d.reference import _git_revision, canonical_json, deterministic_npz, source_inventory
from qc5d.window import AcceptanceWindow


def _png(figure: Figure) -> bytes:
    FigureCanvasAgg(figure)
    figure.tight_layout()
    buffer = io.BytesIO()
    figure.savefig(buffer, format="png", metadata={"Software": "qc5d model reference"})
    figure.clear()
    return buffer.getvalue()


def model_figures(candidates, membership, graph_diagnostics) -> dict[str, bytes]:
    selected = membership.selected
    r = candidates.physical[selected]
    shifted = candidates.internal + membership.phi
    layer = np.sum(candidates.lattice_n[selected].astype(np.int64), axis=1)
    figures = {}
    figure = Figure(figsize=(8, 6), dpi=120)
    ax = figure.add_subplot(111)
    for k in np.unique(layer):
        mask = layer == k
        ax.scatter(r[mask, 0], r[mask, 1], s=9, alpha=0.7, label=f"sum(n)={k}")
    ax.set(
        xlabel="Physical x (dimensionless)",
        ylabel="Physical y (dimensionless)",
        title=f"Selected xy by periodic layer; {len(r)} points",
    )
    ax.set_aspect("equal")
    if len(np.unique(layer)):
        ax.legend(fontsize=7, ncol=3, loc="upper right")
    figures["physical_xy_layers.png"] = _png(figure)
    figure = Figure(figsize=(8, 6), dpi=120)
    ax = figure.add_subplot(111)
    ax.scatter(
        shifted[~selected, 0], shifted[~selected, 1], s=4, alpha=0.4, color="grey", label="Rejected"
    )
    ax.scatter(shifted[selected, 0], shifted[selected, 1], s=7, color="tab:blue", label="Selected")
    boundary = membership.boundary
    ax.scatter(
        shifted[boundary, 0],
        shifted[boundary, 1],
        s=22,
        facecolors="none",
        edgecolors="tab:red",
        label="Boundary",
    )
    vertices = AcceptanceWindow().vertices
    closed = np.vstack((vertices, vertices[0]))
    ax.plot(closed[:, 0], closed[:, 1], color="black", linewidth=1, label="Fixed window")
    ax.set(
        xlabel="Shifted internal u0 (dimensionless)",
        ylabel="Shifted internal u1 (dimensionless)",
        title=f"Candidate membership at phi={tuple(float(v) for v in membership.phi)}",
    )
    ax.set_aspect("equal")
    ax.legend(fontsize=8)
    figures["internal_candidates.png"] = _png(figure)
    figure = Figure(figsize=(12, 5), dpi=120)
    physical_ax, internal_ax = figure.subplots(1, 2)
    # The same explicit subset of IDs in both views; color encodes lexicographic rank.
    indices = np.flatnonzero(selected)
    sample = indices[np.linspace(0, max(len(indices) - 1, 0), min(len(indices), 200), dtype=int)]
    for ax, points, labels in (
        (physical_ax, candidates.physical[sample, :2], ("Physical x", "Physical y")),
        (internal_ax, shifted[sample], ("Shifted internal u0", "Shifted internal u1")),
    ):
        scatter = ax.scatter(
            points[:, 0], points[:, 1], c=np.arange(len(sample)), cmap="viridis", s=14
        )
        ax.set(xlabel=labels[0] + " (dimensionless)", ylabel=labels[1] + " (dimensionless)")
        ax.set_aspect("equal")
        figure.colorbar(scatter, ax=ax, label="Same sampled-ID rank")
    internal_ax.plot(closed[:, 0], closed[:, 1], color="black", linewidth=1)
    figure.suptitle(f"Linked correspondence: same {len(sample)} deterministic selected IDs")
    figures["selected_correspondence.png"] = _png(figure)
    figure = Figure(figsize=(7, 5), dpi=120)
    ax = figure.add_subplot(111)
    histogram = graph_diagnostics["degree_histogram"]
    ax.bar([int(k) for k in histogram], list(histogram.values()), color="tab:blue")
    ax.set(
        xlabel="Parent-lattice degree",
        ylabel="Selected vertex count",
        title=f"Canonical graph: {graph_diagnostics['edge_count']} undirected edges",
        xticks=range(11),
    )
    figures["graph_degree_histogram.png"] = _png(figure)
    return figures


def sweep_figures(candidates: CandidateSet, path: PhasonPath, sweep) -> dict[str, bytes]:
    figure = Figure(figsize=(9, 5), dpi=120)
    ax = figure.add_subplot(111)
    frames = [r["frame"] for r in sweep.records]
    for field, label in (
        ("entered_ids", "Entered"),
        ("exited_ids", "Exited"),
        ("boundary_ids", "Boundary"),
    ):
        ax.plot(frames, [len(r[field]) for r in sweep.records], label=label)
    ax.set(
        xlabel="Sampled frame",
        ylabel="ID count",
        title="Phason membership events (frame 0 is baseline)",
    )
    ax.legend()
    result = {"membership_events.png": _png(figure)}
    figure = Figure(figsize=(12, 7), dpi=120)
    for ax, frame in zip(
        figure.subplots(2, 3).flat, np.linspace(0, len(frames) - 1, 6, dtype=int), strict=True
    ):
        r = candidates.physical[sweep.selected_masks[frame]]
        ax.scatter(r[:, 0], r[:, 1], s=5, color="tab:blue")
        radius = candidates.config.physical_crop.radial_max
        ax.set(
            xlim=(-radius, radius),
            ylim=(-radius, radius),
            xlabel="x",
            ylabel="y",
            title=f"Frame {frame}; phi={tuple(float(v) for v in np.round(path.values[frame], 3))}",
        )
        ax.set_aspect("equal")
    figure.suptitle("Static phason frames; scientific xy coordinates (dimensionless)")
    result["phason_frame_grid.png"] = _png(figure)
    return result


def dataset_arrays(candidates, membership, graph) -> dict[str, np.ndarray]:
    basis, window = SuperspaceBasis(), AcceptanceWindow()
    return {
        "schema_version": np.array("1.0"),
        "dataset_kind": np.array("stage2_reference_modelset"),
        "config_json": np.array(canonical_json(candidates.config.model_dump(mode="json")).decode()),
        "basis/P_parallel": basis.P_parallel,
        "basis/P_perp": basis.P_perp,
        "window/vertices_uv": window.vertices,
        "points/lattice_n_int32": candidates.lattice_n,
        "points/stable_id": candidates.ids,
        "points/lookup_hash_uint64": candidates.hashes,
        "points/physical_xyz_float64": candidates.physical,
        "points/internal_uv_float64": candidates.internal,
        "points/shifted_internal_uv_float64": candidates.internal + membership.phi,
        "points/boundary_distance": membership.signed_distance,
        "points/selected_bool": membership.selected,
        "points/boundary_bool": membership.boundary,
        "phi": membership.phi,
        **graph.arrays(),
    }


def export_model(
    config: ModelConfig,
    output_dir: Path,
    *,
    path: PhasonPath | None = None,
    config_bytes: bytes | None = None,
    path_bytes: bytes | None = None,
    command: list[str] | None = None,
    project_root: Path | None = None,
    overwrite: bool = False,
    progress=None,
) -> dict:
    root = (Path.cwd() if project_root is None else project_root).resolve()
    directory = output_dir.resolve()
    relative = directory.relative_to(root)
    if not (relative.parts[:1] == ("results",) or relative.parts[:2] == ("datasets", "derived")):
        raise ValueError("Model output must be under results/ or datasets/derived/")
    names = {
        "config.json",
        "modelset.npz",
        "dataset.json",
        "diagnostics.json",
        "manifest.json",
        "physical_xy_layers.png",
        "internal_candidates.png",
        "selected_correspondence.png",
        "graph_degree_histogram.png",
    }
    if path is not None:
        names |= {
            "path.json",
            "events.json",
            "sweep.npz",
            "membership_events.png",
            "phason_frame_grid.png",
        }
    if config_bytes is not None:
        names.add("config_input.yaml")
    if path_bytes is not None:
        names.add("path_input" + (".csv" if path.metadata["kind"] == "csv" else ".yaml"))
    if not overwrite and any((directory / name).exists() for name in names):
        raise FileExistsError("output artifact already exists; use --overwrite to replace it")
    if path is not None and (
        np.any(path.values < config.phason_domain.min)
        or np.any(path.values > config.phason_domain.max)
        or not np.all(np.isfinite(path.values))
    ):
        raise ValueError("phason path is outside the precomputed domain or nonfinite")
    candidates = generate_candidates(config, progress=progress)
    membership = evaluate_membership(candidates, config.initial_phi)
    graph = build_graph(candidates.lattice_n[membership.selected])
    graph_diagnostics = check_graph(candidates.lattice_n[membership.selected], graph)
    diagnostics = {
        "candidates": candidates.diagnostics,
        "selected_count": len(membership.selected_ids),
        "boundary_count": int(np.count_nonzero(membership.boundary)),
        "graph": graph_diagnostics,
    }
    artifacts = {
        "config.json": canonical_json(config.model_dump(mode="json")),
        "modelset.npz": deterministic_npz(dataset_arrays(candidates, membership, graph)),
        **model_figures(candidates, membership, graph_diagnostics),
    }
    if config_bytes is not None:
        artifacts["config_input.yaml"] = config_bytes
    if path is not None:
        sweep = phason_sweep(candidates, path)
        diagnostics["phason"] = sweep.diagnostics
        artifacts.update(
            {
                "path.json": canonical_json(
                    {"metadata": path.metadata, "phi_values": path.values.tolist()}
                ),
                "events.json": canonical_json({"schema_version": "1.0", "frames": sweep.records}),
                "sweep.npz": deterministic_npz(
                    {
                        "schema_version": np.array("1.0"),
                        "candidate_ids": candidates.ids,
                        "phi": path.values,
                        "selected_masks": sweep.selected_masks,
                        "boundary_masks": sweep.boundary_masks,
                        "signed_distances": sweep.signed_distances,
                    }
                ),
                **sweep_figures(candidates, path, sweep),
            }
        )
        if path_bytes is not None:
            artifacts["path_input" + (".csv" if path.metadata["kind"] == "csv" else ".yaml")] = (
                path_bytes
            )
    diagnostics["passed"] = graph_diagnostics["passed"] and (
        path is None or diagnostics["phason"]["passed"]
    )
    inventory = source_inventory()
    artifacts["dataset.json"] = canonical_json(
        {
            "schema_version": "1.0",
            "dataset_kind": "stage2_reference_modelset",
            "coordinate_units": "dimensionless",
            "scientific_coordinates_unscaled": True,
            "phason_convention": "u=P_perp*n+phi",
            "candidate_order": "tuple_lexicographic",
            "stable_id_encoding": "five_signed_decimal_int32_values_comma_separated",
            "lookup_hash": "BLAKE2b_8_bytes_on_20_little_endian_int32_bytes_digest_as_LE_uint64",
            "npz_byte_order": "little_endian",
            "edge_orientation": "positive_generator_once",
            "config": config.model_dump(mode="json"),
            "config_hash_method": "canonical_effective_config_utf8"
            if config_bytes is None
            else "input_bytes",
            "source_files": inventory,
            "dependency_versions": {
                name: version(name)
                for name in ("numpy", "scipy", "pydantic", "matplotlib", "PyYAML")
            },
            "path_input_sha256": None if path_bytes is None else sha256_bytes(path_bytes),
            "path_effective_sha256": None if path is None else sha256_bytes(artifacts["path.json"]),
        }
    )
    artifacts["diagnostics.json"] = canonical_json(diagnostics)
    directory.mkdir(parents=True, exist_ok=True)
    for name, payload in artifacts.items():
        (directory / name).write_bytes(payload)
    manifest = ResultManifest(
        schema_version="1.0",
        command=command
        or ["qc5d", "generate" if path is None else "phason-sweep", "--out", relative.as_posix()],
        config_hash=sha256_bytes(
            artifacts["config.json"] if config_bytes is None else config_bytes
        ),
        source_hash=sha256_bytes(canonical_json({"files": inventory})),
        code_revision=_git_revision(root),
        platform=PlatformRecord(
            system=platform.system(),
            release=platform.release(),
            machine=platform.machine(),
            python_version=platform.python_version(),
        ),
        timestamp=datetime.now(UTC),
        outputs=[
            OutputRecord(
                path=(relative / name).as_posix(),
                sha256=sha256_bytes(payload),
                size_bytes=len(payload),
            )
            for name, payload in sorted(artifacts.items())
        ],
    )
    (directory / "manifest.json").write_bytes(canonical_json(manifest.model_dump(mode="json")))
    return diagnostics


def check_dataset(dataset: Path, *, project_root: Path | None = None) -> dict:
    """Read-only graph/coordinate/membership and artifact-integrity validation."""
    root = (Path.cwd() if project_root is None else project_root).resolve()
    dataset = dataset.resolve()
    if dataset.is_dir():
        dataset /= "modelset.npz"
    if dataset.name != "modelset.npz":
        raise ValueError("expected a reference modelset.npz or its directory")
    manifest = ResultManifest.model_validate_json((dataset.parent / "manifest.json").read_bytes())
    recorded = set()
    for record in manifest.outputs:
        artifact = (root / record.path).resolve()
        if not artifact.is_relative_to(root):
            raise ValueError("manifest output resolves outside project root")
        if artifact.stat().st_size != record.size_bytes or sha256_file(artifact) != record.sha256:
            raise ValueError("artifact checksum/size mismatch")
        recorded.add(artifact)
    if dataset not in recorded:
        raise ValueError("dataset is not included in its manifest")
    with np.load(dataset, allow_pickle=False) as archive:
        arrays = {name: archive[name] for name in archive.files}
    if (
        arrays["schema_version"].item() != "1.0"
        or arrays["dataset_kind"].item() != "stage2_reference_modelset"
    ):
        raise ValueError("unsupported dataset schema/kind")
    config = ModelConfig.model_validate_json(arrays["config_json"].item())
    external_config = ModelConfig.model_validate_json((dataset.parent / "config.json").read_bytes())
    if config != external_config:
        raise ValueError("embedded and external model config disagree")
    basis, window = SuperspaceBasis(), AcceptanceWindow()
    for key, wanted in (
        ("basis/P_parallel", basis.P_parallel),
        ("basis/P_perp", basis.P_perp),
        ("window/vertices_uv", window.vertices),
    ):
        if not np.array_equal(arrays[key], wanted):
            raise ValueError("dataset geometry disagrees with canonical basis/window")
    lattice = arrays["points/lattice_n_int32"]
    if lattice.dtype != np.dtype("int32") or lattice.ndim != 2 or lattice.shape[1] != 5:
        raise ValueError("dataset lattice must be signed int32 [M,5]")
    if np.any(np.abs(lattice.astype(np.int64)) > config.index_bound):
        raise ValueError("dataset contains tuples outside index bound")
    order = np.lexsort(tuple(lattice[:, j] for j in range(4, -1, -1)))
    if not np.array_equal(order, np.arange(len(lattice))):
        raise ValueError("dataset tuples must be lexicographically ordered")
    ids, hashes = stable_ids(lattice), lookup_hashes(lattice)
    identity = identity_diagnostics(lattice, hashes)
    if identity["duplicate_tuple_count"] or identity["hash_collision_count"]:
        raise ValueError("dataset identity collision")
    physical, internal = project_tuples(lattice, basis)
    if not np.all(crop_mask(physical, config)):
        raise ValueError("dataset contains tuples outside scientific crop")
    for key, wanted in (
        ("points/stable_id", ids),
        ("points/lookup_hash_uint64", hashes),
        ("points/physical_xyz_float64", physical),
        ("points/internal_uv_float64", internal),
    ):
        if arrays[key].dtype != wanted.dtype or not np.array_equal(arrays[key], wanted):
            raise ValueError("dataset identities or scientific coordinates disagree")
    candidates = CandidateSet(config, lattice, physical, internal, ids, hashes, {})
    state = evaluate_membership(candidates, arrays["phi"])
    for key, wanted in (
        ("points/selected_bool", state.selected),
        ("points/boundary_bool", state.boundary),
        ("points/boundary_distance", state.signed_distance),
        ("points/shifted_internal_uv_float64", internal + state.phi),
    ):
        if arrays[key].dtype != wanted.dtype or not np.array_equal(arrays[key], wanted):
            raise ValueError("dataset membership disagrees with positive phason convention")
    graph = ParentGraph(
        *(
            arrays[key]
            for key in (
                "edges/source_id",
                "edges/target_id",
                "edges/generator",
                "edges/sign",
                "edges/physical_length",
                "edges/internal_displacement",
            )
        )
    )
    return {
        **check_graph(lattice[state.selected], graph),
        "candidate_count": len(lattice),
        "boundary_count": int(np.count_nonzero(state.boundary)),
        "manifest_payload_checksums_verified": len(recorded),
        "identity": identity,
    }
