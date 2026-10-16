"""Fresh portable non-editable install; never overwrites an environment."""

import argparse
import importlib.util
import json
import os
import subprocess
import sys
from datetime import UTC, datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--env", type=Path, default=ROOT / ".venv-clean")
    parser.add_argument("--record", type=Path, default=ROOT / "clean-install.json")
    parser.add_argument("--wheelhouse", type=Path)
    parser.add_argument("--run-reproduction", action="store_true")
    parser.add_argument("--output", type=Path, default=ROOT / "reproduction")
    args = parser.parse_args()
    if args.env.exists() or args.record.exists():
        raise FileExistsError("use new environment and record paths; existing files preserved")
    args.env.parent.mkdir(parents=True, exist_ok=True)
    rows = []
    env = dict(os.environ, MPLBACKEND="Agg", PYTHONHASHSEED="0")
    for key in (
        "OMP_NUM_THREADS",
        "OPENBLAS_NUM_THREADS",
        "MKL_NUM_THREADS",
        "NUMEXPR_NUM_THREADS",
    ):
        env[key] = "1"

    def run(command):
        start = datetime.now(UTC).isoformat()
        result = subprocess.run(
            [str(x) for x in command],
            cwd=ROOT,
            env=env,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        rows.append(
            {
                "command": [str(x) for x in command],
                "start_utc": start,
                "end_utc": datetime.now(UTC).isoformat(),
                "exit_code": result.returncode,
                "output": result.stdout + result.stderr,
            }
        )
        print(result.stdout + result.stderr, flush=True)
        if result.returncode:
            raise RuntimeError(f"command failed with exit {result.returncode}")

    code = 0
    try:
        # Minimal Linux installations may omit ensurepip. If this invoking
        # interpreter already has pip, let its --python mode seed a fresh venv
        # without installing or changing any OS package.
        if importlib.util.find_spec("ensurepip") is None:
            if importlib.util.find_spec("pip") is None:
                raise RuntimeError("Python venv needs ensurepip or an invoking interpreter with pip.")
            run([sys.executable, "-m", "venv", "--without-pip", args.env])
            run([sys.executable, "-m", "pip", "--python", args.env, "install", "pip"])
        else:
            run([sys.executable, "-m", "venv", args.env])
        python = args.env / ("Scripts/python.exe" if os.name == "nt" else "bin/python")
        if args.wheelhouse:
            run(
                [
                    python,
                    "-m",
                    "pip",
                    "install",
                    "--no-index",
                    "--find-links",
                    args.wheelhouse,
                    "-r",
                    ROOT / "requirements-lock.txt",
                ]
            )
            run([python, "-m", "pip", "install", "--no-build-isolation", "--no-deps", ROOT])
        else:
            run([python, "-m", "pip", "install", str(ROOT) + "[dev]"])
        run([python, "-m", "pip", "check"])
        if args.run_reproduction:
            run([python, ROOT / "scripts/reproduce.py", "--output", args.output])
    except Exception as error:
        code = 1
        rows.append({"error": str(error)})
    finally:
        with args.record.open("x", encoding="utf-8") as out:
            json.dump({"gate": "PASS" if code == 0 else "FAIL", "commands": rows}, out, indent=2)
    raise SystemExit(code)


if __name__ == "__main__":
    main()
