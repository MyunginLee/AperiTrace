# AperiTrace: linked finite-model investigation

AperiTrace is implemented as the Python package `qc5d`.
This software connects exact five-integer lattice identities, physical 3D and
internal 2D projections, linear-offset membership history, and finite diffraction.
Python is the reference scientific implementation. Unity is an optional Windows
and Quest inspection interface; its sampled-state presentation is not Python's
event-resolved runtime. See `APP.md` for app controls and build instructions.

## Install and run

The source repository contains `pyproject.toml`,
`src/qc5d/`, tests, examples and this manual. Extract it and enter that directory.
Commands below use Windows PowerShell. Python >=3.11 is declared; actual checks
use CPython 3.12.10 on Windows 11 and 3.12.3 on Ubuntu/WSL2, on one physical
host. The portable reproduction entry point is `scripts/clean_install.py
--run-reproduction`; see `REPRODUCIBILITY.md` for the complete workflow.
Dependency ranges are in `pyproject.toml`;
`requirements-lock.txt` records the measured environment, not universal support.

```powershell
py -3.12 -m venv .venv
.venv/Scripts/python.exe -m pip install -r requirements-lock.txt
.venv/Scripts/python.exe -m pip install --no-build-isolation --no-deps .
.venv/Scripts/python.exe -m pip check
.venv/Scripts/python.exe -m pytest -q
.venv/Scripts/python.exe examples/softwarex_linked_demo.py --output demo_run
.venv/Scripts/python.exe -m qc5d.exploration --output state.json
.venv/Scripts/python.exe -m qc5d.exploration --recompute state.json
.venv/Scripts/python.exe -m qc5d.exploration --recompute examples/data/example_a_after.json
.venv/Scripts/python.exe -m qc5d.linked_viewer --export clicked_state.json
```

Dependencies are not redistributed in this repository.
Ordinary online installation retrieves them from the package index. An
offline installation needs compatible wheels matching the lock file; wheels
for this Windows/Python host do not establish another platform's compatibility.

The viewer has three simultaneous views. Click a candidate in physical or
internal space to focus its exact tuple; left/right moves the offset parameter
by 0.005, clamped to [0,1]. E exports the current state. Existing export files
are never overwritten: give `--export` a new filename before another export.
Teal/gray distinguishes selected/unselected candidates, the red cross marks
the shared focus, and the header displays count and numerical status.
Reciprocal colors are log10(max(I,1e-8)) on the common [-8,0] scale, with qz=0
in this bundled display. Display coordinates are not physical extra dimensions.

For a window-free actual application capture:

```powershell
$env:MPLBACKEND = 'Agg'
.venv/Scripts/python.exe -m qc5d.linked_viewer --capture application.png --export capture_state.json
.venv/Scripts/python.exe -m qc5d.exploration --recompute capture_state.json
```

The saved PNG comes from the application's canvas. It is not a Unity/Quest
screenshot. Remove the session's `MPLBACKEND` setting before using an
interactive Matplotlib backend. Backend availability depends on the Python
installation; the scientific CLI does not require a display.

## Scientific contract and stable API

Coordinates are generated from the canonical 5D decagonal basis, with
dimensionless units and angular q (no implicit 2*pi conversion). Selection is
signed finite-edge interior distance > epsilon for `u + phi` in the fixed
projected-cube decagon. Scientific epsilon is `1e-10 * R_W` in the measured
configuration. A separate heuristic roundoff diagnostic never changes epsilon
or merges membership roots. Unit scattering weights are the supported workflow.

```python
from qc5d.exploration import bundled_example

app = bundled_example()
path = app.path((-0.61, 0.43), (0.67, -0.31))
answers = path.query(app.cache, [0.0, 0.075, 0.075, 1.0])
mask, status = path.state(0.075)
payload = app.export_state(path, 0.075, (0, -1, 1, 0, 1))
```

`qc5d.events` exposes `PhaseCache`, `LinearPath`, `Intervals`, `QueryResult`
and `clip_intervals`. The higher-level `Exploration` wrapper validates matching
canonical tuples/IDs/coordinates and keeps a phase cache tied to candidate order.
When using the lower-level `LinearPath.query(cache, ...)` directly, the caller
must preserve the row correspondence between internal and physical arrays;
equal row counts alone cannot prove it. Paths and q must be finite real arrays.
The stable wrapper rejects unsupported boundary policies and out-of-domain paths.
Complex phase magnitude is limited to 1e12 radians and cache construction is
limited to 25 million complex terms (400 MB of phase storage alone).

Changing q, physical coordinates, their order, or weights invalidates phase
terms. Changing window, internal coordinates, epsilon, start or end invalidates
intervals. A new path can reuse the unchanged cache. Query parameters are
finite, nondecreasing and in [0,1]; repetitions are allowed, reverse/nonmonotone
queries are rejected. Reverse exploration constructs the reversed path.

