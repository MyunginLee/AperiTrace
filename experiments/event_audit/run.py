"""Execute all prospectively fixed phason audit cases into a new result directory."""

# ruff: noqa: E402 -- single-thread settings must precede importing numerical libraries.

import argparse
import hashlib
import json
import os
import platform
import sys
from datetime import UTC, datetime
from pathlib import Path
from time import perf_counter

for key in ("OMP_NUM_THREADS", "OPENBLAS_NUM_THREADS", "MKL_NUM_THREADS", "NUMEXPR_NUM_THREADS"):
    os.environ[key] = "1"

import numpy as np
from model import clip_intervals, decimal_intervals, event_amplitudes, phase_cache, sampling_losses

from qc5d.diffraction import structure_factor
from qc5d.graph import build_graph
from qc5d.model_config import ModelConfig, PhasonDomain, PhysicalCrop
from qc5d.modelset import evaluate_membership, generate_candidates
from qc5d.reference import deterministic_npz
from qc5d.window import AcceptanceWindow

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
SEGMENTS = (
    ("horizontal", (-0.70, 0.133), (0.70, 0.133)),
    ("diagonal", (-0.63, -0.57), (0.61, 0.59)),
    ("oblique", (-0.61, 0.43), (0.67, -0.31)),
    ("short", (-0.12, -0.071), (0.13, 0.079)),
)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_json(path, value):
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, indent=2, allow_nan=False)
        stream.write("\n")


def normalized(amplitudes, counts):
    return np.abs(amplitudes) ** 2 / np.maximum(counts, 1)[:, None] ** 2


def snapshots(candidates, start, end, times, q):
    masks, amplitudes, distances = [], [], []
    start, end = np.asarray(start), np.asarray(end)
    for time in times:
        state = evaluate_membership(candidates, start + time * (end - start))
        masks.append(state.selected)
        distances.append(state.signed_distance)
        amplitudes.append(structure_factor(state.selected_physical, q))
    masks = np.asarray(masks)
    return masks, np.asarray(amplitudes), np.asarray(distances), np.count_nonzero(masks, axis=1)


def graph_checks(candidates, start, end):
    result = []
    for time in (0.0, 0.5, 1.0):
        state = evaluate_membership(
            candidates, np.asarray(start) + time * (np.asarray(end) - start)
        )
        tuples = candidates.lattice_n[state.selected]
        graph = build_graph(tuples)
        lookup = {
            tuple(int(x) for x in row): str(identity)
            for row, identity in zip(tuples, candidates.ids[state.selected], strict=True)
        }
        expected = set()
        for row, identity in lookup.items():
            for axis in range(5):
                other = list(row)
                other[axis] += 1
                if tuple(other) in lookup:
                    expected.add((identity, lookup[tuple(other)], axis))
        actual = set(
            zip(
                graph.source_id.tolist(),
                graph.target_id.tolist(),
                graph.generator.tolist(),
                strict=True,
            )
        )
        result.append(
            {
                "time": time,
                "passed": actual == expected,
                "edge_count": len(actual),
                "selected_count": len(tuples),
                "edges": sorted(actual),
            }
        )
    return result


def benchmark(candidates, window, start, end, q, frames, table, cache):
    times = np.linspace(0, 1, frames)

    def direct():
        output, counts = [], []
        for time in times:
            state = evaluate_membership(
                candidates, np.asarray(start) + time * (np.asarray(end) - start)
            )
            output.append(structure_factor(state.selected_physical, q))
            counts.append(len(state.selected_ids))
        return np.asarray(output), np.asarray(counts)

    def cold():
        intervals = clip_intervals(
            window.vertices, candidates.internal, start, end, window.boundary_epsilon
        )
        phases = phase_cache(candidates.physical, q)
        amplitudes, counts, _ = event_amplitudes(intervals, phases, times)
        return amplitudes, counts

    def warm():
        amplitudes, counts, _ = event_amplitudes(table, cache, times)
        return amplitudes, counts

    operations = {"direct": direct, "cold_event": cold, "warm_event": warm}
    for operation in operations.values():
        operation()
    seconds = {name: [] for name in operations}
    errors = []
    for repetition in range(5):
        names = list(operations)
        names = names[repetition % 3 :] + names[: repetition % 3]
        outputs = {}
        for name in names:
            started = perf_counter()
            outputs[name] = operations[name]()
            seconds[name].append(perf_counter() - started)
        authority, counts = outputs["direct"]
        for name in ("cold_event", "warm_event"):
            amps, obtained_counts = outputs[name]
            errors.append(
                {
                    "method": name,
                    "repetition": repetition,
                    "counts_equal": bool(np.array_equal(counts, obtained_counts)),
                    "normalized_amplitude_max_abs": float(
                        np.max(np.abs(amps - authority) / np.maximum(counts, 1)[:, None])
                    ),
                    "normalized_intensity_max_abs": float(
                        np.max(np.abs(normalized(amps, counts) - normalized(authority, counts)))
                    ),
                }
            )
    medians = {name: float(np.median(values)) for name, values in seconds.items()}
    return {
        "frames": frames,
        "seconds": seconds,
        "median_seconds": medians,
        "cold_speedup": medians["direct"] / medians["cold_event"],
        "warm_speedup": medians["direct"] / medians["warm_event"],
        "validation": errors,
        "scope": (
            "Python membership and finite diffraction only; cold includes clipping and cache; "
            "no graph/Unity/display"
        ),
        "passed": all(
            row["counts_equal"]
            and row["normalized_amplitude_max_abs"] <= 1e-12
            and row["normalized_intensity_max_abs"] <= 1e-12
            for row in errors
        ),
    }


