// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;
using System.Linq;

namespace Quill.LanguageServer
{
    public sealed class PropInfo
    {
        public string Name, Type, Doc;
        public bool ReadOnly;
    }

    public sealed class SignalInfo
    {
        public string Name, Doc;
        public string[] Params = new string[0];
    }

    public sealed class MethodInfo
    {
        public string Name, Signature, Doc;
    }

    /// <summary>What an element type offers: properties, signals and methods, with docs for hover.</summary>
    public sealed class ElementInfo
    {
        public string Name, Doc;
        /// <summary>Accepts properties it doesn't declare (ShaderEffect uniforms, PropertyChanges targets).</summary>
        public bool Open;
        public readonly Dictionary<string, PropInfo> Props = new Dictionary<string, PropInfo>();
        public readonly Dictionary<string, SignalInfo> Signals = new Dictionary<string, SignalInfo>();
        public readonly Dictionary<string, MethodInfo> Methods = new Dictionary<string, MethodInfo>();

        public ElementInfo P(string name, string type, string doc, bool readOnly = false)
        {
            Props[name] = new PropInfo { Name = name, Type = type, Doc = doc, ReadOnly = readOnly };
            return this;
        }

        public ElementInfo S(string name, string doc, params string[] ps)
        {
            Signals[name] = new SignalInfo { Name = name, Doc = doc, Params = ps };
            return this;
        }

        public ElementInfo M(string name, string signature, string doc)
        {
            Methods[name] = new MethodInfo { Name = name, Signature = signature, Doc = doc };
            return this;
        }

        public ElementInfo From(ElementInfo other)
        {
            foreach (var kv in other.Props) Props[kv.Key] = kv.Value;
            foreach (var kv in other.Signals) Signals[kv.Key] = kv.Value;
            foreach (var kv in other.Methods) Methods[kv.Key] = kv.Value;
            return this;
        }
    }

    /// <summary>
    /// The built-in elements, globals and enums, as documented for authors. Kept in step with the
    /// engine by a test (every property an element seeds must be listed here).
    /// </summary>
    public static class Schema
    {
        public static readonly Dictionary<string, ElementInfo> Elements = new Dictionary<string, ElementInfo>();

        public static readonly string[] PropertyTypes = { "real", "int", "bool", "string", "color", "var", "alias", "list", "double", "url" };

        public static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "true", "false", "null", "undefined", "var", "let", "const", "if", "else", "for", "while",
            "return", "break", "continue", "function", "property", "signal", "readonly", "default",
            "required", "alias", "on", "typeof", "new", "in", "of", "import",
        };

        /// <summary>Global names and a short description.</summary>
        public static readonly Dictionary<string, string> Globals = new Dictionary<string, string>
        {
            { "Theme", "The current theme's palette (Theme.quill): `Theme.accent`, `Theme.radius`… Live: assign to restyle." },
            { "Qt", "Colour helpers (`Qt.rgba`, `Qt.lighter`…) and alignment / mouse-button enums." },
            { "Math", "JavaScript-style maths: `Math.min`, `Math.clamp`, `Math.PI`…" },
            { "Easing", "Easing curves for animations: `Easing.OutCubic`, `Easing.InOutQuad`…" },
            { "Text", "Text enums: alignment (`Text.AlignHCenter`), wrapping (`Text.WordWrap`), eliding (`Text.ElideRight`)." },
            { "Font", "Capitalization enums: `Font.AllUppercase`, `Font.AllLowercase`, `Font.Capitalize`…" },
            { "Drag", "Drag axis enums: `Drag.XAxis`, `Drag.YAxis`, `Drag.XAndYAxis`." },
            { "Animation", "`Animation.Infinite` — loop forever (`loops: Animation.Infinite`)." },
            { "console", "`console.log(...)`, `console.warn(...)`, `console.error(...)` to the Unity console." },
            { "parent", "The item this one is inside." },
            { "index", "In a Repeater delegate: this instance's position in the model." },
            { "modelData", "In a Repeater delegate: this instance's entry of a list model." },
            { "parseInt", "`parseInt(text, radix)`" }, { "parseFloat", "`parseFloat(text)`" },
            { "Number", "`Number(value)`" }, { "String", "`String(value)`" }, { "Boolean", "`Boolean(value)`" },
            { "isNaN", "`isNaN(value)`" }, { "isFinite", "`isFinite(value)`" },
            { "qsTr", "`qsTr(text)` — returns the text (no translation layer yet)." },
            { "NaN", "Not a number." }, { "Infinity", "Positive infinity." },
        };

