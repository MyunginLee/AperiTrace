"""Independently verify the practical sampling coverage claim used in the paper."""

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from qc5d.exploration import bundled_example, recompute_state
from qc5d.recover_interval import build_example, missed_intervals


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    assert not args.output.exists()
    summary, payloads = build_example()
    app = bundled_example()
    path = app.path(summary["changed_path"]["start"], summary["changed_path"]["end"])
    times = np.linspace(0, 1, 33)
    missing_before, _ = missed_intervals(path, times)
    augmented = np.sort(np.append(times, summary["midpoint"]))
    missing_after, _ = missed_intervals(path, augmented)
    assert len(missing_before) == 2 and len(missing_after) == 0 and len(augmented) == 34
    assert set(app.candidates.ids[missing_before]) == set(summary["missed_ids"])
    assert all(identity in payloads["inside"]["selected_ids"] for identity in summary["missed_ids"])
    assert [payloads[name]["N"] for name in ("before", "inside", "after")] == [123, 125, 123]
    result = {
        "gate": "PASS",
        "sampling_before": {"queries": 33, "omitted_fully_interior_intervals": 2},
        "sampling_after": {"queries": 34, "omitted_fully_interior_intervals": 0},
        "added_parameter": summary["midpoint"],
        "recovered_ids": summary["missed_ids"],
        "direct_recompute": {name: recompute_state(payload) for name, payload in payloads.items()},
        "state_content_sha256": {
            name: hashlib.sha256(
                json.dumps(payload, sort_keys=True, allow_nan=False).encode()
            ).hexdigest()
            for name, payload in payloads.items()
        },
        "scope": "This canonical illustrative case only; not all diffraction extrema or materials",
    }
    with args.output.open("x", encoding="utf-8") as stream:
        json.dump(result, stream, indent=2)
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
