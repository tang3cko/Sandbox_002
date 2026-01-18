# Project Necromancer

## Purpose

This practice project helps you deeply understand GPU Instancing and mass rendering by implementing VAT (Vertex Animation Texture) from scratch.

---

## Goal

You learn how to render massive amounts of animated characters (like DOOM or World War Z) by building everything from data structures to shaders without relying on existing libraries.

---

## Architecture

| Component | Role |
|-----------|------|
| **The Soul Extractor** | Baker - Editor tool that bakes animations into textures |
| **The Flesh Texture** | Data - Position/Normal Texture (RGBAHalf/RGBAFloat) |
| **The Reanimator** | Shader - Custom vertex shader for VAT playback |
| **The Legion** | Instancing - Mass rendering via `DrawMeshInstancedIndirect` |

---

## Implementation steps

1. **Baker Tool**: Editor extension that writes `AnimationClip` frames to `Texture2D`
2. **Playback Shader**: Shader that decodes texture and animates vertices
3. **Instancing Script**: Test script rendering 1000+ instances via GPU Instancing

---

## Project structure

```text
Assets/
└── _Project/
    └── 01_VAT/
        ├── Generated/      # Baked VAT assets (.gitignore)
        ├── Models/         # Source FBX models (.gitignore)
        ├── Scenes/
        ├── Scripts/
        │   ├── Core/       # Runtime scripts
        │   └── Editor/     # Editor extensions (Baker, etc.)
        └── Shaders/
```

---

## Technical specs

### Texture format

- **Format**: RGBAHalf or RGBAFloat
- **U-axis**: Vertex ID (0 ~ VertexCount)
- **V-axis**: Animation frame (Time)
- **RGBA**: X, Y, Z position + reserved

### Shader logic

1. Get vertex index via `SV_VertexID`
2. Calculate frame from `_Time.y` + instance offset
3. Sample position from texture via `tex2Dlod`
4. Interpolate between frames for smooth playback

---

## Setup

1. Download a character with animation from [Mixamo](https://www.mixamo.com/)
2. Place FBX file in `Assets/_Project/01_VAT/Models/`
3. Open Baker window: `Window > VAT > Baker`
4. Select the model and bake

## Environment

- Unity 6
- Universal Render Pipeline (URP)
