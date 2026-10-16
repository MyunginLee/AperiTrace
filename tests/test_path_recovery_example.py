import json

import numpy as np
import pytest

from qc5d.exploration import recompute_state
from qc5d.recover_interval import build_example, main


def test_changed_path_discovers_and_recomputes_a_previously_visible_identity():
    summary, payloads = build_example()
    assert summary["outcome"] == "RECOVERED_CLEAR_FLOAT64_INTERVAL"
    assert summary["focus"] == [-1, 1, 0, 0, -2]
    assert len(summary["missed_ids"]) == 2
    assert summary["focus_selected_at_baseline_samples"] == 7
    assert summary["focus_selected_at_changed_samples"] == 0
    assert summary["new_interval_object"] and summary["same_phase_cache_object"]
    assert [state["focus_selected"] for state in summary["states"]] == [False, True, False]
    assert all(state["status"] == "clear_float64" for state in summary["states"])
    assert np.max(np.abs(summary["inside_minus_before_intensity"])) > 1e-6
    for payload in payloads.values():
        result = recompute_state(payload)
        assert result["N"] == payload["N"]
        assert result["count_normalized_amplitude_error"] <= 1e-12
        assert result["absolute_normalized_intensity_error"] <= 1e-12


def test_stationary_path_does_not_invent_a_positive_outcome():
    summary, payloads = build_example((0, 0), (0, 0))
    assert summary["outcome"] == "NO_CLEAR_MISSED_INTERVAL"
    assert summary["missed_ids"] == [] and payloads == {}


@pytest.mark.parametrize("samples", [True, 1, 4098])
def test_sample_budget_is_validated(samples):
    with pytest.raises(ValueError):
        build_example(samples=samples)


def test_cli_exports_are_recomputable_and_never_overwritten(tmp_path):
    output = tmp_path / "changed-path"
    main(["--output", str(output)])
    for name in ("before", "inside", "after"):
        payload = json.loads((output / (name + ".json")).read_text())
        assert recompute_state(payload)["N"] == payload["N"]
    with pytest.raises(FileExistsError):
        main(["--output", str(output)])


def test_custom_no_miss_path_is_reported_as_such(tmp_path):
    output = tmp_path / "stationary"
    main(["--output", str(output), "--start", "0", "0", "--end", "0", "0"])
    assert (
        json.loads((output / "summary.json").read_text())["outcome"] == "NO_CLEAR_MISSED_INTERVAL"
    )
    assert not (output / "inside.json").exists()
