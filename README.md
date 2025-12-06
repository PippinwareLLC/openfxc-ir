# openfxc-ir (bootstrap docs)

Purpose: Lower the semantic model from `openfxc-sem` into a backend-agnostic IR (formatVersion 1), then optimize that IR before profile/legalization/backends (DX9/DXBC, DXIL, SPIR-V).

## Scope (lower)
- Input: semantic JSON from `openfxc-sem analyze` (SM1-SM5, FX).
- Output: IR JSON with functions/blocks/instructions/values/resources, no DX9/DXBC specifics.
- CLI goal: `openfxc-ir lower [--profile <name>] [--entry <name>] < input.sem.json > output.ir.json`.

## Scope (optimize)
- Input: IR JSON from `openfxc-ir lower`.
- Output: optimized IR JSON, preserving invariants and backend agnosticism.
- CLI goal: `openfxc-ir optimize --passes constfold,dce,component-dce,copyprop,algebraic < input.ir.json > output.ir.opt.json`.

## Key principles
- Backend-agnostic: no DXBC opcodes/registers/containers.
- SSA-ish, typed IR with explicit control flow and resource operations.
- Partial results with diagnostics on unsupported constructs.

## Compatibility
Target coverage by shader-model era/features (bootstrap status: planned unless noted):

| Profile band | Lower | Optimize | Notes |
| --- | --- | --- | --- |
| SM1.x (vs_1_1/ps_1_1) | Planned: minimal arithmetic/control plus legacy texture/sample mapping to abstract ops; diagnostics for unsupported intrinsics. | Planned: pass pipeline applies to lowered ops only (constfold/dce/component-dce/copyprop/algebraic). | Dependent on `openfxc-sem` surface; no DX9 opcode names introduced. |
| SM2.x-SM3.x (vs_2_0/ps_2_0/ps_3_0) | Primary target for first implementation: functions/params, resources (`Sample`), branches/loops, swizzles/masks. | Primary target for optimization passes; invariant checks enforced post-pipeline. | Snapshot corpus in tests once implemented. |
| SM4.x-SM5.x (vs_4_0/ps_4_0/vs_5_0/ps_5_0) | Planned via shared IR path; core arithmetic/flow/resources only (no backend/profile payloads). | Planned; same pass set remains profile-agnostic provided IR is well-typed. | No DXIL/DXBC containers emitted here. |
| FX (technique/pass metadata) | Planned: carry entry/technique metadata alongside IR; backend choice deferred. | Passthrough: optimizations ignore FX metadata but preserve IR invariants. | Remains backend/profile agnostic. |

## Quickstart (future)
- Build: `dotnet build src/openfxc-ir/openfxc-ir.csproj` (to be created).
- Lower: `openfxc-hlsl parse foo.hlsl | openfxc-sem analyze --profile vs_4_0 | openfxc-ir lower > foo.ir.json`
- Optimize: `openfxc-ir lower < foo.sem.json | openfxc-ir optimize --passes constfold,dce,component-dce,copyprop,algebraic > foo.ir.opt.json`

## Docs
- Lower TDD: `temp/openfxc-ir-lower-TDD.md`
- Optimize TDD: `temp/openfxc-ir-optimize.md`
- Milestones: `temp/openfxc-ir-lower-MILESTONES.md` (includes optimize track)
- TODO: `temp/openfxc-ir-lower-TODO.md`
