"""Chunked finite Z^5 patches and authoritative positive-sign phason membership."""

from collections.abc import Callable, Iterator
from dataclasses import dataclass

import numpy as np
from numpy.typing import ArrayLike

from qc5d.basis import SuperspaceBasis
from qc5d.geometry import finite_vectors, immutable_array
from qc5d.identity import identity_diagnostics, lookup_hashes, stable_ids
from qc5d.model_config import ModelConfig
from qc5d.window import AcceptanceWindow, PointState


class GenerationCancelled(RuntimeError):
    pass


def integer_chunks(bound: int, chunk_size: int) -> Iterator[np.ndarray]:
    """Decode base-(2N+1) ordinals; n4 varies fastest, n0 slowest."""
    if type(bound) is not int or not 0 <= bound <= 1000:
        raise ValueError("bound must be an integer in [0,1000]")
    if type(chunk_size) is not int or not 1 <= chunk_size <= 1_000_000:
        raise ValueError("chunk_size must be an integer in [1,1000000]")
    radix = 2 * bound + 1
    for start in range(0, radix**5, chunk_size):
        ordinals = np.arange(start, min(start + chunk_size, radix**5), dtype=np.int64)
        values = np.empty((len(ordinals), 5), dtype=np.int32)
        for j in range(4, -1, -1):
            values[:, j] = ordinals % radix - bound
            ordinals //= radix
        yield values


def project_tuples(tuples: np.ndarray, basis: SuperspaceBasis) -> tuple[np.ndarray, np.ndarray]:
    """Fixed five-term accumulation makes row arithmetic independent of chunk size."""
    physical = np.zeros((len(tuples), 3), dtype=np.float64)
    internal = np.zeros((len(tuples), 2), dtype=np.float64)
    for j in range(5):
        physical += tuples[:, j, None] * basis.P_parallel[:, j]
        internal += tuples[:, j, None] * basis.P_perp[:, j]
    return physical, internal


def crop_mask(physical: np.ndarray, config: ModelConfig) -> np.ndarray:
    crop = config.physical_crop
    dimensions = 2 if crop.kind == "cylinder" else 3
    return (np.linalg.norm(physical[:, :dimensions], axis=1) <= crop.radial_max) & (
        np.abs(physical[:, 2]) <= crop.z_abs_max
    )


def expansion_parameters(config: ModelConfig, window: AcceptanceWindow) -> dict:
    domain = config.phason_domain
    rho = float(np.linalg.norm(np.maximum(np.abs(domain.min), np.abs(domain.max))))
    epsilon = config.boundary_epsilon_relative * window.characteristic_radius
    guard = (
        64
        * np.finfo(np.float64).eps
        * (1 + np.sqrt(5) * config.index_bound + window.characteristic_radius + rho + epsilon)
    )
    return {
        "method": "origin_centered_bounding_radius_triangle_inequality",
        "window_radius": window.characteristic_radius,
        "phason_maximum_norm": rho,
        "boundary_epsilon": epsilon,
        "roundoff_guard": float(guard),
        "candidate_internal_radius": float(window.characteristic_radius + rho + epsilon + guard),
    }


@dataclass(frozen=True, slots=True, eq=False)
class CandidateSet:
    config: ModelConfig
    lattice_n: np.ndarray
    physical: np.ndarray
    internal: np.ndarray
    ids: np.ndarray
    hashes: np.ndarray
    diagnostics: dict


