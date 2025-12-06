# TDD - `openfxc-ir` (IR Lowering for OpenFXC)

## 0. Overview
`openfxc-ir` lowers the semantic model produced by `openfxc-sem` into a backend-agnostic intermediate representation (IR). It must stay free of DX9/DXBC concerns: no opcodes, registers, or container fields.

Pipeline position:
```
openfxc-hlsl parse -> openfxc-sem analyze -> openfxc-ir lower -> openfxc-ir optimize -> openfxc-profile -> openfxc-dx9 -> openfxc-dxbc
 (AST)                 (semantic model)       (IR module)         (IR transforms)      (profile rules)     (DX9 lowering)   (bytecode)
```

## 1. Scope & Goals
- Consume semantic JSON from `openfxc-sem analyze`.
- Emit SSA-ish, typed IR: functions, basic blocks, instructions, values, masks; explicit control flow; abstract resources.
- Backend agnostic: no DX9/DXBC specifics.
- Emit diagnostics when lowering is impossible.

Non-goals: optimization, hardware limit enforcement, DX9 selection/packing, DXBC/DXIL/SPIR-V emission.

## 2. CLI Contract
```
openfxc-ir lower [--profile <name>] [--entry <name>] < input.sem.json > output.ir.json
```
- `--profile` defaults to semantic input profile; `--entry` overrides entry name.
- Exit codes: `0` success (with diagnostics allowed), `1` internal error (I/O/JSON/etc).

## 3. Schemas (High Level)
Input: semantic JSON (profile, symbols, types, entryPoints, diagnostics, node ids).
Output (conceptual):
```json
{
  "formatVersion": 1,
  "profile": "vs_2_0",
  "entryPoint": { "function": "main", "stage": "Vertex" },
  "functions": [ { "name": "main", "returnType": "float4", "parameters": [...], "blocks": [...] } ],
  "values": [ { "id": 1, "type": "float4", "kind": "Parameter" }, ... ],
  "resources": [ /* textures/samplers/cbuffers/globals */ ],
  "diagnostics": []
}
```
Tests must assert: functions/blocks/instructions/values are well-formed; types/masks attached; entry point identified; no DX9 artifacts.

## 4. IR Design Invariants
1) SSA-ish: each computed value has one definition.  
2) Typed: every value and instruction carries a type.  
3) Blocks: each ends with a terminator; no instructions after terminators.  
4) Backend agnostic: no DX9 op names, registers, or container fields.  
5) Component masks: swizzles/partial writes represented explicitly.

## 5. Lowering Responsibilities
- Functions/params/returns lowered to IR functions with entry blocks.
- Expressions -> IR ops:
  - Binary arithmetic -> `Add`/`Sub`/`Mul`/`Div` with typed operands.
  - Swizzles -> `Swizzle` op or operand masks producing correct component types.
  - Intrinsics -> abstract ops (e.g., `Mul`/`MatrixMultiply`/`Sample`), never DX9 opcodes.
- Control flow:
  - If/else: blocks for entry/then/else/merge; conditional branches; (phi nodes optional initially, but CFG correct).
  - Loops: entry/cond/body/exit blocks with proper branches and induction values.
- Resources:
  - Abstract table entries for textures/samplers/cbuffers/globals.
  - Sampling as `Sample` op with resource/sampler/value operands.
- Returns/outputs: `Return` terminator with value matching function return type; semantics remain metadata (no register binding).

## 6. Diagnostics
- Unsupported intrinsic: diagnostic (e.g., “Intrinsic 'foo' not supported by IR lowering.”).
- Missing entry point: diagnostic; may skip lowering.
- Impossible type combination: diagnostic; skip offending instruction.
- Lowering tolerates semantic diagnostics but may bail on critical ones.

## 7. Tests (TDD)
- Unit tests: instruction lowering (binary/unary/intrinsics), CFG for if/else and loops, resource sampling.
- Snapshot tests: run `parse -> sem -> ir lower` on small shaders (VS/PS SM2, PS texture, branching, loop); compare IR JSON goldens.
- Invariant tests over IR JSON: single def per value (except params/globals), no instructions after terminators, no DX9 keywords in op/resource/value kinds.

## 8. Definition of Done (initial)
- CLI works end-to-end producing IR JSON.
- Coverage: functions/params/returns, expressions (binary/unary/calls/swizzles), resources, simple CFG (if/else, loops).
- Backend agnostic guarantee enforced by tests.
- Invariants hold (SSA-ish, typed, block termination).
- Integration on a small SM2/3/4/5 corpus succeeds without crashes; diagnostics only when expected.

## 9. Future Extensions (not required for DoD)
- Explicit SSA phi nodes.
- Broader intrinsic lowering (Reflect/Refract/DDX/DDY/etc).
- HIR vs LIR forms; IR pretty-printer; hooks for DXIL/SPIR-V backends.

## 10. Project structure
- Core class library: expose the IR model and lowering API (consume semantic JSON, produce IR JSON/object model).
- CLI: thin wrapper over the core library; only argument parsing and I/O.
- Tests: reference the core library directly, alongside `openfxc-sem` and `openfxc-hlsl` (lex/parse) to build end-to-end fixtures without invoking the CLI.
- For optimization, see `temp/openfxc-ir-optimize.md` for the follow-on stage that consumes this IR.
