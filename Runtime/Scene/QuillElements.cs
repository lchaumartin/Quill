// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;

namespace Quill
{
    /// <summary>
    /// `Item` — the invisible base visual element. Positioned by x/y (relative to parent,
    /// top-left origin, y-down) with a width/height, plus visible/opacity.
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
    /// `Rectangle` — a filled, optionally rounded box, with an optional border
    /// (`border.width`, `border.color`). Rendered in the full-screen SDF pass.
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
        }
    }

    /// <summary>
    /// `Text` — a run of text. Content-sized: the renderer measures the string and writes back
    /// `width`/`height` so the text can participate in anchoring like any other item.
    /// </summary>
    public sealed class QuillText : QuillItem
    {
        public override void SeedDefaults()
        {
            base.SeedDefaults();
            Property("text").SetValue("");
            Property("color").SetValue("white");
            Property("fontSize").SetValue(16.0);
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
    /// `Row` / `Column` / `Grid` — positioners. They lay out their children via reactive bindings
    /// (see <see cref="Positioners"/>) and size themselves to their content.
    /// </summary>
    public sealed class QuillPositioner : QuillItem
    {
        public override void SeedDefaults()
        {
            base.SeedDefaults();
            Property("spacing").SetValue(0.0);
            // Grid-only; harmless on Row/Column.
            Property("columns").SetValue(4.0);
        }
    }

    /// <summary>
    /// `NumberAnimation` / `PropertyAnimation` — interpolates a numeric property over time.
    /// Drives `parent.<onProperty>` (when declared as `NumberAnimation on x`) or an explicit
    /// `target` + `property`. Advanced each frame by the engine.
    /// </summary>
    public sealed class QuillAnimation : QuillItem
    {
        // Runtime state (not Quill properties).
        public double Elapsed;
        public int LoopsDone;

        public override void SeedDefaults()
        {
            base.SeedDefaults();
            Property("from").SetValue(0.0);
            Property("to").SetValue(0.0);
            Property("duration").SetValue(250.0);   // ms
            Property("loops").SetValue(1.0);          // -1 = infinite
            Property("running").SetValue(true);
            Property("property").SetValue("");        // when not using `on`
            Property("target").SetValue(null);
            Property("easing.type").SetValue("Linear");
        }
    }

    /// <summary>
    /// `MouseArea` — an invisible input region. Exposes reactive <c>pressed</c> / <c>containsMouse</c>
    /// state (bind visuals to them) and fires <c>onPressed/onReleased/onClicked/onEntered/onExited/
    /// onPositionChanged</c>. Each signal also raises a C# event for app-side wiring.
    /// </summary>
    public sealed class QuillMouseArea : QuillItem
    {
        public event System.Action Pressed, Released, Clicked, Entered, Exited, PositionChanged;

        public override void SeedDefaults()
        {
            base.SeedDefaults();
            Property("enabled").SetValue(true);
            Property("hoverEnabled").SetValue(false);
            Property("pressed").SetValue(false);
            Property("containsMouse").SetValue(false);
            Property("mouseX").SetValue(0.0);   // pointer position in area-local pixels
            Property("mouseY").SetValue(0.0);
        }

        public bool Enabled => Flag("enabled");
        public bool HoverEnabled => Flag("hoverEnabled", false);

        /// <summary>Run the Quill handler (base) and raise the matching C# event.</summary>
        public override void Emit(string signal)
        {
            base.Emit(signal);
            switch (signal)
            {
                case "onPressed": Pressed?.Invoke(); break;
                case "onReleased": Released?.Invoke(); break;
                case "onClicked": Clicked?.Invoke(); break;
                case "onEntered": Entered?.Invoke(); break;
                case "onExited": Exited?.Invoke(); break;
                case "onPositionChanged": PositionChanged?.Invoke(); break;
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

    /// <summary>Maps Quill type names to element instances. Extend this to add new element types.</summary>
    public static class QuillTypeRegistry
    {
        private static readonly Dictionary<string, Func<QuillItem>> _factories =
            new Dictionary<string, Func<QuillItem>>
            {
                { "Item", () => new QuillItem() },
                { "Rectangle", () => new QuillRectangle() },
                { "Text", () => new QuillText() },
                { "Image", () => new QuillImage() },
                { "Row", () => new QuillPositioner() },
                { "Column", () => new QuillPositioner() },
                { "Grid", () => new QuillPositioner() },
                { "NumberAnimation", () => new QuillAnimation() },
                { "PropertyAnimation", () => new QuillAnimation() },
                { "MouseArea", () => new QuillMouseArea() },
                { "ShaderEffect", () => new QuillShaderEffect() },
            };

        public static bool IsKnown(string typeName) => _factories.ContainsKey(typeName);

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
