# OpenFXC IR Design – Backend-Agnostic (Not DX9-Specific)

## 0. Purpose

This document explains the design of the **OpenFXC IR layer** and why:

* `openfxc-ir lower`
* `openfxc-ir optimize`

are **explicitly not DX9-specific**.

The goal is to:

1. Clearly separate **front-end**, **middle-end (IR)**, and **back-end** responsibilities.
2. Make it possible to target **DXBC (SM2–5)** today, and potentially **DXIL / SPIR-V / other backends** tomorrow, **without redesigning the IR**.
3. Avoid “leaking” DX9 concepts into IR where they don’t belong, which would make the compiler fragile and hard to extend.

This document is specifically about the IR layer; HLSL parsing/semantics are covered in their respective projects.

---

## 1. Big Picture: Layers & Responsibilities

OpenFXC’s core compilation pipeline is layered as follows:

```text
HLSL Source
   ↓
openfxc-hlsl lex/parse         (syntax tree)
   ↓
openfxc-sem analyze            (symbols, types, semantics)
   ↓
openfxc-ir lower               (backend-agnostic IR)
   ↓
openfxc-ir optimize            (backend-agnostic IR transforms)
   ↓
openfxc-profile legalize       (profile-specific constraints, still backend-agnostic)
   ↓
openfxc-dx9 lower              (IR → DX9 logical program: SM1–3/SM2–5 ops & registers)
   ↓
openfxc-dxbc emit              (DXBC bytecode container)
```

**Key principle:**

> The IR and its transforms (`lower` + `optimize`) must be reusable for non-DX9 backends.

* DX9 is “just” one backend (`openfxc-dx9` + `openfxc-dxbc`).
* Later, a `openfxc-dxil` or `openfxc-spirv` backend should be able to consume IR without needing to change it.

---

## 2. What IR *Is* and *Isn’t*

### 2.1 IR *is*:

* A **typed, SSA-ish intermediate representation** of HLSL shaders.
* A graph of:

  * Functions
  * Parameters
  * Basic blocks
  * Instructions
  * Values (with types and optional component masks)
* A **language-level IR**, not a hardware-level IR.

IR operations describe **what** the program does, not **how** a particular GPU instruction set implements it.

Examples of IR ops:

* `Add`, `Sub`, `Mul`, `Div`
* `Dot`, `Normalize`, `Saturate`
* `Compare` (`Lt`, `Gt`, `Eq`, etc.)
* `Sample` (abstract texture sampling)
* `LoadInput`, `StoreOutput` (abstract I/O)
* `Return`, `Branch`, `BranchCond`

### 2.2 IR is **not**:

* DX9 opcodes (`mov`, `dp3`, `texld`, `texldp`, etc.).
* DX9 registers (`r0`, `v0`, `o0`, `c0`, `t0`, `s0`).
* DXBC container concepts (chunks like RDEF, ISGN, SHDR).
* Shader model–specific limitations (instruction counts, texture limits) – those are handled in `openfxc-profile`.

If you see anything DX9-like in IR (e.g., “Texld”, “r0”, “v0”), that’s a bug in design.

---

## 3. Why IR Must Be Backend-Agnostic

### 3.1 Future backends

We want to treat DX9 as one backend among several. The same HLSL IR should be consumable by:

* `openfxc-dx9` → DXBC SM2–5
* Future: `openfxc-dxil` → DXIL for SM6+
* Future: `openfxc-spirv` → SPIR-V for Vulkan/glslang integration

If IR is polluted with DX9 assumptions, then:

* Every new backend will have to **undo** those assumptions.
* Certain optimizations become difficult or unsafe.
* Maintenance cost explodes.

### 3.2 Clean separation: HLSL meaning vs hardware encoding

* **Semantic analysis** answers: *“What does this HLSL program mean?”*
* **IR** answers: *“How do we represent that meaning in a clean, analyzable form?”*
* **DX9 backend** answers: *“How do we encode that meaning as DX9 instructions and registers?”*

Mixing these questions in the IR layer makes the compiler brittle.

---

## 4. IR Lowering: What It Produces (and Why It’s Backend-Neutral)

### 4.1 From semantics to IR

`openfxc-ir lower` takes:

* Typed AST
* Semantic info (symbols, types, semantics, intrinsic resolution)

and produces IR that:

* Still knows **types** (e.g., `float4`, `float3x3`).
* Still knows **semantic information** (e.g., `POSITION0`, `SV_Target1`) – as metadata, *not* as DX9 registers.
* Still knows **resources** (textures, samplers, cbuffers) as abstract resource handles.
* Expresses all computation in terms of **simple, abstract operations**.

Example:

HLSL:

```hlsl
float4 main(float4 pos : POSITION0) : SV_Position
{
    return pos;
}
```

IR:

```text
function main (param %1: float4 [semantic POSITION0]) -> float4 [semantic SV_Position]
entry:
    %2 = LoadInput %1                ; abstract: read input parameter
    Return %2
```

**Note:**

* No `v0`, `o0`, `mov`.
* Just `LoadInput` and `Return` with semantics attached to function signature, not registers.

