import copy
import json
from itertools import product

import numpy as np
import pytest

from qc5d.geometry import convex_hull_indices, polygon_turns, signed_area, signed_boundary_distance
from qc5d.window import AcceptanceWindow, BoundaryPolicy, classify_polygon

SQUARE = np.array([[-1, -1], [1, -1], [1, 1], [-1, 1]], dtype=np.float64)


def test_all_cube_vertices_and_derived_decagonal_hull():
    window = AcceptanceWindow()
    assert window.cube_vertices.shape == (32, 5)
    assert len(set(map(tuple, window.cube_vertices))) == 32
    assert set(map(tuple, window.cube_vertices)) == set(product((-0.5, 0.5), repeat=5))
    assert window.projected_cube_vertices.dtype == np.float64
    assert window.vertices.dtype == np.float64
    assert window.hull_indices.dtype == np.int32
    assert len(window.vertices) == 10
    np.testing.assert_array_equal(
        window.vertices, window.projected_cube_vertices[window.hull_indices]
    )
    assert signed_area(window.vertices) > 0
    assert np.all(polygon_turns(window.vertices) > 0)
    assert tuple(window.vertices[0]) == min(map(tuple, window.vertices))
    assert window.diagnostics()["central_symmetry_residual"] < 1e-12
    assert window.diagnostics()["tenfold_rotation_residual"] < 1e-12
    angle = 2 * np.pi / 10
    rotation = np.array([[np.cos(angle), -np.sin(angle)], [np.sin(angle), np.cos(angle)]])
    np.testing.assert_allclose(
        window.vertices @ rotation.T, np.roll(window.vertices, -1, axis=0), atol=1e-12, rtol=0
    )
    np.testing.assert_allclose(
        window.vertices + np.roll(window.vertices, 5, axis=0), 0, atol=1e-12, rtol=0
    )
    assert not np.any(window.classify(window.projected_cube_vertices).states == "OUTSIDE")


def test_hull_order_is_deterministic_under_input_permutation():
    window = AcceptanceWindow()
    permutation = np.random.default_rng(41).permutation(32)
    shuffled = window.projected_cube_vertices[permutation]
    indices = convex_hull_indices(shuffled)
    np.testing.assert_array_equal(shuffled[indices], window.vertices)
    np.testing.assert_array_equal(AcceptanceWindow().vertices, window.vertices)


def test_signed_distance_uses_segments_including_outside_corners():
    points = np.array([[0, 0], [0.2, 0.4], [1, 0], [1, 1], [2, 2], [2, 0]])
    np.testing.assert_allclose(
        signed_boundary_distance(SQUARE, points),
        [1, 0.6, 0, 0, -np.sqrt(2), -1],
        atol=1e-14,
        rtol=0,
    )


@pytest.mark.parametrize(
    ("policy", "expected"),
    [("exclude", [True, False, False]), ("include", [True, True, False]), ("report_only", None)],
)
def test_explicit_boundary_states_and_policies(policy, expected):
    result = classify_polygon(
        SQUARE, [[0, 0], [1, 0], [2, 0]], boundary_epsilon=1e-10, boundary_policy=policy
    )
    assert result.states.tolist() == ["INSIDE", "BOUNDARY", "OUTSIDE"]
    assert result.boundary_policy == BoundaryPolicy(policy)
    if expected is None:
        assert result.selected is None
    else:
        assert result.selected.tolist() == expected
        with pytest.raises(ValueError):
            result.selected.setflags(write=True)


def test_epsilon_band_is_symmetric_about_boundary():
    epsilon = 1e-6
    result = classify_polygon(
        SQUARE,
        [[1 - epsilon / 2, 0], [1 + epsilon / 2, 0], [1 - 2 * epsilon, 0], [1 + 2 * epsilon, 0]],
        boundary_epsilon=epsilon,
    )
    assert result.states.tolist() == ["BOUNDARY", "BOUNDARY", "INSIDE", "OUTSIDE"]
    assert result.selected.tolist() == [False, False, True, False]


def test_window_vertices_and_edge_midpoints_are_boundary():
    window = AcceptanceWindow()
    midpoints = (window.vertices + np.roll(window.vertices, -1, axis=0)) / 2
    points = np.vstack((window.vertices, midpoints))
    result = window.classify(points)
    assert result.boundary_epsilon == 1e-10 * window.characteristic_radius
    assert np.all(result.states == "BOUNDARY")
    assert not np.any(result.selected)
    assert np.all(window.classify(points, boundary_policy="include").selected)
    assert window.classify([0, 0]).states.item() == "INSIDE"
    assert window.classify([3 * window.characteristic_radius, 0]).states.item() == "OUTSIDE"


def test_empty_and_tensor_classification_shapes():
    window = AcceptanceWindow()
    assert window.classify(np.empty((0, 2))).states.shape == (0,)
    assert window.classify(np.zeros((2, 3, 2))).states.shape == (2, 3)


@pytest.mark.parametrize("epsilon", [-1, np.inf, np.nan])
def test_invalid_epsilon_is_rejected(epsilon):
    with pytest.raises(ValueError, match="epsilon"):
        classify_polygon(SQUARE, [0, 0], boundary_epsilon=epsilon)


def test_invalid_polygon_and_policy_are_rejected():
    with pytest.raises(ValueError, match="counter-clockwise"):
        signed_boundary_distance(SQUARE[::-1], [0, 0])
    with pytest.raises(ValueError):
        classify_polygon(SQUARE, [0, 0], boundary_epsilon=0, boundary_policy="implicit")


def test_window_serialization_round_trip_and_corruption():
    window = AcceptanceWindow()
    restored = AcceptanceWindow.from_dict(json.loads(json.dumps(window.to_dict())))
    np.testing.assert_array_equal(restored.vertices, window.vertices)
    broken = copy.deepcopy(window.to_dict())
    broken["vertices_uv"][0][0] += 0.01
    with pytest.raises(ValueError, match="generated window"):
        AcceptanceWindow.from_dict(broken)
    with pytest.raises(ValueError):
        window.vertices.setflags(write=True)


def test_self_intersecting_star_is_not_accepted_as_convex():
    angle = np.arange(5) * 2 * np.pi / 5
    pentagon = np.column_stack((np.cos(angle), np.sin(angle)))
    star = pentagon[[0, 2, 4, 1, 3]]
    assert np.all(polygon_turns(star) > 0)
    with pytest.raises(ValueError, match="self-intersections"):
        signed_boundary_distance(star, [0, 0])
