"""Recorded tests, primary example, direct recomputation and scientific comparison."""

import argparse
import json
import os
import platform
import subprocess
import sys
from datetime import UTC, datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output = args.output.resolve()
    args.output.mkdir(parents=True, exist_ok=False)
    env = dict(os.environ, MPLBACKEND="Agg", PYTHONHASHSEED="0")
    for key in (
        "OMP_NUM_THREADS",
        "OPENBLAS_NUM_THREADS",
        "MKL_NUM_THREADS",
        "NUMEXPR_NUM_THREADS",
    ):
        env[key] = "1"
    records = []

    def run(name, command):
        started = datetime.now(UTC).isoformat()
        result = subprocess.run(
            [str(x) for x in command],
            cwd=ROOT,
            env=env,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        (args.output / (name + ".log")).write_text(result.stdout + result.stderr, encoding="utf-8")
        records.append(
            {
                "name": name,
                "command": [str(x) for x in command],
                "start_utc": started,
                "end_utc": datetime.now(UTC).isoformat(),
                "exit_code": result.returncode,
            }
        )
        print(name, result.returncode, flush=True)
        if result.returncode:
            raise RuntimeError(f"{name} failed; retained log is evidence, not PASS")

    info = {
        "platform": platform.platform(),
        "python": sys.version,
        "processor": platform.processor(),
        "machine": platform.machine(),
        "logical_cores": os.cpu_count(),
    }
    if sys.platform == "darwin":
        cpu = subprocess.run(
            ["sysctl", "-n", "machdep.cpu.brand_string"], capture_output=True, text=True
        )
        info["cpu_model"] = (
            cpu.stdout.strip() if cpu.returncode == 0 else "unavailable; operator to provide"
        )
    elif sys.platform.startswith("linux"):
        cpuinfo = Path("/proc/cpuinfo")
        info["cpu_model"] = next(
            (
                line.split(":", 1)[1].strip()
                for line in cpuinfo.read_text().splitlines()
                if line.startswith("model name")
            ),
            "unavailable; operator to provide",
        )
    else:
        info["cpu_model"] = "operator to provide model name; processor signature recorded"
    code = 0
    try:
        run(
            "environment",
            [
                sys.executable,
                "-c",
                "import json,numpy,qc5d,platform;print(json.dumps({'numpy':numpy.__version__,"
                "'qc5d_file':qc5d.__file__,'processor':platform.processor()}))",
            ],
        )
        run("packages", [sys.executable, "-m", "pip", "freeze"])
        run("pip_check", [sys.executable, "-m", "pip", "check"])
        run("tests", [sys.executable, "-m", "pytest", "-q"])
        run(
            "primary",
            [
                sys.executable,
                ROOT / "examples/softwarex_linked_demo.py",
                "--output",
                args.output / "primary",
            ],
        )
        for name in ("before", "inside", "after"):
            run(
                "recompute_" + name,
                [
                    sys.executable,
                    "-m",
                    "qc5d.exploration",
                    "--recompute",
                    args.output / f"primary/{name}.json",
                ],
            )
        run(
            "summary",
            [
                sys.executable,
                ROOT / "scripts/numerical_summary.py",
                "--output",
                args.output / "numerical_summary.json",
                "--reference",
                ROOT / "examples/data/reproduction_reference.json",
            ],
        )
        run(
            "changed_path",
            [
                sys.executable,
                ROOT / "examples/softwarex_changed_path.py",
                "--output",
                args.output / "changed_path",
            ],
        )
        for name in ("before", "inside", "after"):
            run(
                "changed_path_recompute_" + name,
                [
                    sys.executable,
                    "-m",
                    "qc5d.exploration",
                    "--recompute",
                    args.output / f"changed_path/{name}.json",
                ],
            )
        run(
            "timing_reanalysis",
            [
                sys.executable,
                ROOT / "scripts/reanalyse_timings.py",
                "--output",
                args.output / "timing_summary.json",
            ],
        )
        run(
            "quickstart_export",
            [sys.executable, "-m", "qc5d.exploration", "--output", args.output / "quickstart.json"],
        )
        run(
            "quickstart_recompute",
            [
                sys.executable,
                "-m",
                "qc5d.exploration",
                "--recompute",
                args.output / "quickstart.json",
            ],
        )
        run(
            "paper_figures_tables",
            [
                sys.executable,
                ROOT / "scripts/regenerate_paper.py",
                "--output",
                args.output / "paper",
            ],
        )
    except Exception as error:
        code = 1
        info["error"] = str(error)
    finally:
        with (args.output / "result.json").open("x", encoding="utf-8") as out:
            json.dump(
                {
                    "gate": "PASS" if code == 0 else "FAIL",
                    "environment": info,
                    "commands": records,
                    "validation_scope": "Local execution only; independence is not inferred.",
                },
                out,
                indent=2,
            )
    raise SystemExit(code)


if __name__ == "__main__":
    main()
