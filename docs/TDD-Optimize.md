# TDD - `openfxc-ir optimize` (IR Optimization for OpenFXC)

## 0. Overview
`openfxc-ir optimize` consumes the IR produced by `openfxc-ir lower` and applies backend-agnostic optimizations. It feeds profile legalization (`openfxc-profile`) and backend lowering (`openfxc-dx9`, later DXIL/SPIR-V).

Pipeline context:
```
openfxc-hlsl parse -> openfxc-sem analyze -> openfxc-ir lower -> openfxc-ir optimize -> openfxc-profile -> openfxc-dx9 -> openfxc-dxbc
(AST)                  (semantic model)       (raw IR)              (optimized IR)       (profile rules)     (DX9 lowering)   (bytecode)
```

Principle: operate only on the IR abstraction; remain backend- and profile-agnostic.

## 1. Scope & Goals
- Input: IR JSON from `openfxc-ir lower`.
- Apply a configurable pipeline:
  - Constant folding
  - Algebraic simplifications
  - Dead code elimination (DCE)
  - Component-level DCE (kill unused swizzle lanes)
  - Copy propagation
- Preserve semantics and IR invariants (SSA-ish, typed, valid CFG, no backend bias).
- Output: valid IR JSON with fewer instructions/values where possible and no dangling references.

Non-goals: profile-dependent rewrites, backend-specific lowering, aggressive FP reordering, or changing side effects (texture samples, UAV writes).

## 2. CLI Contract
```
openfxc-ir optimize [options] < input.ir.json > output.ir.opt.json
```
Options:
- `--passes constfold,dce,component-dce,copyprop,algebraic` (parsed and reported; currently not executed)
- `--profile <name>` (optional passthrough/heuristics)

Exit codes: `0` success (diagnostics allowed), `1` internal error (I/O/JSON/etc).

## 3. Input / Output Schema
Input: IR JSON per `openfxc-ir lower` (functions, blocks, instructions, values, resources, diagnostics, profile).
Output: same shape/schema; some functions/blocks/instructions/values may be removed or rewritten; diagnostics may be appended (e.g., inconsistent IR detected).

Tests must assert: IR invariants still hold; no reference to removed `valueId`/block; backend agnostic.

## 4. Pass Specs (TDD)
### 4.1 Constant folding (`constfold`)
- Fold arithmetic/comparisons/logical ops and constant constructors when operands are constant.
- Skip unsafe folds (e.g., divide by zero).
- Preserve types; replace instruction with constant value and clean up dead def.
Tests: scalar and vector arithmetic, comparisons; ensure type preserved; no dead refs.

### 4.2 Dead code elimination (`dce`)
- Remove instructions whose results are unused and side-effect free (pure arithmetic, swizzles, casts).
- Preserve side effects (texture samples, stores, returns, branches, etc.).
Tests: unused temporaries removed; used temporaries kept; chains of dead instructions fully removed.

### 4.3 Component-level DCE (`component-dce`)
- Track per-component liveness (X/Y/Z/W); trim masks and operands accordingly.
- Optional further reduction (broadcast) deferred; first version ensures unused lanes marked dead.
Tests: return of `.x` only marks X live; masks trimmed accordingly; types/masks remain valid.

### 4.4 Copy propagation (`copyprop`)
- Rewrite aliases from simple moves to their sources; rely on DCE to drop redundant moves.
Tests: alias chains collapse; redundant moves removed after DCE; no type changes.

### 4.5 Algebraic simplification (`algebraic`)
- Apply identities: `x+0`, `x-0`, `x*1`, `x/1` -> `x`; `x*0` -> `0` (scalar/vector).
Tests: identities hold; types preserved.

## 5. IR Invariants After Optimize
1) SSA-ish: each non-parameter/global value has one definition.  
2) Typed: instruction typing rules still hold.  
3) CFG validity: blocks terminate; no stray instructions after terminators.  
4) Reference consistency: no operand refers to removed value/block.  
5) Backend-agnostic: no DX9/DXBC or profile leakage.  
Add an invariant test suite to enforce these post-passes.

## 6. Test Strategy
- Unit tests per pass using small IR snippets (hand-built or helper builders).
- Snapshot tests for combined pipeline:
  ```
  openfxc-hlsl parse shader.hlsl \
    | openfxc-sem analyze --profile ps_2_0 \
    | openfxc-ir lower \
    | openfxc-ir optimize --passes constfold,dce,component-dce,copyprop,algebraic \
    > shader.ir.opt.json
  ```
  Include dead code, foldable constants, swizzles, branches/loops.
- Invariant tests: run optimize then `AssertValidIr()` (defs/refs/terminators/types/backend-agnostic scan).

## 7. Definition of Done
- CLI reads IR JSON, writes optimized IR JSON; `--passes` selects pipeline (currently noted as unimplemented until passes land).
- Passes implemented/tested: `constfold`, `dce`, `component-dce`, `copyprop`, `algebraic` (future).
- IR invariants preserved; no backend/profile leakage.
- End-to-end pipeline on a sample corpus succeeds without internal errors; expected instruction count reductions; behaviorally equivalent modulo allowed simplifications.

## 8. Project Structure
- Core class library: exposes IR model and optimization pipeline; consumes IR JSON objects and emits optimized IR JSON/objects.
- CLI: thin wrapper over the core library (argument parsing and I/O only).
- Tests: target the core library directly, using `openfxc-hlsl` + `openfxc-sem` + `openfxc-ir lower` to build fixtures; do not rely on the CLI for test execution.
