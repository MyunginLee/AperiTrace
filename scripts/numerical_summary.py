"""Regenerate twelve-case history and finite-sum outputs from installed source."""

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from qc5d.diffraction import structure_factor
from qc5d.exploration import Exploration
from qc5d.model_config import ModelConfig, PhasonDomain, PhysicalCrop
from qc5d.modelset import generate_candidates

PATHS = {
    "horizontal": ((-0.70, 0.133), (0.70, 0.133)),
    "diagonal": ((-0.63, -0.57), (0.61, 0.59)),
    "oblique": ((-0.61, 0.43), (0.67, -0.31)),
    "short": ((-0.12, -0.071), (0.13, 0.079)),
}
Q = [[0.0, 0.0, 0.0], [1.6, 0.0, 0.0], [1.6, 0.0, 1.0]]


def digest(tuples):
    data = "\n".join(",".join(map(str, row)) for row in tuples).encode("ascii")
    return hashlib.sha256(data).hexdigest()


def summary():
    rows = []
    for radius in (2, 4, 6):
        config = ModelConfig(
            index_bound=7,
            physical_crop=PhysicalCrop(radial_max=radius, z_abs_max=1.5),
            phason_domain=PhasonDomain(min=(-0.75, -0.75), max=(0.75, 0.75)),
        )
        candidates = generate_candidates(config)
        app = Exploration(candidates, Q)
        for name, (start, end) in PATHS.items():
            path = app.path(start, end)
            intervals = path.intervals
            eligible = intervals.ever_selected & (intervals.lower > 0) & (intervals.upper < 1)
            losses = []
            for count in (9, 33, 129):
                masks = np.array([path.reference_mask(t) for t in np.linspace(0, 1, count)])
                missed = np.flatnonzero(eligible & ~masks.any(axis=0))
                for i in missed:
                    assert path.reference_mask((intervals.lower[i] + intervals.upper[i]) / 2)[i]
                losses.append(
                    {
                        "query_count": count,
                        "eligible": int(eligible.sum()),
                        "missed": len(missed),
                        "missed_tuples": candidates.lattice_n[missed].tolist(),
                        "missed_bounds": np.column_stack(
                            (intervals.lower[missed], intervals.upper[missed])
                        ).tolist(),
                        "selected_digests": [digest(candidates.lattice_n[m]) for m in masks],
                    }
                )
            times = np.linspace(0, 1, 33)
            replay = path.query(app.cache, times)
            f = []
            counts = []
            statuses = []
            for i, t in enumerate(times):
                mask = path.reference_mask(t)
                returned, status = path.state(t)
                np.testing.assert_array_equal(mask, returned)
                direct = structure_factor(candidates.physical[mask], Q)
                n = int(mask.sum())
                np.testing.assert_allclose(
                    replay.amplitudes[i] / max(1, n), direct / max(1, n), atol=1e-12, rtol=0
                )
                assert replay.counts[i] == n
                f.append(direct)
                counts.append(n)
                statuses.append(status)
            f = np.asarray(f)
            rows.append(
                {
                    "case": f"r{radius}_{name}",
                    "candidate_count": len(candidates.ids),
                    "candidate_id_digest": digest(candidates.lattice_n),
                    "cache_bytes_at_Q121": len(candidates.ids) * 121 * 16,
                    "sampling": losses,
                    "counts": counts,
                    "status": statuses,
                    "F_real": f.real.tolist(),
                    "F_imag": f.imag.tolist(),
                    "I_normalized": (np.abs(f) ** 2 / np.maximum(counts, 1)[:, None] ** 2).tolist(),
                }
            )
    totals = {
        str(t): sum(next(s["missed"] for s in r["sampling"] if s["query_count"] == t) for r in rows)
        for t in (9, 33, 129)
    }
    return {
        "q": Q,
        "case_count": len(rows),
        "eligible": sum(r["sampling"][0]["eligible"] for r in rows),
        "missed": totals,
        "cases": rows,
        "meaning": "case-wise excursions, not unique atoms; sampled states remain correct",
    }


def compare(actual, expected, location="root"):
    if isinstance(expected, dict):
        assert actual.keys() == expected.keys(), location
        for key, value in expected.items():
            compare(actual[key], value, location + "." + key)
    elif isinstance(expected, list):
        assert len(actual) == len(expected), location
        for i, value in enumerate(expected):
            compare(actual[i], value, f"{location}[{i}]")
    elif isinstance(expected, float):
        np.testing.assert_allclose(actual, expected, atol=1e-12, rtol=0, err_msg=location)
    else:
        assert actual == expected, f"{location}: {actual!r} != {expected!r}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--reference", type=Path)
    args = parser.parse_args()
    result = summary()
    with args.output.open("x", encoding="utf-8") as out:
        json.dump(result, out, indent=2, allow_nan=False)
    if args.reference:
        compare(result, json.loads(args.reference.read_text(encoding="utf-8")))
    print(json.dumps({"gate": "PASS", "eligible": result["eligible"], "missed": result["missed"]}))


if __name__ == "__main__":
    main()
