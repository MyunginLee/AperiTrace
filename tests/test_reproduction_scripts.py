"""Fixed comparison contract and non-overwriting release helper checks."""

import importlib.util
import subprocess
import sys
from pathlib import Path

import pytest
import yaml

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location(
    "numerical_summary", ROOT / "scripts/numerical_summary.py"
)
summary = importlib.util.module_from_spec(spec)
spec.loader.exec_module(summary)


def test_reference_tolerance():
    summary.compare(
        {"value": [1.0], "ids": [[0, -1, 1, 0, 1]]},
        {"value": [1.0 + 1e-13], "ids": [[0, -1, 1, 0, 1]]},
    )


@pytest.mark.parametrize(
    "actual,expected",
    [
        (1.000000001, 1.0),
        (127, 126),
        ([1], [1, 2]),
        ({"extra": 1}, {}),
        ("uncertain", "clear_float64"),
        ([[0, -1, 1, 0, 0]], [[0, -1, 1, 0, 1]]),
    ],
)
def test_mismatch_is_not_pass(actual, expected):
    with pytest.raises(AssertionError):
        summary.compare(actual, expected)


def test_install_refuses_existing_environment(tmp_path):
    env = tmp_path / "existing"
    env.mkdir()
    marker = env / "preserve"
    marker.write_text("unchanged")
    result = subprocess.run(
        [
            sys.executable,
            ROOT / "scripts/clean_install.py",
            "--env",
            env,
            "--record",
            tmp_path / "record.json",
        ],
        capture_output=True,
    )
    assert result.returncode != 0 and marker.read_text() == "unchanged"
    assert not (env / "pyvenv.cfg").exists()


def test_release_identity():
    cff = yaml.safe_load((ROOT / "CITATION.cff").read_text())
    assert cff["version"] == "0.1.0" and cff["license"] == "MIT"
    assert len(cff["authors"]) == 1
    assert {
            "family-names": "Lee",
            "given-names": "Myungin",
            "affiliation": "College of Computer, Mathematical, and Natural Sciences, University of Maryland, College Park, MD, USA",
        }.items() <= cff["authors"][0].items()
    assert (ROOT / "LICENSE").read_bytes() == (ROOT / "Licence.txt").read_bytes()
    # Publication adds real identifiers/contact; it must not break scientific tests.
    if "repository-code" in cff:
        assert cff["repository-code"].startswith("https://")
    if "doi" in cff:
        assert cff["doi"].startswith("10.")
