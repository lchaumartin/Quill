// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;

namespace Quill
{
    /// <summary>
    /// `Item` — the invisible base visual element. Positioned by x/y (relative to parent,
    /// top-left origin, y-down) with a width/height, plus visible/opacity and `state`.
    /// </summary>
    public class QuillItem : QuillObject
    {
        public virtual void SeedDefaults()
        {
            Property("x").SetValue(0.0);
            Property("y").SetValue(0.0);
            Property("width").SetValue(0.0);
            Property("height").SetValue(0.0);
            Property("visible").SetValue(true);
            Property("opacity").SetValue(1.0);
        }

        /// <summary>Absolute top-left X in surface pixels (walks the parent chain).</summary>
        public float AbsX()
        {
            float x = 0;
            for (var o = this; o is QuillItem item; o = o.Parent as QuillItem) x += item.Num("x");
            return x;
        }

        public float AbsY()
        {
            float y = 0;
            for (var o = this; o is QuillItem item; o = o.Parent as QuillItem) y += item.Num("y");
            return y;
        }

        /// <summary>Effective opacity, multiplied down the tree (Quill semantics).</summary>
        public float EffectiveOpacity()
        {
            float a = 1f;
            for (var o = this; o is QuillItem item; o = o.Parent as QuillItem) a *= item.Num("opacity", 1f);
            return a;
        }

        public bool EffectiveVisible()
        {
            for (var o = this; o is QuillItem item; o = o.Parent as QuillItem)
                if (!item.Flag("visible")) return false;
            return true;
        }
    }

    /// <summary>
    /// Base of elements that take part in the tree (scope, ids, bindings) but never draw, lay out
    /// or take input: animations, Behavior, Timer, State, Transition, Repeater…
    /// </summary>
    public class QuillNonVisual : QuillItem
    {
        public override void SeedDefaults() { }
    }

    /// <summary>
    /// `Rectangle` — a filled, optionally rounded box, with an optional border
    /// (`border.width`, `border.color`). `softness` feathers the edge over that many pixels, centred on
    /// the outline — a soft shadow or glow is a Rectangle with a large softness behind the real one.
    /// Rendered in the full-screen SDF pass.
    /// </summary>
    public sealed class QuillRectangle : QuillItem
    {
        public override void SeedDefaults()
        {
            base.SeedDefaults();
            Property("color").SetValue("white");
            Property("radius").SetValue(0.0);
            Property("border.width").SetValue(0.0);
            Property("border.color").SetValue("black");
            Property("softness").SetValue(0.0);
        }
    }

    /// <summary>
    /// `Text` — a run of text, with line breaks (`\n`), wrapping (`wrapMode` + a set `width`),
    /// alignment, eliding, bold/italic, a line-height factor, <c>font.family</c> (a font under a
    /// <c>Resources</c> folder, or installed font names, comma-separated fallbacks) and
    /// <c>font.letterSpacing</c> (extra pixels between characters) and <c>font.capitalization</c>
    /// (<c>Font.AllUppercase</c>…). The renderer measures it and writes
    /// back `contentWidth` / `contentHeight` / `lineCount`; unless the document sets `width` / `height`,
    /// the item takes its content size (QML's implicit size), so anchors work off the real extent.
    /// </summary>
    public sealed class QuillText : QuillItem
    {
        // The engine's implicit-size bindings (width/height follow the content). While they drive
        // the size, the renderer doesn't wrap or align against it (that would feed back).
        internal Binding ImplicitWidth, ImplicitHeight;

        /// <summary>True when width comes from the document (or anchors), not from the content.</summary>
        public bool HasExplicitWidth => ImplicitWidth == null || FindProperty("width")?.Driver != ImplicitWidth;
        public bool HasExplicitHeight => ImplicitHeight == null || FindProperty("height")?.Driver != ImplicitHeight;

        public override void SeedDefaults()
        {
            base.SeedDefaults();
            Property("text").SetValue("");
            Property("color").SetValue("white");
            Property("fontSize").SetValue(16.0);
            Property("horizontalAlignment").SetValue(1.0);   // Text.AlignLeft
            Property("verticalAlignment").SetValue(32.0);    // Text.AlignTop
            Property("wrapMode").SetValue(0.0);              // Text.NoWrap
            Property("elide").SetValue(0.0);                 // Text.ElideNone
            Property("lineHeight").SetValue(1.0);
            Property("font.bold").SetValue(false);
            Property("font.italic").SetValue(false);
            Property("font.family").SetValue("");
            Property("font.letterSpacing").SetValue(0.0);
            Property("font.capitalization").SetValue(0.0);   // Font.MixedCase
            Property("contentWidth").SetValue(0.0);
            Property("contentHeight").SetValue(0.0);
            Property("lineCount").SetValue(0.0);
        }
    }

    /// <summary>
    /// `Image` — a textured quad. `source` is a path under any `Resources` folder
    /// (e.g. `source: "icons/logo"`). `color` tints the texture; alpha follows `opacity`.
    /// </summary>
    public sealed class QuillImage : QuillItem
    {
        public override void SeedDefaults()
        {
            base.SeedDefaults();
            Property("source").SetValue("");
            Property("color").SetValue("white");
        }
    }

