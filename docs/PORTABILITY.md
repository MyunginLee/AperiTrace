# Scientific portability contract

Default `python -m pytest -q` includes the unchanged historical Windows fixture,
its exact SHA-256 manifest, portable scientific regeneration, schema validation,
and adversarial roundoff/corruption checks. Archive integrity and independently
recomputed scientific quantities are validated separately under the rules below.

- Exact: tuple IDs, masks, counts, shapes, ordering, schema/category strings,
  graph connectivity, embedded/external metadata equality, file/dataset hashes.
  Binary/NPZ dtypes must match their descriptors exactly. Linked JSON has no
  NumPy dtype descriptors: lattice/focus values must be actual integers, not
  floats or booleans; `N` must be a JSON integer. Numeric geometry and diffraction
  arrays accept finite integer/float values but reject booleans, including mixed
  boolean/numeric inputs. Each real/imaginary amplitude component is shape-checked
  independently; broadcasting is not a validation policy.
- Independently recomputed coordinates, basis/window vertices and signed distances:
  absolute `atol=1e-12, rtol=0`, consistent with the existing basis/window import
  convention. Shapes must match before comparison.
- Recomputed complex F/N and I/N², or already normalized intensity:
  absolute `atol=1e-12, rtol=0`. Complex-amplitude error is its magnitude divided
  by `max(1,N)`, not separate real/imaginary error budgets. The unit-weight count
  and authoritative central-normalization **metadata** remain exact. Diffraction
  array entries, including a stored `q=0` entry, use the same normalized float
  comparison budget. The generated unit-weight `q=0` result is exactly `F=N` and
  `I_normalized=1` for nonempty selection; this does not make serialized array
  comparison bitwise. Linked JSON has no separate central-normalization metadata.
- Recomputed scientific epsilon: relative `64 * float64 machine epsilon`, absolute
  tolerance zero. This accommodates radius roundoff, not changes in membership policy.

Tolerance is not applied to bytes/checksums, categorical status, masks or IDs.
The `.qc5d` preview parser also accepts float64 basis roundoff at `1e-12`;
float32 window/coordinate and signed-distance losses must satisfy the existing
header-declared tolerances (bounded above by `1e-6`). Integer IDs, masks,
ordering, graphs and payload checksum remain exact. This does not promote
float32 presentation values to authoritative scientific coordinates.
Near a membership boundary discrete results remain governed by the documented
reference-double predicate; no platform-independent exact-real certificate is claimed.

Version-1 dataset identifiers such as `stage2_reference_modelset` and input-checksum
keys `stage2_modelset` / `stage3_diffraction` are retained for format compatibility.
They identify data types and provenance; changing them would invalidate existing
archives and checksum-linked app data.

Implementation mapping: `qc5d.exploration._same_geometry` checks reconstructed
linked geometry; `recompute_state` reconstructs the canonical tuple projection,
reference mask, exact status/count/IDs, and fresh finite sums. It uses the same
`SCIENTIFIC_FLOAT_ATOL` constant as the schema/authoritative validator. Epsilon
uses `np.isclose(..., rtol=64*np.finfo(float).eps, atol=0)`. The serialized geometry
is checked, not trusted as the source of recomputation. Within-process
`Exploration.export_state` path-ownership checks deliberately remain exact;
they are not cross-platform JSON import checks. Empty `q` serializes as `[]` and
reconstructs as a `(0,3)` query matrix, with empty amplitude/intensity vectors.

Executable contract coverage: `tests/test_linked_portability.py`,
`tests/test_portability_contract.py`, and `tests/test_path_recovery_example.py`.
The changed-path example is `python -m qc5d.recover_interval --output NEW_FOLDER`;
see `docs/PATH_RECOVERY_EXAMPLE.txt`. It discovers a missed interval rather than
hard-coding a midpoint, exports its state, and invokes direct recomputation.

`python scripts/historical_fixture_byte_compare.py` is an explicit opt-in byte
diagnostic. It reports genuine whole-payload mismatches from platform roundoff and
fresh provenance; it never regenerates the frozen files and is not the default
scientific release gate. Bitwise archival integrity and bitwise re-execution are
different requirements.

The default figure-reproduction command outputs SVG/PNG plus numerical tables,
without a TeX compiler. `regenerate_paper.py --pdf --output NEW_DIRECTORY` can
also render PDF figures where the optional font-subsetting backend works.
PDF generation requires a functioning local Matplotlib PDF/font-subsetting backend.
