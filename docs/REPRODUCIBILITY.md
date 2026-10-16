# Reproducing AperiTrace

## Install in a fresh environment

```sh
python scripts/clean_install.py --run-reproduction
```

This creates `.venv-clean` and runs the complete headless workflow into
`reproduction`. Existing environments and output directories are never replaced.
Use `--env`, `--record`, and `--output` to choose different destinations.
Alternatively install `.[dev]` in your own environment and run
`python scripts/reproduce.py --output reproduction-new`.

Python >=3.11 is declared. Prior release checks used CPython 3.12.10 on Windows
and 3.12.3 on Ubuntu/WSL2 with NumPy 2.5.4, on the same physical host.
`requirements-lock.txt` retains the original NumPy 2.5.3 benchmark environment;
it is not a claim that original timings reproduce across platforms.
Compatible wheels are needed for an offline installation; use
`clean_install.py --wheelhouse DIRECTORY` with the retained lock.

## What is recomputed

- Bundled tests, including frozen fixture integrity and portable regeneration.
- Original and changed-path examples and direct before/interior/after finite sums.
- Twelve canonical cases, with radii 2/4/6 and horizontal/diagonal/oblique/short paths.
- Membership omissions at 9, 33 and 129 uniform queries, compared with the
  included numerical reference using exact IDs/counts/status and fixed
  normalized floating tolerance `atol=1e-12, rtol=0`.
- Retained benchmark median/IQR calculations from all stored repetitions.
- Current figure designs and numerical TeX table/macro inputs.

The commands write logs and numerical results to the output directory you chose.
These are local diagnostic outputs, not source files to commit. The runner
records local machine and environment information there; review generated
outputs before sharing them. It does not upload or publish anything.

## Retained publication data

`publication_data/benchmark_final/summary.json` and
`publication_data/benchmark_boundary/summary.json` hold the original 30 workloads
and 600 raw timing records, with setup/query components, method order and
environment metadata. These measurements are reanalysed, not retimed by the
default workflow. `publication_data/validation` retains numerical validation
outputs, including uncertainty decomposition and reference comparisons.
`publication_data/examples` contains scientific exports, diffraction arrays
and pixel-preserved application panels used by the figures. Historical captures are
not regenerated headset observations. `SHA256SUMS` locks these retained inputs.

Capture sidecars record the scientific identity, offset, diffraction arrays,
source-image checksum and pixel crop coordinates. Only the scientific panels
and boundary-status strip are distributed. The PNGs preserve the recorded RGB
values; they are not reconstructed renderings.

## Run new measurements

```sh
python scripts/benchmark.py --output results/benchmark-new
python scripts/benchmark.py --boundary-stress --output results/boundary-new
```

The script reconstructs the canonical support, uses the same four path designs,
single-thread numerical settings, one warmup and five interleaved repetitions
of B0/B1/B2/B3. Enumeration, validation, plotting and Unity are outside the
timings. Counts and direct finite sums are checked. `--pilot` runs the two dense-q
workloads with one measured repetition and must not be reported as the full
benchmark. Fresh timings will depend on hardware and software. They do not
replace the retained paper measurements automatically.

The independent original experiment is executable with
`python experiments/event_audit/run.py --out results/event-audit-new`.
It includes finite-support, interval, graph, Decimal-on-frozen-input and
direct-sum checks. It is more expensive than the standard reproduction run.
The numerical procedure is documented in `PROTOCOL.md` beside the experiment.

## Targeted resampling

```sh
python -m qc5d.recover_interval --output changed-path
python scripts/audit_targeted_resampling.py --output coverage.json
```

The audit verifies Table S5's 33/2 to 34/0 query/omission result and directly
recomputes all three exports. See `PATH_RECOVERY_EXAMPLE.txt` for custom paths
and negative/no-miss outcomes.

## Figures and numerical tables

```sh
python scripts/regenerate_paper.py --output reproduction-figures
python scripts/regenerate_paper.py --output reproduction-pdf-figures --pdf
```

SVG/PNG generation needs no TeX installation. PDF figure generation also needs
a working Matplotlib PDF/font backend. Scientific tables and macros are generated
from retained data. Generated numeric TeX snippets contain computed values and
table rows only, not manuscript text. Manuscript drafts, PDFs, bibliography,
publisher templates and submission documents are not distributed here and
are not required by any reproduction command.
