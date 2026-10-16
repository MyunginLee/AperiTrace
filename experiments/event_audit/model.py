"""Experimental convex phason intervals; canonical qc5d remains unchanged."""

from dataclasses import dataclass
from decimal import Decimal, localcontext

import numpy as np

from qc5d.geometry import finite_vectors, immutable_array, signed_boundary_distance


def _inputs(vertices, internal, start, end, epsilon):
    polygon = finite_vectors(vertices, 2)
    points = finite_vectors(internal, 2)
    a, b = finite_vectors(start, 2), finite_vectors(end, 2)
    if polygon.ndim != 2 or points.ndim != 2 or a.shape != (2,) or b.shape != (2,):
        raise ValueError("polygon/points must be matrices and endpoints must be pairs")
    if not np.isfinite(epsilon) or epsilon < 0:
        raise ValueError("epsilon must be finite and nonnegative")
    signed_boundary_distance(polygon, [[0.0, 0.0]])  # independent strict convexity check
    return polygon, points, a, b


@dataclass(frozen=True, slots=True, eq=False)
class Intervals:
    lower: np.ndarray
    upper: np.ndarray
    possible: np.ndarray

    def selected(self, time):
        if not np.isfinite(time) or not 0 <= time <= 1:
            raise ValueError("segment parameter must be finite in [0,1]")
        return self.possible & (self.lower < time) & (time < self.upper)

    @property
    def ever_selected(self):
        return self.possible & (np.maximum(self.lower, 0) < np.minimum(self.upper, 1))

    def critical_parameters(self):
        valid = self.ever_selected
        values = np.concatenate(([0.0, 1.0], self.lower[valid], self.upper[valid]))
        return np.unique(values[np.isfinite(values) & (values >= 0) & (values <= 1)])


def clip_intervals(vertices, internal, start, end, epsilon):
    """Open inequalities inward_slack > epsilon, with no merging of floating roots."""
    polygon, points, a, b = _inputs(vertices, internal, start, end, epsilon)
    edges = np.roll(polygon, -1, axis=0) - polygon
    normals = np.column_stack((-edges[:, 1], edges[:, 0])) / np.linalg.norm(edges, axis=1)[:, None]
    query = points + a
    offset = polygon[:, 0] * normals[:, 0] + polygon[:, 1] * normals[:, 1]
    slack = query[:, 0, None] * normals[:, 0] + query[:, 1, None] * normals[:, 1] - offset - epsilon
    delta = b - a
    slope = delta[0] * normals[:, 0] + delta[1] * normals[:, 1]
    lower = np.full(len(points), -np.inf)
    upper = np.full(len(points), np.inf)
    possible = np.ones(len(points), dtype=np.bool_)
    for edge, velocity in enumerate(slope):
        if velocity > 0:
            lower = np.maximum(lower, -slack[:, edge] / velocity)
        elif velocity < 0:
            upper = np.minimum(upper, -slack[:, edge] / velocity)
        else:
            possible &= slack[:, edge] > 0
    possible &= lower < upper
    return Intervals(
        immutable_array(lower), immutable_array(upper), immutable_array(possible, np.bool_)
    )


def decimal_intervals(vertices, internal, start, end, epsilon):
    """80-digit independent geometry on frozen float inputs, not exact C5 trigonometry."""
    polygon, points, a, b = _inputs(vertices, internal, start, end, epsilon)
    with localcontext() as context:
        context.prec = 80

        def cvt(value):
            return Decimal.from_float(float(value))

        vertices_d = [[cvt(x) for x in row] for row in polygon]
        start_d, end_d = [cvt(x) for x in a], [cvt(x) for x in b]
        epsilon_d = cvt(epsilon)
        rows = []
        for point in points:
            lower, upper, possible = Decimal("-Infinity"), Decimal("Infinity"), True
            query = [cvt(point[j]) + start_d[j] for j in range(2)]
            for index, vertex in enumerate(vertices_d):
                other = vertices_d[(index + 1) % len(vertices_d)]
                dx, dy = other[0] - vertex[0], other[1] - vertex[1]
                length = (dx * dx + dy * dy).sqrt()
                normal = (-dy / length, dx / length)
                slack = sum((query[j] - vertex[j]) * normal[j] for j in range(2)) - epsilon_d
                slope = sum((end_d[j] - start_d[j]) * normal[j] for j in range(2))
                if slope > 0:
                    lower = max(lower, -slack / slope)
                elif slope < 0:
                    upper = min(upper, -slack / slope)
                else:
                    possible &= slack > 0
            rows.append((float(lower), float(upper), possible and lower < upper))
    return Intervals(
        immutable_array([x[0] for x in rows]),
        immutable_array([x[1] for x in rows]),
        immutable_array([x[2] for x in rows], np.bool_),
    )


