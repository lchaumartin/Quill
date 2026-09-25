<!-- Copyright (c) 2026 Leo CHAUMARTIN. MIT licensed — see LICENSE.md. -->

# Changelog

All notable changes to **Quill** are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **`Quill/Effect/Blur`** — a frosted backdrop-blur `ShaderEffect` with `radius`, `cornerRadius` and
  `tint`, as a dual-SubShader: `_CameraOpaqueTexture` on URP, `GrabPass` on Built-in.
- **`Quill/Effect/LiquidGlass`** — a rounded glass pane that refracts, blurs and rim-lights the
  backdrop (`cornerRadius`, `refraction`, `softness`, `radius`, `tint`); same dual-SubShader backdrop
  sources as `Quill/Effect/Blur`. `softness` is the width of a rounded, lit, refracting bevel along a
  crisp silhouette (0 = sharp flat edge) rather than an alpha fade.
- **Liquid Glass sample** (`Samples~/LiquidGlass`) — a ready-to-play scene with a control-centre UI
  built from LiquidGlass panes over an animated 3D backdrop; reusable `GlassPane`, `GlassToggle`,
  `GlassChip` and `GlassLevel` components; `QuillTweens` for eased `foo`/`fooTarget` transitions.
- Liquid Glass sample: a **Tune glass** panel (button or Tab) with live sliders for refraction, blur,
  corner roundness, edge softness and tint, driving every pane through a `glassStyle` element; new
  `GlassSlider` component; `GlassPane` gains `corner` and `lens`.

### Changed
- Relicensed under the **MIT License** (previously proprietary / all rights reserved).
- The `GameMenu` sample's animated background is now a blurred, dimmed backdrop.

### Fixed
- Replacing a binding — with a new one, or by assigning a value in a handler or from C# — now fully
  detaches the old one. Before, it kept listening to its dependencies and could overwrite the new
  value, e.g. a component's default binding beating a use-site override such as `display: ...`.
- `ShaderEffect` no longer writes a float `_Time` on its materials (Unity rejected it every frame with
  "Trying to set builtin parameter"). Effect shaders use Unity's built-in `_Time` (`_Time.y` = seconds).
- `Quill/Effect/LiquidGlass` and `Quill/Effect/Blur` now declare their uniforms in `Properties` and a
  `UnityPerMaterial` cbuffer. Before, URP's SRP Batcher dropped per-material values, so with several
  effects on screen every pane drew with one pane's `_Rect`, radius and tint.
- `GameMenu` backdrop tint and the README `ShaderEffect` example used `#AARRGGBB`, which Unity parses
  as `#RRGGBBAA` first — the menu got a faint red wash instead of an 82 % dark dim. Now `#RRGGBBAA`.

### Removed
- The `Quill/Effect/Plasma` and `Quill/Effect/Radial` example shaders. `Quill/Effect/Blur` is now the
  reference template for writing a `ShaderEffect` shader.

## [0.1.0] — 2026-06-22

First packaged release.

### Added
- **Reactive core**: properties with automatic dependency-tracked bindings and a per-frame flush.
- **Markup pipeline**: lexer, recursive-descent parser, Pratt expression parser, element tree.
- **Expressions**: arithmetic (`+ - * / %`), comparisons, logical (`&& || !`), ternary, member
  access, string concatenation, and `Math.*` functions plus `Math.PI` / `Math.E`.
- **Elements**: `Item`, `Rectangle` (radius, border), `Text`, `Image`, `Row` / `Column` / `Grid`,
  `Repeater`, `NumberAnimation`, `MouseArea`, `ShaderEffect`.
- **Anchors** (fill, centerIn, edges, centers, margins, offsets) on an absolute anchor-line model.
- **Components**: every `.ui` file is a reusable type with use-site overrides, component-local id
  scopes, and signals; a Controls library (Button, Slider, Switch, CheckBox, ProgressBar).
- **Input**: hit-testing with `pressed` / `containsMouse` / `mouseX` / `mouseY` and pointer signals;
  new and legacy input backends.
- **Rendering**: one-pass `StructuredBuffer` SDF rectangles, combined glyph mesh text, textured
  image / effect quads.
- **C# interop**: `GetValue` / `SetValue` / `OnChanged` / `Connect`, `MouseArea` C# events, and the
  no-code `QuillBindings` inspector component.
- **Surface lifecycle**: `Visible` / `Toggle` for menus.
- **Editor**: `.ui` ScriptedImporter (a scene-setup menu ships with the sample).
- **Samples**: a widget gallery and a full game-menu hierarchy, plus a bootstrap component.

### Known limitations
- HDRP is not supported (shaders are authored for URP / Built-in).
- See `SCOPE.md` for the full list of deferred features and non-goals.
