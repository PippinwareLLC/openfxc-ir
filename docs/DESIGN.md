# OpenFXC IR Design - Backend-Agnostic (Not DX9-Specific)

## 0. Purpose

This document explains why the **OpenFXC IR layer** is deliberately backend-agnostic and not DX9-specific. It covers:

- `openfxc-ir lower`
- `openfxc-ir optimize`

Goals:

1. Keep front-end, IR, and backend responsibilities separate.
2. Target DXBC (SM2-5) today and allow DXIL/SPIR-V/other backends later without redesigning IR.
3. Avoid leaking DX9 concepts into IR, which would make the compiler fragile and hard to extend.

This is about the IR layer; parsing and semantic analysis live in their own projects.

---

## 1. Big Picture: Layers and Responsibilities

OpenFXC's pipeline:

```
HLSL Source
  |
openfxc-hlsl lex/parse          (syntax tree)
  |
openfxc-sem analyze             (symbols, types, semantics)
  |
openfxc-ir lower                (backend-agnostic IR)
  |
openfxc-ir optimize             (backend-agnostic IR transforms)
  |
openfxc-profile legalize        (profile-specific constraints, still backend-agnostic)
  |
openfxc-dx9 lower               (IR -> DX9 logical program: SM1-3/SM2-5 ops and registers)
  |
openfxc-dxbc emit               (DXBC bytecode container)
```

Key principle:

> The IR and its transforms (`lower` + `optimize`) must be reusable for non-DX9 backends.

DX9 is just one consumer (`openfxc-dx9` + `openfxc-dxbc`). A future `openfxc-dxil` or `openfxc-spirv` backend should consume the same IR without changes.

---

## 2. What IR Is and Is Not

### 2.1 IR is

- A typed, SSA-ish intermediate representation of HLSL shaders.
- A graph of functions, parameters, basic blocks, instructions, and values (with types and optional component masks).
- A language-level IR, not a hardware-level IR: operations describe what the program does, not how a GPU ISA encodes it.

Examples of IR ops:

- `Add`, `Sub`, `Mul`, `Div`
- `Dot`, `Normalize`, `Saturate`
- `Compare` (`Lt`, `Gt`, `Eq`, etc.)
- `Sample` (abstract texture sampling)
- `LoadInput`, `StoreOutput` (abstract I/O)
- `Return`, `Branch`, `BranchCond`

### 2.2 IR is not

- DX9 opcodes (`mov`, `dp3`, `texld`, etc.).
- DX9 registers (`r0`, `v0`, `o0`, `c0`, `t0`, `s0`).
- DXBC container concepts (chunks like RDEF, ISGN, SHDR).
- Shader-model-specific limits (instruction counts, texture limits); those belong in `openfxc-profile`.

If IR shows DX9-like names such as `Texld`, `r0`, or `v0`, that is a design bug.

---

## 3. Why IR Must Be Backend-Agnostic

### 3.1 Future backends

IR should be consumable by:

- `openfxc-dx9` -> DXBC SM2-5
- Future: `openfxc-dxil` -> DXIL for SM6+
- Future: `openfxc-spirv` -> SPIR-V for Vulkan/glslang

If IR is polluted with DX9 assumptions:

- Every new backend must undo those assumptions.
- Optimizations become harder or unsafe.
- Maintenance cost increases.

### 3.2 Clean separation of concerns

- Semantic analysis answers: what does this HLSL program mean?
- IR answers: how do we represent that meaning in a clean, analyzable form?
- Backends answer: how do we encode that meaning for a specific target?

Mixing these layers makes the compiler brittle.

---

## 4. IR Lowering: Backend-Neutral Outputs

`openfxc-ir lower` takes semantic info (typed AST, symbols, types, intrinsics) and produces IR that:

- Preserves types (e.g., `float4`, `float3x3`).
- Preserves semantic information (e.g., `POSITION0`, `SV_Target1`) as metadata, not registers.
- Represents resources (textures, samplers, cbuffers) as abstract handles.
- Uses abstract operations for all computation.

