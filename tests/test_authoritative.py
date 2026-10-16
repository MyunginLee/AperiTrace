import io
import json
import zipfile

import numpy as np
import pytest
from jsonschema import Draft202012Validator

from qc5d.authoritative import (
    ScientificData,
    authoritative_payloads,
    load_authoritative,
    scientific_data,
    validate_scientific,
)
from qc5d.checksums import sha256_bytes
from qc5d.data_schema import ScientificEnvelope, schema_registry
from qc5d.model_config import ModelConfig
from qc5d.modelset import generate_candidates
from qc5d.reference import canonical_json, deterministic_npz
from qc5d.safe_npz import read_npz


@pytest.fixture
def scientific():
    candidates = generate_candidates(
        ModelConfig(index_bound=1, physical_crop={"radial_max": 0.9, "z_abs_max": 0.9})
    )
    return scientific_data(
        candidates,
        q=np.array([[0.0, 0, 0], [1.0, 2, 0], [-1.0, -2, 0]]),
        phasons=[(0, 0), (0.181, 0.047), (-0.119, 0.047)],
    )


def write_data(directory, data):
    archive, metadata = authoritative_payloads(data)
    directory.mkdir(parents=True, exist_ok=True)
    (directory / "authoritative.npz").write_bytes(archive)
    (directory / "metadata.json").write_bytes(metadata)
    return archive, metadata


def test_lossless_authoritative_roundtrip_all_fields_and_schema(tmp_path, scientific):
    archive, metadata = write_data(tmp_path, scientific)
    Draft202012Validator(schema_registry()["scientific_dataset"]).validate(json.loads(metadata))
    restored, result = load_authoritative(tmp_path)
    assert result["passed"] and result["candidate_count"] == 31
    assert result["phason_frame_count"] == 3 and result["diffraction_validated"]
    assert restored.metadata == scientific.metadata
    for key in scientific.arrays:
        assert restored.arrays[key].dtype == scientific.arrays[key].dtype
        np.testing.assert_array_equal(restored.arrays[key], scientific.arrays[key])
        with pytest.raises(ValueError):
            restored.arrays[key].setflags(write=True)
    assert authoritative_payloads(restored) == (archive, metadata)


@pytest.mark.parametrize(
    "key,mode",
    [
        ("points/lattice_n_int32", "missing"),
        ("points/physical_xyz_float64", "shape"),
        ("points/internal_uv_float64", "dtype"),
        ("points/stable_id", "duplicate"),
        ("points/physical_xyz_float64", "value"),
        ("points/selected_bool", "value"),
        ("points/boundary_distance", "value"),
        ("graph/endpoints_uint32", "value"),
        ("diffraction/F_complex128", "value"),
        ("diffraction/intensity_raw", "nan"),
        ("metadata/json_utf8", "missing"),
    ],
)
def test_rehashed_malformed_scientific_archive_is_rejected(tmp_path, scientific, key, mode):
    archive, metadata = write_data(tmp_path, scientific)
    arrays = read_npz(archive)
    if mode == "missing":
        del arrays[key]
    elif mode == "shape":
        arrays[key] = arrays[key][:, :2]
    elif mode == "dtype":
        arrays[key] = arrays[key].astype(np.float32)
    elif mode == "duplicate":
        arrays[key][0] = arrays[key][1]
    else:
        arrays[key].flat[0] = float("nan") if mode == "nan" else arrays[key].flat[0] + 1
    modified = deterministic_npz(arrays)
    envelope = json.loads(metadata)
    envelope["authoritative_sha256"] = sha256_bytes(modified)
    (tmp_path / "authoritative.npz").write_bytes(modified)
    (tmp_path / "metadata.json").write_bytes(canonical_json(envelope))
    with pytest.raises(ValueError):
        load_authoritative(tmp_path)


def test_semantic_invariants_detect_rehashed_corrupt_coordinates_and_duplicate_tuples(scientific):
    # Bypass descriptor mismatch deliberately to exercise scientific validation itself.
    for key in (
        "points/physical_xyz_float64",
        "points/lattice_n_int32",
        "diffraction/F_complex128",
    ):
        arrays = {name: value.copy() for name, value in scientific.arrays.items()}
        if key.endswith("lattice_n_int32"):
            arrays[key][0] = arrays[key][1]
        else:
            arrays[key].flat[0] += 0.01
        payload = scientific.metadata.model_dump(mode="json")
        for component in ("candidates", "state", "graph", "diffraction"):
            if payload[component] is not None:
                for record in payload[component]["datasets"].values():
                    if record["path"] == key:
                        record["sha256"] = sha256_bytes(arrays[key].tobytes())
        metadata = ScientificEnvelope.model_validate(
            {"authoritative_sha256": "a" * 64, "metadata": payload}
        ).metadata
        with pytest.raises(ValueError, match="inconsistent|duplicate|derived"):
            validate_scientific(ScientificData(arrays, metadata))


def test_checksum_major_version_and_external_metadata_mismatch(tmp_path, scientific):
    archive, metadata = write_data(tmp_path, scientific)
    (tmp_path / "authoritative.npz").write_bytes(archive[:-3])
    with pytest.raises(ValueError, match="checksum"):
        load_authoritative(tmp_path)
    (tmp_path / "authoritative.npz").write_bytes(archive)
    modified = json.loads(metadata)
    modified["schema_version"] = "2.0.0"
    (tmp_path / "metadata.json").write_bytes(canonical_json(modified))
    with pytest.raises(ValueError, match="1.0.0"):
        load_authoritative(tmp_path)
    modified = json.loads(metadata)
    modified["metadata"]["input_checksums"] = {"changed": "b" * 64}
    (tmp_path / "metadata.json").write_bytes(canonical_json(modified))
    with pytest.raises(ValueError, match="embedded"):
        load_authoritative(tmp_path)


def test_bounded_npz_rejects_pickle_duplicate_members_and_forged_allocation():
    payload = io.BytesIO()
    np.savez(payload, objects=np.array([{}], dtype=object))
    with pytest.raises(ValueError, match="pickle"):
        read_npz(payload.getvalue())
    member = io.BytesIO()
    np.lib.format.write_array(member, np.zeros(1), allow_pickle=False)
    payload = io.BytesIO()
    with zipfile.ZipFile(payload, "w") as archive:
        archive.writestr("a.npy", member.getvalue())
        with pytest.warns(UserWarning):
            archive.writestr("a.npy", member.getvalue())
    with pytest.raises(ValueError, match="duplicate"):
        read_npz(payload.getvalue())
    forged = io.BytesIO()
    np.lib.format.write_array_header_1_0(
        forged, {"descr": "<f8", "fortran_order": False, "shape": (10**12,)}
    )
    payload = io.BytesIO()
    with zipfile.ZipFile(payload, "w") as archive:
        archive.writestr("huge.npy", forged.getvalue())
    with pytest.raises(ValueError, match="allocation|length"):
        read_npz(payload.getvalue())
    with pytest.raises(ValueError, match="malformed"):
        read_npz(b"not a zip file")
