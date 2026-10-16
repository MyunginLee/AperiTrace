"""Bounded-memory complex128 finite-patch structure factors and numerical metrics."""

import numpy as np
from numpy.typing import ArrayLike
from scipy.ndimage import maximum_filter

from qc5d.diffraction_config import DiffractionConfig
from qc5d.geometry import immutable_array


def real_array(value: ArrayLike, *, shape_tail: int | None = None) -> np.ndarray:
    array = np.asarray(value)
    if array.dtype.kind not in "fiu":
        raise ValueError("real numeric input required; complex values are never silently truncated")
    array = np.asarray(array, dtype=np.float64)
    if not np.all(np.isfinite(array)):
        raise ValueError("all real coordinates/weights must be finite")
    if shape_tail is not None and (array.ndim != 2 or array.shape[1] != shape_tail):
        raise ValueError(f"expected a [M,{shape_tail}] array")
    return array


def working_block_shape(q_count: int, point_count: int, chunk_size: int, point_chunk_size: int):
    for size in (chunk_size, point_chunk_size):
        if type(size) is not int or not 1 <= size <= 4096:
            raise ValueError("chunk sizes must be integers in [1,4096]")
    point_rows = min(max(point_count, 1), point_chunk_size)
    q_rows = min(max(q_count, 1), chunk_size, max(1, 1_000_000 // point_rows))
    return q_rows, point_rows


def structure_factor(
    positions_xyz: ArrayLike,
    q_vectors_xyz: ArrayLike,
    weights: ArrayLike | None = None,
    chunk_size: int = 512,
    *,
    point_chunk_size: int = 256,
) -> np.ndarray:
    """F(q)=sum f_m exp(+i q.r_m), with fixed-order carried cumulative sums."""
    positions = real_array(positions_xyz, shape_tail=3)
    q = real_array(q_vectors_xyz, shape_tail=3)
    weights = np.ones(len(positions)) if weights is None else real_array(weights)
    if weights.shape != (len(positions),):
        raise ValueError("weights must be a real [M] vector matching positions")
    q_rows, point_rows = working_block_shape(len(q), len(positions), chunk_size, point_chunk_size)
    with np.errstate(over="raise", invalid="raise"):
        try:
            bound = float(np.sum(np.abs(weights)))
            if bound > np.sqrt(np.finfo(np.float64).max) / 2:
                raise ValueError("weight sum exceeds supported finite intensity range")
            output = np.zeros(len(q), dtype=np.complex128)
            for start in range(0, len(q), q_rows):
                queries = q[start : start + q_rows]
                carry = np.zeros(len(queries), dtype=np.complex128)
                for offset in range(0, len(positions), point_rows):
                    points = positions[offset : offset + point_rows]
                    phase = queries[:, 0, None] * points[None, :, 0]
                    phase += queries[:, 1, None] * points[None, :, 1]
                    phase += queries[:, 2, None] * points[None, :, 2]
                    if np.any(np.abs(phase) > 1e12):
                        raise ValueError("phase magnitude exceeds supported 1e12 radian range")
                    terms = np.empty(phase.shape, dtype=np.complex128)
                    terms.real = 0.0
                    terms.imag = phase
                    np.exp(terms, out=terms)
                    terms *= weights[None, offset : offset + len(points)]
                    # Carry participates in the first addition, preserving global point order.
                    terms[:, 0] += carry
                    np.cumsum(terms, axis=1, dtype=np.complex128, out=terms)
                    carry = terms[:, -1].copy()
                output[start : start + len(queries)] = carry
        except FloatingPointError as error:
            raise ValueError("unsupported input would overflow float64 diffraction") from error
    if not np.all(np.isfinite(output)):
        raise ValueError("nonfinite structure factor")
    return immutable_array(output, np.complex128)


def intensity(amplitudes: ArrayLike) -> np.ndarray:
    array = np.asarray(amplitudes, dtype=np.complex128)
    if not np.all(np.isfinite(array)):
        raise ValueError("amplitudes must be finite")
    with np.errstate(over="raise", invalid="raise"):
        try:
            value = array.real**2 + array.imag**2
        except FloatingPointError as error:
            raise ValueError("intensity would overflow float64") from error
    return immutable_array(value)


def normalize_intensity(values: ArrayLike, central_intensity: float) -> np.ndarray:
    array = real_array(values)
    if np.any(array < 0) or not np.isfinite(central_intensity) or central_intensity <= 0:
        raise ValueError("central normalization needs nonnegative intensity and finite I(0)>0")
    with np.errstate(over="raise", invalid="raise"):
        try:
            normalized = array / central_intensity
        except FloatingPointError as error:
            raise ValueError("normalization would overflow float64") from error
    return immutable_array(normalized)


def cartesian_grid(config: DiffractionConfig) -> tuple[np.ndarray, np.ndarray]:
    axis = np.linspace(-config.q_extent, config.q_extent, config.grid_samples)
    x, y = np.meshgrid(axis, axis, indexing="xy")
    q = np.column_stack((x.ravel(), y.ravel(), np.full(x.size, config.q_z)))
    return immutable_array(axis), immutable_array(q)


def polar_grid(config: DiffractionConfig) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    radii = np.linspace(0, config.q_extent, config.polar_radial_samples)
    angles = 2 * np.pi * np.arange(config.polar_angular_samples) / config.polar_angular_samples
    q = np.stack(
        (
            radii[:, None] * np.cos(angles),
            radii[:, None] * np.sin(angles),
            np.full((len(radii), len(angles)), config.q_z),
        ),
        axis=-1,
    ).reshape(-1, 3)
    return immutable_array(radii), immutable_array(angles), immutable_array(q)


def angular_metrics(normalized_map: ArrayLike, radii: ArrayLike, config: DiffractionConfig) -> dict:
    image, radius = real_array(normalized_map), real_array(radii)
    expected = (config.polar_radial_samples, config.polar_angular_samples)
    if image.shape != expected or radius.shape != (expected[0],) or np.any(image < 0):
        raise ValueError("polar intensity/radius shapes disagree with configuration")
    results = []
    step = config.polar_angular_samples // 10
    for rotation in range(1, 10):
        shifted = np.roll(image, rotation * step, axis=1)
        mask = (radius[:, None] >= config.central_mask_radius) & (
            np.maximum(image, shifted) >= config.low_signal_threshold
        )
        a, b = image[mask], shifted[mask]
        if len(a) < 2:
            results.append(
                {
                    "degrees": rotation * 36,
                    "sample_count": len(a),
                    "nrmse": None,
                    "correlation": None,
                }
            )
            continue
        rms = float(np.sqrt(np.mean((a * a + b * b) / 2)))
        residual = float(np.sqrt(np.mean((a - b) ** 2)) / rms) if rms > 0 else None
        a0, b0 = a - np.mean(a), b - np.mean(b)
        denominator = float(np.sqrt(np.sum(a0 * a0) * np.sum(b0 * b0)))
        correlation = (
            float(np.clip(np.sum(a0 * b0) / denominator, -1, 1)) if denominator > 0 else None
        )
        results.append(
            {
                "degrees": rotation * 36,
                "sample_count": len(a),
                "nrmse": residual,
                "correlation": correlation,
            }
        )
    valid = all(row["nrmse"] is not None and row["correlation"] is not None for row in results)
    max_nrmse = max(row["nrmse"] for row in results) if valid else None
    min_correlation = min(row["correlation"] for row in results) if valid else None
    return {
        "valid": valid,
        "rotations": results,
        "maximum_nrmse": max_nrmse,
        "minimum_correlation": min_correlation,
        "passed": valid
        and max_nrmse <= config.tenfold_nrmse_max
        and min_correlation >= config.tenfold_correlation_min,
        "central_mask_radius": config.central_mask_radius,
        "low_signal_threshold": config.low_signal_threshold,
        "nrmse_denominator": "RMS of both masked normalized maps",
        "mask": "radius>=central_mask AND max(I,I_rotated)>=low_signal",
    }


def ordered_peaks(
    q: ArrayLike, values: ArrayLike, config: DiffractionConfig, *, grid=False
) -> list[dict]:
    vectors, intensities = real_array(q, shape_tail=3), real_array(values)
    if intensities.shape != (len(vectors),) or np.any(intensities < 0):
        raise ValueError("peak q/intensity dimensions or values disagree")
    eligible = np.linalg.norm(vectors[:, :2], axis=1) >= config.central_mask_radius
    eligible &= intensities >= config.low_signal_threshold
    if grid:
        image = intensities.reshape(config.grid_samples, config.grid_samples)
        maximum = maximum_filter(image, size=3, mode="constant", cval=-np.inf)
        eligible &= (image == maximum).ravel()
    indices = np.flatnonzero(eligible)
    keys = vectors[indices]
    ranks = np.round(intensities[indices], config.peak_tie_decimals)
    order = np.lexsort((keys[:, 2], keys[:, 1], keys[:, 0], -ranks))
    return [
        {
            "sample_index": int(i),
            "q_xyz": vectors[i].tolist(),
            "normalized_intensity": float(intensities[i]),
        }
        for i in indices[order[: config.top_k]]
    ]


def peak_comparison(first: list[dict], second: list[dict]) -> dict:
    if not first or not second:
        return {
            "valid": False,
            "nearest_peak_position_rmse": None,
            "matched_intensity_rmse": None,
            "top_k_coordinate_overlap": 0,
        }
    a, b = np.array([p["q_xyz"] for p in first]), np.array([p["q_xyz"] for p in second])
    distances = np.linalg.norm(a[:, None, :] - b[None, :, :], axis=-1)
    nearest = np.argmin(distances, axis=1)
    delta = np.array([p["normalized_intensity"] for p in first]) - np.array(
        [second[i]["normalized_intensity"] for i in nearest]
    )
    return {
        "valid": True,
        "nearest_peak_position_rmse": float(np.sqrt(np.mean(np.min(distances, axis=1) ** 2))),
        "matched_intensity_rmse": float(np.sqrt(np.mean(delta**2))),
        "top_k_coordinate_overlap": len(set(map(tuple, a)) & set(map(tuple, b))),
        "matching": "directed nearest neighbor; ties resolved by second peak order",
    }
