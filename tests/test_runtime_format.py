import hashlib
import struct

import numpy as np
import pytest

from qc5d.authoritative import authoritative_payloads, scientific_data
from qc5d.checksums import sha256_bytes
from qc5d.data_schema import ExportConfig
from qc5d.model_config import ModelConfig
from qc5d.modelset import generate_candidates
from qc5d.runtime import CANDIDATE, EDGE, HEADER, encode_runtime, parse_runtime, preview_state


@pytest.fixture
def runtime():
    data = scientific_data(
        generate_candidates(
            ModelConfig(index_bound=1, physical_crop={"radial_max": 0.9, "z_abs_max": 0.9})
        )
    )
    archive, _ = authoritative_payloads(data)
    binary, report = encode_runtime(data, sha256_bytes(archive))
    return data, binary, report


def resign(binary):
    binary = bytearray(binary)
    binary[72:104] = hashlib.sha256(binary[192:-32]).digest()
    binary[-32:] = hashlib.sha256(binary[:-32]).digest()
    return bytes(binary)


@pytest.mark.parametrize(
    "offset,fmt,small,large",
    [
        (192, "d", 2e-15, 2e-8),
        (392, "f", 2e-7, 2e-5),
        (512, "f", 2e-7, 2e-5),
    ],
)
@pytest.mark.parametrize("within_budget", [True, False])
def test_runtime_recomputation_roundoff_vs_changed_geometry(
    runtime, offset, fmt, small, large, within_budget
):
    _, binary, _ = runtime
    changed = bytearray(binary)
    old = struct.unpack_from("<" + fmt, changed, offset)[0]
    struct.pack_into("<" + fmt, changed, offset, old + (small if within_budget else large))
    assert changed != binary
    signed = resign(changed)
    if within_budget:
        parsed = parse_runtime(signed)
        assert parsed.header["candidate_count"] == 31
    else:
        with pytest.raises(ValueError, match="basis/window|distance"):
            parse_runtime(signed)


def test_exact_layout_loss_report_and_all_roundtrip_fields(runtime):
    data, binary, report = runtime
    parsed = parse_runtime(binary)
    assert HEADER.size == 192 and CANDIDATE.itemsize == 52 and EDGE.itemsize == 12
    assert report["passed"] and all(report["checks"].values())
    assert binary[:8] == b"QC5DRT\0\0" and binary[12:16] == bytes([4, 3, 2, 1])
    assert struct.unpack_from("<I", binary, 24)[0] == 31
    assert len(binary) == 2298 and report["binary_sha256"] == sha256_bytes(binary)
    assert CANDIDATE.fields["hash"][1] == 44  # packed, not native alignment
    np.testing.assert_array_equal(parsed.basis[:3], data.arrays["basis/P_parallel"])
    np.testing.assert_array_equal(parsed.basis[3:], data.arrays["basis/P_perp"])
    np.testing.assert_array_equal(parsed.window, data.arrays["window/vertices_uv"].astype("<f4"))
    for field, name in (
        ("n", "lattice_n_int32"),
        ("physical", "physical_xyz_float64"),
        ("internal", "internal_uv_float64"),
        ("distance", "boundary_distance"),
        ("hash", "lookup_hash_uint64"),
    ):
        wanted = data.arrays["points/" + name].astype(parsed.candidates[field].dtype)
        np.testing.assert_array_equal(parsed.candidates[field], wanted)
    np.testing.assert_array_equal(parsed.ids, data.arrays["points/stable_id"])
    np.testing.assert_array_equal(parsed.edges["endpoints"], data.arrays["graph/endpoints_uint32"])
    np.testing.assert_array_equal(parsed.edges["generator"], data.arrays["graph/generator"])
    np.testing.assert_array_equal(parsed.edges["sign"], data.arrays["graph/sign"])
    np.testing.assert_array_equal(parsed.selected, data.arrays["points/selected_bool"])
    np.testing.assert_array_equal(parsed.boundary, data.arrays["points/boundary_bool"])
    np.testing.assert_array_equal(preview_state(parsed).selected, parsed.selected)
    assert not parsed.header["authoritative_source_file_verified"]
    assert parsed.header["display_scales"] == [1.0, 0.35, 1.0]
    assert encode_runtime(data, report["authoritative_sha256"])[0] == binary
    for index in range(len(parsed.candidates)):
        scalar = struct.unpack_from("<5i6fQ", binary, 472 + 52 * index)
        assert scalar[:5] == tuple(parsed.candidates["n"][index])
        assert scalar[5:8] == tuple(parsed.candidates["physical"][index])
        assert scalar[8:10] == tuple(parsed.candidates["internal"][index])
        assert scalar[10] == parsed.candidates["distance"][index]
        assert scalar[11] == parsed.candidates["hash"][index]
    for index in range(len(parsed.edges)):
        scalar = struct.unpack_from("<IIBbH", binary, 472 + 31 * 52 + 12 * index)
        assert scalar[:2] == tuple(parsed.edges["endpoints"][index])
        assert scalar[2:] == (int(parsed.edges["generator"][index]), 1, 0)
    with pytest.raises(ValueError):
        parsed.candidates.setflags(write=True)


