"""Equation-generated canonical C5 orthogonal decomposition of R5."""

from dataclasses import dataclass, field

import numpy as np
from numpy.typing import ArrayLike, NDArray

from qc5d.geometry import finite_vectors, immutable_array


@dataclass(frozen=True, slots=True, eq=False)
class SuperspaceBasis:
    """Immutable canonical basis. Row-vector batches use n @ P.T."""

    P_parallel: NDArray[np.float64] = field(init=False, repr=False)
    P_perp: NDArray[np.float64] = field(init=False, repr=False)
    Q: NDArray[np.float64] = field(init=False, repr=False)

    def __post_init__(self) -> None:
        j = np.arange(5, dtype=np.float64)
        angle = 2.0 * np.pi * j / 5.0
        scale = np.sqrt(2.0 / 5.0)
        parallel = np.vstack(
            (scale * np.cos(angle), scale * np.sin(angle), np.full(5, 1.0 / np.sqrt(5.0)))
        )
        perpendicular = np.vstack((scale * np.cos(2.0 * angle), scale * np.sin(2.0 * angle)))
        object.__setattr__(self, "P_parallel", immutable_array(parallel))
        object.__setattr__(self, "P_perp", immutable_array(perpendicular))
        object.__setattr__(self, "Q", immutable_array(np.vstack((parallel, perpendicular))))

    def project(self, n: ArrayLike) -> tuple[NDArray[np.float64], NDArray[np.float64]]:
        coordinates = finite_vectors(n, 5)
        return coordinates @ self.P_parallel.T, coordinates @ self.P_perp.T

    def reconstruct(self, physical: ArrayLike, internal: ArrayLike) -> NDArray[np.float64]:
        r = finite_vectors(physical, 3)
        u = finite_vectors(internal, 2)
        if r.shape[:-1] != u.shape[:-1]:
            raise ValueError("physical and internal batch shapes must match")
        return np.concatenate((r, u), axis=-1) @ self.Q

    def diagnostics(self, n: ArrayLike | None = None) -> dict[str, float]:
        vectors = np.eye(5) if n is None else finite_vectors(n, 5)
        if vectors.size == 0:
            raise ValueError("reconstruction diagnostics require at least one vector")
        physical, internal = self.project(vectors)
        return {
            "orthogonality_max_abs": float(np.max(np.abs(self.Q @ self.Q.T - np.eye(5)))),
            "cross_subspace_max_abs": float(np.max(np.abs(self.P_parallel @ self.P_perp.T))),
            "completeness_max_abs": float(
                np.max(
                    np.abs(
                        self.P_parallel.T @ self.P_parallel
                        + self.P_perp.T @ self.P_perp
                        - np.eye(5)
                    )
                )
            ),
            "reconstruction_max_abs": float(
                np.max(np.abs(self.reconstruct(physical, internal) - vectors))
            ),
        }

    def to_dict(self) -> dict:
        return {
            "schema_version": "1.0",
            "kind": "canonical_c5_real_dft",
            "coordinate_units": "dimensionless",
            "P_parallel": self.P_parallel.tolist(),
            "P_perp": self.P_perp.tolist(),
            "Q": self.Q.tolist(),
        }

    @classmethod
    def from_dict(cls, payload: dict) -> "SuperspaceBasis":
        if (
            payload.get("schema_version") != "1.0"
            or payload.get("kind") != "canonical_c5_real_dft"
            or payload.get("coordinate_units") != "dimensionless"
        ):
            raise ValueError("unsupported basis schema, kind, or units")
        basis = cls()
        for name, shape in (("P_parallel", (3, 5)), ("P_perp", (2, 5)), ("Q", (5, 5))):
            stored = np.asarray(payload[name], dtype=np.float64)
            if stored.shape != shape or not np.all(np.isfinite(stored)):
                raise ValueError(f"invalid serialized {name}")
            if not np.allclose(stored, getattr(basis, name), rtol=0.0, atol=1e-12):
                raise ValueError(f"serialized {name} disagrees with the canonical equations")
        return basis
