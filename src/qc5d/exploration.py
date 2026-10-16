"""Stable, headless linked-state API and independently recomputable JSON export."""

import argparse
import json
from pathlib import Path

import numpy as np

from qc5d.basis import SuperspaceBasis
from qc5d.data_schema import SCIENTIFIC_FLOAT_ATOL
from qc5d.diffraction import real_array, structure_factor
from qc5d.events import LinearPath, PhaseCache
from qc5d.identity import stable_ids
from qc5d.model_config import ModelConfig, PhasonDomain, PhysicalCrop
from qc5d.modelset import evaluate_membership, generate_candidates, project_tuples
from qc5d.window import AcceptanceWindow


def _same_geometry(stored, canonical):
    """Roundoff-bounded reconstruction, never an identity/membership tolerance."""
    try:
        value = _real_values(stored)
    except (ValueError, TypeError):
        return False
    return (
        value.dtype.kind in "fiu"
        and value.shape == canonical.shape
        and np.allclose(value, canonical, atol=SCIENTIFIC_FLOAT_ATOL, rtol=0, equal_nan=False)
    )


def _focus_tuple(value):
    try:
        values = list(value)
    except TypeError as error:
        raise ValueError("focus must be an exact five-integer tuple") from error
    if len(values) != 5 or any(
        isinstance(x, (bool, np.bool_)) or not isinstance(x, (int, np.integer)) for x in values
    ):
        raise ValueError("focus must be an exact five-integer tuple")
    return tuple(int(x) for x in values)


def _real_values(value):
    raw = np.asarray(value, dtype=object)
    if any(
        isinstance(x, (bool, np.bool_)) or not isinstance(x, (int, float, np.integer, np.floating))
        for x in raw.flat
    ):
        raise ValueError("real numeric values required; booleans are not scientific numbers")
    return real_array(value)


class Exploration:
    def __init__(self, candidates, q):
        n = candidates.lattice_n
        if (
            n.ndim != 2
            or n.shape[1] != 5
            or n.dtype.kind not in "iu"
            or len(np.unique(n, axis=0)) != len(n)
            or not np.array_equal(stable_ids(n), candidates.ids)
        ):
            raise ValueError("unique integer tuples and matching stable IDs required")
        if candidates.config.boundary_policy != "exclude":
            raise ValueError("event exploration supports only explicit exclude convention")
        physical, internal = project_tuples(n, SuperspaceBasis())
        if (
            not _same_geometry(candidates.physical, physical)
            or not _same_geometry(candidates.internal, internal)
            or np.any(np.abs(n.astype(np.float64)) > candidates.config.index_bound)
        ):
            raise ValueError("candidate coordinates must match canonical tuple projection")
        self.candidates = candidates
        self.cache = PhaseCache.build(candidates.physical, q)
        self.window = AcceptanceWindow()
        self.epsilon = (
            candidates.config.boundary_epsilon_relative * self.window.characteristic_radius
        )

    def path(self, start, end):
        # Reference validation also checks endpoint shape, finiteness and support.
        evaluate_membership(self.candidates, start)
        evaluate_membership(self.candidates, end)
        return LinearPath(self.window.vertices, self.candidates.internal, start, end, self.epsilon)

    def export_state(self, path, parameter, focus):
        if (
            not np.array_equal(path.internal, self.candidates.internal)
            or not np.array_equal(path.vertices, self.window.vertices)
            or path.epsilon != self.epsilon
        ):
            raise ValueError("path does not belong to this exploration geometry")
        mask, status = path.state(parameter)
        result = path.query(self.cache, [parameter])
        focus = _focus_tuple(focus)
        if focus not in {tuple(row) for row in self.candidates.lattice_n}:
            raise ValueError("focus must be an existing exact tuple")
        return {
            "schema": "qc5d-linked-state/1",
            "model": self.candidates.config.model_dump(mode="json"),
            "units": "dimensionless; angular q in inverse coordinate units",
            "selection": "signed finite-edge interior distance > epsilon; u+phi; exclude",
            "epsilon": self.epsilon,
            "weights": "unit",
            "q": self.cache.q.tolist(),
            "candidate_tuples": self.candidates.lattice_n.tolist(),
            "candidate_ids": self.candidates.ids.tolist(),
            "selected_ids": self.candidates.ids[mask].tolist(),
            "physical": self.candidates.physical.tolist(),
            "internal": self.candidates.internal.tolist(),
            "window": self.window.vertices.tolist(),
            "start": path.start.tolist(),
            "end": path.end.tolist(),
            "parameter": float(parameter),
            "phi": path.phi(parameter).tolist(),
            "focus": list(focus),
            "status": status,
            "status_meaning": (
                "heuristic float64 clearance; uncertain uses reference-double, "
                "not exact-real certification"
            ),
            "N": int(result.counts[0]),
            "F_real": result.amplitudes[0].real.tolist(),
            "F_imag": result.amplitudes[0].imag.tolist(),
            "I_normalized": result.normalized_intensity[0].tolist(),
        }


