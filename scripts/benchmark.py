"""Matched setup/query benchmark; validation and plotting are outside timings."""

# ruff: noqa: E402 -- single-thread before importing numerical libraries.
import argparse
import json
import os
import platform
from pathlib import Path
from time import perf_counter

for key in ("OMP_NUM_THREADS", "OPENBLAS_NUM_THREADS", "MKL_NUM_THREADS", "NUMEXPR_NUM_THREADS"):
    os.environ[key] = "1"

import numpy as np

from qc5d.diffraction import structure_factor
from qc5d.events import LinearPath, PhaseCache, polygon_data, sum_cache
from qc5d.geometry import signed_boundary_distance
from qc5d.model_config import ModelConfig, PhasonDomain, PhysicalCrop
from qc5d.modelset import generate_candidates
from qc5d.window import AcceptanceWindow

PATHS = {
    "horizontal": ((-0.70, 0.133), (0.70, 0.133)),
    "diagonal": ((-0.63, -0.57), (0.61, 0.59)),
    "oblique": ((-0.61, 0.43), (0.67, -0.31)),
    "short": ((-0.12, -0.071), (0.13, 0.079)),
}


def canonical_workload(name):
    """Regenerate the same support; no private historical run directory required."""
    radial, segment = name.split("_", 1)
    config = ModelConfig(
        index_bound=7,
        physical_crop=PhysicalCrop(radial_max=int(radial[1:]), z_abs_max=1.5),
        phason_domain=PhasonDomain(min=(-0.75, -0.75), max=(0.75, 0.75)),
    )
    candidates = generate_candidates(config)
    start, end = (np.asarray(v) for v in PATHS[segment])
    window = AcceptanceWindow()
    epsilon = 1e-10 * np.max(np.linalg.norm(window.vertices, axis=1))
    path = LinearPath(window.vertices, candidates.internal, start, end, epsilon)
    return (
        candidates.internal,
        candidates.physical,
        start,
        end,
        epsilon,
        path.intervals.critical_parameters(),
    )


