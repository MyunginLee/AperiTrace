"""Diffraction result exports, figures, provenance, and command workflows."""

import io
import platform
from datetime import UTC, datetime
from importlib.metadata import version
from pathlib import Path

import numpy as np
from matplotlib.backends.backend_agg import FigureCanvasAgg
from matplotlib.figure import Figure

from qc5d.checksums import sha256_bytes
from qc5d.diffraction_config import ConvergenceConfig, DiffractionConfig
from qc5d.manifest import OutputRecord, PlatformRecord, ResultManifest
from qc5d.model_config import read_settings
from qc5d.modelset import evaluate_membership
from qc5d.reference import _git_revision, canonical_json, deterministic_npz, source_inventory
from qc5d.science import (
    analytic_fixtures,
    analyze_convergence,
    analyze_phasons,
    analyze_points,
    load_candidates,
)


def png(figure: Figure) -> bytes:
    FigureCanvasAgg(figure)
    figure.tight_layout()
    stream = io.BytesIO()
    figure.savefig(stream, format="png", metadata={"Software": "qc5d diffraction"})
    figure.clear()
    return stream.getvalue()


def reserve_output(directory: Path, root: Path, names: set[str], overwrite: bool):
    relative = directory.resolve().relative_to(root.resolve())
    if not (relative.parts[:1] == ("results",) or relative.parts[:2] == ("datasets", "derived")):
        raise ValueError("scientific outputs must be under results/ or datasets/derived/")
    if not overwrite and any((directory / name).exists() for name in names | {"manifest.json"}):
        raise FileExistsError("output artifact already exists; use --overwrite to replace it")


def write_artifacts(directory, root, artifacts, config_bytes, command, metadata):
    inventory = source_inventory()
    metadata.update(
        {
            "schema_version": "1.0",
            "source_files": inventory,
            "dependency_versions": {
                name: version(name)
                for name in ("numpy", "scipy", "pydantic", "matplotlib", "PyYAML")
            },
            "numerical_authority": "Python float64/complex128",
            "coordinate_units": "dimensionless model length",
            "q_units": "radians per dimensionless model length",
            "amplitude_units": "unit scattering weight",
            "intensity_units": "squared unit scattering weight",
            "normalization": "I(q)/I(0)",
            "weighting": "real unit weights",
            "windowing": "none; hard finite crop",
            "structure_factor_sign": "exp(+i*q.r)",
            "npz_byte_order": "little_endian",
            "accumulation": (
                "fixed xyz term order and carried sequential point-order complex128 cumsum"
            ),
            "determinism_scope": (
                "numerical/config/figure payloads in recorded environment; "
                "runtime and manifest timestamps vary"
            ),
        }
    )
    metadata.setdefault("config_hash_method", "exact archived input bytes")
    artifacts["metadata.json"] = canonical_json(metadata)
    relative = directory.resolve().relative_to(root.resolve())
    directory.mkdir(parents=True, exist_ok=True)
    for name, payload in artifacts.items():
        (directory / name).write_bytes(payload)
    manifest = ResultManifest(
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
                path=(relative / name).as_posix(),
                sha256=sha256_bytes(payload),
                size_bytes=len(payload),
            )
            for name, payload in sorted(artifacts.items())
        ],
    )
    (directory / "manifest.json").write_bytes(canonical_json(manifest.model_dump(mode="json")))


def map_panel(ax, image, config):
    step = 2 * config.q_extent / (config.grid_samples - 1)
    extent = config.q_extent + step / 2
    artist = ax.imshow(
        np.log10(np.maximum(image, 1e-8)),
        origin="lower",
        extent=(-extent, extent, -extent, extent),
        cmap="magma",
        vmin=-8,
        vmax=0,
    )
    ax.set(
        xlabel="q_x (rad/model length)",
        ylabel="q_y (rad/model length)",
        xlim=(-config.q_extent, config.q_extent),
        ylim=(-config.q_extent, config.q_extent),
    )
    return artist


