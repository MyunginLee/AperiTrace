"""Stable identity is the exact tuple, not a position in a finite patch."""

import hashlib

import numpy as np
from numpy.typing import ArrayLike

from qc5d.geometry import immutable_array


def lattice_tuples(value: ArrayLike) -> np.ndarray:
    array = np.asarray(value)
    if array.ndim != 2 or array.shape[1] != 5 or array.dtype.kind not in "iu":
        raise ValueError("lattice tuples must be an integer [M,5] array")
    limits = np.iinfo(np.int32)
    if np.any(array < limits.min) or np.any(array > limits.max):
        raise ValueError("lattice coordinates must fit signed int32")
    return np.asarray(array, dtype=np.int32)


def tuple_id(row: ArrayLike) -> str:
    array = lattice_tuples(np.asarray(row).reshape(1, -1))[0]
    return ",".join(str(int(value)) for value in array)


def stable_ids(value: ArrayLike) -> np.ndarray:
    array = lattice_tuples(value)
    # Maximum five signed int32 decimals and four separators = 59 characters.
    return immutable_array([tuple_id(row) for row in array], "U59")


def lookup_hashes(value: ArrayLike) -> np.ndarray:
    """BLAKE2b digest_size=8 on 20 LE signed-int32 bytes; digest decoded LE uint64."""
    array = lattice_tuples(value).astype("<i4", copy=False)
    hashes = [
        int.from_bytes(hashlib.blake2b(row.tobytes(), digest_size=8).digest(), "little")
        for row in array
    ]
    return immutable_array(hashes, np.uint64)


def identity_diagnostics(value: ArrayLike, hashes: np.ndarray | None = None) -> dict:
    array = lattice_tuples(value)
    unique = len(np.unique(array, axis=0))
    hashes = lookup_hashes(array) if hashes is None else np.asarray(hashes)
    if hashes.shape != (len(array),):
        raise ValueError("hash count disagrees with tuple count")
    return {
        "tuple_count": len(array),
        "duplicate_tuple_count": len(array) - unique,
        "hash_collision_count": unique - len(np.unique(hashes)),
    }
