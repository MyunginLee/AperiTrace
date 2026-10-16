"""Linked JSON uses the documented typed portability policy, not bitwise floats."""

import copy

import numpy as np
import pytest

from qc5d.exploration import Exploration, bundled_example, recompute_state


@pytest.fixture(scope="module")
def state():
    app = bundled_example()
    path = app.path((-0.61, 0.43), (0.67, -0.31))
    return app.export_state(path, 0.075, (0, -1, 1, 0, 1))


@pytest.mark.parametrize("field", ["physical", "internal", "window", "phi"])
@pytest.mark.parametrize("delta,accepted", [(2e-15, True), (2e-10, False), (np.nan, False)])
def test_linked_float_policy(state, field, delta, accepted):
    payload = copy.deepcopy(state)
    if field == "phi":
        payload[field][0] += delta
    else:
        payload[field][0][0] += delta
    if accepted:
        assert recompute_state(payload)["N"] == state["N"]
    else:
        with pytest.raises(ValueError):
            recompute_state(payload)


@pytest.mark.parametrize("field", ["physical", "internal", "window", "phi"])
def test_linked_shapes_cannot_broadcast(state, field):
    payload = copy.deepcopy(state)
    payload[field] = payload[field][:1]
    with pytest.raises(ValueError):
        recompute_state(payload)


@pytest.mark.parametrize("relative,accepted", [(np.finfo(float).eps, True), (1e-9, False)])
def test_linked_epsilon_roundoff_only(state, relative, accepted):
    payload = copy.deepcopy(state)
    payload["epsilon"] *= 1 + relative
    if accepted:
        assert recompute_state(payload)["N"] == state["N"]
    else:
        with pytest.raises(ValueError, match="convention"):
            recompute_state(payload)


@pytest.mark.parametrize("field", ["selected_ids", "status", "N"])
def test_linked_discrete_fields_remain_exact(state, field):
    payload = copy.deepcopy(state)
    payload[field] = (
        [] if field == "selected_ids" else "changed" if field == "status" else state["N"] + 1
    )
    with pytest.raises(ValueError):
        recompute_state(payload)


@pytest.mark.parametrize("bad", [True, None, "1e-10", [1e-10]])
def test_linked_epsilon_is_a_numeric_scalar(state, bad):
    payload = copy.deepcopy(state)
    payload["epsilon"] = bad
    with pytest.raises(ValueError, match="convention"):
        recompute_state(payload)


@pytest.mark.parametrize("field", ["F_real", "F_imag", "I_normalized"])
@pytest.mark.parametrize("bad", ["one_row", "scalar", "boolean", "nonfinite"])
def test_diffraction_arrays_have_independent_typed_shapes(state, field, bad):
    payload = copy.deepcopy(state)
    payload[field] = {
        "one_row": payload[field][:1],
        "scalar": payload[field][0],
        "boolean": [False] * len(payload[field]),
        "nonfinite": [np.inf] * len(payload[field]),
    }[bad]
    with pytest.raises(ValueError):
        recompute_state(payload)


def test_identical_q_does_not_allow_component_broadcasting():
    app = bundled_example()
    same_q = Exploration(app.candidates, [[1.6, 0, 0]] * 3)
    path = same_q.path((-0.61, 0.43), (0.67, -0.31))
    payload = same_q.export_state(path, 0.075, (0, -1, 1, 0, 1))
    payload["F_real"] = payload["F_real"][:1]
    with pytest.raises(ValueError, match="amplitude"):
        recompute_state(payload)


@pytest.mark.parametrize("bad", [True, 126.0, "126", None])
def test_count_requires_a_json_integer(state, bad):
    payload = copy.deepcopy(state)
    payload["N"] = bad
    with pytest.raises(ValueError, match="N disagree"):
        recompute_state(payload)


@pytest.mark.parametrize(
    "bad", [[False, -1, 1, 0, 1], [0.0, -1, 1, 0, 1], [0, -1, 1], [0, -1, 1, 0, 1, 0]]
)
def test_focus_requires_five_actual_integers(state, bad):
    payload = copy.deepcopy(state)
    payload["focus"] = bad
    with pytest.raises(ValueError, match="five-integer"):
        recompute_state(payload)


def test_empty_q_linked_export_round_trips():
    original = bundled_example()
    app = Exploration(original.candidates, np.empty((0, 3)))
    path = app.path((-0.61, 0.43), (0.67, -0.31))
    payload = app.export_state(path, 0.075, (0, -1, 1, 0, 1))
    assert payload["q"] == payload["F_real"] == payload["F_imag"] == payload["I_normalized"] == []
    assert recompute_state(payload)["N"] == payload["N"]


def test_boolean_inside_integer_tuple_cannot_coerce_to_zero(state):
    payload = copy.deepcopy(state)
    row, column = np.argwhere(np.asarray(payload["candidate_tuples"]) == 0)[0]
    payload["candidate_tuples"][row][column] = False
    with pytest.raises(ValueError, match="integer identity"):
        recompute_state(payload)


@pytest.mark.parametrize("field", ["F_imag", "internal", "start", "q"])
def test_mixed_boolean_numeric_array_is_not_silently_coerced(state, field):
    payload = copy.deepcopy(state)
    array = np.asarray(payload[field])
    # All four fields have zero entries in the default export except the start;
    # use an otherwise valid stationary path for that case.
    if field == "start":
        app = bundled_example()
        path = app.path((0, 0), (0, 0))
        payload = app.export_state(path, 0, (0, -1, 1, 0, 1))
        payload["start"][0] = False
    else:
        position = tuple(np.argwhere(array == 0)[0])
        if len(position) == 1:
            payload[field][position[0]] = False
        else:
            payload[field][position[0]][position[1]] = False
    with pytest.raises(ValueError):
        recompute_state(payload)


@pytest.mark.parametrize("field", ["F_real", "F_imag", "I_normalized"])
@pytest.mark.parametrize("delta,accepted", [(2e-15, True), (2e-9, False)])
def test_normalized_output_budget_is_not_bitwise(state, field, delta, accepted):
    payload = copy.deepcopy(state)
    payload[field][0] += delta * (state["N"] if field.startswith("F_") else 1)
    if accepted:
        assert recompute_state(payload)["N"] == state["N"]
    else:
        with pytest.raises(ValueError):
            recompute_state(payload)
