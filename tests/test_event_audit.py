"""New experimental phason audit checks; canonical policies are never modified."""

import importlib.util
import sys
from pathlib import Path

import numpy as np
import pytest

from qc5d.diffraction import structure_factor
from qc5d.model_config import ModelConfig, PhasonDomain, PhysicalCrop
from qc5d.modelset import evaluate_membership, generate_candidates
from qc5d.window import AcceptanceWindow

PATH = Path(__file__).resolve().parents[1] / "experiments/event_audit/model.py"
SPEC = importlib.util.spec_from_file_location("event_audit_model", PATH)
MODEL = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MODEL
SPEC.loader.exec_module(MODEL)
SQUARE = np.array([[-1.0, -1.0], [1.0, -1.0], [1.0, 1.0], [-1.0, 1.0]])


@pytest.mark.parametrize("epsilon", [0.0, 1e-10, 0.1])
def test_open_interval_has_known_square_solution(epsilon):
    interval = MODEL.clip_intervals(SQUARE, [[0.0, 0.0]], [-2.0, 0.0], [2.0, 0.0], epsilon)
    assert interval.lower[0] == pytest.approx((1 + epsilon) / 4, abs=1e-15)
    assert interval.upper[0] == pytest.approx((3 - epsilon) / 4, abs=1e-15)
    assert not interval.selected(interval.lower[0])[0]
    assert not interval.selected(interval.upper[0])[0]
    assert interval.selected(0.5)[0]


@pytest.mark.parametrize(
    "position,wanted", [([0.0, 0.0], True), ([1.0, 0.0], False), ([2.0, 0.0], False)]
)
def test_stationary_and_parallel_boundary(position, wanted):
    interval = MODEL.clip_intervals(SQUARE, [position], [0.0, 0.0], [0.0, 0.0], 1e-10)
    assert bool(interval.selected(0.5)[0]) == wanted


def test_vertex_tangency_has_no_selected_excursion():
    interval = MODEL.clip_intervals(SQUARE, [[0.0, 0.0]], [0.0, 2.0], [2.0, 0.0], 0.0)
    assert not interval.ever_selected[0]


def test_known_excursion_missed_by_three_frames_is_retained():
    interval = MODEL.clip_intervals(SQUARE, [[0.0, 0.0]], [-0.9, 2.8], [1.1, 0.8], 0.0)
    assert interval.lower[0] == pytest.approx(0.9)
    losses = MODEL.sampling_losses(interval, 3)
    assert losses["missed_excursions"] == [0]
    assert losses["interior_event_count"] == 2
    assert losses["frame_change_count"] == 0


def test_event_replay_handles_both_events_between_frames_and_exact_roots():
    interval = MODEL.Intervals(np.array([0.2, 0.5]), np.array([0.3, 0.8]), np.array([True, True]))
    cache = np.array([[1 + 2j, 3 - 1j], [4 - 1j, -2 + 7j]])
    times = np.array([0.0, 0.2, 0.25, 0.3, 0.5, 0.6, 0.8, 1.0])
    amplitudes, counts, _ = MODEL.event_amplitudes(interval, cache, times)
    for index, time in enumerate(times):
        assert counts[index] == np.count_nonzero(interval.selected(time))
        np.testing.assert_allclose(
            amplitudes[index], cache[interval.selected(time)].sum(axis=0), atol=1e-14
        )
    _, counts, updates = MODEL.event_amplitudes(interval, cache, [0.0, 1.0])
    assert counts.tolist() == [0, 0] and updates.tolist() == [0, 4]


