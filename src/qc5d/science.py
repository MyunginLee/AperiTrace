"""Analytic fixtures and finite-patch diffraction, convergence, and phason calculations."""

from itertools import product
from pathlib import Path
from time import perf_counter

import numpy as np

from qc5d.basis import SuperspaceBasis
from qc5d.checksums import sha256_bytes, sha256_file
from qc5d.diffraction import (
    angular_metrics,
    cartesian_grid,
    intensity,
    normalize_intensity,
    ordered_peaks,
    peak_comparison,
    polar_grid,
    structure_factor,
    working_block_shape,
)
from qc5d.diffraction_config import ConvergenceConfig, DiffractionConfig
from qc5d.geometry import immutable_array
from qc5d.identity import lattice_tuples, stable_ids
from qc5d.model_artifacts import check_dataset
from qc5d.model_config import ModelConfig
from qc5d.modelset import (
    CandidateSet,
    compare_membership,
    evaluate_membership,
    generate_candidates,
    project_tuples,
)
from qc5d.reciprocal import reciprocal_indices
from qc5d.reference import canonical_json


def load_candidates(
    dataset: Path, *, project_root: Path | None = None
) -> tuple[CandidateSet, dict]:
    path = dataset.resolve()
    if path.is_dir():
        path /= "modelset.npz"
    integrity = check_dataset(path, project_root=project_root)
    with np.load(path, allow_pickle=False) as archive:
        config = ModelConfig.model_validate_json(archive["config_json"].item())
        candidates = CandidateSet(
            config,
            immutable_array(archive["points/lattice_n_int32"], np.int32),
            immutable_array(archive["points/physical_xyz_float64"]),
            immutable_array(archive["points/internal_uv_float64"]),
            immutable_array(archive["points/stable_id"], "U59"),
            immutable_array(archive["points/lookup_hash_uint64"], np.uint64),
            {},
        )
        phi = archive["phi"].copy()
    return candidates, {
        "dataset_sha256": sha256_file(path),
        "dataset_phi": phi.tolist(),
        "integrity": integrity,
    }


