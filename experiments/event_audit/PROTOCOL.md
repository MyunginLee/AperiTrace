# Event resolved phason and finite diffraction protocol

This protocol defines the canonical cases, numerical comparisons and timing
procedure implemented by the accompanying experiment. Retain all specified
cases, failures and unresolved intervals.

## Question and scope

Can a linear phason segment be audited as membership intervals, revealing
transitions hidden by endpoint/frame comparison, while incremental diffraction
agrees with independent full finite sums? This is a computational application of
convex line clipping and linearity of a structure factor, not a new clipping
theorem, new bulk diffraction law or real-alloy strain result. It extends a local
canonical research analysis; no Unity runtime integration is implied.

## Fixed inputs

Reuse the existing equation-derived C5 basis/window, exact tuple identities,
dimensionless physical coordinates, positive internal shift, finite Euclidean
edge distance, exclude policy and epsilon 1e-10 times window circumradius.
No canonical fixture, schema, implementation or tolerance changes.

Cylindrical radii 2, 4 and 6; z absolute maximum 1.5; index bound 7; conservative
phason box [-0.75,0.75]^2; chunk size 65536. Report the orthogonality-based bound
sqrt(R^2+zmax^2+(window_radius+sqrt(2)*0.75+epsilon+guard)^2). Refuse a completeness
claim if this does not lie below the enumerated bound. This is finite support
completeness, not infinite-volume convergence.

Four fixed straight segments, each for all three crops:

1. Horizontal: (-0.70,0.133) to (0.70,0.133).
2. Diagonal: (-0.63,-0.57) to (0.61,0.59).
3. Oblique: (-0.61,0.43) to (0.67,-0.31).
4. Short: (-0.12,-0.071) to (0.13,0.079).

Sampling comparisons use uniform 9, 33 and 129 frames including endpoints. Each
candidate can have one open selected interval along a convex linear path. An
interior interval containing no sampled time is a missed selected excursion.
Also compare the total analytic enter/exit count with summed frame-mask changes;
do not equate a net frame change with all continuous events. Enumerate all missed
tuple identities and interval endpoints, including cases where none are missed.

Diffraction uses a fixed 11 by 11 qz=0 Cartesian grid on [-8,8]^2, unit weights,
exp(+iq.r), complex128 sums and I/N^2. There is no bulk/alloy interpretation.

## Algorithm and independent checks

For a convex CCW window, inward unit-normal slack is affine in segment parameter.
Clip all inequalities slack>epsilon, retaining open endpoints and tangent/parallel
cases. Do not combine near-coincident floating event times with a guessed physical
tolerance. Treat event times as double-computed parameters, not exact-real values.

Check the midpoint of every nonempty interval between sorted critical parameters
against the existing finite-segment Euclidean classifier. Flag a comparison as
numerically unresolved when the nearest threshold distance is at most
64*machine_epsilon*(1+window_radius+maximum query norm). This separate diagnostic
guard never changes scientific epsilon or selected states. Any disagreement
outside that guard fails; report unresolved counts and do not certify them.
Compare event parameters with an 80-digit Decimal calculation of the same frozen
double inputs. Decimal checks floating arithmetic, not exact trigonometric basis.

Incremental amplitude caches exp(iq.r) for the fixed support and adds/subtracts
changed tuples in stable order. Compare every resolved-interval amplitude to a
fresh authoritative structure_factor call, with maximum |delta F|/max(1,N)<=1e-12
and maximum absolute normalized-intensity difference<=1e-12. Preserve nonzero
roundoff. Graph snapshots must equal independent +/-unit-generator edge sets.
Reverse the path: per-tuple interval endpoints must map to 1-upper and 1-lower
within 1e-12; mismatches fail. Record endpoint ambiguity separately.

## Timing, reproducibility and failure handling

At 33 and 129 samples, time (a) full membership plus direct finite diffraction;
(b) cold interval computation, phase-cache construction and sampled updates;
(c) warm sampled updates. One warmup and five repetitions, single numerical
thread, interleaved methods. Retain all raw durations and median ratios. Do not
claim improvement unless the measured ratio exceeds one; no selected best crop,
favorable repetition or excluded setup cost. Graph/Unity/display are excluded.
Report cache bytes and deterministic phase-term counts separately from timings.

Run all twelve cases twice into separate exclusive destinations. Numerical
payloads should reproduce byte-for-byte in this exact environment; times and
machine/UTC metadata are not subject to byte equality. Source/config hashes and
all raw arrays accompany results. Failure status remains visible, and no
threshold is revised after seeing outcomes. Success can support a bounded
finite-model numerical comparison, not a new material-physics result.
