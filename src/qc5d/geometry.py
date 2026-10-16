"""Float64 planar geometry with an explicit Euclidean boundary-distance convention."""

import numpy as np
from numpy.typing import ArrayLike, NDArray
from scipy.spatial import ConvexHull


def immutable_array(value: ArrayLike, dtype=np.float64) -> NDArray:
    """Use an immutable bytes buffer, so write protection cannot be switched off."""
    array = np.asarray(value, dtype=dtype, order="C")
    return np.frombuffer(array.tobytes(order="C"), dtype=array.dtype).reshape(array.shape)


def finite_vectors(value: ArrayLike, dimension: int) -> NDArray[np.float64]:
    array = np.asarray(value, dtype=np.float64)
    if array.ndim == 0 or array.shape[-1] != dimension:
        raise ValueError(f"expected vectors with final dimension {dimension}")
    if not np.all(np.isfinite(array)):
        raise ValueError("coordinates must be finite")
    return array


def cross2(a: NDArray, b: NDArray) -> NDArray:
    return a[..., 0] * b[..., 1] - a[..., 1] * b[..., 0]


def signed_area(vertices: ArrayLike) -> float:
    points = finite_vectors(vertices, 2)
    return float(np.sum(cross2(points, np.roll(points, -1, axis=0))) / 2.0)


def convex_hull_indices(points: ArrayLike) -> NDArray[np.int32]:
    """Compute a CCW hull and start at the lexicographically smallest (u0,u1)."""
    array = finite_vectors(points, 2)
    if array.ndim != 2 or len(array) < 3:
        raise ValueError("a planar hull requires at least three points")
    indices = ConvexHull(array).vertices
    if signed_area(array[indices]) < 0:
        indices = indices[::-1]
    vertices = array[indices]
    start = int(np.lexsort((vertices[:, 1], vertices[:, 0]))[0])
    return immutable_array(np.roll(indices, -start), np.int32)


def polygon_turns(vertices: ArrayLike) -> NDArray[np.float64]:
    polygon = finite_vectors(vertices, 2)
    edges = np.roll(polygon, -1, axis=0) - polygon
    return cross2(edges, np.roll(edges, -1, axis=0))


def signed_boundary_distance(vertices: ArrayLike, points: ArrayLike) -> NDArray[np.float64]:
    """Positive inside, negative outside: distance to the nearest finite edge segment."""
    polygon = finite_vectors(vertices, 2)
    query = finite_vectors(points, 2)
    if polygon.ndim != 2 or len(polygon) < 3 or not np.all(polygon_turns(polygon) > 0):
        raise ValueError("polygon must be strictly convex and counter-clockwise")
    polygon_edges = np.roll(polygon, -1, axis=0) - polygon
    vertex_offsets = polygon[None, :, :] - polygon[:, None, :]
    if not np.all(cross2(polygon_edges[:, None, :], vertex_offsets) >= 0.0):
        raise ValueError("polygon must be convex, without self-intersections")
    shape = query.shape[:-1]
    flat = query.reshape(-1, 2)
    edges = np.roll(polygon, -1, axis=0) - polygon
    lengths_squared = np.sum(edges * edges, axis=1)
    relative = flat[:, None, :] - polygon[None, :, :]
    parameters = np.clip(np.sum(relative * edges, axis=2) / lengths_squared, 0.0, 1.0)
    residual = relative - parameters[..., None] * edges
    distance = np.sqrt(np.min(np.sum(residual * residual, axis=2), axis=1))
    inside = np.all(cross2(edges, relative) >= 0.0, axis=1)
    return np.where(inside, distance, -distance).reshape(shape)


def symmetry_residual(vertices: ArrayLike, transform: ArrayLike) -> float:
    """Maximum nearest-vertex mismatch after a supplied linear symmetry transform."""
    polygon = finite_vectors(vertices, 2)
    transformed = polygon @ np.asarray(transform, dtype=np.float64).T
    distances = np.linalg.norm(transformed[:, None, :] - polygon[None, :, :], axis=2)
    return float(np.max(np.min(distances, axis=1)))
