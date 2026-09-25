<!-- Copyright (c) 2026 Leo CHAUMARTIN. MIT licensed — see LICENSE.md. -->

# Changelog

All notable changes to **Quill** are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **`Quill/Effect/Blur`** — a frosted backdrop-blur `ShaderEffect` with `radius`, `cornerRadius` and
  `tint`, as a dual-SubShader: `_CameraOpaqueTexture` on URP, `GrabPass` on Built-in.
- **`Quill/Effect/Glass`** — a rounded glass pane that refracts, blurs and rim-lights the
  backdrop (`cornerRadius`, `refraction`, `softness`, `radius`, `tint`); same dual-SubShader backdrop
  sources as `Quill/Effect/Blur`. `softness` is the width of a rounded, lit, refracting bevel along a
  crisp silhouette (0 = sharp flat edge) rather than an alpha fade.
- **Themes.** A theme is a folder under `Resources/QuillThemes/<Name>`: the controls plus `Theme.ui`,
  a palette every document reads (and can write, live) as the global `Theme`. Every theme implements
  the same controls — `Panel`, `Label`, `Button`, `CheckBox`, `Switch`, `Slider`, `ProgressBar`,
  `TabBar`, with the shared `ColorPicker` — so switching theme never means editing a document.
  `QuillThemes.Apply(engine, name)`; a theme falls back to Slate for anything it leaves out.
- **Slate**, the built-in theme (the former controls, restyled and completed: `Label`, `Panel`,
  `TabBar`; `Slider`/`ProgressBar` gain `from`/`to`/`step`; `CheckBox`/`Switch` gain `text`).
- **Theme samples**, one scene each: **Frost** (glass over an animated 3D backdrop), **Arcade**,
  **Tome**, **Vector**, **Pebble**, **Bitmap** and **Pop**, with free (OFL) fonts.
- **Gallery** (`Resources/QuillThemes/Gallery.ui`) — every control of a theme on one screen, with a
  live accent picker.
- **`QuillDocument`** component — document + theme in the inspector (the theme picker lists the themes
  in the project and switches live in Play mode), no code needed; shows the gallery when no document
  is assigned. **GameObject ▸ Quill ▸ Quill Document**.
- `Rectangle.softness` — feathered edges for soft shadows and glows.
- `Text`: `font.family` (a font under `Resources`, or installed font names), `font.letterSpacing`,
  `font.capitalization` (`Font.AllUppercase`…). Text renders one mesh per font.
- `QtObject`, a plain non-visual object.
- **Animation, QML-style**: `ColorAnimation`, `SpringAnimation`, `SmoothedAnimation`,
  `PauseAnimation`, `SequentialAnimation`, `ParallelAnimation`, `ScriptAction`, `PropertyAction`;
  `start/stop/restart/pause/resume/complete()` with `started/stopped/finished` signals; `properties`,
  `targets`, default `from` = current value; the full easing set (`In/Out/InOut/OutIn` × Quad, Cubic,
  Quart, Quint, Sine, Expo, Circ, Back, Elastic, Bounce) with amplitude/period/overshoot.
- **`Behavior on prop`** — animates every change of a property; retargets smoothly.
- **States & transitions** — `State` (`name`, `when`, `extend`), `PropertyChanges` (live bindings,
  `id.prop` shorthand, `restoreEntryValues`, `explicit`), `Transition` (`from`/`to`, wildcards,
  `reversible`); original values and bindings are restored.
- **`Timer`** (`interval`, `repeat`, `triggeredOnStart`, `onTriggered`, `start/stop/restart`).
- **Language**: statements in handlers (`if/else`, `for`, `while`, `var`, `return`, `+= -= *= /=`,
  `++ --`), `function` declarations, calls with arguments, lists (`[a, b]`, `list[i]`, `.length`),
  hex/exponent literals, `===`, `&&`/`||` returning operands, bit flags, grouped-property reads
  (`rect.border.color`), number/string/list methods (`toFixed`, `split`, `arg`, `join`…), globals
  (`parseInt`, `String`, `qsTr`…), `console.log`, enums (`Text.*`, `Drag.*`, `Qt.*`).
