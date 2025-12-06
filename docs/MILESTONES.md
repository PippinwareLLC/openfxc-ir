# Milestones - openfxc-ir (lower + optimize)

- [x] M0: CLI skeleton (lower)
  - [x] `openfxc-ir lower` verb, stdin/file input, JSON passthrough scaffold, exit codes.

- [x] M1: IR model + schema (lower)
  - [x] Define IR JSON (formatVersion 1): types, values, functions, blocks, instructions, resources.
  - [x] Invariant helpers (SSA-ish, typed, block termination).

- [ ] M2: Core lowering
  - [ ] Functions/params/returns lowered.
  - [ ] Expressions: binary/unary/swizzle/call/intrinsics.
  - [ ] Resources: texture/sampler/cbuffer/global representation; `Sample` op.
  - [ ] CFG: if/else and simple loop lowering.

- [ ] M3: Diagnostics + robustness
  - [ ] Unsupported intrinsic/entry/type mismatch diagnostics.
  - [ ] Tolerate semantic diagnostics; partial IR with errors recorded.

- [ ] M4: Tests + snapshots (lower)
  - [ ] Unit tests for ops/CFG/resources.
  - [ ] Snapshot tests over small HLSL corpus (SM2/3/4/5, FX entry).
  - [ ] Invariant tests over IR JSON (SSA-ish, typed, no DX9 artifacts).

- [ ] M5: Docs + polish (lower)
  - [ ] README/TDD alignment, schema notes, usage examples.
  - [ ] Compatibility matrix for IR (coverage by SM era/features).

- [ ] M6: Optimize pipeline skeleton
  - [ ] `openfxc-ir optimize` verb, pass selection (`--passes`), passthrough scaffold.
  - [ ] Share IR model/invariants from lower stage.

- [ ] M7: Core optimize passes
  - [ ] Constant folding.
  - [ ] Dead code elimination.
  - [ ] Component-level DCE (swizzle lane liveness).
  - [ ] Copy propagation.
  - [ ] Algebraic simplifications (`x+0`, `x*1`, `x*0`, etc.).

- [ ] M8: Tests + invariants (optimize)
  - [ ] Unit tests per pass.
  - [ ] Snapshot tests for combined pipeline (lower -> optimize).
  - [ ] Invariant tests post-optimize (SSA-ish, typed, CFG intact, no dangling refs).

- [ ] M9: Docs + polish (optimize)
  - [ ] Update README/TDD/CLI usage for optimize.
  - [ ] Note pass defaults and examples; include pipeline examples end-to-end.