    /// <summary>
    /// `Row` / `Column` / `Grid` / `Flow` — positioners. They lay out their visible children via
    /// reactive bindings (see <see cref="Positioners"/>) and size themselves to their content.
    /// </summary>
    public sealed class QuillPositioner : QuillItem
    {
        public override void SeedDefaults()
        {
            base.SeedDefaults();
            Property("spacing").SetValue(0.0);
            Property("padding").SetValue(0.0);
            // Grid-only; harmless on the others.
            Property("columns").SetValue(4.0);
        }
    }

    /// <summary>
    /// `MouseArea` — an invisible input region. Exposes reactive <c>pressed</c> / <c>containsMouse</c>
    /// / <c>mouseX</c> / <c>mouseY</c> and fires <c>onPressed/onReleased/onClicked/onDoubleClicked/
    /// onPressAndHold/onEntered/onExited/onPositionChanged/onWheel</c>; pointer handlers receive a
    /// <c>mouse</c> object (x, y, button), <c>onWheel</c> a <c>wheel</c> object (angleDelta.x/y).
    /// <c>drag.target</c> makes another item draggable (<c>drag.axis</c>, <c>drag.minimumX</c>…,
    /// <c>drag.active</c>). Each signal also raises a C# event for app-side wiring.
    /// </summary>
    public sealed class QuillMouseArea : QuillItem
    {
        public event System.Action Pressed, Released, Clicked, Entered, Exited, PositionChanged;
        public event System.Action DoubleClicked, PressAndHold, Wheel;

        public override void SeedDefaults()
        {
            base.SeedDefaults();
            Property("enabled").SetValue(true);
            Property("hoverEnabled").SetValue(false);
            Property("pressed").SetValue(false);
            Property("containsMouse").SetValue(false);
            Property("mouseX").SetValue(0.0);   // pointer position in area-local pixels
            Property("mouseY").SetValue(0.0);

            Property("drag.target").SetValue(null);
            Property("drag.axis").SetValue(3.0);          // Drag.XAndYAxis
            Property("drag.minimumX").SetValue(-1e9);
            Property("drag.maximumX").SetValue(1e9);
            Property("drag.minimumY").SetValue(-1e9);
            Property("drag.maximumY").SetValue(1e9);
            Property("drag.threshold").SetValue(4.0);
            Property("drag.active").SetValue(false);

            foreach (var s in new[] { "pressed", "released", "clicked", "doubleClicked", "pressAndHold", "positionChanged" })
                SignalParams[s] = new[] { "mouse" };
            SignalParams["wheel"] = new[] { "wheel" };
        }

        public bool Enabled => Flag("enabled");
        public bool HoverEnabled => Flag("hoverEnabled", false);

        // Drag bookkeeping (runtime, not Quill properties).
        internal double DragStartPx, DragStartPy, DragStartX, DragStartY;
        internal double PressTime;
        internal bool HoldFired;
        internal bool HasWheelListener => Wheel != null;

        /// <summary>Run the Quill handler (base) and raise the matching C# event.</summary>
        public override void Emit(string signal, object[] args)
        {
            base.Emit(signal, args);
            switch (signal)
            {
                case "onPressed": Pressed?.Invoke(); break;
                case "onReleased": Released?.Invoke(); break;
                case "onClicked": Clicked?.Invoke(); break;
                case "onEntered": Entered?.Invoke(); break;
                case "onExited": Exited?.Invoke(); break;
                case "onPositionChanged": PositionChanged?.Invoke(); break;
                case "onDoubleClicked": DoubleClicked?.Invoke(); break;
                case "onPressAndHold": PressAndHold?.Invoke(); break;
                case "onWheel": Wheel?.Invoke(); break;
            }
        }
    }

    /// <summary>
    /// `ShaderEffect` — a quad drawn with a custom Unity shader (referenced by name via the
    /// <c>shader</c> property). Every other declared property is forwarded to the shader as a uniform
    /// of the same name (double→float, bool→float, color→color). Standard uniforms <c>_Rect</c>,
    /// <c>_ScreenSize</c>, <c>_Opacity</c> are always provided; use Unity's built-in <c>_Time</c> for time.
    /// </summary>
    public sealed class QuillShaderEffect : QuillItem
    {
        public override void SeedDefaults()
        {
            base.SeedDefaults();
            Property("shader").SetValue("");   // Unity shader name, e.g. "Quill/Effect/Blur"
        }
    }

    /// <summary>
    /// `Timer` — fires <c>onTriggered</c> after <c>interval</c> ms while <c>running</c>; with
    /// <c>repeat</c> it keeps firing. Methods: <c>start()</c>, <c>stop()</c>, <c>restart()</c>.
    /// </summary>
    public sealed class QuillTimer : QuillNonVisual
    {
        internal double Elapsed;

        public override void SeedDefaults()
        {
            Property("interval").SetValue(1000.0);
            Property("running").SetValue(false);
            Property("repeat").SetValue(false);
            Property("triggeredOnStart").SetValue(false);
        }