@pytest.mark.parametrize(
    "start,end",
    [
        ([-0.7, 0.133], [0.7, 0.133]),
        ([-0.63, -0.57], [0.61, 0.59]),
        ([-0.61, 0.43], [0.67, -0.31]),
        ([-0.12, -0.071], [0.13, 0.079]),
    ],
)
def test_canonical_midpoints_decimal_reverse_and_finite_diffraction(start, end):
    candidates = generate_candidates(
        ModelConfig(
            index_bound=3,
            physical_crop=PhysicalCrop(radial_max=2.0, z_abs_max=1.5),
            phason_domain=PhasonDomain(min=(-0.75, -0.75), max=(0.75, 0.75)),
        )
    )
    window = AcceptanceWindow()
    intervals = MODEL.clip_intervals(
        window.vertices, candidates.internal, start, end, window.boundary_epsilon
    )
    decimal = MODEL.decimal_intervals(
        window.vertices, candidates.internal, start, end, window.boundary_epsilon
    )
    reverse = MODEL.clip_intervals(
        window.vertices, candidates.internal, end, start, window.boundary_epsilon
    )
    active = intervals.ever_selected
    np.testing.assert_allclose(intervals.lower[active], decimal.lower[active], rtol=0, atol=1e-12)
    np.testing.assert_allclose(intervals.upper[active], decimal.upper[active], rtol=0, atol=1e-12)
    np.testing.assert_allclose(
        intervals.lower[active], 1 - reverse.upper[active], rtol=0, atol=1e-12
    )
    roots = intervals.critical_parameters()
    times = (roots[:-1] + roots[1:]) / 2
    q = np.array([[0.0, 0.0, 0.0], [1.37, -2.1, 0.0], [-1.37, 2.1, 0.0]])
    amplitudes, counts, _ = MODEL.event_amplitudes(
        intervals, MODEL.phase_cache(candidates.physical, q), times
    )
    for index, time in enumerate(times):
        authority = evaluate_membership(
            candidates, np.array(start) + time * (np.array(end) - start)
        )
        guard = (
            64
            * np.finfo(float).eps
            * (
                1
                + window.characteristic_radius
                + np.max(np.linalg.norm(candidates.internal + authority.phi, axis=1))
            )
        )
        if np.min(np.abs(authority.signed_distance - window.boundary_epsilon)) <= guard:
            continue
        assert np.array_equal(intervals.selected(time), authority.selected)
        assert counts[index] == len(authority.selected_ids)
        np.testing.assert_allclose(
            amplitudes[index] / counts[index],
            structure_factor(authority.selected_physical, q) / counts[index],
            rtol=0,
            atol=1e-12,
        )


@pytest.mark.parametrize("time", [-1.0, 1.1, np.nan, np.inf])
def test_invalid_parameter_refused(time):
    intervals = MODEL.clip_intervals(SQUARE, [[0.0, 0.0]], [0.0, 0.0], [0.0, 0.0], 0.0)
    with pytest.raises(ValueError):
        intervals.selected(time)


@pytest.mark.parametrize("epsilon", [-1.0, np.nan, np.inf])
def test_invalid_epsilon_refused(epsilon):
    with pytest.raises(ValueError):
        MODEL.clip_intervals(SQUARE, [[0.0, 0.0]], [0.0, 0.0], [0.0, 0.0], epsilon)


@pytest.mark.parametrize("times", [[], [0.4, 0.3], [-1.0, 0.0], [0.0, 2.0], [np.nan]])
def test_invalid_replay_order_refused(times):
    intervals = MODEL.clip_intervals(SQUARE, [[0.0, 0.0]], [0.0, 0.0], [0.0, 0.0], 0.0)
    with pytest.raises(ValueError):
        MODEL.event_amplitudes(intervals, [[1.0 + 0j]], times)


def test_phase_cache_matches_canonical_phase_convention():
    positions = np.array([[0.1, 0.2, 0.3], [-0.5, 2.0, -1.0], [4.0, -3.0, 0.5]])
    q = np.array([[0.0, 0.0, 0.0], [2.0, -3.0, 4.0]])
    np.testing.assert_allclose(
        MODEL.phase_cache(positions, q).sum(axis=0),
        structure_factor(positions, q),
        rtol=0,
        atol=1e-14,
    )


def test_nonconvex_polygon_and_wrong_shapes_refused():
    with pytest.raises(ValueError):
        MODEL.clip_intervals(
            [[0, 0], [2, 0], [1, 0.2], [2, 2], [0, 2]], [[0, 0]], [0, 0], [1, 1], 0.0
        )
    with pytest.raises(ValueError):
        MODEL.clip_intervals(SQUARE, [0, 0], [0, 0], [1, 1], 0.0)
