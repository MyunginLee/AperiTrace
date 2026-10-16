"""Linear-offset interval inspection with explicit float64 boundary diagnostics.

The stable module retains the experimental clipping arithmetic. Sensitive query
states are recomputed by the finite-edge reference predicate, never certified as
exact-real geometry. Only integer tuple identity is exact.
"""

from dataclasses import dataclass

import numpy as np

from qc5d.diffraction import real_array
from qc5d.geometry import immutable_array, signed_boundary_distance


def parameters(values):
    times = real_array(values)
    if (
        times.ndim != 1
        or not len(times)
        or np.any(np.diff(times) < 0)
        or times[0] < 0
        or times[-1] > 1
    ):
        raise ValueError("nonempty nondecreasing parameters in [0,1] required")
    return times


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


def polygon_data(vertices, internal, start, end, epsilon):
    polygon = real_array(vertices, shape_tail=2)
    points = real_array(internal, shape_tail=2)
    a, b = real_array(start), real_array(end)
    if a.shape != (2,) or b.shape != (2,):
        raise ValueError("endpoints must be pairs")
    if not np.isfinite(epsilon) or epsilon < 0:
        raise ValueError("epsilon must be finite and nonnegative")
    signed_boundary_distance(polygon, [[0.0, 0.0]])
    edges = np.roll(polygon, -1, axis=0) - polygon
    normals = np.column_stack((-edges[:, 1], edges[:, 0])) / np.linalg.norm(edges, axis=1)[:, None]
    query = points + a
    offset = polygon[:, 0] * normals[:, 0] + polygon[:, 1] * normals[:, 1]
    slack = query[:, 0, None] * normals[:, 0] + query[:, 1, None] * normals[:, 1] - offset - epsilon
    delta = b - a
    slope = delta[0] * normals[:, 0] + delta[1] * normals[:, 1]
    return polygon, points, a, b, normals, offset, slack, slope


def clip_intervals(vertices, internal, start, end, epsilon):
    """Unclipped open geometric bounds; selected(0/1) preserves interior endpoints."""
    *_, slack, slope = polygon_data(vertices, internal, start, end, epsilon)
    lower, upper = np.full(len(slack), -np.inf), np.full(len(slack), np.inf)
    possible = np.ones(len(slack), dtype=np.bool_)
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


@dataclass(frozen=True, slots=True, eq=False)
class PhaseCache:
    """Immutable unit-weight cache, reusable across paths, not across q/coordinates."""

    positions: np.ndarray
    q: np.ndarray
    values: np.ndarray

    @classmethod
    def build(cls, positions, q, *, max_terms=25_000_000):
        positions, q = real_array(positions, shape_tail=3), real_array(q, shape_tail=3)
        if type(max_terms) is not int or max_terms < 1 or len(positions) * len(q) > max_terms:
            raise ValueError("phase-cache memory limit exceeded")
        phase = positions[:, 0, None] * q[:, 0]
        phase += positions[:, 1, None] * q[:, 1]
        phase += positions[:, 2, None] * q[:, 2]
        if not np.isfinite(phase).all() or np.any(np.abs(phase) > 1e12):
            raise ValueError("phase exceeds supported finite range")
        return cls(
            immutable_array(positions),
            immutable_array(q),
            immutable_array(np.exp(1j * phase), np.complex128),
        )


def sum_cache(cache, mask):
    if not np.any(mask):
        return np.zeros(cache.shape[1], dtype=np.complex128)
    return np.cumsum(cache[mask], axis=0, dtype=np.complex128)[-1].copy()


@dataclass(frozen=True, slots=True, eq=False)
class QueryResult:
    parameters: np.ndarray
    amplitudes: np.ndarray
    counts: np.ndarray
    status: np.ndarray
    changed_events: np.ndarray

    @property
    def normalized_intensity(self):
        """Unit-weight |F|^2/N^2; the empty set explicitly returns zero."""
        return np.abs(self.amplitudes) ** 2 / np.maximum(self.counts, 1)[:, None] ** 2