def workload(vertices, internal, positions, start, end, epsilon, q, times, method):
    before = perf_counter()
    cache = PhaseCache.build(positions, q) if method != "B0" else None
    path = None
    if method == "B3":
        path = LinearPath(vertices, internal, start, end, epsilon)
    if method == "B2":
        _, _, _, _, normals, offset, _, _ = polygon_data(vertices, internal, start, end, epsilon)
        radius = np.max(np.linalg.norm(vertices, axis=1))
        guard = (
            64
            * np.finfo(float).eps
            * (
                1
                + radius
                + np.max(np.linalg.norm(internal, axis=1))
                + max(np.linalg.norm(start), np.linalg.norm(end))
            )
        )
    prepared = perf_counter()
    if method == "B3":
        result = path.query(cache, times)
        f, n = result.amplitudes, result.counts
        uncertain = int(np.count_nonzero(result.status == "uncertain"))
        intensity = result.normalized_intensity
    else:
        fs, ns = [], []
        uncertain = 0
        for t in times:
            shifted = internal + (start + t * (end - start))
            if method == "B2":
                slacks = (
                    shifted[:, 0, None] * normals[:, 0]
                    + shifted[:, 1, None] * normals[:, 1]
                    - offset
                )
                sensitive = np.min(np.abs(slacks - epsilon)) <= guard
                mask = (
                    signed_boundary_distance(vertices, shifted) > epsilon
                    if sensitive
                    else np.all(slacks > epsilon, axis=1)
                )
                uncertain += int(sensitive)
            else:
                mask = signed_boundary_distance(vertices, shifted) > epsilon
            fs.append(
                structure_factor(positions[mask], q)
                if method == "B0"
                else sum_cache(cache.values, mask)
            )
            ns.append(int(mask.sum()))
        f, n = np.asarray(fs), np.asarray(ns)
        intensity = np.abs(f) ** 2 / np.maximum(n, 1)[:, None] ** 2
    after = perf_counter()
    return (
        {
            "setup_seconds": prepared - before,
            "query_seconds": after - prepared,
            "total_seconds": after - before,
            "uncertain_query_count": uncertain,
        },
        f,
        n,
        intensity,
    )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--pilot", action="store_true")
    parser.add_argument("--boundary-stress", action="store_true")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    window = AcceptanceWindow()
    records = []
    raw = []
    names = [f"r{r}_{s}" for r in (2, 4, 6) for s in ("horizontal", "diagonal", "oblique", "short")]
    design = [(name, 121, t) for name in names for t in (33, 129)]
    design += [("r2_oblique", 121, t) for t in (2, 9, 257)]
    design += [("r2_oblique", size**2, 33) for size in (33, 65)]
    if args.pilot:
        design = design[-2:]
    if args.boundary_stress:
        design = [("r2_oblique", 121, 0)]
    for name, nq, nt in design:
        internal, positions, start, end, epsilon, roots = canonical_workload(name)
        size = int(np.sqrt(nq))
        x, y = np.meshgrid(np.linspace(-8, 8, size), np.linspace(-8, 8, size))
        q = np.column_stack((x.ravel(), y.ravel(), np.zeros(nq)))
        times = np.linspace(0, 1, nt)
        if args.boundary_stress:
            times = np.unique(
                np.r_[
                    roots,
                    np.maximum(0, np.nextafter(roots, -np.inf)),
                    np.minimum(1, np.nextafter(roots, np.inf)),
                    (roots[:-1] + roots[1:]) / 2,
                ]
            )
            nt = len(times)
        reference = None
        errors = []
        for rep in range(-1, 1 if args.pilot else 5):
            order = np.roll(np.array(["B0", "B1", "B2", "B3"]), rep % 4).tolist()
            for method in order:
                timing, f, n, intensity = workload(
                    window.vertices,
                    internal,
                    positions,
                    start,
                    end,
                    epsilon,
                    q,
                    times,
                    method,
                )
                if method == "B0" and reference is None:
                    reference = f, n, intensity
                # Rotation may call others first: compare after each repetition.
                if rep >= 0:
                    raw.append(
                        {
                            "case": name,
                            "q_count": nq,
                            "query_count": nt,
                            "repetition": rep,
                            "method_order": order,
                            "method": method,
                            **timing,
                        }
                    )
                if reference is not None:
                    rf, rn, ri = reference
                    assert np.array_equal(n, rn)
                    ef = float(np.max(np.abs(f - rf) / np.maximum(rn, 1)[:, None]))
                    ei = float(np.max(np.abs(intensity - ri)))
                    assert ef < 1e-12 and ei < 1e-12
                    errors.append(
                        {"method": method, "repetition": rep, "amplitude": ef, "intensity": ei}
                    )
        methods = {}
        for method in ("B0", "B1", "B2", "B3"):
            rows = [
                row
                for row in raw
                if row["case"] == name
                and row["q_count"] == nq
                and row["query_count"] == nt
                and row["method"] == method
            ]
            methods[method] = {
                key: {
                    "median": float(np.median([row[key] for row in rows])),
                    "q25": float(np.quantile([row[key] for row in rows], 0.25)),
                    "q75": float(np.quantile([row[key] for row in rows], 0.75)),
                }
                for key in ("setup_seconds", "query_seconds", "total_seconds")
            }
            methods[method]["uncertain_query_count"] = rows[0]["uncertain_query_count"]
        record = {
            "case": name,
            "candidate_count": len(internal),
            "q_count": nq,
            "query_count": nt,
            "cache_bytes": len(internal) * nq * 16,
            "methods": methods,
            "validation": errors,
        }
        records.append(record)
        with (args.output / f"{name}_q{nq}_t{nt}.json").open("x", encoding="utf-8") as stream:
            json.dump(record, stream, indent=2, allow_nan=False)
        print(
            json.dumps(
                {
                    "case": name,
                    "Q": nq,
                    "T": nt,
                    "B1_over_B3": methods["B1"]["total_seconds"]["median"]
                    / methods["B3"]["total_seconds"]["median"],
                }
            ),
            flush=True,
        )
    with (args.output / "summary.json").open("x", encoding="utf-8") as stream:
        json.dump(
            {
                "gate": "PASS",
                "pilot": args.pilot,
                "python": platform.python_version(),
                "platform": platform.platform(),
                "numpy": np.__version__,
                "processor": platform.processor(),
                "logical_cores": os.cpu_count(),
                "threads": 1,
                "repetitions": 1 if args.pilot else 5,
                "warmups": 1,
                "scope": (
                    "setup-inclusive CPU components; no enumeration, I/O, graph, plots or Unity; "
                    "status/fallback/rebaselining included; validation untimed"
                ),
                "cases": records,
                "raw_repetitions": raw,
            },
            stream,
            indent=2,
            allow_nan=False,
        )


if __name__ == "__main__":
    main()