def generate_candidates(
    config: ModelConfig,
    *,
    progress: Callable[[dict], None] | None = None,
    cancelled: Callable[[], bool] | None = None,
) -> CandidateSet:
    basis = SuperspaceBasis()
    expansion = expansion_parameters(config, AcceptanceWindow(basis))
    parts = []
    processed = cropped = kept = chunks = max_rows = 0
    total = (2 * config.index_bound + 1) ** 5
    for lattice in integer_chunks(config.index_bound, config.chunk_size):
        if cancelled is not None and cancelled():
            raise GenerationCancelled("candidate enumeration cancelled; no partial set returned")
        physical, internal = project_tuples(lattice, basis)
        mask = crop_mask(physical, config)
        cropped += int(np.count_nonzero(mask))
        mask &= np.linalg.norm(internal, axis=1) <= expansion["candidate_internal_radius"]
        kept += int(np.count_nonzero(mask))
        if kept > config.max_candidates:
            raise ValueError(
                "candidate memory limit exceeded; reduce patch or raise max_candidates"
            )
        if np.any(mask):
            parts.append(lattice[mask])
        processed += len(lattice)
        max_rows = max(max_rows, len(lattice))
        chunks += 1
        if progress is not None:
            progress({"processed": processed, "total": total, "candidate_count": kept})
    lattice = np.concatenate(parts, axis=0) if parts else np.empty((0, 5), dtype=np.int32)
    physical, internal = project_tuples(lattice, basis)
    ids = stable_ids(lattice)
    hashes = lookup_hashes(lattice)
    identity = identity_diagnostics(lattice, hashes)
    if identity["duplicate_tuple_count"] or identity["hash_collision_count"]:
        raise ValueError("duplicate tuple or lookup hash collision; authoritative export refused")
    lattice = immutable_array(lattice, np.int32)
    physical, internal = immutable_array(physical), immutable_array(internal)
    diagnostics = {
        "enumerated_count": processed,
        "physical_crop_count": cropped,
        "candidate_count": kept,
        "chunk_count": chunks,
        "max_chunk_rows": max_rows,
        "chunk_core_array_bytes_estimate": max_rows * (20 + 8 + 40 + 40 + 16),
        "retained_array_bytes": sum(a.nbytes for a in (lattice, physical, internal, ids, hashes)),
        "memory_measurement": "array storage estimates, not process peak RSS",
        "expansion": expansion,
        **identity,
    }
    return CandidateSet(config, lattice, physical, internal, ids, hashes, diagnostics)


@dataclass(frozen=True, slots=True, eq=False)
class Membership:
    phi: np.ndarray
    selected: np.ndarray
    boundary: np.ndarray
    signed_distance: np.ndarray
    selected_ids: np.ndarray
    selected_physical: np.ndarray
    selected_internal: np.ndarray


def evaluate_membership(candidates: CandidateSet, phi: ArrayLike) -> Membership:
    shift = finite_vectors(phi, 2)
    if shift.shape != (2,):
        raise ValueError("one phason value must have shape [2]")
    domain = candidates.config.phason_domain
    if np.any(shift < domain.min) or np.any(shift > domain.max):
        raise ValueError("phason value is outside the precomputed domain; regenerate candidates")
    window = AcceptanceWindow()
    internal = candidates.internal + shift
    selections, boundaries, distances = [], [], []
    for start in range(0, len(internal), candidates.config.chunk_size):
        classification = window.classify(
            internal[start : start + candidates.config.chunk_size],
            boundary_epsilon=candidates.config.boundary_epsilon_relative
            * window.characteristic_radius,
            boundary_policy=candidates.config.boundary_policy,
        )
        selections.append(classification.selected)
        boundaries.append(classification.states == PointState.BOUNDARY.value)
        distances.append(classification.signed_distance)
    selected = immutable_array(np.concatenate(selections) if selections else [], np.bool_)
    boundary = immutable_array(np.concatenate(boundaries) if boundaries else [], np.bool_)
    distance = immutable_array(np.concatenate(distances) if distances else [])
    return Membership(
        immutable_array(shift),
        selected,
        boundary,
        distance,
        immutable_array(candidates.ids[selected], candidates.ids.dtype),
        immutable_array(candidates.physical[selected]),
        immutable_array(internal[selected]),
    )


def membership_batch(candidates: CandidateSet, phis: ArrayLike) -> Iterator[Membership]:
    """Stream complete per-frame membership without allocating [F,M,edges] temporaries."""
    shifts = finite_vectors(phis, 2)
    if shifts.ndim != 2:
        raise ValueError("batch phason path must have shape [F,2]")
    for shift in shifts:
        yield evaluate_membership(candidates, shift)


def compare_membership(candidates: CandidateSet, previous: Membership, current: Membership) -> dict:
    if (
        previous.selected.shape != candidates.ids.shape
        or current.selected.shape != candidates.ids.shape
    ):
        raise ValueError("membership masks disagree with candidate count")
    return {
        "entered_ids": candidates.ids[current.selected & ~previous.selected].tolist(),
        "exited_ids": candidates.ids[previous.selected & ~current.selected].tolist(),
        "boundary_ids": candidates.ids[current.boundary].tolist(),
    }
