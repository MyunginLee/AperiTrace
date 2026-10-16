"""Acceptance window from all centered 5D cube vertices, with explicit boundary semantics."""

from dataclasses import dataclass, field
from enum import StrEnum
from itertools import product

import numpy as np
from numpy.typing import ArrayLike, NDArray

from qc5d.basis import SuperspaceBasis
from qc5d.geometry import (
    convex_hull_indices,
    immutable_array,
    polygon_turns,
    signed_area,
    signed_boundary_distance,
    symmetry_residual,
)


class PointState(StrEnum):
    INSIDE = "INSIDE"
    OUTSIDE = "OUTSIDE"
    BOUNDARY = "BOUNDARY"


class BoundaryPolicy(StrEnum):
    EXCLUDE = "exclude"
    INCLUDE = "include"
    REPORT_ONLY = "report_only"


@dataclass(frozen=True, slots=True, eq=False)
class Classification:
    states: NDArray
    signed_distance: NDArray[np.float64]
    selected: NDArray[np.bool_] | None
    boundary_policy: BoundaryPolicy
    boundary_epsilon: float


def classify_polygon(
    vertices: ArrayLike,
    points: ArrayLike,
    *,
    boundary_epsilon: float,
    boundary_policy: str | BoundaryPolicy = BoundaryPolicy.EXCLUDE,
) -> Classification:
    if not np.isfinite(boundary_epsilon) or boundary_epsilon < 0.0:
        raise ValueError("boundary_epsilon must be finite and nonnegative")
    policy = BoundaryPolicy(boundary_policy)
    distance = signed_boundary_distance(vertices, points)
    states = np.where(
        distance > boundary_epsilon,
        PointState.INSIDE.value,
        np.where(distance < -boundary_epsilon, PointState.OUTSIDE.value, PointState.BOUNDARY.value),
    )
    selected = None
    if policy != BoundaryPolicy.REPORT_ONLY:
        mask = states == PointState.INSIDE.value
        if policy == BoundaryPolicy.INCLUDE:
            mask |= states == PointState.BOUNDARY.value
        selected = immutable_array(mask, np.bool_)
    return Classification(
        immutable_array(states, states.dtype),
        immutable_array(distance),
        selected,
        policy,
        float(boundary_epsilon),
    )


@dataclass(frozen=True, slots=True, eq=False)
class AcceptanceWindow:
    basis: SuperspaceBasis = field(default_factory=SuperspaceBasis, repr=False)
    cube_vertices: NDArray[np.float64] = field(init=False, repr=False)
    projected_cube_vertices: NDArray[np.float64] = field(init=False, repr=False)
    hull_indices: NDArray[np.int32] = field(init=False, repr=False)
    vertices: NDArray[np.float64] = field(init=False, repr=False)

    def __post_init__(self) -> None:
        cube = immutable_array(list(product((-0.5, 0.5), repeat=5)))
        _, projected = self.basis.project(cube)
        indices = convex_hull_indices(projected)
        object.__setattr__(self, "cube_vertices", cube)
        object.__setattr__(self, "projected_cube_vertices", immutable_array(projected))
        object.__setattr__(self, "hull_indices", indices)
        object.__setattr__(self, "vertices", immutable_array(projected[indices]))

    @property
    def characteristic_radius(self) -> float:
        """Circumradius about the specified centered-window origin."""
        return float(np.max(np.linalg.norm(self.vertices, axis=1)))

    @property
    def boundary_epsilon(self) -> float:
        return 1e-10 * self.characteristic_radius

    def classify(
        self,
        points: ArrayLike,
        *,
        boundary_epsilon: float | None = None,
        boundary_policy: str | BoundaryPolicy = BoundaryPolicy.EXCLUDE,
    ) -> Classification:
        return classify_polygon(
            self.vertices,
            points,
            boundary_epsilon=self.boundary_epsilon
            if boundary_epsilon is None
            else boundary_epsilon,
            boundary_policy=boundary_policy,
        )

    def diagnostics(self) -> dict:
        angle = np.pi / 5.0
        rotation = np.array(((np.cos(angle), -np.sin(angle)), (np.sin(angle), np.cos(angle))))
        return {
            "cube_vertex_count": len(self.cube_vertices),
            "hull_vertex_count": len(self.vertices),
            "area": signed_area(self.vertices),
            "minimum_convex_turn": float(np.min(polygon_turns(self.vertices))),
            "central_symmetry_residual": symmetry_residual(self.vertices, -np.eye(2)),
            "tenfold_rotation_residual": symmetry_residual(self.vertices, rotation),
            "characteristic_radius": self.characteristic_radius,
        }

    def to_dict(self) -> dict:
        return {
            "schema_version": "1.0",
            "kind": "projected_centered_unit_cube",
            "coordinate_units": "dimensionless",
            "hull_order": "counter_clockwise_from_lexicographic_minimum",
            "signed_distance_convention": "positive_inside_euclidean_to_edge_segments",
            "cube_vertices": self.cube_vertices.tolist(),
            "projected_cube_vertices": self.projected_cube_vertices.tolist(),
            "hull_indices": self.hull_indices.tolist(),
            "vertices_uv": self.vertices.tolist(),
            "characteristic_radius": self.characteristic_radius,
            "boundary_epsilon": self.boundary_epsilon,
            "boundary_policy": "exclude",
        }

    @classmethod
    def from_dict(cls, payload: dict, basis: SuperspaceBasis | None = None) -> "AcceptanceWindow":
        window = cls(SuperspaceBasis() if basis is None else basis)
        expected = window.to_dict()
        metadata = (
            "schema_version",
            "kind",
            "coordinate_units",
            "hull_order",
            "signed_distance_convention",
            "boundary_policy",
        )
        if any(payload.get(name) != expected[name] for name in metadata):
            raise ValueError("unsupported window schema or conventions")
        for name in ("cube_vertices", "projected_cube_vertices", "vertices_uv"):
            stored = np.asarray(payload[name], dtype=np.float64)
            wanted = np.asarray(expected[name], dtype=np.float64)
            if stored.shape != wanted.shape or not np.allclose(stored, wanted, rtol=0, atol=1e-12):
                raise ValueError(f"serialized {name} disagrees with the generated window")
        if payload.get("hull_indices") != expected["hull_indices"]:
            raise ValueError("serialized hull indices disagree with the generated order")
        for name in ("characteristic_radius", "boundary_epsilon"):
            if payload.get(name) != expected[name]:
                raise ValueError(f"serialized {name} disagrees with the generated window")
        return window
