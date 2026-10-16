import copy
import json
from dataclasses import FrozenInstanceError

import numpy as np
import pytest

from qc5d.basis import SuperspaceBasis


def test_equation_generated_shapes_dtypes_and_invariants():
    basis = SuperspaceBasis()
    for name, shape in (("P_parallel", (3, 5)), ("P_perp", (2, 5)), ("Q", (5, 5))):
        array = getattr(basis, name)
        assert array.shape == shape
        assert array.dtype == np.float64
    assert np.max(np.abs(basis.Q @ basis.Q.T - np.eye(5))) < 1e-12
    assert np.max(np.abs(basis.P_parallel @ basis.P_perp.T)) < 1e-12
    assert (
        np.max(
            np.abs(
                basis.P_parallel.T @ basis.P_parallel + basis.P_perp.T @ basis.P_perp - np.eye(5)
            )
        )
        < 1e-12
    )
    np.testing.assert_allclose(
        basis.P_parallel[:, 0], [np.sqrt(2 / 5), 0, 1 / np.sqrt(5)], atol=1e-15
    )


@pytest.mark.parametrize("name", ["P_parallel", "P_perp", "Q"])
def test_basis_is_immutable_including_numpy_write_flag(name):
    basis = SuperspaceBasis()
    with pytest.raises(FrozenInstanceError):
        setattr(basis, name, np.zeros_like(getattr(basis, name)))
    with pytest.raises(ValueError):
        getattr(basis, name).setflags(write=True)
    with pytest.raises(ValueError):
        getattr(basis, name).flat[0] = 0


def test_seeded_integer_reconstruction_and_periodic_axis():
    basis = SuperspaceBasis()
    integers = np.random.default_rng(1701).integers(-8, 9, size=(4096, 5))
    physical, internal = basis.project(integers)
    reconstructed = basis.reconstruct(physical, internal)
    assert reconstructed.dtype == np.float64
    assert np.max(np.abs(reconstructed - integers)) < 1e-12
    np.testing.assert_allclose(physical[:, 2], integers.sum(axis=1) / np.sqrt(5), atol=1e-14)
    r, u = basis.project(np.ones(5))
    np.testing.assert_allclose(r, [0, 0, np.sqrt(5)], atol=1e-14)
    np.testing.assert_allclose(u, 0, atol=1e-14)
    assert basis.reconstruct(r, u).shape == (5,)


def test_tensor_projection_and_empty_batch():
    basis = SuperspaceBasis()
    vectors = np.arange(30).reshape(2, 3, 5)
    r, u = basis.project(vectors)
    assert r.shape == (2, 3, 3)
    assert u.shape == (2, 3, 2)
    np.testing.assert_allclose(basis.reconstruct(r, u), vectors, atol=1e-12, rtol=0)
    assert basis.project(np.empty((0, 5)))[0].shape == (0, 3)


@pytest.mark.parametrize("value", [[1, 2], 1, [0, 0, 0, 0, np.nan], [0, 0, 0, 0, np.inf]])
def test_invalid_projection_input_is_rejected(value):
    with pytest.raises(ValueError):
        SuperspaceBasis().project(value)


def test_reconstruction_requires_matching_batch_shapes():
    with pytest.raises(ValueError, match="batch shapes"):
        SuperspaceBasis().reconstruct(np.zeros((2, 3)), np.zeros(2))


def test_basis_serialization_round_trip_rejects_corruption():
    basis = SuperspaceBasis()
    restored = SuperspaceBasis.from_dict(json.loads(json.dumps(basis.to_dict())))
    for name in ("Q", "P_parallel", "P_perp"):
        np.testing.assert_array_equal(getattr(restored, name), getattr(basis, name))
    broken = copy.deepcopy(basis.to_dict())
    broken["P_perp"][0][0] += 0.01
    with pytest.raises(ValueError, match="canonical equations"):
        SuperspaceBasis.from_dict(broken)
    broken["schema_version"] = "2.0"
    with pytest.raises(ValueError, match="schema"):
        SuperspaceBasis.from_dict(broken)
