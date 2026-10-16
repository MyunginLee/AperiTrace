"""Packed little-endian .qc5d v1.0 with loss gates, strict parsing and exact tuple IDs."""

import hashlib
import struct
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from qc5d.authoritative import ScientificData, validate_scientific
from qc5d.basis import SuperspaceBasis
from qc5d.checksums import sha256_bytes
from qc5d.data_schema import SCIENTIFIC_FLOAT_ATOL, ExportConfig
from qc5d.geometry import immutable_array
from qc5d.graph import build_graph
from qc5d.identity import identity_diagnostics, lookup_hashes, stable_ids
from qc5d.modelset import project_tuples
from qc5d.window import AcceptanceWindow, classify_polygon

MAGIC = b"QC5DRT\x00\x00"
HEADER = struct.Struct("<8sHHIIIIIQ32s32s3d2d4dIIQ")
HEADER_BYTES = 192
MAX_BYTES = 256 * 1024 * 1024
CANDIDATE = np.dtype(
    [
        ("n", "<i4", (5,)),
        ("physical", "<f4", (3,)),
        ("internal", "<f4", (2,)),
        ("distance", "<f4"),
        ("hash", "<u8"),
    ]
)
EDGE = np.dtype(
    [("endpoints", "<u4", (2,)), ("generator", "u1"), ("sign", "i1"), ("reserved", "<u2")]
)


@dataclass(frozen=True, slots=True)
class RuntimeData:
    header: dict
    basis: np.ndarray
    window: np.ndarray
    candidates: np.ndarray
    edges: np.ndarray
    selected: np.ndarray
    boundary: np.ndarray

    @property
    def ids(self):
        return stable_ids(self.candidates["n"])


def error_metrics(authority, preview):
    difference = np.asarray(preview, dtype=float) - authority
    return {
        "max_abs": float(np.max(np.abs(difference), initial=0.0)),
        "rms": float(np.sqrt(np.mean(difference**2))) if difference.size else 0.0,
    }


def preview_state(runtime: RuntimeData):
    # This measures quantization with the existing float64 polygon classifier.
    # It is explicitly NOT a GPU/native-float32 arithmetic parity claim.
    shift = np.asarray(runtime.header["phi0"], dtype=np.float32).astype(np.float64)
    return classify_polygon(
        runtime.window.astype(np.float64),
        runtime.candidates["internal"].astype(np.float64) + shift,
        boundary_epsilon=runtime.header["preview_boundary_epsilon"],
        boundary_policy=runtime.header["boundary_policy"],
    )