def analyze_points(ids, lattice, positions, config: DiffractionConfig) -> tuple[dict, dict, dict]:
    started = perf_counter()
    lattice = lattice_tuples(lattice)
    expected_ids = stable_ids(lattice)
    expected_positions, _ = project_tuples(lattice, SuperspaceBasis())
    if not np.array_equal(ids, expected_ids) or not np.array_equal(positions, expected_positions):
        raise ValueError("diffraction identities/coordinates must match the exact canonical tuples")
    if len(np.unique(lattice, axis=0)) != len(lattice):
        raise ValueError("diffraction selected tuples must be unique")
    axis, cart_q = cartesian_grid(config)
    cart_q = cart_q if config.mode != "reciprocal" else cart_q[:0]
    reciprocal = reciprocal_indices(config.reciprocal_index_bound, config.deduplication_tolerance)
    cross = np.abs(reciprocal.q[:, 2]) <= config.q_z_tolerance
    evaluated = (
        np.flatnonzero(cross) if config.reciprocal_cross_section_only else np.arange(len(cross))
    )
    reciprocal_q = reciprocal.q[evaluated] if config.mode != "cartesian" else reciprocal.q[:0]
    radii, angles, polar_q = polar_grid(config)
    q = np.vstack((cart_q, reciprocal_q))
    pairs = len(positions) * (2 * len(q) + len(polar_q) + 1)
    if pairs > config.max_phase_pairs:
        raise ValueError("diffraction phase-pair limit exceeded; reduce patch or sampling")
    options = {"chunk_size": config.q_chunk_size, "point_chunk_size": config.point_chunk_size}
    central = float(intensity(structure_factor(positions, np.zeros((1, 3)), **options))[0])
    amplitudes = structure_factor(positions, q, **options)
    raw = intensity(amplitudes)
    normalized = normalize_intensity(raw, central)
    reverse = normalize_intensity(intensity(structure_factor(positions, -q, **options)), central)
    friedel = float(np.max(np.abs(normalized - reverse), initial=0.0))
    polar_amplitudes = structure_factor(positions, polar_q, **options)
    polar_intensity = normalize_intensity(intensity(polar_amplitudes), central).reshape(
        len(radii), len(angles)
    )
    tenfold = angular_metrics(polar_intensity, radii, config)
    cart_count = len(cart_q)
    peaks = ordered_peaks(cart_q, normalized[:cart_count], config, grid=True) if cart_count else []
    reciprocal_peaks = (
        ordered_peaks(reciprocal_q, normalized[cart_count:], config) if len(reciprocal_q) else []
    )
    checks = {
        "finite_amplitudes_and_intensities": bool(
            np.all(np.isfinite(amplitudes)) and np.all(np.isfinite(polar_intensity))
        ),
        "nonnegative_intensity": bool(np.all(raw >= 0)),
        "central_normalization": central == float(len(positions) ** 2),
        "friedel_symmetry": friedel <= config.friedel_atol,
        "tenfold_symmetry": tenfold["passed"] if config.require_tenfold else True,
    }
    arrays = {
        "schema_version": np.array("1.0"),
        "selected_ids": ids,
        "selected_lattice_n_int32": lattice,
        "selected_physical_xyz_float64": positions,
        "weights_float64": np.ones(len(positions)),
        "cartesian_axis": axis,
        "q_xyz": q,
        "F_complex128": amplitudes,
        "intensity_raw": raw,
        "intensity_normalized": normalized,
        "central_intensity": np.array(central),
        "cartesian_count": np.array(cart_count, dtype=np.int64),
        "reciprocal/q_unique": reciprocal.q,
        "reciprocal/contributing_h_int32": reciprocal.contributing_h,
        "reciprocal/group_offsets_int64": reciprocal.offsets,
        "reciprocal/representative_h_int32": reciprocal.representative_h,
        "reciprocal/cross_section_mask": cross,
        "reciprocal/evaluated_groups": evaluated if len(reciprocal_q) else evaluated[:0],
        "polar/radii": radii,
        "polar/angles_radians": angles,
        "polar/q_xyz": polar_q,
        "polar/F_complex128": polar_amplitudes,
        "polar/normalized_intensity": polar_intensity,
    }
    q_rows, point_rows = working_block_shape(
        len(q), len(positions), config.q_chunk_size, config.point_chunk_size
    )
    summary = {
        "passed": all(checks.values()),
        "checks": checks,
        "selected_count": len(positions),
        "selected_ids_sha256": sha256_bytes(canonical_json({"ids": ids.tolist()})),
        "central_intensity_raw": central,
        "central_intensity_normalized": 1.0,
        "friedel_max_abs_normalized": friedel,
        "tenfold": tenfold,
        "tenfold_required": config.require_tenfold,
        "cartesian_peaks": peaks,
        "reciprocal_peaks": reciprocal_peaks,
        "reciprocal_tuple_count": len(reciprocal.contributing_h),
        "reciprocal_unique_vector_count": len(reciprocal.q),
        "reciprocal_cross_section_count": int(np.count_nonzero(cross)),
        "reciprocal_evaluation": "cross_section"
        if config.reciprocal_cross_section_only
        else "full_finite_index_volume",
        "cross_section_definition": "abs(q_z)<=q_z_tolerance, not the full 3D reciprocal volume",
        "phase_pair_count": pairs,
        "working_q_rows": q_rows,
        "working_point_rows": point_rows,
        "core_working_array_bytes_estimate": q_rows * point_rows * 32,
        "exported_array_bytes": sum(array.nbytes for array in arrays.values()),
        "memory_semantics": "core array/storage estimates, not process peak RSS",
    }
    return arrays, summary, {"calculation_elapsed_seconds": perf_counter() - started}


