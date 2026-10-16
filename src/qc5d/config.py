"""Versioned reference-validation settings; no hand-entered scientific matrices."""

from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field


class ReferenceConfig(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True, allow_inf_nan=False)
    schema_version: Literal["1.0"] = "1.0"
    seed: Annotated[int, Field(strict=True, ge=0)] = 0
    reconstruction_samples: Annotated[int, Field(strict=True, ge=1, le=100_000)] = 1024
    reconstruction_index_bound: Annotated[int, Field(strict=True, ge=1, le=1000)] = 8
    numerical_tolerance: Annotated[float, Field(gt=0.0, le=1e-12)] = 1e-12
    boundary_epsilon_relative: Annotated[float, Field(ge=0.0)] = 1e-10
    boundary_policy: Literal["exclude", "include", "report_only"] = "exclude"