        public override bool TryInvokeMethod(QuillEngine engine, string name, object[] args, out object result)
        {
            result = null;
            switch (name)
            {
                case "start": Property("running").SetValue(true); return true;
                case "stop": Property("running").SetValue(false); return true;
                case "restart":
                    Elapsed = 0;
                    if (Flag("running", false)) engine.TimerStarted(this);
                    else Property("running").SetValue(true);
                    return true;
                default: return false;
            }
        }
    }

    /// <summary>`Behavior on prop { Animation }` — animates every write to its parent's property.</summary>
    public sealed class QuillBehavior : QuillNonVisual
    {
        public override void SeedDefaults()
        {
            Property("enabled").SetValue(true);
        }
    }

    /// <summary>`State { name; when; extend; PropertyChanges { } }` — a named set of property overrides.</summary>
    public sealed class QuillState : QuillNonVisual
    {
        public override void SeedDefaults()
        {
            Property("name").SetValue("");
            Property("extend").SetValue("");
        }
    }

    /// <summary>
    /// `PropertyChanges { target: item; x: 10; color: "red" }` (or `item.x: 10`) inside a State.
    /// Its values are ordinary reactive bindings, applied to the target while the state is active.
    /// </summary>
    public sealed class QuillPropertyChanges : QuillNonVisual
    {
        public override void SeedDefaults()
        {
            Property("restoreEntryValues").SetValue(true);
        }
    }

    /// <summary>`Transition { from; to; reversible; Animation... }` — how a state change animates.</summary>
    public sealed class QuillTransition : QuillNonVisual
    {
        public override void SeedDefaults()
        {
            Property("from").SetValue("*");
            Property("to").SetValue("*");
            Property("reversible").SetValue(false);
            Property("enabled").SetValue(true);
            Property("running").SetValue(false);
        }
    }

    /// <summary>
    /// `Repeater { model: n | [..]; Delegate {} }` — creates one delegate per model entry into its own
    /// parent, right after itself. `model` can change at runtime; each instance gets `index` and
    /// (for list models) `modelData`.
    /// </summary>
    public sealed class QuillRepeater : QuillNonVisual
    {
        internal Parsing.ObjectNode Delegate;
        internal readonly List<QuillItem> Instances = new List<QuillItem>();
        internal bool Wired;

        public override void SeedDefaults()
        {
            Property("model").SetValue(0.0);
            Property("count").SetValue(0.0);
        }

        public override bool TryInvokeMethod(QuillEngine engine, string name, object[] args, out object result)
        {
            result = null;
            if (name != "itemAt") return false;
            int i = args.Length > 0 ? (int)QuillConvert.ToDouble(args[0]) : -1;
            result = i >= 0 && i < Instances.Count ? Instances[i] : null;
            return true;
        }
    }

    /// <summary>Maps Quill type names to element instances. Extend this to add new element types.</summary>
    public static class QuillTypeRegistry
    {
        private static readonly Dictionary<string, Func<QuillItem>> _factories =
            new Dictionary<string, Func<QuillItem>>
            {
                { "Item", () => new QuillItem() },
                { "QtObject", () => new QuillNonVisual() },
                { "Rectangle", () => new QuillRectangle() },
                { "Text", () => new QuillText() },
                { "Image", () => new QuillImage() },
                { "Row", () => new QuillPositioner() },
                { "Column", () => new QuillPositioner() },
                { "Grid", () => new QuillPositioner() },
                { "Flow", () => new QuillPositioner() },
                { "MouseArea", () => new QuillMouseArea() },
                { "ShaderEffect", () => new QuillShaderEffect() },
                { "Timer", () => new QuillTimer() },
                { "Behavior", () => new QuillBehavior() },
                { "State", () => new QuillState() },
                { "PropertyChanges", () => new QuillPropertyChanges() },
                { "Transition", () => new QuillTransition() },
                { "Repeater", () => new QuillRepeater() },

                { "NumberAnimation", () => new QuillAnimation() },
                { "PropertyAnimation", () => new QuillAnimation() },
                { "ColorAnimation", () => new QuillAnimation() },
                { "SpringAnimation", () => new QuillAnimation() },
                { "SmoothedAnimation", () => new QuillAnimation() },
                { "PauseAnimation", () => new QuillAnimation() },
                { "SequentialAnimation", () => new QuillAnimation() },
                { "ParallelAnimation", () => new QuillAnimation() },
                { "ScriptAction", () => new QuillAnimation() },
                { "PropertyAction", () => new QuillAnimation() },
            };

        public static bool IsKnown(string typeName) => _factories.ContainsKey(typeName);

        /// <summary>Every registered element type name (editor tooling lists them).</summary>
        public static IEnumerable<string> Names => _factories.Keys;

        public static QuillItem Create(string typeName)
        {
            if (!_factories.TryGetValue(typeName, out var f))
                throw new InvalidOperationException($"[Quill] Unknown element type '{typeName}'.");
            var item = f();
            item.TypeName = typeName;
            item.SeedDefaults();
            return item;
        }

        public static void Register(string typeName, Func<QuillItem> factory)
            => _factories[typeName] = factory;
    }
}
