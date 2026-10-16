"""Finite patch settings and safe, duplicate-key-rejecting YAML/JSON input."""

from pathlib import Path
from typing import Annotated, Literal

import yaml
from pydantic import BaseModel, ConfigDict, Field, model_validator

Positive = Annotated[float, Field(gt=0, le=1e6)]
Coordinate = Annotated[float, Field(ge=-1e6, le=1e6)]


class Settings(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True, allow_inf_nan=False)


class PhysicalCrop(Settings):
    kind: Literal["cylinder", "sphere"] = "cylinder"
    radial_max: Positive = 10.0
    z_abs_max: Positive = 3.0


class PhasonDomain(Settings):
    kind: Literal["box"] = "box"
    min: tuple[Coordinate, Coordinate] = (-0.25, -0.25)
    max: tuple[Coordinate, Coordinate] = (0.25, 0.25)

    @model_validator(mode="after")
    def ordered(self):
        if any(low > high for low, high in zip(self.min, self.max, strict=True)):
            raise ValueError("phason box min must not exceed max")
        return self


class PhysicalScale(Settings):
    xy: Positive = 1.0
    z: Positive = 0.35


class ModelConfig(Settings):
    schema_version: Literal["1.0"] = "1.0"
    model_name: Literal["canonical_decagonal_3plus2"] = "canonical_decagonal_3plus2"
    index_bound: Annotated[int, Field(strict=True, ge=0, le=1000)] = 7
    physical_crop: PhysicalCrop = Field(default_factory=PhysicalCrop)
    phason_domain: PhasonDomain = Field(default_factory=PhasonDomain)
    boundary_policy: Literal["exclude", "include"] = "exclude"
    boundary_epsilon_relative: Annotated[float, Field(ge=0, le=1e6)] = 1e-10
    physical_scale: PhysicalScale = Field(default_factory=PhysicalScale)
    initial_phi: tuple[Coordinate, Coordinate] = (0.0, 0.0)
    chunk_size: Annotated[int, Field(strict=True, ge=1, le=1_000_000)] = 65536
    max_candidates: Annotated[int, Field(strict=True, ge=1, le=10_000_000)] = 2_000_000

    @model_validator(mode="after")
    def initial_in_domain(self):
        for value, low, high in zip(
            self.initial_phi, self.phason_domain.min, self.phason_domain.max, strict=True
        ):
            if not low <= value <= high:
                raise ValueError("initial_phi must lie within the configured phason domain")
        return self


class UniqueSafeLoader(yaml.SafeLoader):
    """Never interpret Python tags or silently replace duplicate settings."""


def _unique_mapping(loader, node, deep=False):
    loader.flatten_mapping(node)
    result = {}
    for key_node, value_node in node.value:
        key = loader.construct_object(key_node, deep=deep)
        if not isinstance(key, str) or key in result:
            raise ValueError("configuration keys must be unique strings")
        result[key] = loader.construct_object(value_node, deep=deep)
    return result


UniqueSafeLoader.add_constructor(yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG, _unique_mapping)


def read_settings(path: Path) -> dict:
    try:
        payload = yaml.load(path.read_text(encoding="utf-8"), Loader=UniqueSafeLoader)
    except yaml.YAMLError as error:
        raise ValueError("invalid or unsafe YAML/JSON settings") from error
    if not isinstance(payload, dict):
        raise ValueError("settings must be a mapping")
    return payload


def read_model_config(path: Path) -> ModelConfig:
    return ModelConfig.model_validate(read_settings(path))
