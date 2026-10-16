import json
from pathlib import Path

import numpy as np
from jsonschema import Draft202012Validator

from qc5d.authoritative import ScientificData, load_authoritative, validate_scientific
from qc5d.checksums import sha256_file
from qc5d.data_schema import ScientificEnvelope, schema_registry
from qc5d.golden import FIXTURE_RELATIVE, fixture_payloads, verify_fixture
from qc5d.reference import canonical_json
from qc5d.runtime import parse_runtime
from qc5d.safe_npz import read_npz

ROOT = Path(__file__).resolve().parents[1]
GOLDEN_MANIFEST_SHA256 = "3c25572e8dc09cc5f06a6c3c19c32029fae32c8b0a5d09d6396aa2a5bfe86256"


def test_golden_manifest_is_deliberately_pinned_and_payloads_valid():
    directory = ROOT / FIXTURE_RELATIVE
    assert sha256_file(directory / "sha256_manifest.json") == GOLDEN_MANIFEST_SHA256
    result = verify_fixture(directory)
    assert result["passed"] and result["file_count"] == 10
    Draft202012Validator(schema_registry()["fixture_manifest"]).validate(
        json.loads((directory / "sha256_manifest.json").read_bytes())
    )


def test_golden_arrays_ids_phasons_graph_complex_diffraction_and_binary():
    directory = ROOT / FIXTURE_RELATIVE
    data, result = load_authoritative(directory)
    assert result["candidate_count"] == 31 and result["phason_frame_count"] == 4
    assert (
        data.metadata.phason_frames[0].selected_ids == data.metadata.phason_frames[-1].selected_ids
    )
    parsed = parse_runtime(directory / "canonical.qc5d")
    np.testing.assert_array_equal(parsed.ids, data.arrays["points/stable_id"])
    assert parsed.header["authoritative_sha256"] == result["authoritative_sha256"]
    expected = json.loads((directory / "expected_phasons.json").read_bytes())
    assert expected["frames"] == [
        frame.model_dump(mode="json") for frame in data.metadata.phason_frames
    ]
    graph = json.loads((directory / "graph.json").read_bytes())
    assert graph["endpoints_candidate_indices"] == data.arrays["graph/endpoints_uint32"].tolist()
    sample = json.loads((directory / "diffraction.json").read_bytes())
    complex_values = np.array(sample["F_real"]) + 1j * np.array(sample["F_imag"])
    np.testing.assert_array_equal(complex_values, data.arrays["diffraction/F_complex128"])
    assert data.arrays["diffraction/F_complex128"].dtype == np.complex128


def test_frozen_fixture_regeneration_matches_scientific_types_without_writing():
    # Historical Windows payload bytes stay SHA-locked above. A fresh scientific
    # calculation has fresh provenance, and libm roundoff is not an ID change.
    directory = ROOT / FIXTURE_RELATIVE
    frozen, _ = load_authoritative(directory)
    generated = fixture_payloads(ROOT / "configs/canonical_small_fixture_v1.json", ROOT)
    arrays = read_npz(generated["authoritative.npz"])
    embedded = arrays.pop("metadata/json_utf8")
    metadata = ScientificEnvelope.model_validate_json(generated["metadata.json"]).metadata
    assert embedded.tobytes() == canonical_json(metadata.model_dump(mode="json"))
    assert validate_scientific(ScientificData(arrays, metadata))["passed"]
    assert set(arrays) == set(frozen.arrays)
    for name, current in arrays.items():
        old = frozen.arrays[name]
        assert current.shape == old.shape and current.dtype == old.dtype
        if current.dtype.kind in "fc":
            scale = (
                max(1, metadata.state.selected_count)
                if name.endswith("F_complex128")
                else metadata.diffraction.central_intensity
                if name.endswith("intensity_raw")
                else 1.0
            )
            np.testing.assert_allclose(current / scale, old / scale, rtol=0, atol=1e-12)
        else:
            np.testing.assert_array_equal(current, old)
    assert metadata.model == frozen.metadata.model
    assert metadata.candidates.count == frozen.metadata.candidates.count
    assert metadata.candidates.order == frozen.metadata.candidates.order
    assert metadata.state.selected_count == frozen.metadata.state.selected_count
    assert metadata.state.boundary_count == frozen.metadata.state.boundary_count
    assert metadata.phason_frames == frozen.metadata.phason_frames
    assert metadata.graph.edge_count == frozen.metadata.graph.edge_count
    for name in ("expected_phasons.json", "graph.json", "config_input.json"):
        assert generated[name] == (directory / name).read_bytes()
    for name in ("schema_version", "q_count", "weighting", "normalization", "sign", "sampling"):
        assert getattr(metadata.diffraction, name) == getattr(frozen.metadata.diffraction, name)
    assert (
        metadata.diffraction.selected_ids_sha256 == frozen.metadata.diffraction.selected_ids_sha256
    )
    assert metadata.diffraction.central_intensity == frozen.metadata.diffraction.central_intensity
