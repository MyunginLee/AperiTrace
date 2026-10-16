"""Edit a path, discover a missed interval, export and directly recompute it."""

import argparse
import json
from pathlib import Path

import numpy as np

from qc5d.exploration import Exploration, bundled_example, recompute_state

BASELINE_START = (-0.61, 0.43)
BASELINE_END = (0.67, -0.31)
CHANGED_START = (-0.60, 0.45)
CHANGED_END = (0.66, -0.27)
Q = ((1.6, 0, 0), (1.6, 0, 1))


def missed_intervals(path, times):
    """Fully interior excursions with no selected sampled state, not bad samples."""
    sampled = np.stack([path.state(float(t))[0] for t in times])
    intervals = path.intervals
    eligible = intervals.possible & (intervals.lower > 0) & (intervals.upper < 1)
    eligible &= intervals.lower < intervals.upper
    return np.flatnonzero(eligible & ~np.any(sampled, axis=0)), sampled


def build_example(start=CHANGED_START, end=CHANGED_END, samples=33):
    if type(samples) is not int or not 2 <= samples <= 4097:
        raise ValueError("samples must be an integer in [2,4097]")
    canonical = bundled_example()
    app = Exploration(canonical.candidates, Q)
    cache = app.cache
    baseline = app.path(BASELINE_START, BASELINE_END)
    changed = app.path(start, end)
    times = np.linspace(0, 1, samples)
    rows, sampled = missed_intervals(changed, times)
    summary = {
        "schema": "qc5d-path-recovery/1",
        "sample_count": samples,
        "candidate_count": len(app.candidates.ids),
        "baseline_path": {"start": list(BASELINE_START), "end": list(BASELINE_END)},
        "changed_path": {"start": changed.start.tolist(), "end": changed.end.tolist()},
        "new_interval_object": baseline is not changed,
        "same_phase_cache_object": cache is app.cache,
        "q": [list(row) for row in Q],
        "missed_ids": app.candidates.ids[rows].tolist(),
        "meaning": (
            "Illustrative canonical path edit; exact tuple identity, float64 intervals, "
            "not a material discovery or an isolated-site diffraction attribution"
        ),
    }
    for row in rows:
        lower, upper = changed.intervals.lower[row], changed.intervals.upper[row]
        midpoint = float((lower + upper) / 2)
        if changed.sensitive(midpoint) or not changed.reference_mask(midpoint)[row]:
            continue  # Do not present a boundary-sensitive witness as a clear recovery.
        index = int(np.searchsorted(times, midpoint, side="right") - 1)
        before, after = float(times[index]), float(times[index + 1])
        if not before <= lower < midpoint < upper <= after:
            raise RuntimeError("missed excursion does not lie inside its sampled gap")
        focus = tuple(int(x) for x in app.candidates.lattice_n[row])
        payloads, states = {}, []
        for name, parameter in zip(
            ("before", "inside", "after"), (before, midpoint, after), strict=True
        ):
            payload = app.export_state(changed, parameter, focus)
            check = recompute_state(payload)
            payloads[name] = payload
            states.append(
                {
                    "name": name,
                    "parameter": parameter,
                    "phi": payload["phi"],
                    "focus_selected": bool(changed.reference_mask(parameter)[row]),
                    "N": payload["N"],
                    "I_normalized": payload["I_normalized"],
                    "status": payload["status"],
                    "direct_recompute": check,
                }
            )
        if [state["focus_selected"] for state in states] != [False, True, False]:
            raise RuntimeError("reference predicate did not confirm the recovered excursion")
        _, baseline_sampled = missed_intervals(baseline, times)
        summary.update(
            {
                "outcome": "RECOVERED_CLEAR_FLOAT64_INTERVAL",
                "focus": list(focus),
                "focus_selected_at_changed_samples": int(sampled[:, row].sum()),
                "focus_selected_at_baseline_samples": int(baseline_sampled[:, row].sum()),
                "lower": float(lower),
                "upper": float(upper),
                "midpoint": midpoint,
                "states": states,
                "inside_minus_before_intensity": (
                    np.asarray(payloads["inside"]["I_normalized"])
                    - np.asarray(payloads["before"]["I_normalized"])
                ).tolist(),
            }
        )
        return summary, payloads
    summary["outcome"] = "NO_CLEAR_MISSED_INTERVAL"
    summary["states"] = []
    return summary, {}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--start", type=float, nargs=2, default=CHANGED_START)
    parser.add_argument("--end", type=float, nargs=2, default=CHANGED_END)
    parser.add_argument("--samples", type=int, default=33)
    args = parser.parse_args(argv)
    summary, payloads = build_example(args.start, args.end, args.samples)
    args.output.mkdir(parents=True, exist_ok=False)
    for name, value in {"summary": summary, **payloads}.items():
        with (args.output / (name + ".json")).open("x", encoding="utf-8") as stream:
            json.dump(value, stream, indent=2, allow_nan=False)
            stream.write("\n")
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
