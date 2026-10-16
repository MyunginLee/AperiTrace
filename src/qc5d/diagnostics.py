"""Local environment diagnostics. Hardware detection is optional and time-bounded."""

import csv
import importlib
import json
import os
import platform
import shutil
import struct
import subprocess
import sys
from importlib.metadata import PackageNotFoundError, version
from pathlib import Path

from qc5d import __version__

REQUIRED_DEPENDENCIES = ("numpy", "scipy", "pydantic", "h5py", "matplotlib", "PyYAML")
DEVELOPMENT_DEPENDENCIES = ("pytest", "ruff", "jsonschema")


def _dependency(name: str, *, required: bool) -> dict:
    try:
        dependency_version = version(name)
    except PackageNotFoundError:
        return {"version": None, "required": required, "status": "missing"}
    if required:
        try:
            importlib.import_module("yaml" if name == "PyYAML" else name)
        except Exception as error:
            return {
                "version": dependency_version,
                "required": True,
                "status": "import_error",
                "error": type(error).__name__,
            }
    return {"version": dependency_version, "required": required, "status": "available"}


def _float_precision() -> dict:
    report = {
        "python_float": {
            "radix": sys.float_info.radix,
            "mantissa_bits": sys.float_info.mant_dig,
            "epsilon": sys.float_info.epsilon,
            "decimal_digits": sys.float_info.dig,
        }
    }
    try:
        import numpy as np
    except Exception:
        report["numpy"] = {"status": "unavailable"}
        return report
    for name, dtype in (("float64", np.float64), ("float32", np.float32)):
        info = np.finfo(dtype)
        report[name] = {
            "bits": int(info.bits),
            "stored_fraction_bits": int(info.nmant),
            "epsilon": float(info.eps),
            "tiny": float(info.tiny),
            "max": float(info.max),
            "decimal_precision": int(info.precision),
        }
    return report


def _run_probe(command: list[str]) -> str | None:
    try:
        completed = subprocess.run(
            command,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=5,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None
    return completed.stdout.strip() if completed.returncode == 0 else None


def detect_gpu() -> dict:
    """Report detected hardware; this does not certify compute or rendering support."""
    nvidia_smi = shutil.which("nvidia-smi")
    if nvidia_smi:
        output = _run_probe(
            [
                nvidia_smi,
                "--query-gpu=name,driver_version,memory.total",
                "--format=csv,noheader,nounits",
            ]
        )
        if output:
            devices = [
                {
                    "name": row[0].strip(),
                    "driver_version": row[1].strip(),
                    "memory_mib": row[2].strip(),
                }
                for row in csv.reader(output.splitlines())
                if len(row) == 3
            ]
            if devices:
                return {"status": "detected", "method": "nvidia-smi", "devices": devices}
    if sys.platform == "win32":
        powershell = shutil.which("powershell") or shutil.which("pwsh")
        if powershell:
            output = _run_probe(
                [
                    powershell,
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "Get-CimInstance Win32_VideoController | "
                    "Select-Object Name,DriverVersion | ConvertTo-Json -Compress",
                ]
            )
            if output:
                try:
                    records = json.loads(output)
                    records = records if isinstance(records, list) else [records]
                    devices = [
                        {"name": row["Name"], "driver_version": row.get("DriverVersion")}
                        for row in records
                        if isinstance(row, dict) and row.get("Name")
                    ]
                except (ValueError, TypeError):
                    devices = []
                if devices:
                    return {"status": "detected", "method": "Windows CIM", "devices": devices}
    return {
        "status": "unavailable",
        "method": None,
        "devices": [],
        "note": "Hardware could not be detected with available local tools; no GPU is assumed.",
    }


def detect_unity(root: Path) -> dict:
    """Inspect project markers only; never launch an Editor or inspect other repositories."""
    locations = [root, root / "unity"]
    unity_folder = root / "unity"
    if unity_folder.is_dir():
        locations.extend(sorted(path for path in unity_folder.iterdir() if path.is_dir()))
    projects = []
    for location in locations:
        version_file = location / "ProjectSettings" / "ProjectVersion.txt"
        manifest_file = location / "Packages" / "manifest.json"
        if version_file.is_file() and manifest_file.is_file():
            editor_version = None
            for line in version_file.read_text(encoding="utf-8-sig").splitlines():
                if line.startswith("m_EditorVersion:"):
                    editor_version = line.partition(":")[2].strip()
                    break
            projects.append(
                {"path": location.relative_to(root).as_posix(), "editor_version": editor_version}
            )
    return {"present": bool(projects), "projects": projects}


def collect_diagnostics(root: Path | None = None) -> dict:
    """Collect finite, JSON-serializable facts about the installed local environment."""
    project_root = Path.cwd() if root is None else root
    dependencies = {name: _dependency(name, required=True) for name in REQUIRED_DEPENDENCIES} | {
        name: _dependency(name, required=False) for name in DEVELOPMENT_DEPENDENCIES
    }
    return {
        "qc5d_version": __version__,
        "healthy": all(
            dependencies[name]["status"] == "available" for name in REQUIRED_DEPENDENCIES
        ),
        "python": {
            "version": platform.python_version(),
            "implementation": platform.python_implementation(),
            "pointer_bits": struct.calcsize("P") * 8,
        },
        "platform": {
            "system": platform.system(),
            "release": platform.release(),
            "machine": platform.machine(),
        },
        "dependencies": dependencies,
        "float_precision": _float_precision(),
        "thread_environment": {
            key: os.environ.get(key)
            for key in ("OMP_NUM_THREADS", "OPENBLAS_NUM_THREADS", "MKL_NUM_THREADS")
        },
        "gpu": detect_gpu(),
        "unity": detect_unity(project_root),
    }