def analytic_fixtures(config: DiffractionConfig) -> tuple[dict, dict]:
    # Eight equally spaced points; reciprocal harmonics have I/I0=1, other DFT bins zero.
    line = np.column_stack((np.arange(8, dtype=float), np.zeros((8, 2))))
    bins = np.arange(-16, 17)
    q_line = np.column_stack((2 * np.pi * bins / 8, np.zeros((len(bins), 2))))
    expected_line = (bins % 8 == 0).astype(float)
    # Three centered points per cubic axis: F = product_j (1 + 2 cos(q_j)).
    cube = np.array(list(product((-1.0, 0.0, 1.0), repeat=3)))
    q_cube = np.array(
        [
            (0, 0, 0),
            (2 * np.pi, 0, 0),
            (0, 2 * np.pi, 0),
            (0, 0, 2 * np.pi),
            (2 * np.pi / 3, 0, 0),
            (0.23, -0.7, 1.2),
            (np.pi, np.pi, np.pi),
        ]
    )
    expected_cube = np.prod(1 + 2 * np.cos(q_cube), axis=1) ** 2 / len(cube) ** 2
    fixtures, arrays = {}, {}
    for name, positions, q, expected in (
        ("periodic_1d", line, q_line, expected_line),
        ("finite_cubic", cube, q_cube, expected_cube),
    ):
        F = structure_factor(
            positions, q, chunk_size=config.q_chunk_size, point_chunk_size=config.point_chunk_size
        )
        central = float(intensity(structure_factor(positions, np.zeros((1, 3))))[0])
        raw = intensity(F)
        normalized = normalize_intensity(raw, central)
        reverse = structure_factor(
            positions, -q, chunk_size=config.q_chunk_size, point_chunk_size=config.point_chunk_size
        )
        friedel = float(
            np.max(np.abs(normalized - normalize_intensity(intensity(reverse), central)))
        )
        residual = float(np.max(np.abs(normalized - expected)))
        checks = {
            "analytic_expected_intensity": residual <= config.friedel_atol,
            "finite_amplitudes_and_intensities": bool(
                np.all(np.isfinite(F)) and np.all(np.isfinite(raw))
            ),
            "nonnegative_intensity": bool(np.all(raw >= 0)),
            "central_normalization": central == float(len(positions) ** 2),
            "friedel_symmetry": friedel <= config.friedel_atol,
        }
        fixtures[name] = {
            "passed": all(checks.values()),
            "checks": checks,
            "normalized_max_abs_error": residual,
            "friedel_max_abs_normalized": friedel,
            "point_count": len(positions),
            "central_intensity": central,
            "expected_normalized_intensity": expected.tolist(),
            "observed_normalized_intensity": normalized.tolist(),
        }
        arrays.update(
            {
                name + "/positions": positions,
                name + "/q_xyz": q,
                name + "/F_complex128": F,
                name + "/F_negative_q_complex128": reverse,
                name + "/expected_normalized_intensity": expected,
                name + "/observed_normalized_intensity": normalized,
            }
        )
    return {"passed": all(row["passed"] for row in fixtures.values()), "cases": fixtures}, arrays


def analyze_phasons(candidates: CandidateSet, config: DiffractionConfig) -> tuple[dict, dict, dict]:
    if config.phason_values is None:
        raise ValueError("phason_values must be supplied")
    frame_config = config.model_copy(update={"require_tenfold": False, "phason_values": None})
    peak_key = "reciprocal_peaks" if config.mode == "reciprocal" else "cartesian_peaks"
    records, masks, spectra, central_values, runtimes = [], [], [], [], []
    previous = previous_summary = previous_map = None
    q_reference = None
    for frame, phi in enumerate(config.phason_values):
        state = evaluate_membership(candidates, phi)
        arrays, summary, runtime = analyze_points(
            state.selected_ids,
            candidates.lattice_n[state.selected],
            state.selected_physical,
            frame_config,
        )
        current_map = arrays["intensity_normalized"]
        if q_reference is None:
            q_reference = arrays["q_xyz"]
        elif not np.array_equal(q_reference, arrays["q_xyz"]):
            raise ValueError("phason frames do not share reciprocal sampling")
        changes = (
            {
                "entered_ids": [],
                "exited_ids": [],
                "boundary_ids": candidates.ids[state.boundary].tolist(),
            }
            if previous is None
            else compare_membership(candidates, previous, state)
        )
        records.append(
            {
                "frame": frame,
                "phi": state.phi.tolist(),
                "selected_ids": state.selected_ids.tolist(),
                "selected_count": len(state.selected_ids),
                "central_intensity_raw": summary["central_intensity_raw"],
                "intensity_map_difference_l2": 0.0
                if previous_map is None
                else float(np.linalg.norm(current_map - previous_map)),
                "intensity_map_difference_rmse": 0.0
                if previous_map is None
                else float(np.sqrt(np.mean((current_map - previous_map) ** 2))),
                "peak_change": None
                if previous_summary is None
                else peak_comparison(previous_summary[peak_key], summary[peak_key]),
                "cartesian_peaks": summary["cartesian_peaks"],
                "reciprocal_peaks": summary["reciprocal_peaks"],
                "peak_sampling": peak_key,
                "friedel_max_abs_normalized": summary["friedel_max_abs_normalized"],
                "calculation_passed": summary["passed"],
                **changes,
            }
        )
        masks.append(state.selected)
        spectra.append(current_map)
        central_values.append(summary["central_intensity_raw"])
        runtimes.append(runtime)
        previous, previous_summary, previous_map = state, summary, current_map
    closed = np.array_equal(config.phason_values[0], config.phason_values[-1])
    endpoint_equal = bool(np.array_equal(masks[0], masks[-1]))
    maps_equal = bool(np.array_equal(spectra[0], spectra[-1]))
    summary = {
        "passed": all(r["calculation_passed"] for r in records)
        and (not closed or endpoint_equal and maps_equal),
        "frames": records,
        "closed": bool(closed),
        "initial_final_ids_equal": endpoint_equal,
        "initial_final_intensity_maps_equal": maps_equal,
        "frame_diffraction_config": frame_config.model_dump(mode="json"),
        "intensity_map_sampling": (
            "the complete shared q_xyz array, including both Cartesian and reciprocal-index "
            "samples when mode=both; Cartesian peaks alone are compared in that mode"
        ),
        "point_identity_relation": (
            "each F uses exactly selected_ids and their unchanged candidate physical coordinates"
        ),
    }
    return (
        {
            "schema_version": np.array("1.0"),
            "candidate_ids": candidates.ids,
            "candidate_lattice_n_int32": candidates.lattice_n,
            "candidate_physical_xyz_float64": candidates.physical,
            "phi": np.array(config.phason_values),
            "selected_masks": np.array(masks),
            "q_xyz": q_reference,
            "normalized_intensity_maps": np.array(spectra),
            "central_intensity_raw": np.array(central_values),
        },
        summary,
        {"frames": runtimes},
    )


