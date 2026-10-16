"""Public semantic-versioned scientific exchange definitions (legacy engines unchanged)."""

from datetime import date
from typing import Annotated, Literal

import numpy as np
from pydantic import Field, HttpUrl, JsonValue, model_validator

from qc5d.basis import SuperspaceBasis
from qc5d.manifest import OutputRecord, RelativeOutputPath, ResultManifest, Sha256
from qc5d.model_config import ModelConfig, Settings
from qc5d.window import AcceptanceWindow

VERSION = "1.0.0"
# Cross-platform scientific comparison, not a file-integrity tolerance.
# Matches the existing basis/window import convention; membership epsilon is unchanged.
SCIENTIFIC_FLOAT_ATOL = 1e-12
Count = Annotated[int, Field(strict=True, ge=0, le=2_000_000)]
Scalar = Annotated[float, Field(ge=-1e6, le=1e6)]
Pair = tuple[Scalar, Scalar]
Five = tuple[float, float, float, float, float]


class Versioned(Settings):
    schema_version: Literal["1.0.0"] = VERSION


class ModelConfiguration(Versioned):
    parameters: ModelConfig


class BasisDefinition(Versioned):
    kind: Literal["canonical_c5_real_dft"] = "canonical_c5_real_dft"
    P_parallel: tuple[Five, Five, Five]
    P_perp: tuple[Five, Five]

    @model_validator(mode="after")
    def canonical(self):
        basis = SuperspaceBasis()
        for name in ("P_parallel", "P_perp"):
            stored, expected = np.asarray(getattr(self, name)), getattr(basis, name)
            if stored.shape != expected.shape or not np.allclose(
                stored, expected, rtol=0, atol=SCIENTIFIC_FLOAT_ATOL
            ):
                raise ValueError(f"{name} differs from the canonical equation-generated basis")
        return self


class WindowDefinition(Versioned):
    kind: Literal["projected_centered_unit_cube"] = "projected_centered_unit_cube"
    vertices_uv: tuple[Pair, ...]
    order: Literal["ccw_from_lexicographic_minimum"] = "ccw_from_lexicographic_minimum"
    signed_distance: Literal["positive_inside_euclidean_edge_segments"] = (
        "positive_inside_euclidean_edge_segments"
    )

    @model_validator(mode="after")
    def canonical(self):
        vertices = np.asarray(self.vertices_uv)
        expected = AcceptanceWindow().vertices
        if vertices.shape != expected.shape or not np.allclose(
            vertices, expected, rtol=0, atol=SCIENTIFIC_FLOAT_ATOL
        ):
            raise ValueError("vertices differ from the generated canonical window")
        return self


class ArrayDescriptor(Settings):
    path: RelativeOutputPath
    dtype: Literal["<i4", "<u4", "<u8", "<f8", "<c16", "|b1", "|i1", "|u1", "<U59"]
    shape: tuple[Count, ...]
    sha256: Sha256


class CandidatePoints(Versioned):
    count: Count
    order: Literal["tuple_lexicographic"] = "tuple_lexicographic"
    datasets: dict[str, ArrayDescriptor]


class SelectedState(Versioned):
    phi: Pair
    selected_count: Count
    boundary_count: Count
    boundary_epsilon: Annotated[float, Field(ge=0, le=1e6)]
    boundary_policy: Literal["exclude", "include"]
    datasets: dict[str, ArrayDescriptor]


class PhasonFrame(Versioned):
    phi: Pair
    selected_ids: tuple[str, ...]
    boundary_ids: tuple[str, ...]
    entered_ids: tuple[str, ...]
    exited_ids: tuple[str, ...]

    @model_validator(mode="after")
    def unique_ids(self):
        for name in ("selected_ids", "boundary_ids", "entered_ids", "exited_ids"):
            values = getattr(self, name)
            if len(set(values)) != len(values):
                raise ValueError(f"duplicate IDs in {name}")
        if set(self.entered_ids) & set(self.exited_ids):
            raise ValueError("an ID cannot enter and exit in the same sampled transition")
        return self


class GraphDefinition(Versioned):
    edge_count: Annotated[int, Field(strict=True, ge=0, le=10_000_000)]
    endpoints: Literal["candidate_row_indices"] = "candidate_row_indices"
    orientation: Literal["positive_generator_once"] = "positive_generator_once"
    datasets: dict[str, ArrayDescriptor]