- **Colours**: `Qt.rgba/hsva/hsla/lighter/darker/tint/alpha/colorEqual`, channels (`c.r`, `c.hsvHue`,
  `c.hslLightness`…); colours print as `#rrggbb[aa]`.
- **Components**: `property alias`, signal parameters (also to C# via `Connect(id, signal, args => …)`),
  `Component.onCompleted`, `on<Property>Changed` handlers, components extending components;
  use-site handlers run alongside the component's own. `engine.Invoke(id, function, args)` from C#.
- **Input**: `onDoubleClicked`, `onPressAndHold`, `onWheel` (`wheel.angleDelta`), a `mouse` object in
  pointer handlers, and `drag.target` with `drag.axis`, bounds, `threshold` and `active`.
- **Layout**: `Flow` positioner; `padding` (+ per side) on positioners; hidden children take no space;
  positioners solve their layout once per change (O(children)).
- **Repeater**: `model` can change at runtime and can be a list (`modelData`); each instance has its
  own id scope; `count`, `itemAt(i)`.
- **Text**: line breaks, `wrapMode`, `horizontalAlignment`/`verticalAlignment`, `elide`, `lineHeight`,
  `font.bold/italic/pixelSize`; `contentWidth`/`contentHeight`/`lineCount`; implicit size unless the
  document sizes it.
- **`ColorPicker`** control (saturation/value square, hue and alpha strips, swatch, hex) and the
  `Quill/ColorField` shader behind it.

### Changed
- Relicensed under the **MIT License** (previously proprietary / all rights reserved).
- A standalone animation (not `on` a property, not inside a group/Behavior/Transition) now waits for
  `running: true` or `start()`, as in QML — it used to start by itself. Value sources are unchanged.
- `Text` no longer overwrites `width`/`height`: it takes its content size only when the document
  doesn't size it, so a set width now wraps/aligns/elides instead of being replaced.
- Hit-testing walks the tree (topmost = last in tree order), so items created at runtime stack
  correctly.
- `QuillObject.Handlers` values take the signal's arguments (`Action<object[]>`); `QuillObject.Emit`
  has an overload with arguments.
- **Samples are themes now**: one sample per theme, each a single gallery scene. The Demos (widget
  gallery, game menu, Playground) and Liquid Glass samples are gone; their controls live on in Slate
  and Frost.
- The controls moved from `Resources/QuillControls` to `Resources/QuillThemes/Slate` and read the
  theme palette.
- Children written inside a component at its use-site now register their ids in the enclosing
  (document) scope, as in QML — a `Panel`'s content is reachable by id from the rest of the document.
- Overlapping `ShaderEffect`s draw in tree order (one render queue each, 4002–4097); text moved to
  queue 4100, above them.
- `on<Property>Changed` handlers start listening once the tree is built, so bindings settling during
  load don't fire them (as in QML).

### Fixed
- Replacing a binding — with a new one, or by assigning a value in a handler or from C# — now fully
  detaches the old one. Before, it kept listening to its dependencies and could overwrite the new
  value, e.g. a component's default binding beating a use-site override such as `display: ...`.
- `ShaderEffect` no longer writes a float `_Time` on its materials (Unity rejected it every frame with
  "Trying to set builtin parameter"). Effect shaders use Unity's built-in `_Time` (`_Time.y` = seconds).
- `Quill/Effect/Glass` and `Quill/Effect/Blur` now declare their uniforms in `Properties` and a
  `UnityPerMaterial` cbuffer. Before, URP's SRP Batcher dropped per-material values, so with several
  effects on screen every pane drew with one pane's `_Rect`, radius and tint.
- The README `ShaderEffect` example used `#AARRGGBB`, which Unity parses as `#RRGGBBAA`. Now `#RRGGBBAA`.

### Removed
- The Demos (`QuillDemo`, `Gallery.ui`, `GameMenu.ui`, `Playground.ui`), Liquid Glass and
  Customizer samples — replaced by the theme samples and the Quill Document component.
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
