# TODO - openfxc-ir (lower + optimize)

## M0: CLI (lower)
- [ ] Scaffold `openfxc-ir lower` command (stdin/file input, profile/entry args).
- [ ] Wire JSON parsing and error handling; formatVersion field.

## M1: IR model (lower)
- [ ] Define IR types/values/functions/blocks/instructions/resources in code.
- [ ] Add invariant checks (SSA-ish, typed, block termination).

## M2: Lowering
- [ ] Functions/params/returns to IR functions/entry blocks.
- [ ] Expressions: binary/unary, swizzle/mask, calls/intrinsics (abstract ops).
- [ ] Resources: abstract table; `Sample` op; no DX9 opcodes/registers.
- [ ] Control flow: if/else, simple loops.

## M3: Diagnostics
- [ ] Unsupported intrinsic lowering diagnostic.
- [ ] Missing entry/invalid entry diagnostic.
- [ ] Type mismatch/unsupported construct diagnostic with graceful recovery.

## M4: Tests
- [ ] Unit tests for ops/CFG/resource/sample lowering.
- [ ] Snapshot tests on small HLSL corpus (SM2/3/4/5, FX entry).
- [ ] Invariant tests: single-def values, typed values, terminator placement, no DX9 names.

## M5: Docs
- [ ] Keep README/TDD/schema aligned; document CLI usage.
- [ ] Add compatibility/capability note for IR coverage by SM era/features.

## M6: Optimize CLI/pipeline
- [ ] Scaffold `openfxc-ir optimize` command with `--passes`.
- [ ] Share IR model/invariant helpers from lower stage.
- [ ] No-op pipeline with passthrough to validate wiring.

## M7: Optimize passes
- [ ] Constant folding.
- [ ] Dead code elimination.
- [ ] Component-level DCE (swizzle lane liveness).
- [ ] Copy propagation.
- [ ] Algebraic identities (`x+0`, `x*1`, `x*0`, `x/1`).

## M8: Optimize tests/docs
- [ ] Unit tests per pass.
- [ ] Snapshot tests lower -> optimize (corpus with dead code, swizzles, branches).
- [ ] Invariant tests post-optimize (SSA-ish, typed, CFG valid, no dangling refs).
- [ ] Update README/TDD with optimize usage and examples.