def analyze_convergence(configs: list[ConvergenceConfig]) -> tuple[dict, dict, dict]:
    if len(configs) < 3:
        raise ValueError("convergence requires at least three increasing physical crops")
    first = configs[0]
    baseline = first.model.model_dump(mode="json")
    baseline.pop("physical_crop")
    radii = [cfg.model.physical_crop.radial_max for cfg in configs]
    if any(a >= b for a, b in zip(radii, radii[1:], strict=False)):
        raise ValueError("convergence radial crops must be strictly increasing")
    for cfg in configs:
        comparable = cfg.model.model_dump(mode="json")
        comparable.pop("physical_crop")
        if comparable != baseline or cfg.diffraction != first.diffraction:
            raise ValueError(
                "hold index/domain/phi/scales and reciprocal settings fixed across crops"
            )
        if (
            cfg.model.physical_crop.kind != first.model.physical_crop.kind
            or cfg.model.physical_crop.z_abs_max != first.model.physical_crop.z_abs_max
        ):
            raise ValueError("hold crop kind and layer crop fixed across convergence")
        if cfg.diffraction.phason_values is not None or cfg.diffraction.mode == "reciprocal":
            raise ValueError("convergence requires Cartesian sampling and no phason path")
    records, arrays, runtimes = [], {}, []
    previous = None
    reference_q = None
    for run, cfg in enumerate(configs):
        start = perf_counter()
        candidates = generate_candidates(cfg.model)
        state = evaluate_membership(candidates, cfg.model.initial_phi)
        spectrum, summary, runtime = analyze_points(
            state.selected_ids,
            candidates.lattice_n[state.selected],
            state.selected_physical,
            cfg.diffraction,
        )
        if reference_q is None:
            reference_q = spectrum["q_xyz"]
        elif not np.array_equal(reference_q, spectrum["q_xyz"]):
            raise ValueError("convergence q grids disagree")
        records.append(
            {
                "run": run,
                "model_config": cfg.model.model_dump(mode="json"),
                "diffraction": summary,
                "candidate_count": len(candidates.ids),
                "peak_stability_from_previous": None
                if previous is None
                else peak_comparison(previous["cartesian_peaks"], summary["cartesian_peaks"]),
            }
        )
        arrays.update({f"run_{run}/{key}": value for key, value in spectrum.items()})
        runtimes.append(
            {
                "run": run,
                **runtime,
                "generation_and_calculation_elapsed_seconds": perf_counter() - start,
            }
        )
        previous = summary
    return (
        arrays,
        {
            "passed": all(r["diffraction"]["passed"] for r in records),
            "runs": records,
            "interpretation": (
                "finite-size sensitivity on identical sampling; "
                "three crops do not establish an infinite diffraction measure"
            ),
        },
        {"runs": runtimes},
    )