Geometric roots are excluded, but a domain endpoint strictly inside is selected.
Thus domain intersections may be [0,b), (a,1], [0,1], or (a,b).
Zero-width and tangent intersections have no selected excursion. Shared-root
entries/exits are applied atomically at the requested query, not exposed in an
ID-sorted fake intermediate state. Near roots remain distinct even if no
representable float lies between them.

`clear_float64` means clear under the conservative implemented diagnostic,
not an exact-real certificate. `uncertain` returns a reference finite-edge
double-precision mask and fresh cached sum, without contaminating subsequent
interval replay. The status remains in display and export. Every 128 queries
the incremental amplitude is rebaselined from its cached phase terms. Counts
are exact integers for the returned floating predicate. I=|F|^2/max(1,N)^2;
empty sets give zero, and empty q gives an empty output array. Empty exports
without an existing focus are not part of the linked-inspection schema.

Exports include scientific conventions, full exact tuples and IDs, coordinates,
window, model/crop settings, epsilon, angular q, offset path and parameter,
focus, count, complex amplitudes, normalized intensity and numerical status.
`recompute_state` reconstructs coordinates from the supplied tuples and checks
direct finite-edge selection and fresh phases. It rejects changed outputs,
duplicate IDs, lost status, invalid shapes and nonfinite values. It verifies
the supplied finite candidate set; it does not independently certify that its
enumeration is complete. Full finite-support checks are a separate experiment.

The typed portability policy and its implementation mapping are in
`PORTABILITY.md`. Real/imaginary amplitude components are shape-checked
independently before combining; count/focus integer types cannot silently coerce
booleans or integer-valued floats. Tolerance is for reconstructed numerical
values, not discrete identity, metadata correspondence or file integrity.

## Changed-path interval-recovery example

Run `python -m qc5d.recover_interval --output NEW_FOLDER`. It changes the original
path from `(-0.61,0.43) -> (0.67,-0.31)` to
`(-0.60,0.45) -> (0.66,-0.27)`, keeps the same 33 sampled parameters and phase
cache, and rebuilds interval geometry. Two site excursions are missed by those
samples. Focus `(-1,1,0,0,-2)` was selected at seven original-path samples but
zero on the changed path. The example discovers its interval, exports a midpoint
and recomputes all three before/inside/after states. Counts are 123/125/123;
finite intensities also change. See `PATH_RECOVERY_EXAMPLE.txt` for exact commands,
values, negative-outcome handling and limits. No paper benchmark is replaced.

## Two research examples

Example A retains a 31-candidate canonical fixture. Focus (-1,0,-1,0,0)
is shared across physical/internal views as phi changes (0,0) to (0.181,0.047).
Selected counts change 11 to 15; adjacency edges change 10 to 18. Bundled
linked-state exports can be recomputed without Unity. Retained actual Unity
desktop camera images and sidecars are supplied as evidence, not regenerated
engine execution. The current Python inspector executes the same scientific
state. A new Unity event-runtime/headset check was not performed.

Example B follows focus (0,-1,1,0,1) in a 571-candidate radius-2 crop along
(-0.61,0.43) to (0.67,-0.31). Its selected interval, about (0.06944,0.07872),
lies between neighboring 33-point uniform samples. The demo regenerates
before/midpoint/after states at fixed q=(1.6,0,0) and (1.6,0,1); all counts
are 126, although identities change. This is whole-trajectory finite
diffraction; it is not an isolated-atom attribution, a bulk-alloy discovery,
or evidence that the sampled endpoint states are inaccurate.

## Paper reproduction and measurement scope

Run `scripts/reproduce.py --output reproduction` for the complete headless
workflow. See `REPRODUCIBILITY.md` for input provenance, figure generation,
and fresh benchmark commands. Reproduction does not require the manuscript.

The retained final benchmark has 29 uniform workloads plus one boundary-biased
workload, one warmup and five rotated repetitions. B0 uses fresh reference
membership/phases; B1 caches phases with reference membership; B2 uses cached
half-spaces with fallback; B3 uses interval replay with fallback and rebasing.
Small and boundary-heavy cases are retained. Setup/query totals exclude
enumeration, I/O, graph building, plotting and Unity, and do not predict FPS.
Reanalysis of stored CPU measurements is distinct from a new machine's timings.
High-precision checks of frozen floating inputs are not exact-real certificates.

Default tests include the frozen fixture and typed scientific regeneration.
Discrete objects and archive integrity remain exact; reconstructed floats use
the explicit budgets in `PORTABILITY.md`. Historical whole-payload byte
comparison is opt-in, not the scientific portability gate.

## Optional Unity and licenses

See `APP.md` for the pinned Unity project, controls, tests and builds. No headset
or APK is required for Python reproduction. Original source/data use MIT;
third-party fonts and colormaps retain their own terms in
`../THIRD_PARTY_NOTICES.md`.