Example HLSL:

```hlsl
float4 main(float4 pos : POSITION0) : SV_Position
{
    return pos;
}
```

IR (conceptual):

```text
function main (param %1: float4 [semantic POSITION0]) -> float4 [semantic SV_Position]
entry:
    %2 = LoadInput %1
    Return %2
```

No `v0`, `o0`, or `mov` appear; semantics stay as metadata.

### Intrinsics remain abstract

HLSL:

```hlsl
float4x4 WorldViewProj;

float4 main(float4 pos : POSITION) : SV_Position
{
    return mul(pos, WorldViewProj);
}
```

IR (conceptual):

```text
%result = MatMulVec %pos, %WorldViewProj
Return %result
```

DX9 backend chooses how to implement `MatMulVec` (dp4 chain, mul/add, etc.); IR only states the math.

### Texture sampling stays abstract

HLSL:

```hlsl
Texture2D DiffuseTex;
SamplerState DiffuseSampler;

float4 main(float2 uv : TEXCOORD0) : SV_Target
{
    return DiffuseTex.Sample(DiffuseSampler, uv);
}
```

IR (conceptual):

```text
resource DiffuseTex      : Texture2D<float4>
resource DiffuseSampler  : Sampler

%color = Sample %DiffuseTex, %DiffuseSampler, %uv
Return %color
```

Mapping to `texld` or other DX9 ops happens downstream.

---

## 5. IR Optimize: Still Backend-Agnostic

`openfxc-ir optimize` works only on IR semantics. Passes include:

- Constant folding
- Algebraic simplifications
- Dead code elimination (DCE)
- Component-level DCE
- Copy propagation

These passes:

- Operate on values, types, and control flow.
- Ignore which backend will consume the IR.

Examples:

- DCE removes unused `Add` results even without knowing whether the backend is DX9, DXIL, or SPIR-V.
- Component DCE can trim unused lanes (e.g., only X is live) without referencing backend swizzle rules.

---

## 6. Where DX9 Belongs: Downstream Backends

DX9-specific logic is confined to:

- `openfxc-profile` (shader-model limits: instruction count, grad support, temp registers, etc.).
- `openfxc-dx9` (mapping IR ops to DX9 ops and logical registers).
- `openfxc-dxbc` (emitting the DXBC container).

Examples handled in `openfxc-dx9`:

- Assigning `LoadInput`/`StoreOutput` to `vN`/`oN`.
- Decomposing `MatMulVec` into DP4 chains or mul/add sequences.
- Mapping `Sample` to `texld` variants.

DXBC emission (`openfxc-dxbc`) owns container chunks (RDEF, ISGN, OSGN, SHDR, STAT) and binary encoding; IR never sees these.

---

## 7. Anti-Patterns for IR

Avoid introducing:

1. DX9 opcode names in IR ops (`Dp3`, `Texld`, etc.). Use abstract ops like `Dot`, `Sample`.
2. DX9 register names in IR values (`r0`, `v0`, `o0`). Semantics are metadata; register assignment is backend-only.
3. Shader-model limits enforced in IR passes (instruction counts, texture limits). Those checks belong in `openfxc-profile`.
4. DXBC-specific types or signatures in IR (chunk IDs, packed registers). Stick to language-level types (scalar/vector/matrix/resource).

If any feature adds these, push the logic downstream.

---

## 8. Design Summary and Guarantees

Design intent:

- `openfxc-ir lower` and `openfxc-ir optimize` define a clean, abstract IR representing HLSL independent of DX9.
- DX9 is one consumer of IR, not a constraint on its shape.

We guarantee:

1. No IR op names reference DX9 instruction mnemonics.
2. No IR values reference DX9 register names.
3. No DXBC encoding details appear in IR.
4. All DX9-specific reasoning lives in `openfxc-profile`, `openfxc-dx9`, and `openfxc-dxbc`.

Rule of thumb: if the logic must change for a DXIL backend, it probably belongs in a backend/profile stage, not in IR.
