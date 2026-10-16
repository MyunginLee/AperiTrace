import cmath
import math
from itertools import product

import numpy as np
import pytest

from qc5d.diffraction import (
    angular_metrics,
    intensity,
    normalize_intensity,
    ordered_peaks,
    peak_comparison,
    structure_factor,
    working_block_shape,
)
from qc5d.diffraction_config import DiffractionConfig
from qc5d.science import analytic_fixtures


def test_analytic_periodic_and_cubic_cases():
    fixtures, arrays = analytic_fixtures(DiffractionConfig())
    assert fixtures["passed"]
    assert fixtures["cases"]["periodic_1d"]["normalized_max_abs_error"] < 1e-12
    assert fixtures["cases"]["finite_cubic"]["normalized_max_abs_error"] < 1e-12
    assert arrays["periodic_1d/F_complex128"].dtype == np.complex128
    for case in fixtures["cases"].values():
        assert all(case["checks"].values())
        assert case["friedel_max_abs_normalized"] <= 1e-12
        assert case["central_intensity"] == case["point_count"] ** 2
    points = np.column_stack((np.arange(7), np.zeros((7, 2))))
    qx = np.array([0.23, 0.81, 1.77, 3.12])
    q = np.column_stack((qx, np.zeros((len(qx), 2))))
    expected = np.exp(3j * qx) * np.sin(7 * qx / 2) / np.sin(qx / 2)
    np.testing.assert_allclose(structure_factor(points, q), expected, atol=1e-12, rtol=0)


def test_complex_amplitudes_against_independent_scalar_oracle():
    rng = np.random.default_rng(13)
    points, q, weights = rng.normal(size=(41, 3)), rng.normal(size=(19, 3)), rng.normal(size=41)
    expected = []
    for wave in q:
        terms = [
            float(w)
            * cmath.exp(
                1j * math.fsum(float(a) * float(b) for a, b in zip(wave, point, strict=True))
            )
            for point, w in zip(points, weights, strict=True)
        ]
        expected.append(complex(math.fsum(v.real for v in terms), math.fsum(v.imag for v in terms)))
    actual = structure_factor(points, q, weights)
    assert actual.dtype == np.complex128 and np.any(np.abs(actual.imag) > 0.1)
    np.testing.assert_allclose(actual, expected, atol=1e-12, rtol=0)
    np.testing.assert_allclose(
        structure_factor(points, -q, weights), actual.conj(), atol=1e-12, rtol=0
    )
    np.testing.assert_array_equal(
        intensity(actual), intensity(structure_factor(points, -q, weights))
    )
    assert np.all(intensity(actual) >= 0)


@pytest.mark.parametrize("q_chunk,point_chunk", list(product((1, 17, 4096), (1, 7, 4096))))
def test_chunk_invariance(q_chunk, point_chunk):
    rng = np.random.default_rng(6)
    points, q, weights = rng.normal(size=(73, 3)), rng.normal(size=(35, 3)), rng.uniform(0.1, 2, 73)
    expected = structure_factor(points, q, weights)
    actual = structure_factor(points, q, weights, q_chunk, point_chunk_size=point_chunk)
    np.testing.assert_array_equal(actual, expected)


@pytest.mark.parametrize("field", ["positions", "q", "weights"])
def test_no_silent_complex_to_real_truncation(field):
    inputs = {"positions": np.zeros((2, 3)), "q": np.zeros((2, 3)), "weights": np.ones(2)}
    inputs[field] = inputs[field].astype(complex)
    with pytest.raises(ValueError, match="complex"):
        structure_factor(inputs["positions"], inputs["q"], inputs["weights"])


@pytest.mark.parametrize(
    "points,q,weights,chunk",
    [
        (np.zeros((2, 3)), np.zeros((1, 3)), np.ones(3), 2),
        (np.zeros((2, 2)), np.zeros((1, 3)), None, 2),
        (np.zeros((2, 3)), [0, 0, 0], None, 2),
        ([[float("nan"), 0, 0]], [[0, 0, 0]], None, 2),
        ([[1e308, 0, 0]], [[1e308, 0, 0]], None, 2),
        ([[1e7, 0, 0]], [[1e7, 0, 0]], None, 2),
        ([[0, 0, 0]], [[0, 0, 0]], [1e160], 2),
        ([[0, 0, 0]], [[0, 0, 0]], None, 0),
        ([[0, 0, 0]], [[0, 0, 0]], None, True),
    ],
)
def test_invalid_or_overflowing_inputs_rejected(points, q, weights, chunk):
    with pytest.raises(ValueError):
        structure_factor(points, q, weights, chunk)


def test_normalization_and_undefined_central_peak():
    points = np.zeros((4, 3))
    weights = np.array([1.0, 2.0, 3.0, 4.0])
    central = intensity(structure_factor(points, np.zeros((1, 3)), weights))[0]
    assert central == 100
    np.testing.assert_array_equal(normalize_intensity([100, 25], central), [1, 0.25])
    for values, i0 in (([1], 0), ([1], -1), ([-1], 1), ([1], float("nan")), ([1j], 1)):
        with pytest.raises(ValueError):
            normalize_intensity(values, i0)
    with pytest.raises(ValueError):
        intensity([complex(1e308, 1e308)])
    assert structure_factor(np.empty((0, 3)), np.zeros((2, 3))).tolist() == [0j, 0j]
    assert structure_factor(points, np.empty((0, 3))).shape == (0,)
    with pytest.raises(ValueError):
        structure_factor(points, np.zeros((1, 3))).setflags(write=True)
    q_rows, point_rows = working_block_shape(10000, 10000, 4096, 4096)
    assert q_rows * point_rows <= 1000000


def test_angular_metric_detects_symmetry_breaking_and_undefined_signal():
    cfg = DiffractionConfig(polar_radial_samples=3, polar_angular_samples=40)
    radii = np.array([0, 4, 12])
    theta = np.arange(40) * 2 * np.pi / 40
    symmetric = np.tile(0.2 + 0.1 * np.cos(10 * theta), (3, 1))
    metrics = angular_metrics(symmetric, radii, cfg)
    assert metrics["passed"] and metrics["maximum_nrmse"] < 1e-12
    asymmetric = np.tile(0.2 + 0.1 * np.cos(theta), (3, 1))
    assert not angular_metrics(asymmetric, radii, cfg)["passed"]
    missing = angular_metrics(np.zeros((3, 40)), radii, cfg)
    assert not missing["valid"] and missing["maximum_nrmse"] is None
    constant = angular_metrics(np.ones((3, 40)), radii, cfg)
    assert not constant["valid"] and constant["minimum_correlation"] is None


def test_peak_order_and_comparison_are_deterministic():
    cfg = DiffractionConfig(top_k=3)
    q = np.array([[1.0, 0, 0], [-1.0, 0, 0], [2.0, 0, 0], [0, 0, 0]])
    values = np.array([0.2 + 1e-14, 0.2, 0.1, 1.0])
    peaks = ordered_peaks(q, values, cfg)
    assert [row["q_xyz"][0] for row in peaks] == [-1, 1, 2]
    assert peaks == ordered_peaks(q, values, cfg)
    assert peak_comparison(peaks, peaks)["nearest_peak_position_rmse"] == 0
    assert peak_comparison(peaks, peaks)["matched_intensity_rmse"] == 0
    assert not peak_comparison([], peaks)["valid"]