def encode_runtime(
    data: ScientificData, authoritative_sha256: str, config: ExportConfig | None = None
) -> tuple[bytes, dict]:
    config = ExportConfig() if config is None else config
    validate_scientific(data)
    if len(authoritative_sha256) != 64 or any(
        c not in "0123456789abcdef" for c in authoritative_sha256
    ):
        raise ValueError("authoritative source must have a lowercase SHA-256 digest")
    a, m = data.arrays, data.metadata
    count = m.candidates.count
    records = np.zeros(count, dtype=CANDIDATE)
    for field, name in (
        ("n", "lattice_n_int32"),
        ("physical", "physical_xyz_float64"),
        ("internal", "internal_uv_float64"),
        ("distance", "boundary_distance"),
        ("hash", "lookup_hash_uint64"),
    ):
        records[field] = a["points/" + name]
    edges = np.zeros(m.graph.edge_count, dtype=EDGE)
    edges["endpoints"] = a["graph/endpoints_uint32"]
    edges["generator"] = a["graph/generator"]
    edges["sign"] = a["graph/sign"]
    window = a["window/vertices_uv"].astype("<f4")
    payload = b"".join(
        (
            a["basis/P_parallel"].tobytes(),
            a["basis/P_perp"].tobytes(),
            window.tobytes(),
            records.tobytes(),
            edges.tobytes(),
            a["points/selected_bool"].astype("u1").tobytes(),
            a["points/boundary_bool"].astype("u1").tobytes(),
        )
    )
    scales = m.model.parameters.physical_scale
    header = HEADER.pack(
        MAGIC,
        1,
        0,
        0x01020304,
        3 | (4 if m.state.boundary_policy == "include" else 0),
        HEADER_BYTES,
        count,
        len(window),
        m.graph.edge_count,
        bytes.fromhex(authoritative_sha256),
        hashlib.sha256(payload).digest(),
        scales.xy,
        scales.z,
        config.internal_uv_scale,
        *m.state.phi,
        m.state.boundary_epsilon,
        config.preview_boundary_epsilon,
        config.coordinate_atol,
        config.boundary_distance_atol,
        CANDIDATE.itemsize,
        EDGE.itemsize,
        0,
    )
    preceding = header + payload
    binary = preceding + hashlib.sha256(preceding).digest()
    if len(binary) > MAX_BYTES:
        raise ValueError("runtime binary exceeds the 256 MiB file budget")
    # Parse all exported fields back before accepting the conversion.
    parsed = parse_runtime(binary)
    coordinate_errors = {
        "physical": error_metrics(a["points/physical_xyz_float64"], parsed.candidates["physical"]),
        "internal": error_metrics(a["points/internal_uv_float64"], parsed.candidates["internal"]),
        "window": error_metrics(a["window/vertices_uv"], parsed.window),
    }
    distance_error = error_metrics(a["points/boundary_distance"], parsed.candidates["distance"])
    preview = preview_state(parsed)
    disagreement = preview.selected != a["points/selected_bool"]
    boundary_disagreement = (preview.states == "BOUNDARY") != a["points/boundary_bool"]
    checks = {
        "coordinate_loss_bounded": all(
            row["max_abs"] <= config.coordinate_atol for row in coordinate_errors.values()
        ),
        "boundary_distance_loss_bounded": distance_error["max_abs"]
        <= config.boundary_distance_atol,
        "stable_ids_preserved": np.array_equal(parsed.ids, a["points/stable_id"]),
        "stable_hashes_preserved": np.array_equal(
            parsed.candidates["hash"], a["points/lookup_hash_uint64"]
        ),
        "selected_mask_roundtrip_exact": np.array_equal(parsed.selected, a["points/selected_bool"]),
        "boundary_mask_roundtrip_exact": np.array_equal(parsed.boundary, a["points/boundary_bool"]),
        "preview_selected_mask_parity_at_phi0": not bool(np.any(disagreement)),
        "graph_roundtrip_exact": np.array_equal(
            parsed.edges["endpoints"], a["graph/endpoints_uint32"]
        ),
    }
    report = {
        "schema_version": "1.0.0",
        "passed": all(checks.values()),
        "checks": checks,
        "config": config.model_dump(mode="json"),
        "candidate_count": count,
        "selected_count": m.state.selected_count,
        "edge_count": m.graph.edge_count,
        "coordinate_error_units": "dimensionless scientific component absolute error",
        "rms_definition": "sqrt(mean(error^2)) over all scalar components",
        "coordinate_errors": coordinate_errors,
        "boundary_distance_error": distance_error,
        "preview_disagreement_ids": a["points/stable_id"][disagreement].tolist(),
        "preview_boundary_disagreement_ids": a["points/stable_id"][boundary_disagreement].tolist(),
        "preview_semantics": (
            "float32-quantized operands classified using Python float64; not GPU parity"
        ),
        "binary_size_bytes": len(binary),
        "binary_sha256": sha256_bytes(binary),
        "authoritative_sha256": authoritative_sha256,
    }
    if not report["passed"]:
        raise ValueError(f"runtime export loss/parity gate failed: {checks}")
    return binary, report


