"""Deterministic phason paths, frame events, and exact forward/reverse auditing."""

import csv
from dataclasses import dataclass
from pathlib import Path
from typing import Annotated, Literal

import numpy as np
from pydantic import Field, model_validator

from qc5d.geometry import finite_vectors, immutable_array
from qc5d.model_config import Coordinate, Positive, Settings, read_settings
from qc5d.modelset import CandidateSet, compare_membership, membership_batch

Pair = tuple[Coordinate, Coordinate]


class PathConfig(Settings):
    schema_version: Literal["1.0"] = "1.0"
    kind: Literal["line", "circle", "closed_loop"]
    frames: Annotated[int, Field(strict=True, ge=2, le=10000)] = 49
    start: Pair | None = None
    end: Pair | None = None
    center: Pair | None = None
    radius: Positive | None = None
    vertices: tuple[Pair, ...] | None = None

    @model_validator(mode="after")
    def fields_for_kind(self):
        provided = {
            k
            for k in ("start", "end", "center", "radius", "vertices")
            if getattr(self, k) is not None
        }
        required = {
            "line": {"start", "end"},
            "circle": {"center", "radius"},
            "closed_loop": {"vertices"},
        }[self.kind]
        if provided != required:
            raise ValueError(f"{self.kind} requires only {sorted(required)}")
        if self.kind == "closed_loop" and (
            len(self.vertices) < 3 or self.frames < len(self.vertices) + 1
        ):
            raise ValueError("closed_loop needs at least three vertices and one frame per segment")
        return self


@dataclass(frozen=True, slots=True, eq=False)
class PhasonPath:
    values: np.ndarray
    metadata: dict


def make_path(config: PathConfig) -> PhasonPath:
    if config.kind == "line":
        values = np.linspace(config.start, config.end, config.frames)
    elif config.kind == "circle":
        angle = np.linspace(0.0, 2 * np.pi, config.frames)
        values = np.column_stack((np.cos(angle), np.sin(angle))) * config.radius + config.center
        values[-1] = values[0]
    else:
        vertices = np.asarray((*config.vertices, config.vertices[0]), dtype=np.float64)
        parameter = np.linspace(0, len(config.vertices), config.frames)
        values = np.column_stack(
            [np.interp(parameter, np.arange(len(vertices)), vertices[:, j]) for j in range(2)]
        )
        values[-1] = values[0]
    return PhasonPath(immutable_array(values), config.model_dump(mode="json", exclude_none=True))


def read_path(path: Path) -> PhasonPath:
    if path.suffix.lower() != ".csv":
        return make_path(PathConfig.model_validate(read_settings(path)))
    with path.open(encoding="utf-8", newline="") as stream:
        reader = csv.DictReader(stream)
        if reader.fieldnames != ["phi_u", "phi_v"]:
            raise ValueError("CSV path must have exactly the header phi_u,phi_v")
        rows = []
        for row in reader:
            if set(row) != {"phi_u", "phi_v"} or None in row.values():
                raise ValueError("CSV row does not have two coordinates")
            rows.append((float(row["phi_u"]), float(row["phi_v"])))
            if len(rows) > 10000:
                raise ValueError("CSV paths support at most 10000 frames")
    if len(rows) < 2:
        raise ValueError("CSV path requires at least two frames")
    values = finite_vectors(rows, 2)
    return PhasonPath(immutable_array(values), {"schema_version": "1.0", "kind": "csv"})


@dataclass(frozen=True, slots=True, eq=False)
class Sweep:
    records: list[dict]
    diagnostics: dict
    selected_masks: np.ndarray
    boundary_masks: np.ndarray
    signed_distances: np.ndarray


def phason_sweep(candidates: CandidateSet, path: PhasonPath) -> Sweep:
    values = finite_vectors(path.values, 2)
    if values.ndim != 2 or not 2 <= len(values) <= 10000:
        raise ValueError("sweep needs a [F,2] path with 2..10000 frames")
    # Recording itself is F*M; refuse unbounded recording before allocation.
    if len(values) * len(candidates.ids) * 10 > 512 * 1024**2:
        raise ValueError("sweep recording exceeds 512 MiB; reduce frames or patch")
    records, selected, boundary, distances = [], [], [], []
    previous = None
    for frame, state in enumerate(membership_batch(candidates, values)):
        changes = (
            {
                "entered_ids": [],
                "exited_ids": [],
                "boundary_ids": candidates.ids[state.boundary].tolist(),
            }
            if previous is None
            else compare_membership(candidates, previous, state)
        )
        records.append(
            {
                "frame": frame,
                "phi_u": float(state.phi[0]),
                "phi_v": float(state.phi[1]),
                "selected_count": len(state.selected_ids),
                **changes,
            }
        )
        selected.append(state.selected)
        boundary.append(state.boundary)
        distances.append(state.signed_distance)
        previous = state
    selected = np.asarray(selected)
    boundary = np.asarray(boundary)
    distances = np.asarray(distances)
    reversible = True
    for reverse_frame, state in enumerate(membership_batch(candidates, values[::-1])):
        index = len(values) - 1 - reverse_frame
        reversible &= bool(np.array_equal(state.selected, selected[index]))
        reversible &= bool(np.array_equal(state.boundary, boundary[index]))
    closed = bool(np.array_equal(values[0], values[-1]))
    nonsingular_frames = not bool(np.any(boundary))
    same_endpoints = bool(np.array_equal(selected[0], selected[-1]))
    crossings = 0
    crossing_consistency = True
    for frame in range(1, len(values)):
        changed = selected[frame] != selected[frame - 1]
        away = changed & ~boundary[frame] & ~boundary[frame - 1]
        crossings += int(np.count_nonzero(away))
        crossing_consistency &= bool(
            np.all(distances[frame, away] * distances[frame - 1, away] < 0)
        )
    passed = reversible and crossing_consistency and (not closed or same_endpoints)
    return Sweep(
        records,
        {
            "passed": passed,
            "frame_count": len(values),
            "closed": closed,
            "nonsingular_sampled_frames": nonsingular_frames,
            "initial_final_selected_ids_equal": same_endpoints,
            "forward_reverse_consistent": reversible,
            "nonboundary_changes_have_signed_distance_crossings": crossing_consistency,
            "nonboundary_change_count": crossings,
            "boundary_frame_count": int(np.count_nonzero(np.any(boundary, axis=1))),
            "total_entered": sum(len(r["entered_ids"]) for r in records),
            "total_exited": sum(len(r["exited_ids"]) for r in records),
            "event_semantics": "differences between sampled frames; not continuous crossing times",
        },
        immutable_array(selected, np.bool_),
        immutable_array(boundary, np.bool_),
        immutable_array(distances),
    )
