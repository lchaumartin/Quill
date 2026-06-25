<!-- Copyright (c) 2026 Leo CHAUMARTIN. All rights reserved. -->

# Quill — Scope

**Quill** is a declarative, reactive UI framework for Unity (URP). Interfaces are authored in `.ui`
documents — a small declarative markup with reactive property bindings — and rendered at a low level
(a single signed-distance-field pass for rectangles, plus lightweight textured layers for text,
images, and custom shaders). It is independent of uGUI and UI Toolkit.

This document defines what the framework does today, what is deliberately deferred, and what is out
of scope. It is the reference for the asset's feature surface.

---

## Implemented

**Reactive core**
- Reactive properties with automatic, dependency-tracked bindings and a per-frame dirty-flush.
- Markup pipeline: lexer → recursive-descent parser → Pratt expression parser → element tree.
- Expressions: `+ - * / %`, comparisons (`== != < > <= >=`), logical `&& || !`, ternary
  `cond ? a : b`, member access, string concatenation, and `Math.*` (abs, min, max, floor, ceil,
  round, sqrt, pow, trig, hypot, clamp, …) plus `Math.PI` / `Math.E`.

**Elements**
- `Item`, `Rectangle` (corner radius, `border.width/color`), `Text` (dynamic font), `Image`
  (texture from `Resources`), positioners `Row` / `Column` / `Grid`, `Repeater`, `NumberAnimation`,
  `MouseArea`, `ShaderEffect`.

**Layout**
- Anchors built on absolute anchor-lines: `fill`, `centerIn`, the four edges, both centers, margins,
  and center offsets — sibling and parent anchoring share one code path.
- Positioners size to content and lay children out reactively.

**Components & reuse**
- Every `.ui` file is a reusable type, instantiated by name with use-site property overrides and
  extra children.
- Component-local `id` scopes (no collisions between instances).
- Signals: `signal name`, emit via `id.name()`, handle with `on<Name>:`.
- A Controls library: Button, Slider, Switch, CheckBox, ProgressBar.

**Input**
- Topmost hit-testing; reactive `pressed`, `containsMouse`, `mouseX`, `mouseY`; signals
  `onPressed/onReleased/onClicked/onEntered/onExited/onPositionChanged`. New and legacy input backends.

**Rendering**
- Rectangles composited in one full-screen SDF pass via a `StructuredBuffer` (scales to thousands).
- Text as one combined glyph mesh per font; images and shader effects as clip-space quads.
- Custom shaders through `ShaderEffect`, with declarative properties forwarded as uniforms.

**C# interoperability**
- `engine.GetValue/SetValue/OnChanged/Connect`, `MouseArea` C# events, and a no-code `QuillBindings`
  inspector component (id + signal → UnityEvent, id + property → UnityEvent&lt;value&gt;).

**Surface lifecycle**
- Show/hide a surface (`Visible` / `Toggle`) for in-game menus.

**Editor & samples**
- `.ui` ScriptedImporter; samples (imported on demand): a widget gallery and a full game-menu
  hierarchy, with a bootstrap component and a scene-setup menu.

---

## Not yet (planned — clean seams already exist)

- **Components**: property **aliases** (`property alias`), signal **parameters**, the `mouse` event object.
- **Animation**: `Behavior on` (implicit), states & transitions, `SequentialAnimation` /
  `ParallelAnimation`, `ColorAnimation`.
- **Data**: dynamic `Repeater` models (runtime count changes), list/data models.
- **Text**: rich text, wrapping, alignment, font selection; clipping and scroll views.
- **Layout**: `Flow` positioner, stretch/spacer layout managers, implicit-size refinements.
- **Input**: keyboard focus, text-input fields, gamepad navigation.
- **Shaders**: `ShaderEffectSource` (render a sub-tree to a texture to post-process real UI).
- **Surfaces**: helpers for ordering multiple stacked surfaces.

---

## Out of scope (for now)

- Full compatibility or parity with any third-party declarative UI toolkit.
- Runtime compilation of arbitrary shader **source strings** (a Unity limitation) — effect shaders
  are referenced by name.
- 3D transforms (rotation / scale / perspective) of UI elements; layout is 2D rectangles.

---

## Platform

Unity 6000.3, URP 17. The rectangle pass needs shader model 4.5 (`StructuredBuffer` in the fragment
stage) — fine on desktop D3D11 / Vulkan / Metal. Works with either the new Input System or the legacy
Input Manager.