@pytest.mark.parametrize(
    "offset,fmt,value,error",
    [
        (8, "H", 2, "schema"),
        (10, "H", 1, "schema"),
        (12, "I", 0, "endianness"),
        (16, "I", 0x80, "flags"),
        (20, "I", 1, "header"),
        (24, "I", 2_000_001, "counts"),
        (28, "I", 9, "counts"),
        (32, "Q", 999999999, "counts"),
        (104, "d", float("nan"), "scales"),
        (128, "d", float("inf"), "phi"),
        (160, "d", 1e-3, "coordinate_atol"),
        (176, "I", 64, "stride"),
        (184, "Q", 1, "reserved"),
    ],
)
def test_malformed_runtime_headers_fail_usefully(runtime, offset, fmt, value, error):
    modified = bytearray(runtime[1])
    struct.pack_into("<" + fmt, modified, offset, value)
    with pytest.raises(ValueError, match=error):
        parse_runtime(resign(modified))


@pytest.mark.parametrize(
    "mutation", ["magic", "checksum", "truncated", "trailing", "payload_digest"]
)
def test_runtime_integrity_and_length_failures(runtime, mutation):
    modified = bytearray(runtime[1])
    if mutation == "magic":
        modified[0] ^= 1
    elif mutation == "checksum":
        modified[400] ^= 1
    elif mutation == "truncated":
        modified = modified[:-1]
    elif mutation == "trailing":
        modified += b"X"
    else:
        modified[72] ^= 1
        modified[-32:] = hashlib.sha256(modified[:-32]).digest()
    with pytest.raises(ValueError):
        parse_runtime(bytes(modified))


@pytest.mark.parametrize(
    "mutation",
    ["hash", "tuple_duplicate", "coordinate", "distance", "mask", "edge", "edge_reserved"],
)
def test_rechecksummed_bad_runtime_fields_are_not_trusted(runtime, mutation):
    modified = bytearray(runtime[1])
    start, count, edges = 192 + 200 + 80, 31, 10
    if mutation == "hash":
        modified[start + 44] ^= 1
    elif mutation == "tuple_duplicate":
        modified[start : start + 20] = modified[start + 52 : start + 72]
    elif mutation == "coordinate":
        struct.pack_into("<f", modified, start + 20, 100)
    elif mutation == "distance":
        struct.pack_into("<f", modified, start + 40, 100)
    elif mutation == "mask":
        modified[start + count * 52 + edges * 12] = 2
    elif mutation == "edge":
        struct.pack_into("<I", modified, start + count * 52, 100)
    else:
        modified[start + count * 52 + 10] = 1
    with pytest.raises(ValueError):
        parse_runtime(resign(modified))


def test_loss_gate_refuses_stricter_coordinate_budget_and_preview_disagreement(runtime):
    data, _, report = runtime
    with pytest.raises(ValueError, match="loss"):
        encode_runtime(data, report["authoritative_sha256"], ExportConfig(coordinate_atol=1e-12))
    near_boundary = scientific_data(
        generate_candidates(
            ModelConfig(
                index_bound=1,
                initial_phi=(1e-7, 0),
                physical_crop={"radial_max": 0.9, "z_abs_max": 0.9},
            )
        )
    )
    archive, _ = authoritative_payloads(near_boundary)
    with pytest.raises(ValueError, match="parity"):
        encode_runtime(near_boundary, sha256_bytes(archive))


def test_include_policy_and_display_scaling_do_not_change_scientific_float_arrays():
    data = scientific_data(
        generate_candidates(ModelConfig(index_bound=1, boundary_policy="include"))
    )
    archive, _ = authoritative_payloads(data)
    binary, _ = encode_runtime(data, sha256_bytes(archive), ExportConfig(internal_uv_scale=2.0))
    parsed = parse_runtime(binary)
    assert parsed.header["flags"] == 7 and parsed.header["display_scales"][2] == 2
    np.testing.assert_array_equal(parsed.selected, data.arrays["points/selected_bool"])
