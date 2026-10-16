"""Reference checks, deterministic artifacts, and scientific provenance."""

import io
import json
import platform
import subprocess
import zipfile
from dataclasses import dataclass
from datetime import UTC, datetime
from importlib.metadata import version
from pathlib import Path

import numpy as np
from matplotlib.backends.backend_agg import FigureCanvasAgg
from matplotlib.figure import Figure

from qc5d.basis import SuperspaceBasis
from qc5d.checksums import sha256_bytes, sha256_file
from qc5d.config import ReferenceConfig
from qc5d.manifest import OutputRecord, PlatformRecord, ResultManifest
from qc5d.window import AcceptanceWindow


def canonical_json(payload: dict) -> bytes:
    return (json.dumps(payload, indent=2, sort_keys=True, allow_nan=False) + "\n").encode("utf-8")


def source_inventory() -> list[dict[str, str]]:
    return [
        {"path": "src/qc5d/" + path.name, "sha256": sha256_file(path)}
        for path in sorted(Path(__file__).parent.glob("*.py"))
    ]


def deterministic_npz(arrays: dict[str, np.ndarray]) -> bytes:
    """Fixed ZIP metadata, sorted members, little-endian NPY v1, no object/pickle data."""
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, mode="w", compression=zipfile.ZIP_STORED) as archive:
        for name, array in sorted(arrays.items()):
            data = io.BytesIO()
            value = np.asarray(array)
            value = value.astype(value.dtype.newbyteorder("<"), copy=False)
            np.lib.format.write_array(data, value, version=(1, 0), allow_pickle=False)
            member = zipfile.ZipInfo(name + ".npy", date_time=(1980, 1, 1, 0, 0, 0))
            member.create_system = 3
            member.external_attr = 0o600 << 16
            archive.writestr(member, data.getvalue())
    return buffer.getvalue()


def window_plot(window: AcceptanceWindow) -> bytes:
    figure = Figure(figsize=(6.4, 6.4), dpi=150)
    FigureCanvasAgg(figure)
    axes = figure.add_subplot(111)
    projected = window.projected_cube_vertices
    axes.scatter(
        projected[:, 0], projected[:, 1], s=28, color="tab:blue", label="32 projected cube vertices"
    )
    closed = np.vstack((window.vertices, window.vertices[0]))
    axes.plot(closed[:, 0], closed[:, 1], color="black", linewidth=1.2, label="Computed hull")
    axes.scatter([0.0], [0.0], marker="+", s=70, color="tab:red", label="Origin")
    for index, point in enumerate(window.vertices):
        offset = point * 1.07
        axes.text(offset[0], offset[1], str(index), fontsize=9, ha="center", va="center")
    margin = 1.22 * window.characteristic_radius
    axes.set(
        xlim=(-margin, margin),
        ylim=(-margin, margin),
        xlabel="Internal u0 (dimensionless)",
        ylabel="Internal u1 (dimensionless)",
        title="Acceptance window from the centered 5D unit cube",
    )
    axes.set_aspect("equal", adjustable="box")
    axes.legend(loc="upper right", fontsize=8)
    figure.tight_layout()
    buffer = io.BytesIO()
    figure.savefig(buffer, format="png", metadata={"Software": "qc5d reference"})
    figure.clear()
    return buffer.getvalue()


def run_checks(config: ReferenceConfig) -> tuple[SuperspaceBasis, AcceptanceWindow, dict]:
    basis = SuperspaceBasis()
    window = AcceptanceWindow(basis)
    rng = np.random.Generator(np.random.PCG64(config.seed))
    bound = config.reconstruction_index_bound
    vectors = rng.integers(-bound, bound + 1, size=(config.reconstruction_samples, 5))
    basis_metrics = basis.diagnostics(vectors)
    window_metrics = window.diagnostics()
    tolerance = config.numerical_tolerance
    checks = {
        **{name: value < tolerance for name, value in basis_metrics.items()},
        "all_32_cube_vertices": window_metrics["cube_vertex_count"] == 2**5,
        "derived_decagonal_hull": window_metrics["hull_vertex_count"] == 10,
        "counter_clockwise": window_metrics["area"] > 0.0,
        "strict_convexity": window_metrics["minimum_convex_turn"] > 0.0,
        "central_symmetry": window_metrics["central_symmetry_residual"] < tolerance,
        "tenfold_rotation": window_metrics["tenfold_rotation_residual"] < tolerance,
    }
    return (
        basis,
        window,
        {
            "basis": basis_metrics,
            "window": window_metrics,
            "tolerance": tolerance,
            "checks": checks,
        },
    )