### 4.2 Intrinsics: abstract, not opcodes

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
%pos     : float4
%M       : float4x4
%result  : float4

%result = MatMulVec %pos, %M
Return %result
```

Later, the DX9 backend may choose to implement `MatMulVec` as:

* 4 DP4s,
* or 4 sequences of `mul` + `add`,
* or something else, depending on profile and optimization.

But IR only cares that it’s a **matrix × vector multiply**, not how DX9 spells it.

### 4.3 Texture sampling

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

%uv    : float2
%color = Sample %DiffuseTex, %DiffuseSampler, %uv
Return %color
```

DX9 backend will map `Sample` to `texld` or equivalent, but IR has no knowledge of that.

---

## 5. IR Optimize: Middle-End Only, Still Not DX9-Specific

`openfxc-ir optimize` works purely at the IR level. Its passes are:

* **Constant folding**
* **Arithmetic simplification**
* **Dead code elimination (DCE)**
* **Component-level DCE**
* **Copy propagation**
* etc.

All of these are **backend-agnostic**:

* They work on values, types, and control flow.
* They do not care what backend will eventually use the IR.

Examples:

### 5.1 Dead code elimination

If you have:

```text
%3 = Add %1, %2
Return %1
```

and `%3` is never used, DCE removes the `Add` regardless of whether the backend is DX9, DXIL, or SPIR-V.

### 5.2 Component DCE

HLSL:

```hlsl
float4 main(float4 pos : POSITION) : SV_Target
{
    float4 a = pos * float4(1, 2, 3, 4);
    return a.xxxx;
}
```

IR optimizations can deduce that only the X component matters without knowing:

* Whether the final backend will use `dp4`, `mov`, `swizzle`, etc.

All that matters is **IR semantics**: only X is live.

---

## 6. Where DX9 *Does* Belong: Backends

DX9-specific concepts are confined to:

* `openfxc-profile`
  (enforcing shader model limits: instruction count, grad support, temp registers, etc.)
* `openfxc-dx9`
  (mapping IR ops to DX9 ops and logical registers)
* `openfxc-dxbc`
  (emitting final DXBC bytecode container)

### 6.1 DX9 lowering (`openfxc-dx9`)

This is where we finally decide:

* Which IR `LoadInput` maps to which DX9 input register (e.g., `v0`).
* Which IR `StoreOutput` maps to which DX9 output register (e.g., `o0`, `oC0`).
* How `MatMulVec` is decomposed into DX9 instructions (`dp4`, `mul`, `add`, etc.).
* How `Sample` maps to `texld` or other DX9-specific opcodes.

Example mapping:

```text
IR:    %result = Dot %a, %b
DX9:   dp3 r0, v0, c0  ; or dp4, depending on types
```

The IR doesn’t know about `dp3` vs `dp4`. That’s backend logic.

### 6.2 DXBC emission (`openfxc-dxbc`)

DXBC is the **binary container**; only the final backend knows:

* Chunks: RDEF, ISGN, OSGN, SHDR, STAT, etc.
* Exact binary encoding for instructions and operands.
* Hashing and signatures.

IR never sees these.

---

## 7. Anti-Patterns: Things IR Must *Not* Do

Here are concrete examples of things that **must not** happen in IR:

1. **Hard-coding DX9 op names**

   * ❌ `IrOp.Dp3`
   * ❌ `IrOp.Texld`
   * ✅ `IrOp.Dot`
   * ✅ `IrOp.Sample`

2. **Storing DX9 register indices in IR values**

   * ❌ `value.Location = "r0"`
   * ❌ `value.Location = "v0"`
   * ✅ `value.Semantic = POSITION0` (metadata)
   * ✅ Register assignment happens in DX9 backend only.

3. **Applying DX9 instruction count limits in IR passes**

   * ❌ `if (InstructionCount > 64 && profile == ps_2_0) error`
   * ✅ That check belongs in `openfxc-profile`.

4. **Encoding DXBC-specific types or signatures in IR**

   * ❌ DXBC chunk IDs.
   * ❌ Packed DXBC registers.
   * ✅ Simple, language-level types (scalar, vector, matrix, resource).

If any refactor or feature introduces this, it should be pushed downstream, not accepted in IR.

---

## 8. Design Summary & Guarantees

**Design Intent:**

* `openfxc-ir lower` and `openfxc-ir optimize` define a **clean, abstract IR** that represents HLSL programs independent of DX9.
* DX9 is a **consumer of IR**, not a constraint on its shape.

**We guarantee:**

1. No IR op names will reference DX9 instruction mnemonics.
2. No IR values will reference DX9 register names.
3. No DXBC encoding details will ever appear in IR.
4. All DX9-specific reasoning is abstracted into:

   * `openfxc-profile`
   * `openfxc-dx9`
   * `openfxc-dxbc`

**If in doubt**:
Ask, “Can this logic work untouched if we wrote a DXIL backend later?”

* If **yes**, it’s likely IR or optimize territory.
* If **no**, it belongs in the DX9 backend or profile layer.
