"""Validated provenance records for generated results, independent of scientific algorithms."""

from datetime import UTC, datetime
from typing import Annotated, Literal

from pydantic import AwareDatetime, BaseModel, ConfigDict, Field, StringConstraints, field_validator

Sha256 = Annotated[str, StringConstraints(strict=True, pattern=r"^[0-9a-f]{64}$")]
NonemptyString = Annotated[str, StringConstraints(strict=True, min_length=1)]
RelativeOutputPath = Annotated[
    str,
    StringConstraints(
        strict=True,
        pattern=r"^[^/\\:\x00-\x1f]+(?:/[^/\\:\x00-\x1f]+)*$",
    ),
]


class PlatformRecord(BaseModel):
    """Public environment identifiers, excluding user names and absolute paths."""

    model_config = ConfigDict(extra="forbid")
    system: NonemptyString
    release: NonemptyString
    machine: NonemptyString
    python_version: NonemptyString


class OutputRecord(BaseModel):
    """An output's portable project-relative path, byte count, and exact checksum."""

    model_config = ConfigDict(extra="forbid")
    path: RelativeOutputPath = Field(
        description="Project-relative POSIX path; '.' and '..' segments are forbidden.",
        json_schema_extra={"not": {"pattern": r"(^|/)\.\.?(/|$)"}},
    )
    sha256: Sha256
    size_bytes: Annotated[int, Field(strict=True, ge=0)]

    @field_validator("path")
    @classmethod
    def reject_dot_segments(cls, value: str) -> str:
        if any(part in {".", ".."} for part in value.split("/")):
            raise ValueError("output path must not contain '.' or '..' segments")
        return value


class ResultManifest(BaseModel):
    """Version 1 provenance contract. Null revision explicitly means no Git revision."""

    model_config = ConfigDict(extra="forbid")
    schema_version: Literal["1.0"]
    command: Annotated[list[NonemptyString], Field(min_length=1)]
    config_hash: Sha256
    source_hash: Sha256
    code_revision: (
        Annotated[str, StringConstraints(strict=True, pattern=r"^(?:[0-9a-f]{40}|[0-9a-f]{64})$")]
        | None
    )
    platform: PlatformRecord
    timestamp: AwareDatetime
    outputs: list[OutputRecord]

    @field_validator("timestamp", mode="before")
    @classmethod
    def require_timestamp_type(cls, value: object) -> object:
        if not isinstance(value, (str, datetime)):
            raise ValueError("timestamp must be an aware datetime or ISO date-time string")
        return value

    @field_validator("timestamp")
    @classmethod
    def use_utc(cls, value: datetime) -> datetime:
        return value.astimezone(UTC)


def result_manifest_schema() -> dict:
    """Produce the checked-in JSON Schema contract without network resolution."""
    return {
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        **ResultManifest.model_json_schema(),
    }
