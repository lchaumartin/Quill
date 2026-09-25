# Quill — a declarative, reactive UI framework for Unity (URP)

*Free and open source under the [MIT License](LICENSE.md). Scope and roadmap: [`SCOPE.md`](SCOPE.md).*

Quill brings a clean declarative authoring model to Unity: real `.ui` text documents, a **reactive
property + binding** engine, **anchors-based layout**, reusable **components**, and a low-level
renderer (no uGUI/Canvas) built around a single full-screen SDF pass plus lightweight textured layers.

## Quick start

1. Add Quill to your project (it runs on URP or the Built-in pipeline).
2. In **Package Manager ▸ Quill ▸ Samples**, import **Demos**.
3. In a scene with a camera, use **GameObject ▸ Quill ▸ Demo Surface** (added by the sample), or add
   the `Quill Demo` component to a GameObject yourself.
4. Assign a `.ui` document to **Quill Demo ▸ Quill File** and press **Play** — it renders and animates
   itself, driven by bindings; resize the Game view and the anchored layout follows.

`.ui` files import as text assets automatically and are syntax-checked on import.

**The imported Demos** are `Gallery.ui` (a widget test bench exercising every element) and `GameMenu.ui`
— a full **game menu hierarchy** (Main → Play/Settings/Credits/Quit, tabbed Settings, confirm dialog)
built from the Controls and navigated with a single `screen` state property + `visible:` bindings.

