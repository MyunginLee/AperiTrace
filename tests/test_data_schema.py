import json
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator, FormatChecker
from pydantic import ValidationError

from qc5d.data_schema import ExportConfig, MaterialProvenance, schema_registry

ROOT = Path(__file__).resolve().parents[1]


@pytest.mark.parametrize("name", list(schema_registry()))
def test_checked_in_public_schemas_match_and_reject_unknown_major(name):
    schema = schema_registry()[name]
    assert json.loads((ROOT / "schemas" / f"{name}_v1.schema.json").read_text("utf-8")) == schema
    Draft202012Validator.check_schema(schema)
    assert schema["properties"]["schema_version"]["const"] == "1.0.0"
    assert list(Draft202012Validator(schema).iter_errors({"schema_version": "2.0.0"}))


@pytest.mark.parametrize("version", ["2.0.0", "0.9.0", "1.1.0", "1.0", 1, None])
def test_public_models_reject_unsupported_versions(version):
    with pytest.raises(ValidationError):
        ExportConfig(schema_version=version)


def test_material_provenance_requires_real_recorded_evidence():
    assert MaterialProvenance().kind == "canonical_model"
    with pytest.raises(ValidationError, match="requires source"):
        MaterialProvenance(kind="imported_material")
    payload = dict(
        kind="imported_material",
        name="Schema example, not a validated material",
        source_url="https://example.org/data",
        source_sha256="a" * 64,
        retrieval_date="2026-10-03",
        license_or_access_terms="Recorded example terms",
        units="angstrom",
        published_parameters={"scale": 1.0},
        references=["Example reference"],
    )
    material = MaterialProvenance.model_validate(payload)
    Draft202012Validator(
        schema_registry()["material_provenance"], format_checker=FormatChecker()
    ).validate(material.model_dump(mode="json"))
    for key in ("source_url", "source_sha256", "retrieval_date", "license_or_access_terms"):
        with pytest.raises(ValidationError):
            MaterialProvenance.model_validate({**payload, key: None})
    with pytest.raises(ValidationError):
        MaterialProvenance(units="angstrom")
    with pytest.raises(ValidationError):
        MaterialProvenance(source_url="https://example.org")


@pytest.mark.parametrize(
    "updates",
    [
        {"coordinate_atol": 1e-3},
        {"boundary_distance_atol": 0},
        {"preview_boundary_epsilon": float("nan")},
        {"internal_uv_scale": -1},
        {"extra": True},
    ],
)
def test_export_tolerances_are_finite_bounded_and_explicit(updates):
    with pytest.raises(ValidationError):
        ExportConfig.model_validate(updates)
