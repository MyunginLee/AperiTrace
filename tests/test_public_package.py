"""Public source hygiene, retained input integrity and portable benchmark setup."""

import hashlib
import importlib.util
import os
import re
import subprocess
from pathlib import Path

import numpy as np
import pytest

ROOT = Path(__file__).resolve().parents[1]


def public_files():
    if (ROOT / ".git").exists():
        paths = subprocess.check_output(
            ["git", "-C", str(ROOT), "ls-files", "-z"], text=True
        ).split("\0")
        return [ROOT / path for path in paths if path]
    # A source ZIP may have been opened in Unity before Python validation.
    # Prune generated engine/dependency caches without inspecting their binaries.
    ignored = {
        "__pycache__",
        ".pytest_cache",
        ".ruff_cache",
        ".git",
        ".vs",
        ".idea",
        "build",
        "builds",
        "dist",
        "library",
        "temp",
        "obj",
        "logs",
        "usersettings",
    }
    files = []
    for folder, directories, names in os.walk(ROOT):
        directories[:] = [
            name
            for name in directories
            if name.lower() not in ignored
            and not name.startswith((".venv", "reproduction", "changed-path"))
            and not name.endswith(".egg-info")
        ]
        for name in names:
            if name in {
                "clean-install.json",
                "coverage.json",
                "state.json",
                "inspected-state.json",
            }:
                continue
            if name.endswith((".before-public-links", ".csproj", ".sln", ".user")):
                continue
            files.append(Path(folder) / name)
    return files


def test_zip_source_inventory_excludes_unity_caches_but_not_private_notes(tmp_path, monkeypatch):
    library = tmp_path / "unity/XRSuperspace/Library/PackageCache"
    library.mkdir(parents=True)
    (library / "foreign.dll").write_bytes(b"\x00\x01")
    (tmp_path / "private_notes.md").write_text("not an allowed generated artifact")
    monkeypatch.setitem(public_files.__globals__, "ROOT", tmp_path)
    assert public_files() == [tmp_path / "private_notes.md"]


def test_no_private_work_material_in_public_source():
    for path in public_files():
        rel = path.relative_to(ROOT)
        assert rel.parts[0].lower() not in {
            "reports",
            "validation_records",
            "prompts",
            "figure_previews",
            "results",
            "paper",
            "manuscript",
            "submission",
            "drafts",
        }, rel
        assert not any(
            word in path.name
            for word in (
                "REPORT",
                "REVIEWER_SIMULATION",
                "CHECKLIST",
                "AUTHOR_ACTIONS",
                "SYNC_REPORT",
            )
        ), rel
        assert path.suffix.lower() not in {
            ".log",
            ".apk",
            ".aab",
            ".exe",
            ".dll",
            ".jks",
            ".keystore",
            ".tex",
            ".pdf",
            ".bib",
            ".doc",
            ".docx",
            ".zip",
            ".cls",
            ".sty",
        }


def test_metadata_update_does_not_create_manuscript_files():
    spec = importlib.util.spec_from_file_location(
        "public_links", ROOT / "scripts/apply_public_links.py"
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    plan = module.build_plan(
        ROOT,
        {
            "repository_url": "https://github.com/example/aperitrace",
            "version_url": "https://github.com/example/aperitrace/releases/tag/v0.1.0",
            "documentation_url": "https://example.org/aperitrace",
        },
    )
    assert {path.relative_to(ROOT).as_posix() for path in plan} == {
        "README.md",
        "RELEASE_NOTES.md",
        "CITATION.cff",
        "pyproject.toml",
    }


def test_no_embedded_editorial_work_notes_or_private_paths():
    forbidden = re.compile(
        r"design_inspiration|manual_visual_review|fig1-00|jac_research|"
        r"major_revision|softwarex_rebuild|author-supplied|author-requested|"
        r"Journal of Applied Crystallography methods/software draft|"
        r"C:[/\\]+Users[/\\]+|/mnt/[a-z]/Users/|/home/[^/\s]+/|"
        r"-----BEGIN (?:RSA |OPENSSH )?PRIVATE KEY|(?:ghp_|github_pat_)[A-Za-z0-9_]{20,}"
    )
    for path in public_files():
        if path == Path(__file__).resolve() or path.suffix in {".ttf", ".npz", ".qc5d", ".png"}:
            continue
        assert not forbidden.search(path.read_text(encoding="utf-8-sig")), path.relative_to(ROOT)


def test_capture_sidecar_contains_only_reproduction_fields():
    import json

    path = ROOT / "publication_data/examples/unity_after_sidecar.json"
    capture = json.loads(path.read_text())
    assert set(capture["state"]) == {"session", "selected_ids", "diffraction"}
    assert set(capture["state"]["session"]["instrument"]) == {"SelectedStableId", "Phi"}
    assert "state_json" not in capture and "labels" not in capture


def test_retained_inputs_are_checksum_locked():
    folder = ROOT / "publication_data"
    rows = (folder / "SHA256SUMS").read_text().splitlines()
    listed = set()
    for row in rows:
        digest, relative = row.split("  ", 1)
        path = (folder / relative).resolve()
        assert path.is_relative_to(folder.resolve())
        assert hashlib.sha256(path.read_bytes()).hexdigest() == digest
        listed.add(relative)
    actual = {
        p.relative_to(folder).as_posix()
        for p in folder.rglob("*")
        if p.is_file() and p.name != "SHA256SUMS"
    }
    assert actual == listed


def benchmark_module():
    spec = importlib.util.spec_from_file_location("public_benchmark", ROOT / "scripts/benchmark.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@pytest.mark.parametrize("radius,count", [(2, 571), (4, 2131), (6, 4871)])
def test_benchmark_regenerates_canonical_support(radius, count):
    internal, physical, start, end, epsilon, roots = benchmark_module().canonical_workload(
        f"r{radius}_oblique"
    )
    assert len(internal) == len(physical) == count
    assert roots[0] == 0 and roots[-1] == 1 and np.all(np.diff(roots) > 0)
    np.testing.assert_array_equal(start, [-0.61, 0.43])
    np.testing.assert_array_equal(end, [0.67, -0.31])
    assert 0 < epsilon < 1e-9


def test_benchmark_methods_match_direct_finite_sum():
    module = benchmark_module()
    internal, physical, start, end, epsilon, _ = module.canonical_workload("r2_oblique")
    q = [[0.0, 0.0, 0.0], [1.6, 0.0, 0.0], [1.6, 0.0, 1.0]]
    times = np.linspace(0, 1, 9)
    reference = None
    for method in ("B0", "B1", "B2", "B3"):
        _, f, n, intensity = module.workload(
            module.AcceptanceWindow().vertices,
            internal,
            physical,
            start,
            end,
            epsilon,
            q,
            times,
            method,
        )
        if reference is None:
            reference = f, n, intensity
        else:
            np.testing.assert_array_equal(n, reference[1])
            np.testing.assert_allclose(
                f / np.maximum(n, 1)[:, None],
                reference[0] / np.maximum(n, 1)[:, None],
                atol=1e-12,
                rtol=0,
            )
            np.testing.assert_allclose(intensity, reference[2], atol=1e-12, rtol=0)