def parse_runtime(source: Path | bytes) -> RuntimeData:
    if isinstance(source, Path):
        if source.stat().st_size > MAX_BYTES:
            raise ValueError("runtime binary exceeds the 256 MiB file budget")
        binary = source.read_bytes()
    else:
        binary = source
    if not HEADER_BYTES + 32 <= len(binary) <= MAX_BYTES:
        raise ValueError("runtime file is truncated or exceeds the byte budget")
    fields = HEADER.unpack_from(binary)
    (
        magic,
        major,
        minor,
        marker,
        flags,
        header_size,
        count,
        vertices,
        edge_count,
        authority,
        payload_hash,
        xy_scale,
        z_scale,
        uv_scale,
        phi_u,
        phi_v,
        epsilon,
        preview_epsilon,
        coordinate_atol,
        distance_atol,
        record_size,
        edge_size,
        reserved,
    ) = fields
    if magic != MAGIC:
        raise ValueError("runtime magic is not QC5DRT")
    if (major, minor) != (1, 0):
        raise ValueError(f"unsupported runtime schema {major}.{minor}; expected 1.0")
    if marker != 0x01020304:
        raise ValueError("runtime endianness marker must be little-endian 0x01020304")
    if flags not in (3, 7) or reserved or header_size != HEADER_BYTES:
        raise ValueError("runtime flags, reserved bytes or header length invalid")
    if record_size != CANDIDATE.itemsize or edge_size != EDGE.itemsize:
        raise ValueError("runtime record stride mismatch")
    if count > 2_000_000 or vertices != 10 or edge_count > 5 * count:
        raise ValueError("runtime counts exceed supported bounds or canonical window size")
    expected_size = HEADER_BYTES + 200 + vertices * 8 + count * 54 + edge_count * 12 + 32
    if len(binary) != expected_size:
        raise ValueError("runtime count/length mismatch, truncation or trailing bytes")
    if hashlib.sha256(binary[:-32]).digest() != binary[-32:]:
        raise ValueError("runtime whole-file integrity checksum mismatch")
    if hashlib.sha256(binary[HEADER_BYTES:-32]).digest() != payload_hash:
        raise ValueError("runtime payload checksum mismatch")
    if authority == bytes(32):
        raise ValueError("runtime authoritative source checksum is absent")
    numeric = np.array(
        [
            xy_scale,
            z_scale,
            uv_scale,
            phi_u,
            phi_v,
            epsilon,
            preview_epsilon,
            coordinate_atol,
            distance_atol,
        ]
    )
    if (
        not np.all(np.isfinite(numeric))
        or min(xy_scale, z_scale, uv_scale) <= 0
        or max(xy_scale, z_scale, uv_scale) > 1e6
        or epsilon < 0
        or epsilon > 1e6
        or max(abs(phi_u), abs(phi_v)) > 1e6
    ):
        raise ValueError("runtime scales, phi or epsilon invalid/nonfinite")
    ExportConfig(
        coordinate_atol=coordinate_atol,
        boundary_distance_atol=distance_atol,
        preview_boundary_epsilon=preview_epsilon,
        internal_uv_scale=uv_scale,
    )
    position = HEADER_BYTES

    def take(dtype, shape):
        nonlocal position
        dtype = np.dtype(dtype)
        size = int(np.prod(shape))
        value = np.frombuffer(binary, dtype=dtype, count=size, offset=position).reshape(shape)
        position += size * dtype.itemsize
        return value

    basis = take("<f8", (5, 5))
    window = take("<f4", (vertices, 2))
    candidates = take(CANDIDATE, (count,))
    edges = take(EDGE, (edge_count,))
    selected_bytes, boundary_bytes = take("u1", (count,)), take("u1", (count,))
    if np.any(selected_bytes > 1) or np.any(boundary_bytes > 1):
        raise ValueError("runtime mask byte must be exactly 0 or 1")
    selected, boundary = (
        immutable_array(selected_bytes, bool),
        immutable_array(boundary_bytes, bool),
    )
    if (
        not np.all(np.isfinite(candidates["physical"]))
        or not np.all(np.isfinite(candidates["internal"]))
        or not np.all(np.isfinite(candidates["distance"]))
        or not np.all(np.isfinite(window))
    ):
        raise ValueError("runtime contains nonfinite float32 data")
    if not np.allclose(
        basis, SuperspaceBasis().Q, rtol=0, atol=SCIENTIFIC_FLOAT_ATOL
    ) or not np.allclose(window, AcceptanceWindow().vertices, rtol=0, atol=coordinate_atol):
        raise ValueError(
            "runtime basis/window disagrees with canonical geometry or loss exceeds tolerance"
        )
    diagnostic = identity_diagnostics(candidates["n"])
    if diagnostic["duplicate_tuple_count"] or diagnostic["hash_collision_count"]:
        raise ValueError("runtime contains duplicate IDs or hash collision")
    order = np.lexsort(tuple(candidates["n"][:, j] for j in range(4, -1, -1)))
    if not np.array_equal(order, np.arange(count)):
        raise ValueError("runtime candidates must be tuple-lexicographic")
    if not np.array_equal(candidates["hash"], lookup_hashes(candidates["n"])):
        raise ValueError("runtime stable hash does not match exact lattice tuple")
    physical, internal = project_tuples(candidates["n"], SuperspaceBasis())
    for name, original in (("physical", physical), ("internal", internal)):
        if error_metrics(original, candidates[name])["max_abs"] > coordinate_atol:
            raise ValueError(f"runtime {name} coordinate loss exceeds declared tolerance")
    authoritative = AcceptanceWindow().classify(
        internal + [phi_u, phi_v],
        boundary_epsilon=epsilon,
        boundary_policy="include" if flags & 4 else "exclude",
    )
    if (
        error_metrics(authoritative.signed_distance, candidates["distance"])["max_abs"]
        > distance_atol
    ):
        raise ValueError("runtime boundary distance mismatch or loss exceeds tolerance")
    if not np.array_equal(selected, authoritative.selected) or not np.array_equal(
        boundary, authoritative.states == "BOUNDARY"
    ):
        raise ValueError("runtime masks disagree with exact-tuple authoritative phi0 membership")
    if np.any(edges["reserved"]) or np.any(edges["endpoints"] >= count):
        raise ValueError("runtime graph reserved bytes or endpoint invalid")
    graph = build_graph(candidates["n"][selected])
    ids = stable_ids(candidates["n"])
    if (
        not np.array_equal(ids[edges["endpoints"][:, 0]], graph.source_id)
        or not np.array_equal(ids[edges["endpoints"][:, 1]], graph.target_id)
        or not np.array_equal(edges["generator"], graph.generator)
        or not np.array_equal(edges["sign"], graph.sign)
    ):
        raise ValueError("runtime graph differs from complete canonical selected graph")
    header = {
        "schema_version": "1.0.0",
        "schema_major": major,
        "schema_minor": minor,
        "flags": flags,
        "candidate_count": count,
        "window_vertex_count": vertices,
        "edge_count": edge_count,
        "authoritative_sha256": authority.hex(),
        "payload_sha256": payload_hash.hex(),
        "display_scales": [xy_scale, z_scale, uv_scale],
        "phi0": [phi_u, phi_v],
        "boundary_policy": "include" if flags & 4 else "exclude",
        "authoritative_boundary_epsilon": epsilon,
        "preview_boundary_epsilon": preview_epsilon,
        "coordinate_atol": coordinate_atol,
        "boundary_distance_atol": distance_atol,
        "binary_size_bytes": len(binary),
        "binary_sha256": sha256_bytes(binary),
        "authoritative_source_file_verified": False,
    }
    return RuntimeData(header, basis, window, candidates, edges, selected, boundary)