class LinearPath:
    """Frozen geometry, a linear path and no tolerance-based root merging.

    A conservative root-distance diagnostic includes every facet root, even
    inactive ones. It over-flags some queries. The guard is heuristic, separate
    from scientific epsilon. At flagged states output is reference-double with
    status='uncertain'. Clear queries retain clipped float64 membership.
    """

    def __init__(self, vertices, internal, start, end, epsilon):
        polygon, points, a, b, normals, offset, slack, slope = polygon_data(
            vertices, internal, start, end, epsilon
        )
        self.vertices, self.internal = immutable_array(polygon), immutable_array(points)
        self.start, self.end = immutable_array(a), immutable_array(b)
        self.epsilon = float(epsilon)
        self.normals, self.offset = immutable_array(normals), immutable_array(offset)
        self.intervals = clip_intervals(polygon, points, a, b, epsilon)
        maxnorm = float(np.max(np.linalg.norm(points, axis=1), initial=0))
        radius = float(np.max(np.linalg.norm(polygon, axis=1)))
        self.guard = (
            64
            * np.finfo(float).eps
            * (1 + radius + maxnorm + max(np.linalg.norm(a), np.linalg.norm(b)))
        )
        moving = slope != 0
        roots = -slack[:, moving] / slope[moving]
        widths = self.guard / np.abs(slope[moving])
        band_starts, band_ends = (roots - widths).ravel(), (roots + widths).ravel()
        relevant = (band_ends >= 0) & (band_starts <= 1)
        # Independently sorted diagnostic band endpoints count overlapping bands.
        # They do not alter, merge or group any membership-event root.
        self.diagnostic_starts = immutable_array(np.sort(band_starts[relevant]))
        self.diagnostic_ends = immutable_array(np.sort(band_ends[relevant]))
        self.parallel_sensitive = bool(np.any(np.abs(slack[:, ~moving]) <= self.guard))
        valid = self.intervals.ever_selected
        entries = np.flatnonzero(valid & (self.intervals.lower >= 0) & (self.intervals.lower <= 1))
        exits = np.flatnonzero(valid & (self.intervals.upper >= 0) & (self.intervals.upper <= 1))
        self.entries = entries[np.lexsort((entries, self.intervals.lower[entries]))]
        self.exits = exits[np.lexsort((exits, self.intervals.upper[exits]))]
        self.entry_times = self.intervals.lower[self.entries]
        self.exit_times = self.intervals.upper[self.exits]

    def phi(self, parameter):
        parameters([parameter])
        return self.start + parameter * (self.end - self.start)

    def sensitive(self, parameter):
        parameters([parameter])
        if self.parallel_sensitive:
            return True
        opened = np.searchsorted(self.diagnostic_starts, parameter, side="right")
        closed = np.searchsorted(self.diagnostic_ends, parameter, side="left")
        return bool(opened > closed)

    def reference_mask(self, parameter):
        return (
            signed_boundary_distance(self.vertices, self.internal + self.phi(parameter))
            > self.epsilon
        )

    def halfspace_mask(self, parameter):
        p = self.internal + self.phi(parameter)
        return np.all(
            p[:, 0, None] * self.normals[:, 0] + p[:, 1, None] * self.normals[:, 1] - self.offset
            > self.epsilon,
            axis=1,
        )

    def state(self, parameter):
        uncertain = self.sensitive(parameter)
        return (
            self.reference_mask(parameter) if uncertain else self.intervals.selected(parameter),
            "uncertain" if uncertain else "clear_float64",
        )

    def query(self, cache: PhaseCache, times, *, rebaseline_every=128):
        times = parameters(times)
        if not isinstance(cache, PhaseCache) or len(cache.positions) != len(self.internal):
            raise ValueError("phase-cache candidate rows must match path")
        if type(rebaseline_every) is not int or rebaseline_every < 1:
            raise ValueError("rebaseline period must be a positive integer")
        interval = self.intervals
        state = interval.selected(times[0]).copy()
        amplitude = sum_cache(cache.values, state)
        ec = int(np.searchsorted(self.entry_times, times[0], side="left"))
        xc = int(np.searchsorted(self.exit_times, times[0], side="right"))
        count = int(state.sum())
        amplitudes, counts, statuses, changes = [], [], [], []
        for index, time in enumerate(times):
            es = int(np.searchsorted(self.entry_times, time, side="left"))
            xs = int(np.searchsorted(self.exit_times, time, side="right"))
            entering, exiting = self.entries[ec:es], self.exits[xc:xs]
            # All changes reach one query atomically. No intermediate ID-order states
            # are exported. Strict roots exclude entries and already remove exits.
            pending = [(interval.lower[row], int(row), 1) for row in entering]
            pending += [(interval.upper[row], int(row), -1) for row in exiting]
            for _, row, sign in sorted(pending):
                if state[row] == (sign > 0):
                    raise ArithmeticError("invalid event ordering or repeated update")
                state[row] = sign > 0
                amplitude += sign * cache.values[row]
                count += sign
            ec, xc = es, xs
            if index and index % rebaseline_every == 0:
                amplitude = sum_cache(cache.values, state)
            uncertain = self.sensitive(time)
            if uncertain:
                reference = self.reference_mask(time)
                output, n = sum_cache(cache.values, reference), int(reference.sum())
            else:
                output, n = amplitude.copy(), count
            amplitudes.append(output)
            counts.append(n)
            statuses.append("uncertain" if uncertain else "clear_float64")
            changes.append(len(entering) + len(exiting))
        return QueryResult(
            immutable_array(times),
            immutable_array(amplitudes, np.complex128),
            immutable_array(counts, np.int32),
            immutable_array(statuses, "U16"),
            immutable_array(changes, np.int32),
        )
