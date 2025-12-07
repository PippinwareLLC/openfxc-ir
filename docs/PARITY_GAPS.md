# Parity Gaps - openfxc-ir

Use this list to track remaining functional gaps to reach parity with expected lowering/optimize scope. Items are grouped and phrased to be checkable.

## Lowering gaps
- [x] Cbuffer field loads: `sm4_cbuffer` now lowers without diagnostics after binding/field resolution.
- [ ] Structured buffers/UAVs: read indexing now lowers (`Index` op); no support for writes or RW* resources; no multidimensional/index swizzle support.
- [ ] Stores/output writes: initial `Store` op emitted for assignable globals/cbuffer/struct members; still no support for UAV/structured writes or validation, needs full semantics and tests.
- [ ] Intrinsic coverage: expanded (basic math/`tex*` plus normalize/dot/pow/exp/log/step/smoothstep/reflect/refract/atan2/fma/etc.), but still missing broader HLSL set (e.g., transpose, determinant, noise, pack/unpack, etc.).
- [ ] Control-flow value merges: no phi/merge handling; branches reuse plain value IDs and can be incorrect across paths.
- [ ] SM1.x coverage: unimplemented; compatibility matrix remains “planned.”

## Optimize gaps
- [ ] Component-DCE: placeholder/no-op; implement per-lane liveness trimming (tests expect placeholder only).
- [ ] Pass precision: passes are not SSA/CFG aware (no phi handling, no side-effect model beyond a small pure-op list); copyprop/DCE can be unsound in complex control flow.
- [ ] Constfold/algebraic limits: only scalar numeric strings; no vector/matrix folding or mask-aware simplifications; no folding of `Index`/resource ops.
- [ ] Side-effect modeling: no explicit modeling for resource/sample/store effects; DCE/passes should honor side-effectful ops.
- [ ] Redundancy: no CSE or redundancy elimination for repeated `Index`/resource accesses.

## Invariants and validation
- [ ] Backend-agnostic check is a simple string scan; strengthen to catch subtler leaks.
- [ ] CFG validation is minimal: no reachability/dominance/branch-target validation; block tags are free-form strings.
- [ ] Type-checking: only presence of type strings is enforced; op/operand type rules are unchecked.

## Resources/metadata
- [ ] FX/technique metadata is not projected into IR (only entry function).
- [ ] Sampler/state nuances and UAV/structured buffer write semantics are absent; resources are treated as load-only.

## Testing gaps
- [ ] Optimize snapshots: only `ps_texture` corpus; add SM3 branch/loop and SM4/5 cases.
- [ ] Lowering snapshots: `sm4_cbuffer` remains diagnostic by design; add passing coverage once field loads are fixed.
- [ ] Negative tests: backend-leak detection, CFG invariants, and side-effect preservation are untested.
- [ ] Corpus coverage: no runs over `samples/` directory; tests rely on small snippets.

## CLI/docs
- [ ] Optimize pass defaults include the `component-dce` placeholder; clarify behavior when a pass is unavailable.
- [ ] Pass name validation is soft (info diagnostics only); consider erroring on unknown passes or listing available ones.
