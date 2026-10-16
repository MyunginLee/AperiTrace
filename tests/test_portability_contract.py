"""Roundoff acceptance is separate from exact identity and archive integrity."""

import numpy as np
import pytest

from qc5d.authoritative import ScientificData, scientific_data, validate_scientific
from qc5d.basis import SuperspaceBasis
from qc5d.checksums import sha256_bytes
from qc5d.data_schema import BasisDefinition, ScientificEnvelope, WindowDefinition
from qc5d.model_config import ModelConfig
from qc5d.modelset import generate_candidates
from qc5d.window import AcceptanceWindow


@pytest.fixture
def sample():
    candidates = generate_candidates(
        ModelConfig(index_bound=1, physical_crop={"radial_max": 0.9, "z_abs_max": 0.9})
    )
    return scientific_data(candidates, q=np.array([[0.0, 0.0, 0.0], [1.0, 2.0, 0.0]]))


def modified(sample, key, amount, *, rehash=True):
    arrays = {name: value.copy() for name, value in sample.arrays.items()}
    arrays[key].flat[0] += amount
    payload = sample.metadata.model_dump(mode="json")
    if rehash:
        for part in ("candidates", "state", "graph", "diffraction"):
            if payload[part] is not None:
                for record in payload[part]["datasets"].values():
                    if record["path"] == key:
                        record["sha256"] = sha256_bytes(arrays[key].tobytes())
    metadata = ScientificEnvelope.model_validate(
        {"authoritative_sha256": "a" * 64, "metadata": payload}
    ).metadata
    return ScientificData(arrays, metadata)


@pytest.mark.parametrize("kind", ["parallel", "perpendicular", "window"])
@pytest.mark.parametrize("delta,accepted", [(2e-15, True), (2e-10, False), (np.nan, False)])
def test_canonical_schema_roundoff_not_arbitrary_science(kind, delta, accepted):
    basis = SuperspaceBasis()
    if kind == "window":
        payload = {"vertices_uv": AcceptanceWindow().vertices.copy()}
        payload["vertices_uv"][0, 0] += delta
        model = WindowDefinition
    else:
        payload = {"P_parallel": basis.P_parallel.copy(), "P_perp": basis.P_perp.copy()}
        payload["P_parallel" if kind == "parallel" else "P_perp"][0, 0] += delta
        model = BasisDefinition
    if accepted:
        model.model_validate(payload)
    else:
        with pytest.raises(ValueError):
            model.model_validate(payload)


@pytest.mark.parametrize("mode", ["shape", "order"])
def test_window_shape_and_canonical_order_are_not_tolerant(mode):
    vertices = AcceptanceWindow().vertices
    stored = vertices[:1] if mode == "shape" else np.roll(vertices, 1, axis=0)
    with pytest.raises(ValueError):
        WindowDefinition(vertices_uv=stored)


@pytest.mark.parametrize(
    "key",
    [
        "points/physical_xyz_float64",
        "points/internal_uv_float64",
        "points/boundary_distance",
        "diffraction/F_complex128",
        "diffraction/intensity_raw",
        "diffraction/intensity_normalized",
    ],
)
@pytest.mark.parametrize("delta,accepted", [(2e-15, True), (2e-6, False)])
def test_recomputed_floats_accept_roundoff_but_reject_changed_science(sample, key, delta, accepted):
    data = modified(sample, key, delta)
    if accepted:
        assert validate_scientific(data)["passed"]
    else:
        with pytest.raises(ValueError):
            validate_scientific(data)


def test_even_tiny_byte_change_requires_exact_descriptor_checksum(sample):
    data = modified(sample, "points/physical_xyz_float64", 2e-15, rehash=False)
    with pytest.raises(ValueError, match="checksum"):
        validate_scientific(data)


@pytest.mark.parametrize("field", ["selected_count", "boundary_count"])
def test_counts_remain_exact(sample, field):
    metadata = sample.metadata.model_copy(
        update={
            "state": sample.metadata.state.model_copy(
                update={field: getattr(sample.metadata.state, field) + 1}
            )
        }
    )
    with pytest.raises(ValueError, match="counts"):
        validate_scientific(ScientificData(sample.arrays, metadata))


@pytest.mark.parametrize("relative,accepted", [(np.finfo(float).eps, True), (1e-9, False)])
def test_scientific_epsilon_is_not_relaxed_by_coordinate_tolerance(sample, relative, accepted):
    metadata = sample.metadata.model_copy(
        update={
            "state": sample.metadata.state.model_copy(
                update={"boundary_epsilon": sample.metadata.state.boundary_epsilon * (1 + relative)}
            )
        }
    )
    data = ScientificData(sample.arrays, metadata)
    if accepted:
        assert validate_scientific(data)["passed"]
    else:
        with pytest.raises(ValueError, match="epsilon"):
            validate_scientific(data)


def test_serialized_basis_metadata_correspondence_stays_exact(sample):
    data = modified(sample, "basis/P_parallel", 2e-15)
    with pytest.raises(ValueError, match="inconsistent"):
        validate_scientific(data)


def test_central_normalization_metadata_is_exact_not_an_array_tolerance(sample):
    metadata = sample.metadata.model_copy(
        update={
            "diffraction": sample.metadata.diffraction.model_copy(
                update={
                    "central_intensity": np.nextafter(
                        sample.metadata.diffraction.central_intensity, np.inf
                    )
                }
            )
        }
    )
    with pytest.raises(ValueError, match="normalization mismatch"):
        validate_scientific(ScientificData(sample.arrays, metadata))