**The Liquid Glass sample** is a complete scene: import **Liquid Glass**, open `LiquidGlass.unity` and
press **Play**. See [Liquid Glass sample](#the-liquid-glass-sample) below.

## Elements

| Element           | Properties (beyond x/y/width/height/visible/opacity)                         |
|-------------------|------------------------------------------------------------------------------|
| `Item`            | — (invisible base; container/anchor target)                                  |
| `Rectangle`       | `color`, `radius`, `border.width`, `border.color`                            |
| `Text`            | `text`, `color`, `fontSize` (content-sized)                                  |
| `Image`           | `source` (a `Resources` path), `color` (tint)                               |
| `Row` `Column` `Grid` | `spacing`; Grid also `columns`, `rowSpacing`, `columnSpacing`             |
| `NumberAnimation` | `from`, `to`, `duration` (ms), `loops`, `running`, `easing.type`, `target`, `property` |
| `Repeater`        | `model` (a constant count); its single child is the delegate, gets `index`     |
| `MouseArea`       | `enabled`, `hoverEnabled`, reactive `pressed` / `containsMouse`; `on*` signals |

`Rectangle` is drawn in the full-screen SDF pass (rounded corners + anti-aliased border).
`Text` uses Unity's dynamic-font atlas; `Image` is a textured quad. Add new element types via
`QuillTypeRegistry.Register(...)`.

## Anchors

Every item exposes six anchor lines in absolute coordinates — `left, right, top, bottom,
horizontalCenter, verticalCenter` — so anchoring is just a binding onto another item's line:

```ui
Rectangle {
    anchors.left: bar.right          // pin to a sibling's edge
    anchors.leftMargin: 20
    anchors.verticalCenter: parent.verticalCenter
}
```

Supported: `anchors.fill`, `anchors.centerIn`, the four edges (`left/right/top/bottom`), the two
centers, `anchors.margins` plus per-side margins, and `horizontalCenterOffset` /
`verticalCenterOffset`. Because lines are absolute, anchoring to a sibling and to `parent` use the
same math. Anchors override explicit `x/y/width/height` on the axes they control, exactly as you would expect.
Layout is fully reactive: change a window size, a sibling's geometry, or a model value, and only the
affected items recompute.

## Positioners

`Row`, `Column`, and `Grid` lay out their children automatically and size themselves to their
content — all through bindings, so layout reacts live to child sizes and spacing changes:

```ui
Row {
    spacing: 8
    Rectangle { width: 40; height: 40 }
    Rectangle { width: 40; height: 40 }
}
Grid { columns: 3; spacing: 6; /* children flow row-major */ }
```

Grid sizes each column to its widest child and each row to its tallest. Positioners manage the main
axis (Row → x, Column → y, Grid → both); don't also anchor a positioned child on the same axis.

## Animation

`NumberAnimation` interpolates a numeric property over time. Use the `on` form to bind it to a
property, or an explicit `target` + `property`:

```ui
NumberAnimation on phase {
    from: 0; to: 1
    duration: 1500
    loops: Animation.Infinite        // or a finite count
    easing.type: Easing.InOutQuad
}
```

Easing curves: `Linear`, `In/Out/InOutQuad`, `In/Out/InOutCubic`, `In/Out/InOutSine`, `OutBack`.
`Easing.*` and `Animation.Infinite` are built-in. Animations are advanced by the engine each frame
(`QuillEngine.Update(dt)`, called from `QuillSurface`), so a document animates itself with no C#.

## Repeater

`Repeater` instantiates its delegate (its single child) `model` times, generating real scene items
**into the Repeater's parent** — so they flow through anchors, positioners, and rendering like any
other element. Each instance gets an injected `index`:

```ui
Repeater {
    model: 2000
    Rectangle {
        width: 14; height: 14
        x: 24 + (index % 50) * 18                 // % - / give you grid math
        y: 24 + ((index - index % 50) / 50) * 18
        color: "#42a5f5"
    }
}
```

Or drop one inside a positioner and let it lay the instances out:
`Grid { columns: 50; Repeater { model: 2000; Rectangle { ... } } }`.

`model` is currently a constant (evaluated once at load); dynamic models are a follow-up. The
rectangle layer uploads all rects into a single `StructuredBuffer` and composites them in **one
draw call**, so thousands of `Repeater` cells stay a single pass.

> Requires shader model 4.5 (StructuredBuffer in the fragment stage) — fine on desktop D3D11/Vulkan/Metal.

## Talking to C# — values & events

Keep a reference to the `QuillEngine` (the one `QuillDemo` creates), then reach **document-level ids**
(use-site `id`s; component internals stay private). Three patterns, pick per need:

**1. Pull a value when you need it** — simplest, ideal for "apply settings on close":

```csharp
float master = (float)engine.GetNumber("masterVol", "value");
bool  fs     = engine.GetBool("fullscreen", "checked");
```

**2. Push: react to a value changing** — for live updates, no per-frame polling:

```csharp
engine.OnChanged("masterVol", "value", v => audio.volume = (float)(double)v);
```

**3. Subscribe to signals** — for actions/events:

```csharp
engine.Connect("playBtn", "clicked", () => StartGame());   // any signal, by bare name
// or, for a MouseArea directly:
((QuillMouseArea)engine.FindId("hitArea")).Clicked += OnHit;
```

Going the other way (C# → Quill) is just `engine.SetValue("hpBar", "value", hp / maxHp)`. So the clean
split is: **C# owns the model**, drives Quill with `SetValue`, and listens via `OnChanged`/`Connect`;
Quill owns presentation and notifies C# through signals. Re-subscribe after a reload (`LoadFromSource`
rebuilds the tree). Avoid reading values every frame in `Update` when a `Connect`/`OnChanged` hook
will do — but a one-shot pull on a button press is perfectly fine and the easiest place to start.

**No-code option — the `Quill Bindings` component.** Drop it on the surface GameObject and wire hooks
in the inspector: a **Signals** list maps `id` + `signal` → a `UnityEvent` (e.g. `playBtn`/`clicked` →
your scene loader), and a **Values** list maps `id` + `property` → `UnityEvent<float/bool/string>`
(e.g. `masterVol`/`value` → an AudioMixer). It finds the engine automatically and re-wires on reload,
so designers can connect game logic without touching C#.

## Showing & hiding a surface (menus)

Treat each `QuillSurface` as one screen/menu. Toggle it with `Visible` (or `Show()`/`Hide()`/`Toggle()`):

```csharp
surface.Visible = false;   // closed menu: nothing drawn, animations + input paused
surface.Toggle();          // e.g. on the Escape key-down edge
```

Don't use the component's `enabled` flag for this — disabling the MonoBehaviour stops its update
loop but leaves the layer renderers active, so the last frame stays frozen on screen. `Visible`
deactivates the whole layer subtree (nothing draws) and pauses ticking, then resumes on show.

For multiple menus, give each its own `QuillSurface` + document and toggle them independently. If two
visible surfaces must stack in a defined order, offset their layer render-queues (they currently
share 4000–4002).

## Custom shaders — ShaderEffect

`ShaderEffect` draws a quad with a custom Unity shader.
Because Unity can't compile shader *source strings* at runtime, you reference a `.shader` **by name**;
every custom Quill property on the effect is forwarded as a **uniform of the same name**:

```ui
ShaderEffect {
    anchors.fill: parent
    shader: "Quill/Effect/Blur"
    tint: "#4fc3f780"                    // -> color uniform `tint` (#RRGGBBAA)
    radius: 4.0 + 24.0 * slider.value    // -> float uniform `radius`, fully reactive
}
```

The engine always supplies `_Rect` (x,y,w,h px), `_ScreenSize` and `_Opacity`. For animation, use
Unity's built-in `_Time` (`_Time.y` is seconds since the level loaded).
Forwarding rules: `real`→`float`, `bool`→`float`, `color`→`color`. Write your shader against the
Quill convention (clip-space vertex with the `_ProjectionParams.x` Y-flip, `uv` 0..1 top-left) — see
`Shaders/QuillEffectBlur.shader` as a copy-paste template. Effects render on the overlay layer (above
rectangles, below text), so a `Rectangle` placed behind an effect is covered by it regardless of tree
order — tint inside the shader instead. The `GameMenu` sample uses a blurred backdrop.

> **Declare your uniforms.** List every forwarded uniform (plus `_Rect` and `_Opacity`) in the
> shader's `Properties` block, and on URP put them in `CBUFFER_START(UnityPerMaterial)`. With bare
> global uniforms the SRP Batcher ignores per-material values, so when several effects are on screen
> they all draw with one effect's rect and properties (wrong shapes, missing tints).

### The bundled effect — `Quill/Effect/Blur`

A frosted backdrop blur, and the base layer to build glass-style panels on:

| Property       | Type    | Meaning                                              | Unset (`0`) |
|----------------|---------|------------------------------------------------------|-------------|
| `radius`       | `real`  | blur radius in screen pixels                          | no blur     |
| `cornerRadius` | `real`  | rounded-corner radius in pixels                       | square      |
| `tint`         | `color` | colour laid over the blur, by its alpha               | untinted    |

Every default is the "off" state, because unset Quill properties arrive at the shader as `0`.

**What it blurs differs by pipeline**, and it is worth knowing which you are on:

- **URP** samples `_CameraOpaqueTexture` — a copy of the opaque scene taken *before* transparents. It
  holds the game world but no Quill UI, so the blur picks up the scene behind the surface and ignores
  Quill rectangles under it. **Enable "Opaque Texture" on your URP asset**; with it off the texture is
  black and the panel renders flat dark.
- **Built-in** uses a `GrabPass`, which copies the live framebuffer at this queue. Quill rectangles
  draw at queue 4000, before effects, so here the blur *does* pick up Quill panels behind it.

Blurring an arbitrary Quill sub-tree identically on both pipelines needs `ShaderEffectSource`, which
isn't implemented yet — see `SCOPE.md`.

### `Quill/Effect/LiquidGlass`

The whole effect rect becomes a rounded pane of glass: the backdrop is refracted toward the centre
near the edges (a convex lens look), blurred, and lit with a thin rim highlight and a soft top-down
sheen. It reads its backdrop exactly like `Quill/Effect/Blur`, so the same URP / Built-in notes apply.

```ui
ShaderEffect {
    width: 420; height: 260
    shader: "Quill/Effect/LiquidGlass"
    cornerRadius: 32
    refraction: 40
    radius: 6
}
```

| Property       | Type    | Meaning                                                   | Unset (`0`)          |
|----------------|---------|-----------------------------------------------------------|----------------------|
| `cornerRadius` | `real`  | rounded-corner radius in pixels                           | square               |
| `refraction`   | `real`  | width in pixels of the lens band along the edge           | no refraction        |
| `softness`     | `real`  | width in pixels of the rounded (bevelled) glass edge      | sharp, flat edge     |
| `radius`       | `real`  | blur radius in screen pixels                              | no blur              |
| `tint`         | `color` | colour laid over the backdrop, by its alpha               | untinted             |

The silhouette is always crisp; `softness` shapes the edge instead of fading it. It is the width of a
quarter-round bevel that curves from vertical at the silhouette to flat `softness` pixels in: its
normals refract the backdrop and catch a top-left highlight, a fainter bottom-right bounce and a
grazing reflection. A few pixels read as a polished edge, tens of pixels as a thick rounded slab; the
bevel is capped at half the pane's smaller side, so thin panes become glass tubes. At `0` the edge is
flat, with a thin rim line. The sheen is always on — it is part of what makes it read as glass.

### The Liquid Glass sample

`Samples~/LiquidGlass` is a ready-to-play scene (`LiquidGlass.unity`): a lock-screen / control-centre
layout, built entirely from LiquidGlass panes, floating over an animated 3D backdrop of drifting
orbs and an aurora wallpaper. Everything is live: hover and press states ease in, the toggles and
chips switch, the levels drag, the music player runs, and the brightness level, **Focus** and
**Night Shift** change the 3D scene behind the glass through C#.

**Tune glass** (top right, or **Tab**) slides in a panel of glass sliders — refraction, blur, corner
roundness, edge softness and tint — and every pane on screen follows them live, the sliders included.
The panes read their look from a `glassStyle` element in the document; `GlassPane` falls back to its
built-in defaults when there is none, so the components still work on their own.

- `Resources/QuillLiquidGlass/LiquidGlassShowcase.ui` — the document.
- `Components/GlassPane.ui`, `GlassToggle.ui`, `GlassChip.ui`, `GlassLevel.ui`, `GlassSlider.ui` —
  reusable glass building blocks; copy them into your own project. `GlassSlider` is three glass panes
  that never overlap (filled run, knob, rest of the track); the knob turns into a clear lens while held.
- `LiquidGlassShowcase.cs` — bootstrap + the app side (clock, player, scene wiring). On URP it turns
  on the camera's **Opaque Texture** by itself (a per-camera override), so no URP-asset change is needed.
- `QuillTweens.cs` — smooth transitions by convention: any element with both `foo` and `fooTarget`
  has `foo` eased toward `fooTarget` every frame (numbers and colours). A stand-in until Quill has a
  `Behavior` element.
- `LiquidGlassBackdrop.cs` + two unlit shaders — the 3D backdrop; identical on URP and Built-in.

Two layout rules the sample follows, and your glass UIs should too: **glass panes never overlap each
other** (effects share one render queue, so their relative order is undefined), and **anything drawn
on glass is `Text`** (rectangles render below effects).

> In a build, add custom effect shaders to **Project Settings ▸ Graphics ▸ Always Included Shaders**
> (or keep them under `Resources`) so `Shader.Find` can locate them; in the editor it just works.
> `ShaderEffectSource` (rendering a sub-tree to a texture) isn't implemented yet.

## Reusable components

Any `.ui` file can become a **reusable type**, instantiated by name like a built-in element. The
`Resources/QuillControls/` folder ships `Button`, `Slider`, `Switch`, `CheckBox`, and `ProgressBar`;
`QuillDemo` registers everything in that folder automatically, so a document can just write:

```ui
Button { width: 200; text: "Run"; onClicked: runs = runs + 1 }
Slider { id: vol; width: 320; value: 0.4 }
Text   { text: "volume " + vol.value }     // read a control's state by id
Switch { id: snd; checked: true }
ProgressBar { value: load }
```

**Authoring a component** (see `Resources/QuillControls/Slider.ui`):

- The component's **root-declared properties are its public API** — set them at the use-site, read
  them by id. `property real value: 0.4` on the Slider root *is* its value.
- Declare **signals** with `signal clicked` and emit them from inside with a call statement:
  `onClicked: control.clicked()`. The use-site handles them with `onClicked: ...`.
- **`id`s are component-local**: two `Slider`s each have their own internal `handle`/`ma` — no
  collisions. The use-site `id` lives in the outer document scope.
- Use-site **property overrides win** over the component's defaults, and use-site **children** are
  appended to the root.

Register your own from C# (`QuillEngine.RegisterComponent("MyWidget", uiText)`) or by dropping the
`.ui` under any `Resources/QuillControls` folder. The `GameMenu` sample uses Button, Slider, Switch,
and CheckBox throughout.

> Not yet: property **aliases** (`property alias`), signal parameters, and the `mouse` event object —
> for now expose state through root properties. The seams are in place for all three.

## Input — MouseArea

`MouseArea` is an invisible input region. It exposes **reactive** `pressed` and `containsMouse`
state you bind visuals to, and fires signals you handle with assignment statements:

```ui
Rectangle {
    id: button
    property int count: 0
    color: click.pressed ? "#1565c0" : (click.containsMouse ? "#1e88e5" : "#2b3147")

    Text { text: "Clicked " + button.count + " times"; anchors.centerIn: parent }

    MouseArea {
        id: click
        anchors.fill: parent
        hoverEnabled: true
        onClicked: button.count = button.count + 1   // or { a = 1; b = 2 }
    }
}
```

Signals: `onPressed`, `onReleased`, `onClicked`, `onEntered`, `onExited`, `onPositionChanged`.
Handler bodies are assignment statements (`target = expr`, dotted targets like `panel.visible = true`
allowed). Hit-testing picks the topmost area (later in tree order); `enabled: false` opts out.
`mouseX` / `mouseY` give the pointer position in area-local pixels — enough to build a draggable
slider (`onPositionChanged: value = mouseX / width`). The **ternary** `cond ? a : b` and logical
`&& || !` pair naturally with `pressed`/`containsMouse`.

For app-side logic, every signal also raises a C# event — grab the area by id and subscribe:

```csharp
var area = (QuillMouseArea)_engine.FindId("click");
area.Clicked += () => Debug.Log("clicked from C#");
```

Input is read by `QuillSurface` (new Input System if present, else the legacy manager) and fed to
`QuillEngine.Update(dt, x, y, down)`. To drive it from your own input source, call that overload yourself.

## Bindings

`width: parent.width * 0.42`, `x: bar.x + bar.width + 20`, `opacity: 0.55 + 0.45 * phase`.
Dependencies are captured automatically while a binding evaluates; when a source changes, the binding
is invalidated and recomputed on the next frame's flush. Expressions support `+ - * / %`, comparisons
(`== != < > <= >=`), logical `&& || !`, the ternary `cond ? a : b`, parentheses, unary minus,
`a.b` member access, string concatenation, and `Math.*` functions
(`Math.abs/min/max/floor/ceil/round/trunc/sign/sqrt/pow/exp/log/log2/log10/sin/cos/tan/asin/acos/atan/atan2/hypot/random/clamp`,
plus `Math.PI` / `Math.E`), e.g. `width: Math.max(40, parent.width * 0.2)`. Custom properties:
`property real phase: 0` (also `int`, `bool`, `string`, `color`, `var`). Colours accept `"red"`,
`"#RRGGBB"`, `"#RRGGBBAA"`, and Quill-style `"#AARRGGBB"`.

## Architecture

```
.ui text
  └─ Parsing/      Lexer → Parser → AST (objects + expressions; dotted LHS like anchors.left)
        └─ QuillEngine   instantiate → set up anchor lines → wire bindings → resolve anchors → flush
              ├─ Core/    QuillProperty (reactive cell) + Binding (auto dependency tracking) + queue
              ├─ Scene/   QuillObject / QuillItem / Rectangle / Text / Image / Row·Column·Grid /
              │             NumberAnimation + Anchors + Positioners + Animations + registry
              └─ Render/   QuillSurface orchestrates three layers:
                              QuillRectLayer  → full-screen SDF, StructuredBuffer    queue 4000
                              QuillImageLayer → one textured quad per Image          queue 4001
                              QuillTextLayer  → combined glyph mesh per font         queue 4002
```

### The binding engine (the heart of it)

A `QuillProperty` is a reactive cell. While a `Binding` evaluates, it is pushed on a stack; any property
read during evaluation records a dependency edge. When a property changes, its dependent bindings are
invalidated, queued, and recomputed on the next `Flush()`. Anchors are built entirely on top of this:
anchor lines are bindings, and x/y/width/height are bindings derived from the declared anchors.

## Known limitations (prototype)

- **Layer ordering** is fixed: text over images over rectangles, not strictly interleaved by tree
  order. Fine for the common case (labels/icons on top of panels); a future unified per-quad path
  would remove it.
- **Text** is content-sized and writes its measured `width`/`height` back each frame (so position
  anchors work). Don't drive a Text's size with `anchors.fill`.
- **Images** load from `Resources` by path string (`source: "icons/logo"` → `Resources/icons/logo`).
- The SDF pass uploads rects via a `StructuredBuffer` (safety ceiling 131072), one draw call.

## Extending it

- **New element**: subclass `QuillItem`, seed defaults, `QuillTypeRegistry.Register(...)`; add a draw path
  if it isn't a rectangle.
- **More expression power** (functions, ternary, lists): extend `Parser` + the AST `Eval` methods.

## Suggested next layers

Property **aliases** + signal parameters (to round out the component system), `Behavior on` (implicit
animation), states & transitions, `SequentialAnimation`/`ParallelAnimation`, `ColorAnimation`, the
`mouse` event object, keyboard focus

## Prior art

Quill's authoring model — declarative object trees, reactive property bindings, anchor-based layout,
components and signals — follows a design lineage that will be familiar to anyone who has written
QML. Quill is an independent implementation written for Unity: it is not affiliated with or endorsed
by any third-party UI toolkit, and no code is derived from one. It is likewise independent of uGUI
and UI Toolkit.

## Contributing

Issues and pull requests are welcome at
[github.com/lchaumartin/Quill](https://github.com/lchaumartin/Quill). Two things to keep in mind when
working in this repo:

- It is a Unity package, so `.meta` files are part of the source — commit them alongside the files
  they describe, and never regenerate them by hand.
- `SCOPE.md` is the reference for the feature surface. If a change moves something between
  *Implemented*, *Not yet*, and *Out of scope*, update that file in the same commit, and add a
  `CHANGELOG.md` entry.

## License

MIT © 2026 Leo CHAUMARTIN. See [`LICENSE.md`](LICENSE.md) for the full text.

You are free to use Quill in commercial and closed-source projects, modify it, and redistribute it;
the only requirement is that the copyright notice and permission notice travel with substantial
portions of the software.