def _git_revision(root: Path) -> str | None:
    if not (root / ".git").exists():
        return None
    try:
        result = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=root,
            capture_output=True,
            text=True,
            timeout=5,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None
    return result.stdout.strip() if result.returncode == 0 else None


@dataclass(frozen=True)
class ReferenceRun:
    passed: bool
    diagnostics: dict
    manifest: ResultManifest


def export_reference(
    kind: str,
    output_dir: Path,
    *,
    config: ReferenceConfig | None = None,
    project_root: Path | None = None,
    command: list[str] | None = None,
    config_hash: str | None = None,
    overwrite: bool = False,
) -> ReferenceRun:
    """Export one command's artifacts and a validated result manifest."""
    if kind not in {"basis-check", "window-check", "make-reference-fixtures"}:
        raise ValueError("unsupported reference command")
    config = ReferenceConfig() if config is None else config
    root = (Path.cwd() if project_root is None else project_root).resolve()
    directory = output_dir.resolve()
    try:
        relative = directory.relative_to(root)
    except ValueError as error:
        raise ValueError("output directory must be inside the project root") from error
    parts = relative.parts
    allowed = (
        parts[:1] == ("results",)
        or parts[:2] == ("datasets", "derived")
        or (parts[:1] == ("unity",) and "StreamingAssets" in parts)
    )
    if not allowed:
        raise ValueError(
            "output directory must be under results/, datasets/derived/, or StreamingAssets"
        )
    names = ["manifest.json"]
    if kind != "window-check":
        names.append("basis_v1.json")
    if kind != "basis-check":
        names.extend(("window_v1.json", "window_v1.npz", "window_diagnostic.png"))
    if not overwrite and any((directory / name).exists() for name in names):
        raise FileExistsError("output artifact already exists; use --overwrite to replace it")

    basis, window, diagnostics = run_checks(config)
    inventory = source_inventory()
    effective_config = config.model_dump(mode="json")
    generation = {
        "config": effective_config,
        "source_files": inventory,
        "dependency_versions": {name: version(name) for name in ("numpy", "scipy", "matplotlib")},
        "config_hash_method": "canonical_effective_config_utf8"
        if config_hash is None
        else "input_bytes",
    }
    artifacts = {}
    if kind != "window-check":
        basis_checks = {name: diagnostics["checks"][name] for name in diagnostics["basis"]}
        artifacts["basis_v1.json"] = canonical_json(
            {
                "schema_version": "1.0",
                "basis": basis.to_dict(),
                "diagnostics": diagnostics["basis"],
                "checks": basis_checks,
                "generation": generation,
            }
        )
    if kind != "basis-check":
        classification = window.classify(
            window.projected_cube_vertices,
            boundary_epsilon=config.boundary_epsilon_relative * window.characteristic_radius,
            boundary_policy=config.boundary_policy,
        )
        artifacts["window_v1.json"] = canonical_json(
            {
                "schema_version": "1.0",
                "window": window.to_dict(),
                "diagnostics": diagnostics["window"],
                "generation": generation,
                "projected_cube_classification": {
                    "states": classification.states.tolist(),
                    "signed_boundary_distance": classification.signed_distance.tolist(),
                    "selected": None
                    if classification.selected is None
                    else classification.selected.tolist(),
                    "boundary_epsilon": classification.boundary_epsilon,
                    "boundary_policy": classification.boundary_policy.value,
                },
            }
        )
        artifacts["window_v1.npz"] = deterministic_npz(
            {
                "schema_version": np.array("1.0"),
                "cube_vertices": window.cube_vertices,
                "projected_cube_vertices": window.projected_cube_vertices,
                "hull_indices": window.hull_indices,
                "vertices_uv": window.vertices,
                "boundary_epsilon": np.array(classification.boundary_epsilon),
                "boundary_policy": np.array(classification.boundary_policy.value),
                "states": classification.states,
                "signed_boundary_distance": classification.signed_distance,
            }
        )
        artifacts["window_diagnostic.png"] = window_plot(window)

    selected_checks = diagnostics["checks"]
    if kind == "basis-check":
        selected_checks = {name: diagnostics["checks"][name] for name in diagnostics["basis"]}
    passed = all(selected_checks.values())
    directory.mkdir(parents=True, exist_ok=True)
    for name, payload in artifacts.items():
        (directory / name).write_bytes(payload)
    manifest = ResultManifest(
        schema_version="1.0",
        command=command or ["qc5d", kind, "--output-dir", relative.as_posix()],
        config_hash=config_hash or sha256_bytes(canonical_json(effective_config)),
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
    return ReferenceRun(passed, diagnostics, manifest)