class DiffractionResult(Versioned):
    q_count: Count
    selected_ids_sha256: Sha256
    central_intensity: Annotated[float, Field(gt=0)]
    weighting: Literal["real_unit"] = "real_unit"
    windowing: Literal["hard_scientific_crop_no_taper"] = "hard_scientific_crop_no_taper"
    normalization: Literal["I(q)/I(0)"] = "I(q)/I(0)"
    sign: Literal["exp(+i*q.r)"] = "exp(+i*q.r)"
    sampling: Literal["explicit_q_xyz_samples"] = "explicit_q_xyz_samples"
    datasets: dict[str, ArrayDescriptor]


class MaterialProvenance(Versioned):
    kind: Literal["canonical_model", "imported_material"] = "canonical_model"
    name: str = "idealized canonical decagonal model set"
    units: str = "dimensionless"
    source_url: HttpUrl | None = None
    source_sha256: Sha256 | None = None
    retrieval_date: date | None = None
    license_or_access_terms: str | None = None
    published_parameters: dict[str, JsonValue] = Field(default_factory=dict)
    references: tuple[str, ...] = ()

    @model_validator(mode="after")
    def recorded_not_inferred(self):
        if not self.name.strip() or not self.units.strip():
            raise ValueError("material name and units must be explicit")
        if self.kind == "imported_material" and (
            self.source_url is None
            or self.source_sha256 is None
            or self.retrieval_date is None
            or not self.license_or_access_terms
            or not self.license_or_access_terms.strip()
            or not self.published_parameters
            or not self.references
        ):
            raise ValueError(
                "imported material requires source, checksum, date, terms, "
                "parameters and references"
            )
        if self.kind == "canonical_model" and (
            self.units != "dimensionless"
            or self.source_url is not None
            or self.source_sha256 is not None
            or self.retrieval_date is not None
            or self.license_or_access_terms is not None
            or self.published_parameters
        ):
            raise ValueError("canonical provenance must not fabricate imported material metadata")
        return self


class SourceFile(Settings):
    path: RelativeOutputPath
    sha256: Sha256


class ScientificMetadata(Versioned):
    kind: Literal["authoritative_canonical_modelset"] = "authoritative_canonical_modelset"
    coordinate_units: Literal["dimensionless"] = "dimensionless"
    q_units: Literal["radians_per_model_length"] = "radians_per_model_length"
    scientific_coordinates_unscaled: Literal[True] = True
    phason_convention: Literal["u=P_perp*n+phi"] = "u=P_perp*n+phi"
    model: ModelConfiguration
    basis: BasisDefinition
    window: WindowDefinition
    candidates: CandidatePoints
    state: SelectedState
    phason_frames: tuple[PhasonFrame, ...]
    graph: GraphDefinition
    diffraction: DiffractionResult | None
    material: MaterialProvenance
    source_files: tuple[SourceFile, ...]
    source_hash: Sha256
    config_hash: Sha256
    input_checksums: dict[str, Sha256]
    code_revision: str | None
    dependency_versions: dict[str, str]


class ScientificEnvelope(Versioned):
    file_name: Literal["authoritative.npz"] = "authoritative.npz"
    authoritative_sha256: Sha256
    metadata: ScientificMetadata


class ScientificResultManifest(Versioned):
    provenance: ResultManifest


class ExportConfig(Versioned):
    coordinate_atol: Annotated[float, Field(gt=0, le=1e-6)] = 1e-6
    boundary_distance_atol: Annotated[float, Field(gt=0, le=1e-6)] = 1e-6
    preview_boundary_epsilon: Annotated[float, Field(ge=0, le=1e-3)] = 1e-6
    internal_uv_scale: Annotated[float, Field(gt=0, le=1e6)] = 1.0


class FixtureManifest(Versioned):
    files: tuple[OutputRecord, ...]


class GoldenFixtureConfig(Versioned):
    model: ModelConfiguration
    phasons: tuple[Pair, ...]
    q_vectors: tuple[tuple[Scalar, Scalar, Scalar], ...]
    runtime: ExportConfig = Field(default_factory=ExportConfig)


def schema_registry() -> dict:
    return {
        name: {
            "$schema": "https://json-schema.org/draft/2020-12/schema",
            **model.model_json_schema(),
        }
        for name, model in (
            ("model_configuration", ModelConfiguration),
            ("basis", BasisDefinition),
            ("acceptance_window", WindowDefinition),
            ("candidate_points", CandidatePoints),
            ("selected_state", SelectedState),
            ("phason_frame", PhasonFrame),
            ("graph", GraphDefinition),
            ("diffraction_result", DiffractionResult),
            ("scientific_result_manifest", ScientificResultManifest),
            ("material_provenance", MaterialProvenance),
            ("scientific_dataset", ScientificEnvelope),
            ("runtime_export_config", ExportConfig),
            ("fixture_manifest", FixtureManifest),
            ("golden_fixture_config", GoldenFixtureConfig),
        )
    }