def recompute_state(payload):
    """Reconstruct from integer tuples; do not trust serialized masks or amplitudes."""
    if (
        payload.get("schema") != "qc5d-linked-state/1"
        or payload.get("weights") != "unit"
        or payload.get("units") != "dimensionless; angular q in inverse coordinate units"
        or payload.get("selection")
        != "signed finite-edge interior distance > epsilon; u+phi; exclude"
    ):
        raise ValueError("unsupported export conventions")
    config = ModelConfig.model_validate(payload["model"])
    raw_n = np.asarray(payload["candidate_tuples"], dtype=object)
    if (
        raw_n.ndim != 2
        or raw_n.shape[1] != 5
        or any(
            isinstance(x, (bool, np.bool_)) or not isinstance(x, (int, np.integer))
            for x in raw_n.flat
        )
    ):
        raise ValueError("invalid integer identity; booleans and floats are not lattice integers")
    n = np.asarray(payload["candidate_tuples"])
    if (
        n.ndim != 2
        or n.shape[1] != 5
        or n.dtype.kind not in "iu"
        or np.any(np.abs(n.astype(np.float64)) > config.index_bound)
        or len(np.unique(n, axis=0)) != len(n)
    ):
        raise ValueError("invalid or duplicate integer identity")
    ids = stable_ids(n)
    r, u = project_tuples(n, SuperspaceBasis())
    w = AcceptanceWindow()
    epsilon = config.boundary_epsilon_relative * w.characteristic_radius
    if (
        not isinstance(payload["epsilon"], (int, float))
        or isinstance(payload["epsilon"], bool)
        or not np.isclose(payload["epsilon"], epsilon, rtol=64 * np.finfo(float).eps, atol=0)
        or config.boundary_policy != "exclude"
    ):
        raise ValueError("selection convention mismatch")
    if payload["candidate_ids"] != ids.tolist() or _focus_tuple(payload["focus"]) not in {
        tuple(row) for row in n
    }:
        raise ValueError("identity or focus mismatch")
    for stored, wanted in (
        (payload["physical"], r),
        (payload["internal"], u),
        (payload["window"], w.vertices),
    ):
        if not _same_geometry(stored, wanted):
            raise ValueError("exported geometry disagrees with canonical identity")
    path = LinearPath(
        w.vertices, u, _real_values(payload["start"]), _real_values(payload["end"]), epsilon
    )
    phi = path.phi(payload["parameter"])
    if (
        not _same_geometry(payload["phi"], phi)
        or np.any(phi < config.phason_domain.min)
        or np.any(phi > config.phason_domain.max)
    ):
        raise ValueError("invalid exported phason")
    mask = path.reference_mask(payload["parameter"])
    expected_status = "uncertain" if path.sensitive(payload["parameter"]) else "clear_float64"
    if payload.get("status") != expected_status:
        raise ValueError("numerical status lost or changed")
    if (
        type(payload["N"]) is not int
        or payload["selected_ids"] != ids[mask].tolist()
        or payload["N"] != int(mask.sum())
    ):
        raise ValueError("selected identities or N disagree with reference")
    q = _real_values(payload["q"])
    if q.shape == (0,):
        q = q.reshape(0, 3)  # Linked schema defines q as triples, including zero rows.
    amplitude = structure_factor(r[mask], q)
    real, imag = _real_values(payload["F_real"]), _real_values(payload["F_imag"])
    if real.shape != amplitude.shape or imag.shape != amplitude.shape:
        raise ValueError("invalid exported amplitude")
    old = real + 1j * imag
    scale = max(1, int(mask.sum()))
    af = float(np.max(np.abs(old - amplitude), initial=0) / scale)
    ni = np.abs(amplitude) ** 2 / scale**2
    stored_i = _real_values(payload["I_normalized"])
    if stored_i.shape != ni.shape or not np.isfinite(stored_i).all():
        raise ValueError("invalid exported intensity")
    ai = float(np.max(np.abs(stored_i - ni), initial=0))
    if af > SCIENTIFIC_FLOAT_ATOL or ai > SCIENTIFIC_FLOAT_ATOL:
        raise ValueError("exported diffraction disagrees with direct recomputation")
    return {
        "N": int(mask.sum()),
        "status": expected_status,
        "count_normalized_amplitude_error": af,
        "absolute_normalized_intensity_error": ai,
    }


def bundled_example():
    config = ModelConfig(
        index_bound=3,
        physical_crop=PhysicalCrop(radial_max=2.0, z_abs_max=1.5),
        phason_domain=PhasonDomain(min=(-0.75, -0.75), max=(0.75, 0.75)),
    )
    candidates = generate_candidates(config)
    grid = np.linspace(-8, 8, 11)
    x, y = np.meshgrid(grid, grid)
    q = np.column_stack((x.ravel(), y.ravel(), np.zeros(x.size)))
    return Exploration(candidates, q)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--recompute", type=Path)
    args = parser.parse_args(argv)
    if args.recompute:
        print(
            json.dumps(
                recompute_state(json.loads(args.recompute.read_text(encoding="utf-8"))), indent=2
            )
        )
    elif args.output:
        app = bundled_example()
        path = app.path((-0.61, 0.43), (0.67, -0.31))
        row = np.flatnonzero(np.all(app.candidates.lattice_n == (0, -1, 1, 0, 1), axis=1))[0]
        midpoint = (path.intervals.lower[row] + path.intervals.upper[row]) / 2
        payload = app.export_state(path, midpoint, (0, -1, 1, 0, 1))
        with args.output.open("x", encoding="utf-8") as stream:
            json.dump(payload, stream, indent=2, allow_nan=False)
        print(json.dumps(recompute_state(payload), indent=2))
    else:
        parser.error("--output or --recompute required")


if __name__ == "__main__":
    main()
