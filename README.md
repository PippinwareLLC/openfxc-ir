# openfxc-ir

Lower the semantic model from `openfxc-sem` into a backend-agnostic IR (formatVersion 1), then (future) optimize that IR before profile/legalization/backends (DX9/DXBC, DXIL, SPIR-V).

## Scope (lower)
- Input: semantic JSON (formatVersion 3) from `openfxc-sem analyze` (SM1–SM5, FX).
- Output: IR JSON with functions/blocks/instructions/values/resources, no DX9/DXBC specifics.
- CLI: `openfxc-ir lower [--profile <name>] [--entry <name>] [--input <path>] < input.sem.json > output.ir.json`.

## Scope (optimize)
- Input: IR JSON from `openfxc-ir lower`.
- Output: optimized IR JSON, preserving invariants and backend agnosticism (passes not implemented yet).
- CLI goal: `openfxc-ir optimize --passes constfold,dce,component-dce,copyprop,algebraic < input.ir.json > output.ir.opt.json`.

## Key principles
- Backend-agnostic: no DXBC/DXIL/SPIR-V opcodes, registers, or containers.
- SSA-ish, typed IR with explicit control flow and resource operations.
- Partial results with diagnostics on unsupported constructs; never crash on invalid input.

## Compatibility matrix (current)
| Profile band | Lower | Optimize | Notes |
| --- | --- | --- | --- |
| SM1.x (vs_1_1/ps_1_1) | Planned | Planned | Depends on `openfxc-sem` surface; expect diagnostics for unsupported legacy intrinsics. |
| SM2.x–SM3.x (vs_2_0/ps_2_0/ps_3_0) | **Alpha**: functions/params, resources (`Sample`), swizzles, if/else/loops, common intrinsics (`mul`, `tex*`). | Not yet | Snapshot coverage in tests (`ps_texture`, `ps_sm3_texproj`). |
| SM4.x–SM5.x (vs_4_0/ps_4_0/vs_5_0/ps_5_0/cs_5_0) | **Experimental**: arithmetic/control flow lower; resource loads emit diagnostics for unsupported cases (cbuffer fields, structured buffers). | Not yet | Snapshots capture current diagnostics (`sm4_cbuffer`, `sm5_structured`). |
| FX (techniques/passes) | **Minimal**: entry lowering works when present; technique metadata not yet projected into IR. | Not yet | Snapshot `fx_basic` covers entry path. |

## IR schema (formatVersion 1)
- `profile`, `entryPoint` (function/stage), `functions` (name/returnType/parameters/blocks), `blocks` (id/instructions with `op`/`operands`/`result`/`terminator`/`type`/`tag`), `values` (id/kind/type/name/semantic), `resources` (name/kind/type), `diagnostics` (severity/message/stage).
- Invariants: typed values, single-definition per result, blocks terminated, operands refer to known values, no backend-specific tokens in ops/tags.

## Quickstart
- Build: `dotnet build openfxc-ir.sln`
- Lower (library): `var module = new LoweringPipeline().Lower(new LoweringRequest(semanticJson, profileOverride, entryOverride));`
- Lower (CLI): `openfxc-hlsl parse foo.hlsl | openfxc-sem analyze --profile vs_2_0 | openfxc-ir lower --entry main > foo.ir.json`
- Tests: `dotnet test` (includes snapshot goldens under `tests/OpenFXC.Ir.Tests/snapshots`).

## Docs
- Design: `docs/DESIGN.md`
- Lower TDD: `docs/TDD.md`
- Optimize TDD: `docs/TDD-Optimize.md`
- Milestones: `docs/MILESTONES.md`
- TODO: `docs/TODO.md`