def case(candidates, radius, name, start, end, q, destination):
    window = AcceptanceWindow()
    table = clip_intervals(
        window.vertices, candidates.internal, start, end, window.boundary_epsilon
    )
    decimal = decimal_intervals(
        window.vertices, candidates.internal, start, end, window.boundary_epsilon
    )
    reverse = clip_intervals(
        window.vertices, candidates.internal, end, start, window.boundary_epsilon
    )
    active = table.ever_selected
    decimal_error = max(
        float(np.max(np.abs(table.lower[active] - decimal.lower[active]))),
        float(np.max(np.abs(table.upper[active] - decimal.upper[active]))),
    )
    reverse_error = max(
        float(np.max(np.abs(table.lower[active] - (1 - reverse.upper[active])))),
        float(np.max(np.abs(table.upper[active] - (1 - reverse.lower[active])))),
    )
    roots = table.critical_parameters()
    times = (roots[:-1] + roots[1:]) / 2
    cache = phase_cache(candidates.physical, q)
    amplitudes, counts, updates = event_amplitudes(table, cache, times)
    masks, direct, distances, direct_counts = snapshots(candidates, start, end, times, q)
    query_norm = max(
        np.linalg.norm(candidates.internal + np.asarray(start), axis=1).max(),
        np.linalg.norm(candidates.internal + np.asarray(end), axis=1).max(),
    )
    guard = 64 * np.finfo(np.float64).eps * (1 + window.characteristic_radius + query_norm)
    margins = np.min(np.abs(distances - window.boundary_epsilon), axis=1)
    resolved = margins > guard
    analytic_masks = np.asarray([table.selected(time) for time in times])
    differences = np.count_nonzero(analytic_masks != masks, axis=1)
    amplitude_error = np.max(
        np.abs(amplitudes[resolved] - direct[resolved])
        / np.maximum(direct_counts[resolved], 1)[:, None]
    )
    intensity_error = np.max(
        np.abs(
            normalized(amplitudes[resolved], counts[resolved])
            - normalized(direct[resolved], direct_counts[resolved])
        )
    )
    losses = [sampling_losses(table, frames) for frames in (9, 33, 129)]
    graphs = graph_checks(candidates, start, end)
    expansion = candidates.diagnostics["expansion"]
    support_bound = float(np.sqrt(radius**2 + 1.5**2 + expansion["candidate_internal_radius"] ** 2))
    science = {
        "radius": radius,
        "segment": name,
        "start": start,
        "end": end,
        "candidate_count": len(candidates.ids),
        "epsilon": window.boundary_epsilon,
        "finite_support_norm_bound": support_bound,
        "index_bound": 7,
        "critical_parameter_count": len(roots),
        "interval_count": len(times),
        "resolved_interval_count": int(np.count_nonzero(resolved)),
        "unresolved_interval_count": int(np.count_nonzero(~resolved)),
        "guard_is_diagnostic_not_scientific_epsilon": float(guard),
        "resolved_membership_difference_count": int(np.sum(differences[resolved])),
        "unresolved_membership_difference_count": int(np.sum(differences[~resolved])),
        "resolved_counts_equal": bool(np.array_equal(counts[resolved], direct_counts[resolved])),
        "normalized_amplitude_max_abs": float(amplitude_error),
        "normalized_intensity_max_abs": float(intensity_error),
        "decimal_parameter_max_abs": decimal_error,
        "reverse_parameter_max_abs": reverse_error,
        "sampling_losses": losses,
        "graph_checks": graphs,
        "cache_bytes": int(cache.nbytes),
        "q_count": len(q),
        "unit_weights": True,
        "real_material": False,
        "unresolved_intervals_are_not_certified": True,
        "passed": support_bound < 7
        and bool(np.array_equal(active, decimal.ever_selected))
        and bool(np.array_equal(active, reverse.ever_selected))
        and decimal_error <= 1e-12
        and reverse_error <= 1e-12
        and np.sum(differences[resolved]) == 0
        and np.array_equal(counts[resolved], direct_counts[resolved])
        and amplitude_error <= 1e-12
        and intensity_error <= 1e-12
        and all(row["passed"] for row in graphs),
    }
    arrays = {
        "lattice_n": candidates.lattice_n,
        "physical": candidates.physical,
        "internal": candidates.internal,
        "q": q,
        "lower": table.lower,
        "upper": table.upper,
        "possible": table.possible,
        "critical_parameters": roots,
        "midpoint_parameters": times,
        "resolved": resolved,
        "threshold_margins": margins,
        "authority_selected": masks,
        "analytic_selected": analytic_masks,
        "authority_amplitudes": direct,
        "event_amplitudes": amplitudes,
        "authority_counts": direct_counts,
        "event_counts": counts,
        "event_updates": updates,
        "signed_distances": distances,
        "decimal_lower": decimal.lower,
        "decimal_upper": decimal.upper,
    }
    destination.mkdir()
    with (destination / "science.npz").open("xb") as stream:
        stream.write(deterministic_npz(arrays))
    write_json(destination / "science.json", science)
    timings = [
        benchmark(candidates, window, start, end, q, frames, table, cache) for frames in (33, 129)
    ]
    write_json(destination / "timings.json", timings)
    print(
        json.dumps(
            {
                "case": destination.name,
                "passed": science["passed"],
                "missed_excursions_33": len(losses[1]["missed_excursions"]),
                "unresolved_intervals": science["unresolved_interval_count"],
                "cold_speedup_129": timings[1]["cold_speedup"],
            }
        ),
        flush=True,
    )
    return science, all(timing["passed"] for timing in timings)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    destination = args.out.resolve()
    if ROOT / "results" not in destination.parents:
        raise ValueError("output must be a new directory strictly under project results")
    destination.mkdir(parents=True, exist_ok=False)
    sources = [
        HERE / "PROTOCOL.md",
        HERE / "model.py",
        HERE / "run.py",
        ROOT / "tests/test_event_audit.py",
    ]
    sources += sorted((ROOT / "src/qc5d").glob("*.py"))
    source_hashes = {path.relative_to(ROOT).as_posix(): sha(path) for path in sources}
    write_json(destination / "source_manifest.json", source_hashes)
    write_json(
        destination / "execution.json",
        {
            "started_utc": datetime.now(UTC).isoformat(),
            "python": sys.version,
            "platform": platform.platform(),
            "numpy": np.__version__,
            "numerical_threads": 1,
            "argument_output": destination.relative_to(ROOT).as_posix(),
        },
    )
    axis = np.linspace(-8, 8, 11)
    x, y = np.meshgrid(axis, axis, indexing="xy")
    q = np.column_stack((x.ravel(), y.ravel(), np.zeros(x.size)))
    summaries, timing_checks = [], []
    for radius in (2.0, 4.0, 6.0):
        config = ModelConfig(
            index_bound=7,
            physical_crop=PhysicalCrop(radial_max=radius, z_abs_max=1.5),
            phason_domain=PhasonDomain(min=(-0.75, -0.75), max=(0.75, 0.75)),
            chunk_size=65536,
        )
        candidates = generate_candidates(config)
        for name, start, end in SEGMENTS:
            science, timing_check = case(
                candidates, radius, name, start, end, q, destination / f"r{int(radius)}_{name}"
            )
            summaries.append(
                {key: value for key, value in science.items() if key != "graph_checks"}
            )
            timing_checks.append(timing_check)
    write_json(destination / "science_summary.json", summaries)
    manifest = {
        "schema": "xr-event-audit/1",
        "gate": "PASS"
        if all(row["passed"] for row in summaries) and all(timing_checks)
        else "FAIL",
        "source_hashes": source_hashes,
        "files": {
            path.relative_to(destination).as_posix(): sha(path)
            for path in sorted(destination.rglob("*"))
            if path.is_file()
        },
        "finished_utc": datetime.now(UTC).isoformat(),
        "case_count": 12,
        "claim_scope": "resolved float64 intervals and this canonical finite workload only",
    }
    write_json(destination / "manifest.json", manifest)
    return 0 if manifest["gate"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
