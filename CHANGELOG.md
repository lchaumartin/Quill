<!-- Copyright (c) 2026 Leo CHAUMARTIN. All rights reserved. -->

# Changelog

All notable changes to **Quill** are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

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
