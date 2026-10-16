"""Versioned reciprocal sampling and predeclared scientific validation tolerances."""

from typing import Annotated, Literal

from pydantic import Field, model_validator

from qc5d.model_config import Coordinate, ModelConfig, Settings


class DiffractionConfig(Settings):
    schema_version: Literal["1.0"] = "1.0"
    mode: Literal["cartesian", "reciprocal", "both"] = "both"
    weighting: Literal["real_unit"] = "real_unit"
    windowing: Literal["none"] = "none"
    normalization: Literal["central"] = "central"
    q_extent: Annotated[float, Field(gt=0, le=1000)] = 12.0
    grid_samples: Annotated[int, Field(strict=True, ge=3, le=501)] = 61
    q_z: Coordinate = 0.0
    reciprocal_index_bound: Annotated[int, Field(strict=True, ge=0, le=5)] = 2
    deduplication_tolerance: Annotated[float, Field(gt=0, le=1e-3)] = 1e-10
    q_z_tolerance: Annotated[float, Field(gt=0, le=1e-6)] = 1e-10
    reciprocal_cross_section_only: bool = True
    q_chunk_size: Annotated[int, Field(strict=True, ge=1, le=4096)] = 512
    point_chunk_size: Annotated[int, Field(strict=True, ge=1, le=4096)] = 256
    max_phase_pairs: Annotated[int, Field(strict=True, ge=1, le=1_000_000_000)] = 100_000_000
    polar_radial_samples: Annotated[int, Field(strict=True, ge=3, le=201)] = 25
    polar_angular_samples: Annotated[int, Field(strict=True, ge=20, le=3600)] = 180
    central_mask_radius: Annotated[float, Field(gt=0, le=1000)] = 0.75
    low_signal_threshold: Annotated[float, Field(gt=0, lt=1)] = 1e-4
    top_k: Annotated[int, Field(strict=True, ge=1, le=100)] = 20
    peak_tie_decimals: Annotated[int, Field(strict=True, ge=8, le=15)] = 12
    friedel_atol: Annotated[float, Field(gt=0, le=1e-12)] = 1e-12
    tenfold_nrmse_max: Annotated[float, Field(gt=0, le=1e-10)] = 1e-10
    tenfold_correlation_min: Annotated[float, Field(ge=0.9999999999, le=1)] = 0.9999999999
    require_tenfold: bool = True
    phason_values: tuple[tuple[Coordinate, Coordinate], ...] | None = None

    @model_validator(mode="after")
    def sampling_constraints(self):
        if self.grid_samples % 2 != 1:
            raise ValueError("Cartesian grid must have odd size to include the origin")
        if self.polar_angular_samples % 10:
            raise ValueError("polar angle count must be divisible by ten")
        if self.central_mask_radius >= self.q_extent:
            raise ValueError("central mask radius must be smaller than reciprocal extent")
        if self.require_tenfold and abs(self.q_z) > self.q_z_tolerance:
            raise ValueError("tenfold validation requires the q_z≈0 cross-section")
        if self.phason_values is not None and not 2 <= len(self.phason_values) <= 100:
            raise ValueError("short phason diffraction path needs 2..100 frames")
        return self


class ConvergenceConfig(Settings):
    schema_version: Literal["1.0"] = "1.0"
    model: ModelConfig
    diffraction: DiffractionConfig = Field(default_factory=DiffractionConfig)