def diffraction_figures(arrays, summary, model, phi, config):
    caption = (
        f"crop={model.physical_crop.kind}, R={model.physical_crop.radial_max:g}, "
        f"|z|≤{model.physical_crop.z_abs_max:g}; N={summary['selected_count']}; phi={tuple(phi)}"
    )
    result = {}
    if arrays["cartesian_count"].item():
        figure = Figure(figsize=(8, 7), dpi=120)
        ax = figure.add_subplot(111)
        image = arrays["intensity_normalized"][: int(arrays["cartesian_count"])].reshape(
            config.grid_samples, config.grid_samples
        )
        artist = map_panel(ax, image, config)
        figure.colorbar(artist, ax=ax, label="log10(max(I/I(0), 1e-8))")
        ax.set_title(
            f"Finite-patch q_z={config.q_z:g}; extent ±{config.q_extent:g}\n{caption}", fontsize=10
        )
        result["diffraction_map.png"] = png(figure)
    reciprocal_count = len(arrays["q_xyz"]) - int(arrays["cartesian_count"])
    if reciprocal_count:
        figure = Figure(figsize=(8, 7), dpi=120)
        ax = figure.add_subplot(111)
        q = arrays["q_xyz"][-reciprocal_count:]
        values = arrays["intensity_normalized"][-reciprocal_count:]
        artist = ax.scatter(
            q[:, 0],
            q[:, 1],
            c=np.log10(np.maximum(values, 1e-8)),
            s=25,
            cmap="magma",
            vmin=-8,
            vmax=0,
        )
        figure.colorbar(artist, ax=ax, label="log10(max(I/I(0), 1e-8))")
        section = (
            "cross-section" if config.reciprocal_cross_section_only else "volume projected to xy"
        )
        ax.set(
            xlabel="q_x (rad/model length)",
            ylabel="q_y (rad/model length)",
            title=(f"Five-index {section}; |q components|≤{np.max(np.abs(q)):.3g}\n{caption}"),
        )
        ax.set_aspect("equal")
        result["reciprocal_index_map.png"] = png(figure)
    figure = Figure(figsize=(10, 5), dpi=120)
    first, second = figure.subplots(1, 2)
    polar = arrays["polar/normalized_intensity"]
    a = first.imshow(polar, origin="lower", aspect="auto", extent=(0, 360, 0, config.q_extent))
    b = second.imshow(
        polar - np.roll(polar, config.polar_angular_samples // 10, axis=1),
        origin="lower",
        aspect="auto",
        extent=(0, 360, 0, config.q_extent),
        cmap="coolwarm",
    )
    for ax in (first, second):
        ax.set(xlabel="Angle (degrees)", ylabel="|q_xy| (rad/model length)")
    first.set_title("Polar intensity I/I(0)")
    second.set_title("I/I(0) minus 36° rotation")
    figure.colorbar(a, ax=first)
    figure.colorbar(b, ax=second)
    figure.suptitle(f"Tenfold metric, q_z={config.q_z:g}; {caption}", fontsize=10)
    result["tenfold_residual.png"] = png(figure)
    return result


def run_diffraction(
    dataset, config_file, output, *, phason_output=None, root=None, overwrite=False, command=None
):
    root = Path.cwd().resolve() if root is None else root.resolve()
    config = DiffractionConfig.model_validate(read_settings(config_file))
    config_bytes = config_file.read_bytes()
    names = {
        "diffraction.npz",
        "summary.json",
        "config.json",
        "config_input.yaml",
        "metadata.json",
        "runtime.json",
        "tenfold_residual.png",
    }
    if config.mode != "reciprocal":
        names.add("diffraction_map.png")
    if config.mode != "cartesian":
        names.add("reciprocal_index_map.png")
    reserve_output(output, root, names, overwrite)
    if config.phason_values is not None:
        if phason_output is None:
            raise ValueError("configured phason path requires an explicit phason output directory")
        if phason_output.resolve() == output.resolve():
            raise ValueError("diffraction and phason outputs must be distinct")
        reserve_output(
            phason_output,
            root,
            {
                "phason_diffraction.npz",
                "summary.json",
                "runtime.json",
                "metadata.json",
                "config.json",
                "config_input.yaml",
                "phason_maps.png",
                "phason_changes.png",
            },
            overwrite,
        )
    candidates, provenance = load_candidates(dataset, project_root=root)
    state = evaluate_membership(candidates, provenance["dataset_phi"])
    arrays, summary, runtime = analyze_points(
        state.selected_ids, candidates.lattice_n[state.selected], state.selected_physical, config
    )
    artifacts = {
        "diffraction.npz": deterministic_npz(arrays),
        "summary.json": canonical_json(summary),
        "config.json": canonical_json(config.model_dump(mode="json")),
        "config_input.yaml": config_bytes,
        "runtime.json": canonical_json(runtime),
        **diffraction_figures(arrays, summary, candidates.config, state.phi.tolist(), config),
    }
    metadata = {
        "kind": "finite_patch_diffraction",
        "config": config.model_dump(mode="json"),
        "model_config": candidates.config.model_dump(mode="json"),
        "phi": state.phi.tolist(),
        "dataset_provenance": provenance,
        "selected_count": len(state.selected_ids),
        "selected_ids_sha256": summary["selected_ids_sha256"],
        "deduplication": (
            "lexicographic-index greedy representative Euclidean ball; "
            "no transitive chaining; CSR retains every h"
        ),
        "cross_section": summary["cross_section_definition"],
        "figure_display_floor": 1e-8,
    }
    command = command or [
        "qc5d",
        "diffraction",
        "--dataset",
        dataset.resolve().relative_to(root).as_posix(),
        "--config",
        config_file.resolve().relative_to(root).as_posix(),
        "--out",
        output.resolve().relative_to(root).as_posix(),
    ]
    if config.phason_values is not None:
        phason_arrays, phason_summary, phason_runtime = analyze_phasons(candidates, config)
        phason_artifacts = {
            "phason_diffraction.npz": deterministic_npz(phason_arrays),
            "summary.json": canonical_json(phason_summary),
            "runtime.json": canonical_json(phason_runtime),
            "config.json": canonical_json(config.model_dump(mode="json")),
            "config_input.yaml": config_bytes,
            **phason_figures(phason_arrays, phason_summary, candidates.config, config),
        }
        write_artifacts(
            phason_output,
            root,
            phason_artifacts,
            config_bytes,
            command,
            {
                **metadata,
                "kind": "phason_diffraction",
                "phason_values": config.phason_values,
                "frame_config": phason_summary["frame_diffraction_config"],
            },
        )
        summary_passed = summary["passed"] and phason_summary["passed"]
    else:
        summary_passed = summary["passed"]
    write_artifacts(output, root, artifacts, config_bytes, command, metadata)
    return {"passed": summary_passed, "diffraction": summary, "runtime": runtime}


def phason_figures(arrays, summary, model, config):
    result = {}
    if config.mode != "reciprocal":
        indices = np.linspace(
            0, len(summary["frames"]) - 1, min(5, len(summary["frames"])), dtype=int
        )
        figure = Figure(figsize=(17, 4.5), dpi=120)
        layout = figure.add_gridspec(1, len(indices) + 1, width_ratios=[1] * len(indices) + [0.05])
        axes = [figure.add_subplot(layout[0, i]) for i in range(len(indices))]
        for ax, frame in zip(axes, indices, strict=True):
            record = summary["frames"][frame]
            image = arrays["normalized_intensity_maps"][frame, : config.grid_samples**2].reshape(
                config.grid_samples, config.grid_samples
            )
            artist = map_panel(ax, image, config)
            ax.set_title(
                f"Frame {frame}; N={record['selected_count']}\nphi={tuple(record['phi'])}",
                fontsize=9,
            )
        figure.colorbar(
            artist, cax=figure.add_subplot(layout[0, -1]), label="log10(max(I/I(0),1e-8))"
        )
        figure.suptitle(
            f"Phason diffraction; crop R={model.physical_crop.radial_max:g}, "
            f"|z|≤{model.physical_crop.z_abs_max:g}; q_z={config.q_z:g}, "
            f"extent ±{config.q_extent:g}",
            fontsize=10,
        )
        result["phason_maps.png"] = png(figure)
    figure = Figure(figsize=(10, 5), dpi=120)
    a, b = figure.subplots(1, 2)
    records = summary["frames"]
    frames = [r["frame"] for r in records]
    a.plot(frames, [len(r["entered_ids"]) for r in records], "o-", label="Entered IDs")
    a.plot(frames, [len(r["exited_ids"]) for r in records], "o-", label="Exited IDs")
    a.set(xlabel="Sampled frame", ylabel="Changed ID count")
    a.legend()
    b.plot(frames, [r["intensity_map_difference_rmse"] for r in records], "o-")
    b.set(xlabel="Sampled frame", ylabel="RMSE of normalized I/I(0)")
    counts = [r["selected_count"] for r in records]
    figure.suptitle(
        f"R={model.physical_crop.radial_max:g}, |z|≤{model.physical_crop.z_abs_max:g}; "
        f"N={min(counts)}–{max(counts)}; q_z={config.q_z:g}, "
        f"sampled |q components|≤{np.max(np.abs(arrays['q_xyz'])):.3g} rad/model length",
        fontsize=10,
    )
    result["phason_changes.png"] = png(figure)
    return result


def run_convergence(config_files, output, *, root=None, overwrite=False, command=None):
    root = Path.cwd().resolve() if root is None else root.resolve()
    reserve_output(
        output,
        root,
        {
            "convergence.npz",
            "summary.json",
            "runtime.json",
            "metadata.json",
            "configs.json",
            "convergence_maps.png",
            "convergence_metrics.png",
            *[f"config_{i}_input.yaml" for i in range(len(config_files))],
        },
        overwrite,
    )
    configs = [ConvergenceConfig.model_validate(read_settings(path)) for path in config_files]
    arrays, summary, runtime = analyze_convergence(configs)
    input_bytes = {
        f"config_{i}_input.yaml": path.read_bytes() for i, path in enumerate(config_files)
    }
    config_inventory = {
        "files": [
            {
                "path": path.resolve().relative_to(root).as_posix(),
                "sha256": sha256_bytes(path.read_bytes()),
            }
            for path in config_files
        ]
    }
    artifacts = {
        "convergence.npz": deterministic_npz(arrays),
        "summary.json": canonical_json(summary),
        "runtime.json": canonical_json(runtime),
        "configs.json": canonical_json(
            {"configs": [cfg.model_dump(mode="json") for cfg in configs]}
        ),
        **input_bytes,
        **convergence_figures(arrays, summary, configs),
    }
    command = command or [
        "qc5d",
        "diffraction-convergence",
        "--configs",
        *[p.resolve().relative_to(root).as_posix() for p in config_files],
        "--out",
        output.resolve().relative_to(root).as_posix(),
    ]
    write_artifacts(
        output,
        root,
        artifacts,
        canonical_json(config_inventory),
        command,
        {
            "kind": "finite_crop_convergence",
            "config_inventory": config_inventory,
            "configs": [cfg.model_dump(mode="json") for cfg in configs],
            "config_hash_method": "canonical ordered inventory of exact input config hashes",
        },
    )
    return {"passed": summary["passed"], "runs": summary["runs"], "runtime": runtime}


def convergence_figures(arrays, summary, configs):
    figure = Figure(figsize=(14, 5), dpi=120)
    layout = figure.add_gridspec(1, len(configs) + 1, width_ratios=[1] * len(configs) + [0.05])
    axes = [figure.add_subplot(layout[0, i]) for i in range(len(configs))]
    for run, (ax, cfg) in enumerate(zip(axes, configs, strict=True)):
        image = arrays[f"run_{run}/intensity_normalized"][
            : cfg.diffraction.grid_samples**2
        ].reshape(cfg.diffraction.grid_samples, cfg.diffraction.grid_samples)
        artist = map_panel(ax, image, cfg.diffraction)
        ax.set_title(
            f"R={cfg.model.physical_crop.radial_max:g}, "
            f"|z|≤{cfg.model.physical_crop.z_abs_max:g}\n"
            f"N={summary['runs'][run]['diffraction']['selected_count']}",
            fontsize=10,
        )
    figure.colorbar(artist, cax=figure.add_subplot(layout[0, -1]), label="log10(max(I/I(0),1e-8))")
    figure.suptitle(
        f"Finite crop sensitivity; q_z={configs[0].diffraction.q_z:g}; "
        f"extent ±{configs[0].diffraction.q_extent:g}; identical grid",
        fontsize=10,
    )
    result = {"convergence_maps.png": png(figure)}
    figure = Figure(figsize=(10, 5), dpi=120)
    a, b = figure.subplots(1, 2)
    radii = [cfg.model.physical_crop.radial_max for cfg in configs]
    counts = [row["diffraction"]["selected_count"] for row in summary["runs"]]
    a.plot(radii, counts, "o-")
    a.set(xlabel="Scientific radial crop (model length)", ylabel="Selected count")
    changes = summary["runs"][1:]
    b.plot(
        radii[1:],
        [row["peak_stability_from_previous"]["matched_intensity_rmse"] for row in changes],
        "o-",
    )
    b.set(
        xlabel="Scientific radial crop (model length)",
        ylabel="Matched top-k normalized intensity RMSE",
    )
    cfg = configs[0]
    figure.suptitle(
        f"Layer crop |z|≤{cfg.model.physical_crop.z_abs_max:g}; N={min(counts)}–{max(counts)}; "
        f"I/I(0), q_z={cfg.diffraction.q_z:g}, extent ±{cfg.diffraction.q_extent:g}",
        fontsize=10,
    )
    result["convergence_metrics.png"] = png(figure)
    return result


def run_validation(dataset, output, *, root=None, overwrite=False, command=None):
    root = Path.cwd().resolve() if root is None else root.resolve()
    reserve_output(
        output,
        root,
        {
            "scientific_validation.json",
            "scientific_validation.md",
            "analytic_fixtures.npz",
            "analytic_fixtures.png",
            "config.json",
            "metadata.json",
            "runtime.json",
        },
        overwrite,
    )
    config = DiffractionConfig()
    candidates, provenance = load_candidates(dataset, project_root=root)
    state = evaluate_membership(candidates, provenance["dataset_phi"])
    fixtures, fixture_arrays = analytic_fixtures(config)
    _, canonical, runtime = analyze_points(
        state.selected_ids, candidates.lattice_n[state.selected], state.selected_physical, config
    )
    summary = {
        "schema_version": "1.0",
        "passed": fixtures["passed"] and canonical["passed"],
        "analytic": fixtures,
        "canonical": canonical,
        "config": config.model_dump(mode="json"),
        "dataset_provenance": provenance,
    }
    markdown = (
        f"# Scientific validation\n\nGate: {'PASS' if summary['passed'] else 'FAIL'} "
        "for the recorded analytic and finite-patch cases.\n\n"
        + "\n".join(
            f"- {name}: normalized max error {case['normalized_max_abs_error']:.16g}; "
            f"Friedel error {case['friedel_max_abs_normalized']:.16g}; "
            f"calculated I(0)={case['central_intensity']:.16g}; "
            f"{'PASS' if case['passed'] else 'FAIL'}."
            for name, case in fixtures["cases"].items()
        )
        + f"\n- Canonical selected points: {len(state.selected_ids)}; "
        f"I(0)={canonical['central_intensity_raw']:.16g}.\n"
        f"- Friedel normalized max error: {canonical['friedel_max_abs_normalized']:.16g}.\n"
        f"- Tenfold masked maximum NRMSE: {canonical['tenfold']['maximum_nrmse']}; "
        f"minimum correlation: {canonical['tenfold']['minimum_correlation']}.\n\n"
        "Tolerances: Friedel/analytic <=1e-12; tenfold NRMSE <=1e-10 "
        "and correlation >=0.9999999999.\n\n"
        "Dimensionless finite crop, real unit weights, no taper, I/I(0). These cases do not "
        "prove infinite-patch convergence, full reciprocal-volume symmetry, cross-language "
        "parity, or human benefit. Full settings, IDs, numerical results, provenance, "
        "and checksums accompany this report.\n"
    )
    figure = Figure(figsize=(10, 5), dpi=120)
    a, b = figure.subplots(1, 2)
    for ax, name in ((a, "periodic_1d"), (b, "finite_cubic")):
        q = fixture_arrays[name + "/q_xyz"]
        x = q[:, 0] if name == "periodic_1d" else np.arange(len(q))
        ax.plot(x, fixture_arrays[name + "/expected_normalized_intensity"], "o", label="Analytic")
        ax.plot(x, fixture_arrays[name + "/observed_normalized_intensity"], "+", label="Calculated")
        ax.set(
            xlabel="q_x (rad/model length)" if name == "periodic_1d" else "Recorded q-vector case",
            ylabel="I/I(0)",
            title=(
                f"{name}; N={fixtures['cases'][name]['point_count']}\n"
                f"|q components|≤{np.max(np.abs(q)):.3g} rad/model length"
            ),
        )
        ax.legend()
    figure.suptitle(
        "Analytic finite lattices: line x=0..7; cube [-1,1]^3; unit weights, no taper", fontsize=10
    )
    artifacts = {
        "scientific_validation.json": canonical_json(summary),
        "scientific_validation.md": markdown.encode(),
        "analytic_fixtures.npz": deterministic_npz(fixture_arrays),
        "analytic_fixtures.png": png(figure),
        "config.json": canonical_json(config.model_dump(mode="json")),
        "runtime.json": canonical_json(runtime),
    }
    command = command or [
        "qc5d",
        "validate-science",
        "--dataset",
        dataset.resolve().relative_to(root).as_posix(),
        "--out",
        output.resolve().relative_to(root).as_posix(),
    ]
    write_artifacts(
        output,
        root,
        artifacts,
        artifacts["config.json"],
        command,
        {
            "kind": "analytic_and_canonical_scientific_validation",
            "config": config.model_dump(mode="json"),
            "model_config": candidates.config.model_dump(mode="json"),
            "phi": state.phi.tolist(),
            "dataset_provenance": provenance,
            "selected_count": len(state.selected_ids),
            "config_hash_method": "canonical effective config UTF-8",
        },
    )
    return summary
