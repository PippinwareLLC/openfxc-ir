# Parity Gaps - openfxc-ir

Use this list to track remaining functional gaps to reach parity with expected lowering/optimize scope. Items are grouped and phrased to be checkable.

## Lowering gaps
- [x] Cbuffer field loads: `sm4_cbuffer` now lowers without diagnostics after binding/field resolution.
- [x] Structured buffers/UAVs: read indexing now lowers (`Index` op); RW/structured writes now emit indexed `Store` with validation; no multidimensional/index swizzle support.
- [x] Stores/output writes: `Store` op emitted for assignable globals/cbuffer/struct members and RW/structured resources with operand validation; multidimensional/index swizzle support still missing.
- [x] Intrinsic coverage: expanded to cover matrix/bit-cast/math and texture intrinsics plus noise, pack/unpack, and other builtin helpers.
 - [x] Control-flow value merges: locals now lower through explicit loads/stores so branch paths reconcile through memory without SSA/phi gaps.
- [x] SM1.x coverage: ps_1_1 baseline lowering regression now exists; broader intrinsic support still pending.

## Optimize gaps
- [x] Component-DCE: per-lane liveness trimming for swizzles with result type narrowing; broader op coverage still pending.
- [x] Pass precision: passes avoid backend artifacts, track side effects, and now respect CFG structure for copy propagation; no phi handling yet.
- [x] Constfold/algebraic limits: folds scalars, vectors, and matrices (including boolean splats) with algebraic simplifications honoring zero/one constants; no folding of `Index`/resource ops.
- [x] Side-effect modeling: resource/sample/store operations now form optimization barriers and are preserved even when unused.
- [x] Redundancy: common subexpressions are eliminated within blocks for pure operations while respecting side-effect barriers.

## Invariants and validation
- [x] Backend-agnostic check is a simple string scan; strengthen to catch subtler leaks.
- [x] CFG validation is minimal: no reachability/dominance/branch-target validation; block tags are free-form strings.
- [x] Type-checking: instruction results, assignments, stores, swizzles, and returns now validate operand/result types.

## Resources/metadata
- [x] FX/technique metadata is now projected into IR modules alongside functions and resources.
 - [x] Sampler/state nuances and UAV/structured buffer write semantics are absent; resources are treated as load-only.

## Testing gaps
- [x] Optimize snapshots: only `ps_texture` corpus; add SM3 branch/loop and SM4/5 cases.
- [x] Lowering snapshots: `sm4_cbuffer` now included alongside other sample corpora with passing coverage.
- [x] Negative tests: backend-leak detection, CFG invariants, and side-effect preservation are covered by regression cases.
- [x] Corpus coverage: sample smoke tests now lower/optimize DXSDK `Tutorial02`/`Tutorial04`/`Tutorial06` shaders from `samples/`.

## CLI/docs
- [x] Optimize pass defaults include the `component-dce` placeholder; clarify behavior when a pass is unavailable.
- [x] Pass name validation is soft (info diagnostics only); consider erroring on unknown passes or listing available ones.
