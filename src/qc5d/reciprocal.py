"""Finite five-index reciprocal vectors with deterministic representative-radius grouping."""

from dataclasses import dataclass

import numpy as np
from scipy.spatial import cKDTree

from qc5d.basis import SuperspaceBasis
from qc5d.diffraction import real_array
from qc5d.geometry import immutable_array
from qc5d.identity import lattice_tuples
from qc5d.modelset import integer_chunks, project_tuples


@dataclass(frozen=True, slots=True, eq=False)
class ReciprocalSamples:
    q: np.ndarray
    contributing_h: np.ndarray
    offsets: np.ndarray
    representative_h: np.ndarray
    tolerance: float


def deduplicate_reciprocal(q, indices, tolerance: float) -> ReciprocalSamples:
    vectors, lattice = real_array(q, shape_tail=3), lattice_tuples(indices)
    if len(vectors) != len(lattice) or not np.isfinite(tolerance) or tolerance <= 0:
        raise ValueError("reciprocal vector/index counts or tolerance invalid")
    if len(np.unique(lattice, axis=0)) != len(lattice):
        raise ValueError("contributing reciprocal tuples must be unique")
    order = np.lexsort(tuple(lattice[:, j] for j in range(4, -1, -1)))
    vectors, lattice = vectors[order], lattice[order]
    tree = cKDTree(vectors)
    assigned = np.zeros(len(vectors), dtype=bool)
    representatives, members, offsets = [], [], [0]
    for index in range(len(vectors)):
        if assigned[index]:
            continue
        neighbors = np.asarray(
            tree.query_ball_point(vectors[index], tolerance, eps=0, workers=1, return_sorted=True),
            dtype=int,
        )
        neighbors = neighbors[~assigned[neighbors]]
        # Explicit norm recheck documents exact Euclidean tolerance, not approximate search.
        neighbors = neighbors[
            np.linalg.norm(vectors[neighbors] - vectors[index], axis=1) <= tolerance
        ]
        assigned[neighbors] = True
        representatives.append(index)
        members.extend(neighbors.tolist())
        offsets.append(len(members))
    representatives = np.asarray(representatives, dtype=int)
    return ReciprocalSamples(
        immutable_array(vectors[representatives]),
        immutable_array(lattice[members], np.int32),
        immutable_array(offsets, np.int64),
        immutable_array(lattice[representatives], np.int32),
        float(tolerance),
    )


def reciprocal_indices(bound: int, tolerance: float = 1e-10) -> ReciprocalSamples:
    if type(bound) is not int or not 0 <= bound <= 5:
        raise ValueError("reciprocal bound must be an integer in [0,5]")
    indices = np.concatenate(list(integer_chunks(bound, 65536)), axis=0)
    physical, _ = project_tuples(indices, SuperspaceBasis())
    return deduplicate_reciprocal(2 * np.pi * physical, indices, tolerance)
