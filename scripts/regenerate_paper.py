"""Regenerate core publication figures/tables from included retained evidence.

No TeX installation is required; outputs are figures, tables, macros and numerical
audit JSON. Fresh twelve-case/primary-example calculations run in reproduce.py.
Timings and previously executed high-precision checks are retained, not retimed.
"""

import argparse
import json
from pathlib import Path

import paper_tables
import plot_figures as publication

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--pdf", action="store_true", help="also render optional PDF figures")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    inputs = ROOT / "publication_data"
    paper_tables.build(inputs, output)
    publication.build_figures(
        inputs / "examples",
        inputs,
        output,
        output / "figure_previews",
        formats=("svg", "png", "pdf") if args.pdf else ("svg", "png"),
    )
    metrics = json.loads((output / "final_metrics.json").read_text())
    assert metrics["eligible_excursions"] == 636
    assert metrics["original"]["resolved_interval_midpoints"] == 1495
    assert len(list((output / "figure_previews").glob("figure*.svg"))) == 4
    print(
        json.dumps(
            {
                "gate": "PASS",
                "figures": 4,
                "formats": ["svg", "png"] + (["pdf"] if args.pdf else []),
                "meaning": "retained-source plotting, not new timing measurements",
            }
        )
    )


if __name__ == "__main__":
    main()