        public static readonly Dictionary<string, Dictionary<string, string>> Enums = new Dictionary<string, Dictionary<string, string>>();

        static Schema()
        {
            // ---- Enums & namespaces (members) -----------------------------------------------------
            var easing = new Dictionary<string, string>();
            foreach (var n in Quill.Easing.AllNames()) easing[n] = "Easing curve";
            Enums["Easing"] = easing;
            var align = new Dictionary<string, string>
            {
                { "AlignLeft", "Horizontal: left" }, { "AlignRight", "Horizontal: right" }, { "AlignHCenter", "Horizontal: centre" },
                { "AlignJustify", "Horizontal: justified" }, { "AlignTop", "Vertical: top" }, { "AlignBottom", "Vertical: bottom" },
                { "AlignVCenter", "Vertical: centre" }, { "AlignCenter", "Both centres" },
            };
            var text = new Dictionary<string, string>(align)
            {
                { "NoWrap", "wrapMode: one line" }, { "WordWrap", "wrapMode: break between words" },
                { "WrapAnywhere", "wrapMode: break anywhere" }, { "Wrap", "wrapMode: words, breaking long words" },
                { "ElideNone", "elide: never" }, { "ElideLeft", "elide: …at the start" },
                { "ElideMiddle", "elide: …in the middle" }, { "ElideRight", "elide: at the end…" },
            };
            Enums["Text"] = text;
            var qt = new Dictionary<string, string>(align)
            {
                { "LeftButton", "Mouse button" }, { "RightButton", "Mouse button" }, { "MiddleButton", "Mouse button" },
                { "Horizontal", "Orientation" }, { "Vertical", "Orientation" },
                { "rgba", "`Qt.rgba(r, g, b, a)` — a colour from 0..1 channels" },
                { "hsva", "`Qt.hsva(h, s, v, a)` — a colour from hue/saturation/value (0..1)" },
                { "hsla", "`Qt.hsla(h, s, l, a)` — a colour from hue/saturation/lightness (0..1)" },
                { "color", "`Qt.color(\"#rrggbb\")` — parse a colour" },
                { "lighter", "`Qt.lighter(color, factor = 1.5)`" },
                { "darker", "`Qt.darker(color, factor = 2.0)`" },
                { "tint", "`Qt.tint(base, tintColor)` — tint composited over base by its alpha" },
                { "alpha", "`Qt.alpha(color, a)` — the colour with alpha a" },
                { "colorEqual", "`Qt.colorEqual(a, b)`" },
            };
            Enums["Qt"] = qt;
            Enums["Font"] = new Dictionary<string, string>
            {
                { "MixedCase", "font.capitalization: as written" }, { "AllUppercase", "font.capitalization: CAPITALS" },
                { "AllLowercase", "font.capitalization: lowercase" }, { "SmallCaps", "font.capitalization: small capitals (shown as capitals)" },
                { "Capitalize", "font.capitalization: First Letter Of Each Word" },
            };
            Enums["Drag"] = new Dictionary<string, string> { { "XAxis", "" }, { "YAxis", "" }, { "XAndYAxis", "" } };
            Enums["Animation"] = new Dictionary<string, string> { { "Infinite", "Loop forever" } };
            var math = new Dictionary<string, string>
            {
                { "PI", "π" }, { "E", "e" }, { "SQRT2", "√2" }, { "LN2", "ln 2" }, { "LN10", "ln 10" },
            };
            foreach (var f in new[] { "abs", "sign", "floor", "ceil", "round", "trunc", "sqrt", "cbrt", "pow", "exp", "log", "log2",
                                      "log10", "sin", "cos", "tan", "asin", "acos", "atan", "atan2", "hypot", "min", "max", "random" })
                math[f] = $"`Math.{f}(…)`";
            math["clamp"] = "`Math.clamp(value, min, max)`";
            Enums["Math"] = math;
            Enums["console"] = new Dictionary<string, string> { { "log", "`console.log(...)`" }, { "warn", "`console.warn(...)`" }, { "error", "`console.error(...)`" } };

            // ---- Elements -----------------------------------------------------------------------------
            var item = new ElementInfo { Name = "Item", Doc = "The invisible base visual element: a positioned box other items are placed in and anchored to." }
                .P("x", "real", "Left edge, relative to the parent (px).")
                .P("y", "real", "Top edge, relative to the parent (px, y down).")
                .P("width", "real", "Width (px).")
                .P("height", "real", "Height (px).")
                .P("visible", "bool", "Hidden items and their children are neither drawn nor hit-tested.")
                .P("opacity", "real", "0..1, multiplied down the tree.")
                .P("state", "string", "The active State's name (\"\" = the base state).")
                .P("states", "list", "`State { }` objects: named sets of property changes.")
                .P("transitions", "list", "`Transition { }` objects: how state changes animate.")
                .P("anchors.fill", "Item", "Fill this item (usually `parent`).")
                .P("anchors.centerIn", "Item", "Centre on this item.")
                .P("anchors.left", "anchor line", "Pin the left edge: `other.left`, `other.right` or `other.horizontalCenter`.")
                .P("anchors.right", "anchor line", "Pin the right edge.")
                .P("anchors.top", "anchor line", "Pin the top edge: `other.top`, `other.bottom` or `other.verticalCenter`.")
                .P("anchors.bottom", "anchor line", "Pin the bottom edge.")
                .P("anchors.horizontalCenter", "anchor line", "Pin the horizontal centre.")
                .P("anchors.verticalCenter", "anchor line", "Pin the vertical centre.")
                .P("anchors.margins", "real", "Margin on every anchored side.")
                .P("anchors.leftMargin", "real", "Left margin (overrides `margins`).")
                .P("anchors.rightMargin", "real", "Right margin.")
                .P("anchors.topMargin", "real", "Top margin.")
                .P("anchors.bottomMargin", "real", "Bottom margin.")
                .P("anchors.horizontalCenterOffset", "real", "Offset from the horizontal centre.")
                .P("anchors.verticalCenterOffset", "real", "Offset from the vertical centre.")
                .P("parent", "Item", "The item this one is inside.", true)
                .P("left", "anchor line", "This item's left edge, as an anchor target (`anchors.left: other.left`).", true)
                .P("right", "anchor line", "This item's right edge, as an anchor target.", true)
                .P("top", "anchor line", "This item's top edge, as an anchor target.", true)
                .P("bottom", "anchor line", "This item's bottom edge, as an anchor target.", true)
                .P("horizontalCenter", "anchor line", "This item's horizontal centre, as an anchor target.", true)
                .P("verticalCenter", "anchor line", "This item's vertical centre, as an anchor target.", true);
            Add(item);

            Add(new ElementInfo { Name = "QtObject", Doc = "A plain non-visual object: a bag of declared properties (a theme's palette is one)." });

            Add(new ElementInfo { Name = "Rectangle", Doc = "A filled box with optional rounded corners, border and soft edge. Drawn in Quill's single SDF pass." }
                .From(item)
                .P("color", "color", "Fill colour (`\"#rrggbb\"` / `\"#rrggbbaa\"`).")
                .P("radius", "real", "Corner radius (px).")
                .P("border.width", "real", "Border width (px), drawn inside the edge.")
                .P("border.color", "color", "Border colour.")
                .P("softness", "real", "Feathers the edge over this many px — a soft shadow or glow is a Rectangle with a large softness behind the real one."));

            Add(new ElementInfo { Name = "Text", Doc = "A run of text. Content-sized unless you set a width, in which case it wraps, aligns and elides inside it." }
                .From(item)
                .P("text", "string", "The text. `\\n` breaks lines.")
                .P("color", "color", "Text colour.")
                .P("fontSize", "real", "Size in px.")
                .P("font.pixelSize", "real", "Same as `fontSize`.")
                .P("font.pointSize", "real", "Same as `fontSize`.")
                .P("font.bold", "bool", "Bold (synthesized if the font has no bold face).")
                .P("font.italic", "bool", "Italic.")
                .P("font.family", "string", "\"\" = Unity's built-in font; a path under a Resources folder (\"Fonts/MyFont\"); or installed font names, comma-separated fallbacks.")
                .P("font.letterSpacing", "real", "Extra px between characters.")
                .P("font.capitalization", "enum", "`Font.MixedCase`, `Font.AllUppercase`, `Font.AllLowercase`, `Font.Capitalize` — applied at display time.")
                .P("horizontalAlignment", "enum", "`Text.AlignLeft` / `AlignHCenter` / `AlignRight` (needs a width).")
                .P("verticalAlignment", "enum", "`Text.AlignTop` / `AlignVCenter` / `AlignBottom` (needs a height).")
                .P("wrapMode", "enum", "`Text.NoWrap`, `WordWrap`, `WrapAnywhere`, `Wrap` (needs a width).")
                .P("elide", "enum", "`Text.ElideNone`, `ElideLeft`, `ElideMiddle`, `ElideRight` (needs a width).")
                .P("lineHeight", "real", "Line height as a factor of the size.")
                .P("contentWidth", "real", "Measured width of the text.", true)
                .P("contentHeight", "real", "Measured height of the text.", true)
                .P("lineCount", "int", "Number of laid-out lines.", true));

            Add(new ElementInfo { Name = "Image", Doc = "A textured quad. `source` is a path under any Resources folder." }
                .From(item)
                .P("source", "string", "Resources path, e.g. \"icons/logo\".")
                .P("color", "color", "Tint."));

            var positioner = new ElementInfo()
                .From(item)
                .P("spacing", "real", "Gap between children (px).")
                .P("padding", "real", "Inner padding on every side.")
                .P("leftPadding", "real", "Left padding (overrides `padding`).")
                .P("rightPadding", "real", "Right padding.")
                .P("topPadding", "real", "Top padding.")
                .P("bottomPadding", "real", "Bottom padding.")
                .P("columns", "int", "Grid: number of columns.")
                .P("rowSpacing", "real", "Grid: vertical gap (defaults to `spacing`).")
                .P("columnSpacing", "real", "Grid: horizontal gap (defaults to `spacing`).");
            Add(new ElementInfo { Name = "Row", Doc = "Lays its visible children out left to right and sizes itself to them." }.From(positioner));
            Add(new ElementInfo { Name = "Column", Doc = "Lays its visible children out top to bottom and sizes itself to them." }.From(positioner));
            Add(new ElementInfo { Name = "Grid", Doc = "Lays its visible children out in `columns` columns." }.From(positioner));
            Add(new ElementInfo { Name = "Flow", Doc = "Lays its visible children out left to right, wrapping at its width." }.From(positioner));

            Add(new ElementInfo { Name = "MouseArea", Doc = "An invisible input region: pointer state to bind to, pointer signals, and dragging." }
                .From(item)
                .P("enabled", "bool", "Accepts input.")
                .P("hoverEnabled", "bool", "Track the pointer while not pressed (`containsMouse`, `onPositionChanged`).")
                .P("pressed", "bool", "The pointer is pressed on it.", true)
                .P("containsMouse", "bool", "The pointer is over it.", true)
                .P("mouseX", "real", "Pointer x, local px.", true)
                .P("mouseY", "real", "Pointer y, local px.", true)
                .P("drag.target", "Item", "Item to move while dragging.")
                .P("drag.axis", "enum", "`Drag.XAxis`, `Drag.YAxis`, `Drag.XAndYAxis`.")
                .P("drag.minimumX", "real", "Drag bound.").P("drag.maximumX", "real", "Drag bound.")
                .P("drag.minimumY", "real", "Drag bound.").P("drag.maximumY", "real", "Drag bound.")
                .P("drag.threshold", "real", "px before a drag starts.")
                .P("drag.active", "bool", "A drag is in progress.", true)
                .S("pressed", "Pointer pressed.", "mouse").S("released", "Pointer released.", "mouse")
                .S("clicked", "Pressed and released inside.", "mouse").S("doubleClicked", "Two clicks in quick succession.", "mouse")
                .S("pressAndHold", "Held down for a while.", "mouse").S("positionChanged", "Pointer moved.", "mouse")
                .S("entered", "Pointer entered (hoverEnabled).").S("exited", "Pointer left (hoverEnabled).")
                .S("wheel", "Mouse wheel; `wheel.angleDelta.y` is 120 per notch.", "wheel"));

            Add(new ElementInfo { Name = "ShaderEffect", Doc = "A quad drawn with a Unity shader (`shader` = its name). Every other property is forwarded as a uniform.", Open = true }
                .From(item)
                .P("shader", "string", "Unity shader name, e.g. \"Quill/Effect/Glass\"."));

            Add(new ElementInfo { Name = "Timer", Doc = "Fires `onTriggered` after `interval` ms while running." }
                .P("interval", "int", "Milliseconds.").P("running", "bool", "Ticking.").P("repeat", "bool", "Keep firing.")
                .P("triggeredOnStart", "bool", "Fire once as soon as it starts.")
                .S("triggered", "The interval elapsed.")
                .M("start", "start()", "Start").M("stop", "stop()", "Stop").M("restart", "restart()", "Restart from zero"));

            Add(new ElementInfo { Name = "Behavior", Doc = "`Behavior on prop { Animation }` — animates every change of a property." }
                .P("enabled", "bool", "When false, changes apply instantly."));
            Add(new ElementInfo { Name = "State", Doc = "A named set of property overrides (`PropertyChanges`), active when `when` is true or `state` names it." }
                .P("name", "string", "The state's name.").P("when", "bool", "Activate automatically while true.")
                .P("extend", "string", "Name of a state this one builds on."));
            Add(new ElementInfo { Name = "PropertyChanges", Doc = "Inside a State: `target` + the properties to set while the state is active (live bindings).", Open = true }
                .P("target", "Item", "The item to change.").P("restoreEntryValues", "bool", "Restore values when leaving the state.")
                .P("explicit", "bool", "Evaluate once instead of binding."));
            Add(new ElementInfo { Name = "Transition", Doc = "How a state change animates: `from` / `to` state names (\"*\" = any)." }
                .P("from", "string", "State name or \"*\".").P("to", "string", "State name or \"*\".")
                .P("reversible", "bool", "Also used, reversed, for the opposite change.")
                .P("enabled", "bool", "In use.").P("running", "bool", "Animating.", true));
            Add(new ElementInfo { Name = "Repeater", Doc = "Creates one delegate (its single child) per model entry. Each instance gets `index` and, for list models, `modelData`." }
                .P("model", "var", "A count or a list; can change at runtime.")
                .P("count", "int", "Number of instances.", true)
                .M("itemAt", "itemAt(index)", "The instance at an index."));

            var anim = new ElementInfo()
                .P("running", "bool", "Playing.").P("paused", "bool", "Paused.")
                .P("loops", "int", "Repetitions (`Animation.Infinite` = forever).")
                .P("duration", "int", "Milliseconds.")
                .P("target", "Item", "Object to animate (defaults to the enclosing one).")
                .P("targets", "list", "Several objects to animate.")
                .P("property", "string", "Property name to animate.")
                .P("properties", "string", "Comma-separated property names.")
                .P("from", "var", "Start value (defaults to the current value).")
                .P("to", "var", "End value.")
                .P("easing.type", "enum", "`Easing.OutCubic`, `Easing.InOutQuad`…")
                .P("easing.amplitude", "real", "Elastic / bounce amplitude.")
                .P("easing.period", "real", "Elastic period.")
                .P("easing.overshoot", "real", "Back overshoot.")
                .S("started", "Started.").S("stopped", "Stopped.").S("finished", "Ran to the end.")
                .M("start", "start()", "Start").M("stop", "stop()", "Stop").M("restart", "restart()", "Restart")
                .M("pause", "pause()", "Pause").M("resume", "resume()", "Resume").M("complete", "complete()", "Jump to the end");
            string[] animDocs =
            {
                "NumberAnimation", "Animates a number from `from` to `to` over `duration` ms.",
                "PropertyAnimation", "Animates any property (numbers and colours).",
                "ColorAnimation", "Animates a colour.",
                "SpringAnimation", "Follows its target with a spring (`spring`, `damping`, `mass`).",
                "SmoothedAnimation", "Follows its target at a maximum `velocity`.",
                "PauseAnimation", "Waits `duration` ms inside a SequentialAnimation.",
                "SequentialAnimation", "Runs its child animations one after another.",
                "ParallelAnimation", "Runs its child animations together.",
                "ScriptAction", "Runs `script: { … }` inside a sequence.",
                "PropertyAction", "Sets a property instantly inside a sequence (`value`).",
            };
            for (int i = 0; i < animDocs.Length; i += 2)
            {
                var a = new ElementInfo { Name = animDocs[i], Doc = animDocs[i + 1] }.From(anim);
                if (a.Name == "SpringAnimation")
                    a.P("spring", "real", "Stiffness.").P("damping", "real", "0..1").P("mass", "real", "Mass.")
                     .P("epsilon", "real", "Settle threshold.").P("velocity", "real", "Max speed (0 = unlimited).");
                if (a.Name == "SmoothedAnimation") a.P("velocity", "real", "Units per second.");
                if (a.Name == "PropertyAction") a.P("value", "var", "Value to set.");
                if (a.Name == "ScriptAction") a.P("script", "handler", "Statements to run.");
                Add(a);
            }
        }

        private static void Add(ElementInfo e) => Elements[e.Name] = e;

        /// <summary>Non-visual types: they don't take Item's geometry.</summary>
        public static bool IsVisual(string type) => Elements.TryGetValue(type, out var e) && e.Props.ContainsKey("x");
    }
}
