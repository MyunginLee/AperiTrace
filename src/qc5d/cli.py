"""XR Superspace environment and float64 reference commands."""

import argparse
import json
from collections.abc import Sequence
from pathlib import Path

from qc5d import __version__
from qc5d.diagnostics import collect_diagnostics


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="qc5d", description="XR Superspace reference tools")
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("version", help="Print the package version")
    doctor = commands.add_parser("doctor", help="Inspect dependencies, precision, GPU, and Unity")
    doctor.add_argument("--json", action="store_true", help="Print machine-readable JSON")
    for name in ("make-authoritative", "export-unity", "validate-dataset", "inspect-runtime"):
        exchange = commands.add_parser(name, help="Versioned scientific and runtime data exchange")
        if name in {"validate-dataset", "inspect-runtime"}:
            exchange.add_argument("file", type=Path)
        else:
            exchange.add_argument("--dataset", type=Path, required=True)
            exchange.add_argument("--out", type=Path, required=True)
            exchange.add_argument("--overwrite", action="store_true")
            if name == "make-authoritative":
                exchange.add_argument("--diffraction", type=Path)
            else:
                exchange.add_argument("--config", type=Path)
    for name in ("diffraction", "diffraction-convergence", "validate-science"):
        science = commands.add_parser(
            name, help="Finite-patch diffraction and scientific validation"
        )
        science.add_argument("--out", type=Path, required=True)
        science.add_argument("--overwrite", action="store_true", help="Replace named outputs")
        if name == "diffraction-convergence":
            science.add_argument("--configs", nargs="+", type=Path, required=True)
        else:
            science.add_argument("--dataset", type=Path, required=True)
        if name == "diffraction":
            science.add_argument("--config", type=Path, required=True)
            science.add_argument(
                "--phason-out", type=Path, help="Default: sibling phason_diffraction/"
            )
    for name in ("generate", "phason-sweep"):
        model = commands.add_parser(name, help="Generate finite model sets and phason events")
        model.add_argument("--config", type=Path, required=True, help="Model YAML or JSON")
        model.add_argument("--out", type=Path, required=True, help="Generated artifact directory")
        model.add_argument("--overwrite", action="store_true", help="Replace named artifacts")
        if name == "phason-sweep":
            model.add_argument("--path", type=Path, required=True, help="Path YAML, JSON, or CSV")
    graph = commands.add_parser("graph-check", help="Read-only dataset and graph validation")
    graph.add_argument("--dataset", type=Path, required=True, help="modelset.npz or its directory")
    for name in ("basis-check", "window-check", "make-reference-fixtures"):
        reference = commands.add_parser(
            name, help="Validate and export float64 reference artifacts"
        )
        reference.add_argument("--output-dir", type=Path, help="Generated artifact directory")
        reference.add_argument("--config", type=Path, help="Versioned reference configuration JSON")
        reference.add_argument(
            "--overwrite", action="store_true", help="Replace named output artifacts"
        )
    args = parser.parse_args(argv)
    if args.command == "version":
        print(__version__)
        return 0

    if args.command in {
        "make-authoritative",
        "export-unity",
        "validate-dataset",
        "inspect-runtime",
    }:
        from qc5d.data_workflows import (
            export_unity,
            inspect_runtime,
            make_authoritative,
            validate_dataset,
        )

        try:
            if args.command in {"validate-dataset", "inspect-runtime"}:
                result = (
                    validate_dataset if args.command == "validate-dataset" else inspect_runtime
                )(args.file)
            else:
                root = Path.cwd().resolve()
                command = [
                    "qc5d",
                    args.command,
                    "--dataset",
                    args.dataset.resolve().relative_to(root).as_posix(),
                    "--out",
                    args.out.resolve().relative_to(root).as_posix(),
                ]
                if args.overwrite:
                    command.append("--overwrite")
                if args.command == "make-authoritative":
                    if args.diffraction is not None:
                        command += [
                            "--diffraction",
                            args.diffraction.resolve().relative_to(root).as_posix(),
                        ]
                    result = make_authoritative(
                        args.dataset,
                        args.out,
                        diffraction=args.diffraction,
                        overwrite=args.overwrite,
                        command=command,
                    )
                else:
                    if args.config is not None:
                        command += ["--config", args.config.resolve().relative_to(root).as_posix()]
                    result = export_unity(
                        args.dataset,
                        args.out,
                        config_file=args.config,
                        overwrite=args.overwrite,
                        command=command,
                    )
        except KeyboardInterrupt:
            print(f"{args.command}: cancelled")
            return 130
        except (ValueError, OSError, KeyError) as error:
            print(f"{args.command}: {error}")
            return 2
        print(json.dumps(result, indent=2, sort_keys=True, allow_nan=False))
        print("PASS" if result["passed"] else "FAIL")
        return 0 if result["passed"] else 1

    if args.command in {"diffraction", "diffraction-convergence", "validate-science"}:
        from qc5d.science_artifacts import run_convergence, run_diffraction, run_validation

        try:
            root = Path.cwd().resolve()
            command = [
                "qc5d",
                args.command,
                "--out",
                args.out.resolve().relative_to(root).as_posix(),
            ]
            if args.command == "diffraction-convergence":
                command += [
                    "--configs",
                    *[p.resolve().relative_to(root).as_posix() for p in args.configs],
                ]
                result = run_convergence(
                    args.configs,
                    args.out,
                    overwrite=args.overwrite,
                    command=command + (["--overwrite"] if args.overwrite else []),
                )
            else:
                command += ["--dataset", args.dataset.resolve().relative_to(root).as_posix()]
                if args.command == "diffraction":
                    phason_output = args.phason_out or args.out.parent / "phason_diffraction"
                    command += [
                        "--config",
                        args.config.resolve().relative_to(root).as_posix(),
                        "--phason-out",
                        phason_output.resolve().relative_to(root).as_posix(),
                    ]
                    result = run_diffraction(
                        args.dataset,
                        args.config,
                        args.out,
                        phason_output=phason_output,
                        overwrite=args.overwrite,
                        command=command + (["--overwrite"] if args.overwrite else []),
                    )
                else:
                    result = run_validation(
                        args.dataset,
                        args.out,
                        overwrite=args.overwrite,
                        command=command + (["--overwrite"] if args.overwrite else []),
                    )
        except KeyboardInterrupt:
            print(f"{args.command}: cancelled")
            return 130
        except (ValueError, OSError, KeyError) as error:
            print(f"{args.command}: {error}")
            return 2
        print(json.dumps(result, indent=2, sort_keys=True, allow_nan=False))
        print("PASS" if result["passed"] else "FAIL")
        return 0 if result["passed"] else 1

    if args.command in {"generate", "phason-sweep", "graph-check"}:
        from qc5d.model_artifacts import check_dataset, export_model
        from qc5d.model_config import read_model_config
        from qc5d.modelset import GenerationCancelled
        from qc5d.phason import read_path

        try:
            if args.command == "graph-check":
                result = check_dataset(args.dataset)
            else:
                root = Path.cwd().resolve()
                command = [
                    "qc5d",
                    args.command,
                    "--config",
                    args.config.resolve().relative_to(root).as_posix(),
                    "--out",
                    args.out.resolve().relative_to(root).as_posix(),
                ]
                path = None
                path_bytes = None
                if args.command == "phason-sweep":
                    path = read_path(args.path)
                    path_bytes = args.path.read_bytes()
                    command.extend(("--path", args.path.resolve().relative_to(root).as_posix()))
                if args.overwrite:
                    command.append("--overwrite")
                last_percentage = -10

                def progress(data):
                    nonlocal last_percentage
                    percentage = int(100 * data["processed"] / data["total"])
                    if percentage >= last_percentage + 10 or percentage == 100:
                        print(
                            f"enumeration: {percentage}% ({data['processed']}/{data['total']}); "
                            f"candidates={data['candidate_count']}"
                        )
                        last_percentage = percentage

                result = export_model(
                    read_model_config(args.config),
                    args.out,
                    path=path,
                    config_bytes=args.config.read_bytes(),
                    path_bytes=path_bytes,
                    command=command,
                    overwrite=args.overwrite,
                    progress=progress,
                )
        except (KeyboardInterrupt, GenerationCancelled):
            print(f"{args.command}: cancelled")
            return 130
        except (ValueError, OSError, KeyError) as error:
            print(f"{args.command}: {error}")
            return 2
        print(json.dumps(result, indent=2, sort_keys=True, allow_nan=False))
        print("PASS" if result["passed"] else "FAIL")
        return 0 if result["passed"] else 1

    if args.command != "doctor":
        from qc5d.checksums import sha256_bytes
        from qc5d.config import ReferenceConfig
        from qc5d.reference import export_reference

        default = "reference" if args.command == "make-reference-fixtures" else args.command
        directory = Path("results") / default if args.output_dir is None else args.output_dir
        try:
            root = Path.cwd().resolve()
            command = [
                "qc5d",
                args.command,
                "--output-dir",
                directory.resolve().relative_to(root).as_posix(),
            ]
            config = ReferenceConfig()
            config_hash = None
            if args.config is not None:
                raw = args.config.read_bytes()
                config = ReferenceConfig.model_validate_json(raw)
                config_hash = sha256_bytes(raw)
                command.extend(("--config", args.config.resolve().relative_to(root).as_posix()))
            if args.overwrite:
                command.append("--overwrite")
            run = export_reference(
                args.command,
                directory,
                config=config,
                command=command,
                config_hash=config_hash,
                overwrite=args.overwrite,
            )
        except (ValueError, OSError) as error:
            print(f"{args.command}: {error}")
            return 2
        diagnostics = run.diagnostics
        print(
            f"basis: orthogonality={diagnostics['basis']['orthogonality_max_abs']:.3e}, "
            f"cross={diagnostics['basis']['cross_subspace_max_abs']:.3e}, "
            f"reconstruction={diagnostics['basis']['reconstruction_max_abs']:.3e}"
        )
        if args.command != "basis-check":
            print(
                f"window: hull={diagnostics['window']['hull_vertex_count']}, "
                f"central={diagnostics['window']['central_symmetry_residual']:.3e}, "
                f"tenfold={diagnostics['window']['tenfold_rotation_residual']:.3e}"
            )
        print(
            f"{'PASS' if run.passed else 'FAIL'}; "
            f"tolerance={diagnostics['tolerance']:.3e}; manifest written"
        )
        return 0 if run.passed else 1

    report = collect_diagnostics()
    if args.json:
        print(json.dumps(report, indent=2, sort_keys=True, allow_nan=False))
    else:
        print(f"qc5d {report['qc5d_version']}")
        print(f"Python: {report['python']['version']} ({report['python']['implementation']})")
        print("Platform: " + " ".join(report["platform"].values()))
        print("Dependencies:")
        for name, data in report["dependencies"].items():
            print(f"  {name}: {data['version'] or 'not installed'} ({data['status']})")
        precision = report["float_precision"]
        print(f"Python float epsilon: {precision['python_float']['epsilon']}")
        for dtype in ("float64", "float32"):
            if dtype in precision:
                print(
                    f"NumPy {dtype}: {precision[dtype]['bits']} bits, "
                    f"eps={precision[dtype]['epsilon']}"
                )
        print(f"GPU: {report['gpu']['status']} ({report['gpu']['method'] or 'no local probe'})")
        for device in report["gpu"]["devices"]:
            print(f"  {device['name']}; driver={device['driver_version']}")
        print(f"Unity project present: {report['unity']['present']}")
        for project in report["unity"]["projects"]:
            print(f"  {project['path']}: {project['editor_version']}")
        print(f"Required dependencies healthy: {report['healthy']}")
    return 0 if report["healthy"] else 1
