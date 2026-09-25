<!-- Copyright (c) 2026 Leo CHAUMARTIN. MIT licensed — see LICENSE.md. -->

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
- Reactive properties with automatic, dependency-tracked bindings and a per-frame dirty-flush;
  assignment replaces a binding (QML semantics).
- Markup pipeline: lexer → recursive-descent parser → Pratt expression parser → element tree.
- Expressions: arithmetic, comparisons (incl. `===`), `&& || !` returning operands, bit flags,
  ternary, hex/exponent numbers, lists with indexing, grouped-property access, `Math.*`, colour
  functions (`Qt.rgba/hsva/hsla/lighter/darker/tint/alpha`) and channels (`c.hsvHue`…), number /
  string / list methods (`toFixed`, `split`, `arg`, `join`…), globals (`parseInt`, `qsTr`…), enums.
- Statements in handlers, functions and scripts: assignments incl. `+= -= *= /= ++ --`, `if/else`,
  `for`, `while`, `break`, `continue`, `return`, `var` locals, calls, `console.*`.

**Elements**
- `Item`, `Rectangle` (corner radius, `border.width/color`), `Text` (wrapping, alignment, eliding,
  bold/italic, line height, implicit size), `Image` (texture from `Resources`), positioners `Row` /
  `Column` / `Grid` / `Flow` (padding, hidden children skipped), `Repeater` (live count or list model,
  `modelData`, per-instance id scope), `MouseArea`, `ShaderEffect`, `Timer`.

**Animation**
- `NumberAnimation`, `PropertyAnimation`, `ColorAnimation`, `SpringAnimation`, `SmoothedAnimation`,
  `PauseAnimation`, `SequentialAnimation`, `ParallelAnimation`, `ScriptAction`, `PropertyAction`;
  value sources (`on x`) and standalone animations with `start/stop/restart/pause/resume/complete`
  and `started/stopped/finished`; loops; the full QML easing set.
- `Behavior on prop` (retargeting; springs keep momentum).
- States & transitions: `State` (`name`, `when`, `extend`), `PropertyChanges` (live bindings,
  `id.prop` shorthand, `restoreEntryValues`, `explicit`), `Transition` (`from`/`to` with wildcards and
  lists, `reversible`), restoring original values and bindings.

**Layout**
- Anchors built on absolute anchor-lines: `fill`, `centerIn`, the four edges, both centers, margins,
  and center offsets — sibling and parent anchoring share one code path.
- Positioners solve their layout in one binding and size to content reactively.

**Components & reuse**
- Every `.ui` file is a reusable type, instantiated by name with use-site property overrides,
  handlers and extra children; a component's root may be another component.
- Component-local `id` scopes (no collisions between instances).
- `property alias`, signals with parameters, `function` declarations, `Component.onCompleted`,
  `on<Property>Changed` handlers.
- Themes: every theme implements the same controls (Panel, Label, Button, CheckBox, Switch, Slider,
  ProgressBar, TabBar; ColorPicker shared) and exposes a live palette as the global `Theme`. Slate
  ships built in; Frost, Arcade, Tome, Vector, Pebble, Bitmap and Pop come as samples, with free
  (OFL) fonts.

**Input**
- Topmost hit-testing; reactive `pressed`, `containsMouse`, `mouseX`, `mouseY`; signals
  `onPressed/onReleased/onClicked/onDoubleClicked/onPressAndHold/onEntered/onExited/
  onPositionChanged/onWheel` with `mouse` / `wheel` objects; `drag.target` with axis and bounds.
  New and legacy input backends.

**Rendering**
- Rectangles composited in one full-screen SDF pass via a `StructuredBuffer` (scales to thousands).
- Text as one combined glyph mesh per font (`font.family`: Resources fonts or installed fonts,
  letter spacing, capitalization); images and shader effects as clip-space quads.
- Soft edges on rectangles (`softness`) for shadows and glows.
- Custom shaders through `ShaderEffect`, with declarative properties forwarded as uniforms. Bundled
  effects: `Quill/Effect/Blur`, `Quill/Effect/Glass`, `Quill/ColorField`.

**C# interoperability**
- `engine.GetValue/SetValue/OnChanged/Connect/Invoke` (Connect with signal arguments), `MouseArea`
  C# events, and a no-code `QuillBindings` inspector component (id + signal → UnityEvent, id +
  property → UnityEvent&lt;value&gt;).

**Surface lifecycle**
- Show/hide a surface (`Visible` / `Toggle`) for in-game menus.

**Editor & samples**
- `.ui` ScriptedImporter; the **Quill Document** component (document + theme in the inspector, a
  theme picker, **GameObject ▸ Quill ▸ Quill Document**); one sample per theme, each a single
  gallery scene.

---

## Not yet (planned — clean seams already exist)

- **Input**: keyboard focus, `Keys` handlers, text-input fields, gamepad navigation.
- **Layout & views**: clipping (`clip`), `Flickable` / scroll views, stretch/spacer layout managers.
- **Data**: `ListModel` and object-list models (`model.name`).
- **Text**: rich text, kerning.
- **Theme controls**: `TextField`, `Dropdown`, `Dialog`, `Tooltip`.
- **Animation**: `AnchorAnimation`, `ParentChange`, `AnchorChanges`, `PathAnimation`.
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
