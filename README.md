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

## Quickstart (future)
- Build: `dotnet build src/openfxc-ir/openfxc-ir.csproj` (to be created).
- Lower: `openfxc-hlsl parse foo.hlsl | openfxc-sem analyze --profile vs_4_0 | openfxc-ir lower > foo.ir.json`
- Optimize: `openfxc-ir lower < foo.sem.json | openfxc-ir optimize --passes constfold,dce,component-dce,copyprop,algebraic > foo.ir.opt.json`

## Docs
- Lower TDD: `temp/openfxc-ir-lower-TDD.md`
- Optimize TDD: `temp/openfxc-ir-optimize.md`
- Milestones: `temp/openfxc-ir-lower-MILESTONES.md` (includes optimize track)
- TODO: `temp/openfxc-ir-lower-TODO.md`

## Next steps
- Stand up CLI skeleton for `lower`.
- Implement IR model and lowering per TDD invariants.
- Wire the optimize pipeline and passes per TDD.
- Add unit/snapshot/invariant tests and goldens for both lower and optimize.
