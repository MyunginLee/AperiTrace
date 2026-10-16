"""Reanalyse retained CPU timings; never invent new-host performance data."""

import argparse
import json
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    cases = []
    for name in ("benchmark_final", "benchmark_boundary"):
        retained = json.loads((ROOT / f"publication_data/{name}/summary.json").read_text())
        for case in retained["cases"]:
            medians = {}
            for method in ("B0", "B1", "B2", "B3"):
                rows = [
                    r
                    for r in retained["raw_repetitions"]
                    if r["case"] == case["case"]
                    and r["q_count"] == case["q_count"]
                    and r["query_count"] == case["query_count"]
                    and r["method"] == method
                ]
                assert len(rows) == 5
                for component in ("setup_seconds", "query_seconds", "total_seconds"):
                    values = np.quantile([r[component] for r in rows], [0.25, 0.5, 0.75])
                    expected = case["methods"][method][component]
                    np.testing.assert_allclose(
                        values,
                        [expected["q25"], expected["median"], expected["q75"]],
                        atol=0,
                        rtol=0,
                    )
                medians[method] = float(np.median([r["total_seconds"] for r in rows]))
            cases.append(
                {
                    "case": case["case"],
                    "Q": case["q_count"],
                    "T": case["query_count"],
                    "total_medians_seconds": medians,
                    "B1_B3": medians["B1"] / medians["B3"],
                    "B2_B3": medians["B2"] / medians["B3"],
                    "cache_bytes": case["cache_bytes"],
                }
            )
    with args.output.open("x", encoding="utf-8") as out:
        json.dump(
            {
                "gate": "PASS",
                "new_timings": False,
                "timing_host": "original Windows Ryzen 7 5800X",
                "cases": cases,
            },
            out,
            indent=2,
        )
    print("PASS: 30 retained workloads, 600 raw timings, no new-host speed claim")


if __name__ == "__main__":
    main()