def phase_cache(positions, q):
    positions, q = finite_vectors(positions, 3), finite_vectors(q, 3)
    if positions.ndim != 2 or q.ndim != 2:
        raise ValueError("positions and q must be matrices")
    if len(positions) * len(q) > 4_000_000:
        raise ValueError("experimental phase cache exceeds four million complex terms")
    phase = positions[:, 0, None] * q[:, 0]
    phase += positions[:, 1, None] * q[:, 1]
    phase += positions[:, 2, None] * q[:, 2]
    if np.any(np.abs(phase) > 1e12):
        raise ValueError("phase magnitude exceeds canonical supported range")
    return np.exp(1j * phase)


def event_amplitudes(intervals, cache, times):
    """Apply every traversed event, including enter/exit pairs between query frames."""
    times = np.asarray(times, dtype=np.float64)
    cache = np.asarray(cache, dtype=np.complex128)
    if (
        times.ndim != 1
        or not len(times)
        or not np.all(np.isfinite(times))
        or np.any(np.diff(times) < 0)
        or times[0] < 0
        or times[-1] > 1
    ):
        raise ValueError("nonempty nondecreasing parameters in [0,1] required")
    if cache.ndim != 2 or len(cache) != len(intervals.lower) or not np.all(np.isfinite(cache)):
        raise ValueError("cache must be a finite candidate-by-q matrix")
    state = intervals.selected(times[0]).copy()
    amplitude = np.zeros(cache.shape[1], dtype=np.complex128)
    if np.any(state):
        amplitude = np.cumsum(cache[state], axis=0, dtype=np.complex128)[-1].copy()
    ever = intervals.ever_selected
    entries = np.flatnonzero(ever & (intervals.lower >= 0) & (intervals.lower <= 1))
    exits = np.flatnonzero(ever & (intervals.upper >= 0) & (intervals.upper <= 1))
    entries = entries[np.lexsort((entries, intervals.lower[entries]))]
    exits = exits[np.lexsort((exits, intervals.upper[exits]))]
    entry_times, exit_times = intervals.lower[entries], intervals.upper[exits]
    entry_cursor = int(np.searchsorted(entry_times, times[0], side="left"))
    exit_cursor = int(np.searchsorted(exit_times, times[0], side="right"))
    amplitudes, counts, changes = [], [], []
    for time in times:
        entry_stop = int(np.searchsorted(entry_times, time, side="left"))
        exit_stop = int(np.searchsorted(exit_times, time, side="right"))
        pending = [(intervals.lower[row], int(row), 1) for row in entries[entry_cursor:entry_stop]]
        pending += [(intervals.upper[row], int(row), -1) for row in exits[exit_cursor:exit_stop]]
        updates = 0
        for _, row, sign in sorted(pending):
            if state[row] == (sign > 0):
                raise ArithmeticError("event replay tried to repeat an unchanged membership")
            state[row] = sign > 0
            amplitude += sign * cache[row]
            updates += 1
        entry_cursor, exit_cursor = entry_stop, exit_stop
        amplitudes.append(amplitude.copy())
        counts.append(int(np.count_nonzero(state)))
        changes.append(updates)
    return (
        np.asarray(amplitudes),
        np.asarray(counts, dtype=np.int32),
        np.asarray(changes, dtype=np.int32),
    )


def sampling_losses(intervals, frames):
    if type(frames) is not int or frames < 2:
        raise ValueError("frame count must be an integer >=2")
    times = np.linspace(0, 1, frames)
    masks = np.array([intervals.selected(t) for t in times])
    interior = intervals.ever_selected & (intervals.lower > 0) & (intervals.upper < 1)
    missed = np.flatnonzero(interior & ~np.any(masks, axis=0))
    ever = intervals.ever_selected
    exact_count = int(
        np.count_nonzero(ever & (intervals.lower > 0) & (intervals.lower < 1))
        + np.count_nonzero(ever & (intervals.upper > 0) & (intervals.upper < 1))
    )
    observed = int(np.count_nonzero(masks[1:] != masks[:-1]))
    return {
        "frames": frames,
        "interior_event_count": exact_count,
        "frame_change_count": observed,
        "missed_excursions": missed.tolist(),
        "unresolved_by_frame_difference": exact_count - observed,
    }
