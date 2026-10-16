"""Reanalyse retained numerical inputs and generate machine-readable table values."""

import json

import numpy as np


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def build(data, output):
    bench = load(data / "benchmark_final/summary.json")
    boundary = load(data / "benchmark_boundary/summary.json")
    validation = load(data / "validation/summary.json")
    examples = load(data / "examples/summary.json")
    cases = bench["cases"]
    audits = []
    for name, retained in (("benchmark_final", bench), ("benchmark_boundary", boundary)):
        for case in retained["cases"]:
            for method in ("B0", "B1", "B2", "B3"):
                raw = [
                    r
                    for r in retained["raw_repetitions"]
                    if r["case"] == case["case"]
                    and r["q_count"] == case["q_count"]
                    and r["query_count"] == case["query_count"]
                    and r["method"] == method
                ]
                assert len(raw) == 5
                for component in ("setup_seconds", "query_seconds", "total_seconds"):
                    q25, median, q75 = np.quantile([r[component] for r in raw], [0.25, 0.5, 0.75])
                    assert {"median": median, "q25": q25, "q75": q75} == case["methods"][method][
                        component
                    ]
            assert case["cache_bytes"] == case["candidate_count"] * case["q_count"] * 16
        audits.append(
            {
                "input": name,
                "cases": len(retained["cases"]),
                "raw": len(retained["raw_repetitions"]),
            }
        )
    metrics = {
        "original": load(data / "validation/original_recomputed.json")["statistics"],
        "expanded_query_count": sum(c["expanded_query_count"] for c in validation["cases"]),
        "expanded_uncertain": sum(c["uncertain_queries"] for c in validation["cases"]),
        "expanded_amplitude_error": max(
            c["count_normalized_amplitude_max_abs"] for c in validation["cases"]
        ),
        "expanded_intensity_error": max(
            c["absolute_normalized_intensity_max_abs"] for c in validation["cases"]
        ),
        "eligible_excursions": sum(
            c["sampling"][0]["eligible_excursions"] for c in validation["cases"]
        ),
        "boundary_benchmark": boundary["cases"][0],
        "example": examples,
    }
    for t in (33, 129):
        relevant = [c for c in cases if c["q_count"] == 121 and c["query_count"] == t]
        for a, b in (("B0", "B1"), ("B1", "B3"), ("B2", "B3")):
            ratios = [
                c["methods"][a]["total_seconds"]["median"]
                / c["methods"][b]["total_seconds"]["median"]
                for c in relevant
            ]
            metrics[f"{a}_{b}_{t}"] = [min(ratios), max(ratios)]
    (output / "final_metrics.json").write_text(
        json.dumps(metrics, indent=2, allow_nan=False), encoding="utf-8"
    )
    rows = []
    for case in cases + boundary["cases"]:
        values = []
        for method in ("B0", "B1", "B2", "B3"):
            total = case["methods"][method]["total_seconds"]
            values.append(
                f"{total['median'] * 1000:.3f} ({(total['q75'] - total['q25']) * 1000:.3f})"
            )
        rows.append(
            case["case"].replace("_", r"\_")
            + f" & {case['q_count']} & {case['query_count']} & "
            + " & ".join(values)
            + r" \\"
        )
    (output / "timing_rows.tex").write_text("\n".join(rows) + "\n", encoding="utf-8")
    rows = []
    for case in validation["cases"]:
        decomp = case["unresolved_decomposition"]
        values = [
            case["candidate_count"],
            case["original_probe_count"] - case["original_unresolved"],
            case["original_unresolved"],
            decomp.get("adjacent_no_interior", 0),
            decomp.get("representable_interior", 0),
            case["original_candidate_disagreements"],
        ]
        rows.append(
            case["case"].replace("_", r"\_")
            + " & "
            + " & ".join(map(str, values))
            + f" & {case['unresolved_parameter_length_sum']:.3e}"
            + r" \\"
        )
    (output / "ambiguity_rows.tex").write_text("\n".join(rows) + "\n", encoding="utf-8")
    rows = []
    for case in validation["cases"]:
        loss = case["sampling"]
        rows.append(
            case["case"].replace("_", r"\_")
            + f" & {loss[0]['eligible_excursions']} & "
            + " & ".join(str(s["missed_count"]) for s in loss)
            + r" \\"
        )
    (output / "sampling_rows.tex").write_text("\n".join(rows) + "\n", encoding="utf-8")

    def case_for(t, q=121):
        return next(
            c
            for c in cases
            if c["case"] == "r2_oblique" and c["query_count"] == t and c["q_count"] == q
        )

    def total(case, method):
        return case["methods"][method]["total_seconds"]["median"]

    original, example = metrics["original"], examples["example_b"]
    numbers = {
        "TotalEligible": str(metrics["eligible_excursions"]),
        "ClearOriginal": str(original["resolved_interval_midpoints"]),
        "MissNine": str(original["missed_excursions_by_frames"]["9"]),
        "MissThirtyThree": str(original["missed_excursions_by_frames"]["33"]),
        "MissOneTwentyNine": str(original["missed_excursions_by_frames"]["129"]),
        "EventLower": f"{example['lower']:.5f}",
        "EventUpper": f"{example['upper']:.5f}",
        "EventMid": f"{example['parameters'][1]:.5f}",
        "TransientCount": str(example["states"][1]["N"]),
    }

    def scientific(value):
        mantissa, exponent = f"{value:.2e}".split("e")
        return mantissa + r"\times10^{" + str(int(exponent)) + "}"

    numbers["AmplitudeError"] = scientific(metrics["expanded_amplitude_error"])
    numbers["IntensityError"] = scientific(metrics["expanded_intensity_error"])
    with np.load(data / "examples/example_b_arrays.npz", allow_pickle=False) as arrays:
        for name, t in zip(("Before", "Inside", "After"), arrays["times"], strict=True):
            i = np.argmin(abs(arrays["trace_times"] - t))
            numbers["Intensity" + name] = f"{arrays['intensity'][i, 0]:.5f}"
    for prefix, key in (
        ("CacheRatio", "B0_B1_33"),
        ("ReplayRef", "B1_B3_33"),
        ("ReplayHalf", "B2_B3_33"),
        ("LongRef", "B1_B3_129"),
        ("LongHalf", "B2_B3_129"),
    ):
        for suffix, value in zip(("Low", "High"), metrics[key], strict=True):
            numbers[prefix + suffix] = f"{value:.2f}"
    for name, method in zip(("Zero", "One", "Two", "Three"), ("B0", "B1", "B2", "B3"), strict=True):
        numbers["Timing" + name] = f"{total(case_for(33), method) * 1000:.2f}"
    numbers["ShortDirect"] = f"{total(case_for(2), 'B0') * 1000:.2f}"
    numbers["ShortReplay"] = f"{total(case_for(2), 'B3') * 1000:.2f}"
    boundary_ratio = total(metrics["boundary_benchmark"], "B1") / total(
        metrics["boundary_benchmark"], "B3"
    )
    numbers["BoundaryRatio"] = f"{boundary_ratio:.2f}"
    for name, q in (("One", 1089), ("Two", 4225)):
        numbers["Grid" + name + "Ratio"] = (
            f"{total(case_for(33, q), 'B1') / total(case_for(33, q), 'B3'):.2f}"
        )
    for name, n in (("Two", 571), ("Four", 2131), ("Six", 4871)):
        numbers["Memory" + name] = f"{n * 121 * 16 / 1e6:.2f}"
    (output / "verified_numbers.tex").write_text(
        "% Generated from verified numerical evidence.\n"
        + "\n".join(rf"\newcommand{{\{name}}}{{{value}}}" for name, value in numbers.items())
        + "\n",
        encoding="utf-8",
    )
    (output / "numerical_data_check.json").write_text(
        json.dumps(
            {
                "gate": "PASS",
                "retained_timing_reanalysis": audits,
                "numbers": numbers,
                "timings_newly_measured": False,
            },
            indent=2,
        ),
        encoding="utf-8",
    )
    return metrics
