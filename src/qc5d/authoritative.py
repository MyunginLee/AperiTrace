"""Lossless semantic-versioned NPZ scientific exchange and mathematical validation."""

import json
from dataclasses import dataclass
from importlib.metadata import version
from pathlib import Path

import numpy as np

from qc5d.basis import SuperspaceBasis
from qc5d.checksums import sha256_bytes, sha256_file
from qc5d.data_schema import (
    SCIENTIFIC_FLOAT_ATOL,
    ArrayDescriptor,
    BasisDefinition,
    CandidatePoints,
    DiffractionResult,
    GraphDefinition,
    MaterialProvenance,
    ModelConfiguration,
    PhasonFrame,
    ScientificEnvelope,
    ScientificMetadata,
    SelectedState,
    SourceFile,
    WindowDefinition,
)
from qc5d.diffraction import intensity, normalize_intensity, structure_factor
from qc5d.geometry import immutable_array
from qc5d.graph import build_graph
from qc5d.identity import identity_diagnostics, lookup_hashes, stable_ids
from qc5d.modelset import (
    CandidateSet,
    compare_membership,
    crop_mask,
    evaluate_membership,
    project_tuples,
)
from qc5d.reference import _git_revision, canonical_json, deterministic_npz, source_inventory
from qc5d.safe_npz import read_npz
from qc5d.window import AcceptanceWindow


@dataclass(frozen=True, slots=True)
class ScientificData:
    arrays: dict[str, np.ndarray]
    metadata: ScientificMetadata

    @property
    def candidates(self) -> CandidateSet:
        a = self.arrays
        return CandidateSet(
            self.metadata.model.parameters,
            a["points/lattice_n_int32"],
            a["points/physical_xyz_float64"],
            a["points/internal_uv_float64"],
            a["points/stable_id"],
            a["points/lookup_hash_uint64"],
            {},
        )


def describe(arrays, names):
    return {
        name.split("/")[-1]: ArrayDescriptor(
            path=name,
            dtype=arrays[name].dtype.str,
            shape=arrays[name].shape,
            sha256=sha256_bytes(arrays[name].tobytes(order="C")),
        )
        for name in names
    }


def scientific_data(
    candidates: CandidateSet, *, phi=None, phasons=(), q=None, input_checksums=None, root=None
) -> ScientificData:
    basis, window = SuperspaceBasis(), AcceptanceWindow()
    state = evaluate_membership(candidates, candidates.config.initial_phi if phi is None else phi)
    graph = build_graph(candidates.lattice_n[state.selected])
    index = {str(key): i for i, key in enumerate(candidates.ids)}
    endpoints = np.array(
        [
            (index[str(a)], index[str(b)])
            for a, b in zip(graph.source_id, graph.target_id, strict=True)
        ],
        dtype="<u4",
    ).reshape(-1, 2)
    arrays = {
        "basis/P_parallel": basis.P_parallel,
        "basis/P_perp": basis.P_perp,
        "window/vertices_uv": window.vertices,
        "points/lattice_n_int32": candidates.lattice_n,
        "points/physical_xyz_float64": candidates.physical,
        "points/internal_uv_float64": candidates.internal,
        "points/stable_id": candidates.ids,
        "points/lookup_hash_uint64": candidates.hashes,
        "points/candidate_bool": np.ones(len(candidates.ids), dtype=bool),
        "points/selected_bool": state.selected,
        "points/boundary_bool": state.boundary,
        "points/boundary_distance": state.signed_distance,
        "state/phi0": state.phi,
        "graph/endpoints_uint32": endpoints,
        "graph/generator": graph.generator,
        "graph/sign": graph.sign,
    }
    diffraction = None
    if q is not None:
        if len(q) * len(state.selected_ids) > 100_000_000:
            raise ValueError("authoritative diffraction validation exceeds 100 million phase pairs")
        F = structure_factor(state.selected_physical, q)
        central = float(intensity(structure_factor(state.selected_physical, np.zeros((1, 3))))[0])
        arrays.update(
            {
                "diffraction/q_xyz": np.asarray(q, dtype="<f8"),
                "diffraction/F_complex128": F,
                "diffraction/intensity_raw": intensity(F),
                "diffraction/intensity_normalized": normalize_intensity(intensity(F), central),
            }
        )
        diffraction = DiffractionResult(
            q_count=len(q),
            central_intensity=central,
            selected_ids_sha256=sha256_bytes(canonical_json({"ids": state.selected_ids.tolist()})),
            datasets=describe(arrays, [name for name in arrays if name.startswith("diffraction/")]),
        )
    frames, previous = [], None
    for shift in phasons:
        frame = evaluate_membership(candidates, shift)
        events = (
            dict(
                entered_ids=[], exited_ids=[], boundary_ids=candidates.ids[frame.boundary].tolist()
            )
            if previous is None
            else compare_membership(candidates, previous, frame)
        )
        frames.append(
            PhasonFrame(phi=tuple(frame.phi), selected_ids=tuple(frame.selected_ids), **events)
        )
        previous = frame
    inventory = source_inventory()
    point_names = [
        name
        for name in arrays
        if name.startswith("points/")
        and name not in ("points/selected_bool", "points/boundary_bool", "points/boundary_distance")
    ]
    metadata = ScientificMetadata(
        model=ModelConfiguration(parameters=candidates.config),
        basis=BasisDefinition(P_parallel=basis.P_parallel.tolist(), P_perp=basis.P_perp.tolist()),
        window=WindowDefinition(vertices_uv=window.vertices.tolist()),
        candidates=CandidatePoints(
            count=len(candidates.ids), datasets=describe(arrays, point_names)
        ),
        state=SelectedState(
            phi=tuple(state.phi),
            selected_count=len(state.selected_ids),
            boundary_count=int(np.count_nonzero(state.boundary)),
            boundary_epsilon=candidates.config.boundary_epsilon_relative
            * window.characteristic_radius,
            boundary_policy=candidates.config.boundary_policy,
            datasets=describe(
                arrays,
                [
                    "points/selected_bool",
                    "points/boundary_bool",
                    "points/boundary_distance",
                    "state/phi0",
                ],
            ),
        ),
        phason_frames=tuple(frames),
        graph=GraphDefinition(
            edge_count=len(endpoints),
            datasets=describe(arrays, ["graph/endpoints_uint32", "graph/generator", "graph/sign"]),
        ),
        diffraction=diffraction,
        material=MaterialProvenance(),
        source_files=tuple(SourceFile(**item) for item in inventory),
        source_hash=sha256_bytes(canonical_json({"files": inventory})),
        config_hash=sha256_bytes(canonical_json(candidates.config.model_dump(mode="json"))),
        input_checksums={} if input_checksums is None else input_checksums,
        code_revision=_git_revision(Path.cwd() if root is None else root),
        dependency_versions={name: version(name) for name in ("numpy", "scipy", "pydantic")},
    )
    data = ScientificData(
        {name: immutable_array(value, value.dtype) for name, value in arrays.items()}, metadata
    )
    validate_scientific(data)
    return data


