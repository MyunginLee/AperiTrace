"""Canonical undirected parent-lattice edges, never projected-distance thresholds."""

from dataclasses import dataclass

import numpy as np

from qc5d.basis import SuperspaceBasis
from qc5d.geometry import immutable_array
from qc5d.identity import lattice_tuples, stable_ids


@dataclass(frozen=True, slots=True, eq=False)
class ParentGraph:
    source_id: np.ndarray
    target_id: np.ndarray
    generator: np.ndarray
    sign: np.ndarray
    physical_length: np.ndarray
    internal_displacement: np.ndarray

    def arrays(self) -> dict:
        return {
            "edges/source_id": self.source_id,
            "edges/target_id": self.target_id,
            "edges/generator": self.generator,
            "edges/sign": self.sign,
            "edges/physical_length": self.physical_length,
            "edges/internal_displacement": self.internal_displacement,
        }


def build_graph(selected_lattice: np.ndarray) -> ParentGraph:
    lattice = lattice_tuples(selected_lattice)
    keys = [tuple(int(value) for value in row) for row in lattice]
    if len(set(keys)) != len(keys):
        raise ValueError("cannot construct graph with duplicate lattice tuples")
    ordered = sorted(keys)
    indices = {key: i for i, key in enumerate(ordered)}
    ids = stable_ids(np.asarray(ordered, dtype=np.int32).reshape(-1, 5))
    source, target, generators = [], [], []
    for key in ordered:
        for j in range(5):
            neighbor = list(key)
            neighbor[j] += 1
            endpoint = indices.get(tuple(neighbor))
            if endpoint is not None:
                source.append(ids[indices[key]])
                target.append(ids[endpoint])
                generators.append(j)
    j = np.asarray(generators, dtype=np.int8)
    basis = SuperspaceBasis()
    return ParentGraph(
        immutable_array(source, "U59"),
        immutable_array(target, "U59"),
        immutable_array(j, np.int8),
        immutable_array(np.ones(len(j)), np.int8),
        immutable_array(np.linalg.norm(basis.P_parallel[:, j], axis=0)),
        immutable_array(basis.P_perp[:, j].T),
    )


def check_graph(selected_lattice: np.ndarray, graph: ParentGraph) -> dict:
    lattice = lattice_tuples(selected_lattice)
    ids = stable_ids(lattice)
    lookup = {key: row.astype(np.int64) for key, row in zip(ids, lattice, strict=True)}
    count = len(graph.source_id)
    if any(
        array.shape != shape
        for array, shape in (
            (graph.target_id, (count,)),
            (graph.generator, (count,)),
            (graph.sign, (count,)),
            (graph.physical_length, (count,)),
            (graph.internal_displacement, (count, 2)),
        )
    ):
        raise ValueError("graph array dimensions disagree")
    if graph.generator.dtype.kind not in "iu" or graph.sign.dtype.kind not in "iu":
        raise ValueError("graph generators/signs must be integers")
    degrees = dict.fromkeys(ids.tolist(), 0)
    pairs = set()
    basis = SuperspaceBasis()
    for edge in range(count):
        source, target = graph.source_id[edge], graph.target_id[edge]
        if source not in lookup or target not in lookup:
            raise ValueError("graph endpoint is not a selected ID")
        j, sign = int(graph.generator[edge]), int(graph.sign[edge])
        if not 0 <= j < 5 or sign not in (-1, 1):
            raise ValueError("invalid generator or sign")
        difference = lookup[target] - lookup[source]
        wanted = np.zeros(5, dtype=np.int64)
        wanted[j] = sign
        if not np.array_equal(difference, wanted):
            raise ValueError("edge tuple difference is not the recorded signed generator")
        pair = tuple(sorted((source, target)))
        if pair in pairs:
            raise ValueError("duplicate undirected graph edge")
        pairs.add(pair)
        degrees[source] += 1
        degrees[target] += 1
        if not np.isclose(
            graph.physical_length[edge], np.linalg.norm(basis.P_parallel[:, j]), rtol=0, atol=1e-12
        ) or not np.allclose(
            graph.internal_displacement[edge], sign * basis.P_perp[:, j], rtol=0, atol=1e-12
        ):
            raise ValueError("graph displacement disagrees with scientific basis")
    expected = build_graph(lattice)
    # Canonical source order, positive orientation, and all eligible parent edges are required.
    if any(
        not np.array_equal(graph.arrays()[key], expected.arrays()[key])
        for key in ("edges/source_id", "edges/target_id", "edges/generator", "edges/sign")
    ):
        raise ValueError("graph is incomplete or not in canonical edge order")
    values, frequencies = np.unique(list(degrees.values()), return_counts=True)
    return {
        "passed": True,
        "vertex_count": len(ids),
        "edge_count": count,
        "degree_histogram": {str(int(k)): int(v) for k, v in zip(values, frequencies, strict=True)},
        "degree_sum": sum(degrees.values()),
        "maximum_degree": max(degrees.values(), default=0),
        "endpoint_check": True,
        "signed_generator_check": True,
        "no_duplicate_undirected_edges": True,
        "complete_canonical_graph": True,
    }
