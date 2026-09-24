// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quill.Samples
{
    /// <summary>
    /// Smooth state transitions for Quill documents, by naming convention: for every element that
    /// declares both <c>foo</c> and <c>fooTarget</c>, <see cref="Step"/> eases <c>foo</c> toward
    /// <c>fooTarget</c> each frame. Bind <c>fooTarget</c> to the raw state and use <c>foo</c> in visuals:
    /// <code>
    ///   property real hover
    ///   property real hoverTarget: area.containsMouse ? 1 : 0
    ///   refraction: 24 + 12 * hover
    /// </code>
    /// Numbers (real/bool) and colours are supported. Quill has no <c>Behavior</c> element yet; this is
    /// the sample-side stand-in. Pairs are collected once, so call it after <c>LoadFromSource</c>.
    /// </summary>
    public sealed class QuillTweens
    {
        private const string Suffix = "Target";

        private struct Pair
        {
            public QuillProperty Value;
            public QuillProperty Target;
        }

        private readonly List<Pair> _pairs = new List<Pair>();

        public int Count => _pairs.Count;

        public QuillTweens(QuillObject root)
        {
            if (root != null) Collect(root);
            Snap();
        }

        private void Collect(QuillObject obj)
        {
            foreach (var target in obj.Properties)
            {
                string name = target.Name;
                if (name.Length <= Suffix.Length || !name.EndsWith(Suffix, StringComparison.Ordinal)) continue;

                var value = obj.FindProperty(name.Substring(0, name.Length - Suffix.Length));
                if (value != null) _pairs.Add(new Pair { Value = value, Target = target });
            }

            for (int i = 0; i < obj.Children.Count; i++)
                Collect(obj.Children[i]);
        }

        /// <summary>Jump every eased value straight to its target (no transition).</summary>
        public void Snap()
        {
            for (int i = 0; i < _pairs.Count; i++)
            {
                var p = _pairs[i];
                object t = p.Target.Raw;
                if (IsNumeric(t)) p.Value.SetValue(QuillConvert.ToDouble(t));
                else if (TryColor(t, out var c)) p.Value.SetValue(c);
            }
        }

        /// <summary>
        /// Advance every pair. <paramref name="rate"/> is the exponential approach speed (1/s):
        /// about 63% of the remaining distance is covered every 1/rate seconds, at any frame rate.
        /// </summary>
        public void Step(float dt, float rate)
        {
            float k = 1f - Mathf.Exp(-Mathf.Max(0f, rate) * Mathf.Max(0f, dt));

            for (int i = 0; i < _pairs.Count; i++)
            {
                var p = _pairs[i];
                object t = p.Target.Raw;
                object v = p.Value.Raw;

                if (IsNumeric(t))
                {
                    double target = QuillConvert.ToDouble(t);
                    double cur = IsNumeric(v) ? QuillConvert.ToDouble(v) : target;
                    double next = cur + (target - cur) * k;
                    if (Math.Abs(target - next) < 1e-4) next = target;
                    p.Value.SetValue(next);   // no-op when unchanged
                }
                else if (TryColor(t, out var tc))
                {
                    Color cur = v is Color vc ? vc : tc;
                    Color next = Color.LerpUnclamped(cur, tc, k);
                    if (MaxDelta(next, tc) < 1f / 1024f) next = tc;
                    p.Value.SetValue(next);
                }
            }
        }

        private static bool IsNumeric(object v) => v is double || v is float || v is int || v is bool;

        private static bool TryColor(object v, out Color c)
        {
            if (v is Color col) { c = col; return true; }
            if (v is string s && QuillConvert.TryParseColor(s, out c)) return true;
            c = default;
            return false;
        }

        private static float MaxDelta(Color a, Color b)
            => Mathf.Max(Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g)),
                         Mathf.Max(Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a)));
    }
}