def validate_scientific(data: ScientificData) -> dict:
    a, m = data.arrays, data.metadata
    count, edges = m.candidates.count, m.graph.edge_count
    spec = {
        "basis/P_parallel": ("<f8", (3, 5)),
        "basis/P_perp": ("<f8", (2, 5)),
        "window/vertices_uv": ("<f8", (len(m.window.vertices_uv), 2)),
        "points/lattice_n_int32": ("<i4", (count, 5)),
        "points/physical_xyz_float64": ("<f8", (count, 3)),
        "points/internal_uv_float64": ("<f8", (count, 2)),
        "points/stable_id": ("<U59", (count,)),
        "points/lookup_hash_uint64": ("<u8", (count,)),
        "points/candidate_bool": ("|b1", (count,)),
        "points/selected_bool": ("|b1", (count,)),
        "points/boundary_bool": ("|b1", (count,)),
        "points/boundary_distance": ("<f8", (count,)),
        "state/phi0": ("<f8", (2,)),
        "graph/endpoints_uint32": ("<u4", (edges, 2)),
        "graph/generator": ("|i1", (edges,)),
        "graph/sign": ("|i1", (edges,)),
    }
    if m.diffraction is not None:
        q_count = m.diffraction.q_count
        if q_count * m.state.selected_count > 100_000_000:
            raise ValueError("authoritative diffraction validation exceeds 100 million phase pairs")
        spec.update(
            {
                "diffraction/q_xyz": ("<f8", (q_count, 3)),
                "diffraction/F_complex128": ("<c16", (q_count,)),
                "diffraction/intensity_raw": ("<f8", (q_count,)),
                "diffraction/intensity_normalized": ("<f8", (q_count,)),
            }
        )
    missing, extra = set(spec) - set(a), set(a) - set(spec)
    if missing or extra:
        raise ValueError(f"missing datasets {sorted(missing)}; unexpected datasets {sorted(extra)}")
    for name, (dtype, shape) in spec.items():
        value = a[name]
        if value.dtype.str != dtype or value.shape != shape:
            raise ValueError(f"{name}: expected dtype {dtype} shape {shape}")
        if value.dtype.kind in "fc" and not np.all(np.isfinite(value)):
            raise ValueError(f"{name}: nonfinite data")
    descriptors = [
        *m.candidates.datasets.values(),
        *m.state.datasets.values(),
        *m.graph.datasets.values(),
    ]
    if m.diffraction is not None:
        descriptors.extend(m.diffraction.datasets.values())
    expected_described = set(spec) - {"basis/P_parallel", "basis/P_perp", "window/vertices_uv"}
    if (
        len(descriptors) != len(expected_described)
        or {d.path for d in descriptors} != expected_described
    ):
        raise ValueError("metadata dataset descriptors are incomplete or duplicated")
    for descriptor in descriptors:
        value = a[descriptor.path]
        if (
            descriptor.dtype != value.dtype.str
            or descriptor.shape != value.shape
            or descriptor.sha256 != sha256_bytes(value.tobytes(order="C"))
        ):
            raise ValueError(f"{descriptor.path}: descriptor checksum/shape mismatch")
    inventory = [file.model_dump(mode="json") for file in m.source_files]
    paths = [item["path"] for item in inventory]
    if (
        paths != sorted(set(paths))
        or not paths
        or m.source_hash != sha256_bytes(canonical_json({"files": inventory}))
    ):
        raise ValueError("source inventory order/uniqueness/checksum mismatch")
    cfg = m.model.parameters
    if m.config_hash != sha256_bytes(canonical_json(cfg.model_dump(mode="json"))):
        raise ValueError("model configuration checksum mismatch")
    if m.material.kind != "canonical_model":
        raise ValueError("this canonical exchange reader does not validate imported materials")
    lattice = a["points/lattice_n_int32"]
    diagnostic = identity_diagnostics(lattice)
    if diagnostic["duplicate_tuple_count"] or diagnostic["hash_collision_count"]:
        raise ValueError("duplicate lattice IDs or stable hash collision")
    order = np.lexsort(tuple(lattice[:, j] for j in range(4, -1, -1)))
    if not np.array_equal(order, np.arange(count)):
        raise ValueError("candidate tuples must be lexicographically sorted")
    if np.any(np.abs(lattice.astype(np.int64)) > cfg.index_bound):
        raise ValueError("lattice tuple exceeds configured index bound")
    physical, internal = project_tuples(lattice, SuperspaceBasis())
    wanted = {
        "basis/P_parallel": np.asarray(m.basis.P_parallel),
        "basis/P_perp": np.asarray(m.basis.P_perp),
        "window/vertices_uv": np.asarray(m.window.vertices_uv),
        "points/physical_xyz_float64": physical,
        "points/internal_uv_float64": internal,
        "points/stable_id": stable_ids(lattice),
        "points/lookup_hash_uint64": lookup_hashes(lattice),
        "points/candidate_bool": np.ones(count, dtype=bool),
        "state/phi0": np.asarray(m.state.phi),
    }
    if not np.all(crop_mask(physical, cfg)):
        raise ValueError("candidate lies outside scientific crop")
    state = evaluate_membership(data.candidates, m.state.phi)
    wanted.update(
        {
            "points/selected_bool": state.selected,
            "points/boundary_bool": state.boundary,
            "points/boundary_distance": state.signed_distance,
        }
    )
    if (
        m.state.selected_count != len(state.selected_ids)
        or m.state.boundary_count != np.count_nonzero(state.boundary)
        or m.state.boundary_policy != cfg.boundary_policy
        or not np.isclose(
            m.state.boundary_epsilon,
            cfg.boundary_epsilon_relative * AcceptanceWindow().characteristic_radius,
            rtol=64 * np.finfo(np.float64).eps,
            atol=0,
        )
    ):
        raise ValueError("state counts/epsilon/policy disagree with authoritative membership")
    for name, expected in wanted.items():
        # Only independent recomputation is tolerant. Serialized basis/window/phi
        # correspondence, identifiers, masks and all byte checksums remain exact.
        recomputed_float = name in {
            "points/physical_xyz_float64",
            "points/internal_uv_float64",
            "points/boundary_distance",
        }
        matches = (
            np.allclose(a[name], expected, rtol=0, atol=SCIENTIFIC_FLOAT_ATOL)
            if recomputed_float
            else np.array_equal(a[name], expected)
        )
        if not matches:
            raise ValueError(
                f"{name}: inconsistent canonical identities, coordinates or membership"
            )
    graph = build_graph(lattice[state.selected])
    ids = a["points/stable_id"]
    if np.any(a["graph/endpoints_uint32"] >= count):
        raise ValueError("graph endpoint index out of range")
    for column, expected in ((0, graph.source_id), (1, graph.target_id)):
        if not np.array_equal(ids[a["graph/endpoints_uint32"][:, column]], expected):
            raise ValueError("graph endpoints are not the complete selected canonical graph")
    if not np.array_equal(a["graph/generator"], graph.generator) or not np.array_equal(
        a["graph/sign"], graph.sign
    ):
        raise ValueError("graph generator/sign mismatch")
    previous = None
    if len(m.phason_frames) > 100 or count * len(m.phason_frames) > 5_000_000:
        raise ValueError("authoritative phason validation exceeds frame/membership budget")
    for frame in m.phason_frames:
        selected = evaluate_membership(data.candidates, frame.phi)
        expected = (
            dict(entered_ids=[], exited_ids=[], boundary_ids=ids[selected.boundary].tolist())
            if previous is None
            else compare_membership(data.candidates, previous, selected)
        )
        if tuple(selected.selected_ids) != frame.selected_ids or any(
            tuple(expected[key]) != getattr(frame, key) for key in expected
        ):
            raise ValueError("phason frame IDs/events differ from exact authoritative membership")
        previous = selected
    if m.diffraction is not None:
        F = structure_factor(state.selected_physical, a["diffraction/q_xyz"])
        central = float(intensity(structure_factor(state.selected_physical, np.zeros((1, 3))))[0])
        if (
            central != m.diffraction.central_intensity
            or m.diffraction.selected_ids_sha256
            != sha256_bytes(canonical_json({"ids": state.selected_ids.tolist()}))
        ):
            raise ValueError("diffraction identity/normalization mismatch")
        for key, value in (
            ("F_complex128", F),
            ("intensity_raw", intensity(F)),
            ("intensity_normalized", normalize_intensity(intensity(F), central)),
        ):
            # Compare amplitudes per selected point and intensities per N^2.
            # This is the same absolute normalized 1e-12 scientific budget used
            # by the inspection release, not permission to modify stored bytes.
            scale = (
                max(1, len(state.selected_ids))
                if key == "F_complex128"
                else central
                if key == "intensity_raw"
                else 1.0
            )
            if not np.allclose(
                a["diffraction/" + key] / scale,
                value / scale,
                rtol=0,
                atol=SCIENTIFIC_FLOAT_ATOL,
            ):
                raise ValueError(f"diffraction/{key}: not derived from the same selected IDs")
    return {
        "passed": True,
        "schema_version": m.schema_version,
        "candidate_count": count,
        "selected_count": len(state.selected_ids),
        "boundary_count": m.state.boundary_count,
        "edge_count": edges,
        "dataset_count": len(spec),
        "identity": diagnostic,
        "phason_frame_count": len(m.phason_frames),
        "diffraction_validated": m.diffraction is not None,
    }


