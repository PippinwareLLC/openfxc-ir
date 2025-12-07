# openfxc-ir

Lower the semantic model from `openfxc-sem` into a backend-agnostic IR (formatVersion 1), then optimize that IR before profile/legalization/backends (DX9/DXBC, DXIL, SPIR-V).

## Scope (lower)
- Input: semantic JSON (formatVersion 3) from `openfxc-sem analyze` (SM1-SM5, FX).
- Output: IR JSON with functions/blocks/instructions/values/resources, no DX9/DXBC specifics.
- CLI: `openfxc-ir lower [--profile <name>] [--entry <name>] [--input <path>] < input.sem.json > output.ir.json`.

## Scope (optimize)
- Input: IR JSON from `openfxc-ir lower`.
- Output: optimized IR JSON (pipeline runs constfold, algebraic, copyprop, DCE, and component-level DCE).
- CLI: `openfxc-ir optimize [--passes constfold,dce,component-dce,copyprop,algebraic] [--profile <name>] [--input <path>] < input.ir.json > output.ir.opt.json`.
- Defaults: when `--passes` is omitted, all available passes (constfold, algebraic, copyprop, dce, component-dce) run in that order; unknown passes now surface errors that list available options.

## Key principles
- Backend-agnostic: no DXBC/DXIL/SPIR-V opcodes, registers, or containers.
- SSA-ish, typed IR with explicit control flow and resource operations.
- Partial results with diagnostics on unsupported constructs; never crash on invalid input.

## Compatibility matrix (current)
| Profile band | Lower | Optimize | Notes |
| --- | --- | --- | --- |
| SM1.x (vs_1_1/ps_1_1) | **Supported**: baseline ps_1_1 lowering validated across corpus; legacy intrinsic coverage widened. | **Supported**: constfold/algebraic/copyprop/DCE/component-DCE applied. | DXSDK corpus green. |
| SM2.x-SM3.x (vs_2_0/ps_2_0/ps_3_0) | **Supported**: functions/params/resources (Sample/Load/Store), swizzles, if/else/loops, intrinsics (`mul`, `tex*`, dot/normalize/etc.), int/uint/half promotion. | **Supported**: full pass pipeline (constfold, algebraic, copyprop, DCE, component-DCE). | Snapshots (`ps_texture`, `ps_sm3_texproj`) plus full DXSDK sweep. |
| SM4.x-SM5.x (vs_4_0/ps_4_0/vs_5_0/ps_5_0/cs_5_0) | **Supported**: arithmetic/control flow, structured buffer indexing, cbuffer/struct field loads, RW/structured writes, return widening. | **Supported**: same pass pipeline; honors resource side-effects. | Snapshots (`sm4_cbuffer`, `sm5_structured`) and DXSDK corpus pass. |
| FX (techniques/passes) | **Supported**: techniques/passes projected into IR metadata; entry lowering uses FX bindings. | **Supported**: optimize runs on IR while preserving FX metadata. | Snapshot `fx_basic`; full corpus sweep green. |

## IR schema (formatVersion 1)
- `profile`, `entryPoint` (function/stage), `functions` (name/returnType/parameters/blocks), `blocks` (id/instructions with `op`/`operands`/`result`/`terminator`/`type`/`tag`), `values` (id/kind/type/name/semantic), `resources` (name/kind/type), `diagnostics` (severity/message/stage).
- Invariants: typed values, single-definition per result, blocks terminated, operands refer to known values, no backend-specific tokens in ops/tags.

## Quickstart
- Build: `dotnet build openfxc-ir.sln`
- Lower (library): `var module = new LoweringPipeline().Lower(new LoweringRequest(semanticJson, profileOverride, entryOverride));`
- Lower (CLI): `openfxc-hlsl parse foo.hlsl | openfxc-sem analyze --profile vs_2_0 | openfxc-ir lower --entry main > foo.ir.json`
- Optimize (CLI): `openfxc-ir optimize --input foo.ir.json > foo.ir.opt.json`
- End-to-end example: `openfxc-hlsl parse foo.hlsl | openfxc-sem analyze --profile ps_2_0 | openfxc-ir lower --entry main | openfxc-ir optimize > foo.ir.opt.json`
- Tests: `dotnet test` (includes lowering and optimize snapshots under `tests/OpenFXC.Ir.Tests/snapshots`).

## Docs
- Design: `docs/DESIGN.md`
- Lower TDD: `docs/TDD.md`
- Optimize TDD: `docs/TDD-Optimize.md`
- Milestones: `docs/MILESTONES.md`
- TODO: `docs/TODO.md`
