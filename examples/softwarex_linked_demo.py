"""Regenerate the fixed nonzero-q example using only the installed qc5d package."""

import argparse
import json
from pathlib import Path

import numpy as np

from qc5d.exploration import Exploration, bundled_example, recompute_state


def write_new(path, value):
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2, allow_nan=False)
        stream.write("\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    canonical = bundled_example()
    q = [[1.6, 0, 0], [1.6, 0, 1]]
    app = Exploration(canonical.candidates, q)
    path = app.path((-0.61, 0.43), (0.67, -0.31))
    focus = (0, -1, 1, 0, 1)
    row = int(np.flatnonzero(np.all(app.candidates.lattice_n == focus, axis=1))[0])
    lower, upper = path.intervals.lower[row], path.intervals.upper[row]
    parameters = [0.0625, float((lower + upper) / 2), 0.09375]
    states = []
    for name, t in zip(("before", "inside", "after"), parameters, strict=True):
        payload = app.export_state(path, t, focus)
        check = recompute_state(payload)
        write_new(args.output / (name + ".json"), payload)
        states.append(
            {
                "parameter": t,
                "N": payload["N"],
                "focus_selected": bool(path.state(t)[0][row]),
                "intensity": payload["I_normalized"],
                "status": check["status"],
            }
        )
    assert [s["focus_selected"] for s in states] == [False, True, False]
    assert [s["N"] for s in states] == [126, 126, 126]
    expected = [
        [0.022071913710234543, 0.00893227665854889],
        [0.019131062860380216, 0.008508461000690738],
        [0.016206714033874525, 0.00826059354778107],
    ]
    np.testing.assert_allclose([s["intensity"] for s in states], expected, atol=1e-12, rtol=0)
    summary = {
        "gate": "PASS",
        "candidate_count": len(app.candidates.lattice_n),
        "focus": focus,
        "q": q,
        "lower": float(lower),
        "upper": float(upper),
        "states": states,
        "meaning": "whole selected trajectory; other identities also change; finite unit weights",
    }
    write_new(args.output / "summary.json", summary)
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