def authoritative_payloads(data: ScientificData) -> tuple[bytes, bytes]:
    validate_scientific(data)
    metadata = canonical_json(data.metadata.model_dump(mode="json"))
    archive = deterministic_npz(
        {**data.arrays, "metadata/json_utf8": np.frombuffer(metadata, dtype=np.uint8)}
    )
    envelope = ScientificEnvelope(
        authoritative_sha256=sha256_bytes(archive), metadata=data.metadata
    )
    return archive, canonical_json(envelope.model_dump(mode="json"))


def load_authoritative(path: Path) -> tuple[ScientificData, dict]:
    file = path / "authoritative.npz" if path.is_dir() else path
    if file.name != "authoritative.npz":
        raise ValueError(
            "expected authoritative.npz or its directory (legacy modelset files need conversion)"
        )
    metadata_file = file.parent / "metadata.json"
    if metadata_file.stat().st_size > 8 * 1024 * 1024:
        raise ValueError("authoritative JSON metadata exceeds 8 MiB budget")
    envelope = ScientificEnvelope.model_validate_json(metadata_file.read_bytes())
    if sha256_file(file) != envelope.authoritative_sha256:
        raise ValueError("authoritative file SHA-256 checksum mismatch")
    arrays = read_npz(file)
    embedded = arrays.pop("metadata/json_utf8", None)
    if embedded is None or embedded.dtype.str != "|u1" or embedded.ndim != 1:
        raise ValueError("missing or invalid metadata/json_utf8 dataset")
    if embedded.tobytes() != canonical_json(envelope.metadata.model_dump(mode="json")):
        raise ValueError("embedded and external JSON metadata mismatch")
    # Decode through the public model again; never trust arbitrary JSON dictionaries.
    metadata = ScientificMetadata.model_validate(json.loads(embedded.tobytes()))
    data = ScientificData(
        {name: immutable_array(value, value.dtype) for name, value in arrays.items()}, metadata
    )
    result = validate_scientific(data)
    return data, {**result, "authoritative_sha256": envelope.authoritative_sha256}
