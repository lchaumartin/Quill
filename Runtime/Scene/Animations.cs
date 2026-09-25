// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quill
{
    /// <summary>
    /// Every animation element: <c>NumberAnimation</c>, <c>PropertyAnimation</c>, <c>ColorAnimation</c>,
    /// <c>SpringAnimation</c>, <c>SmoothedAnimation</c>, <c>PauseAnimation</c>,
    /// <c>SequentialAnimation</c>, <c>ParallelAnimation</c>, <c>ScriptAction</c>, <c>PropertyAction</c>.
    ///
    /// The element only holds configuration (reactive properties). Running it builds an
    /// <see cref="AnimJob"/> tree, which the engine ticks each frame. An animation runs on its own
    /// when it is a value source (`NumberAnimation on x`, running by default) or a standalone element
    /// (`running: true` or <c>start()</c>); inside a Sequential/Parallel group, a <c>Behavior</c> or a
    /// <c>Transition</c> it is driven by its container.
    /// Methods: <c>start() stop() restart() pause() resume() complete()</c>.
    /// Signals: <c>started</c>, <c>stopped</c>, <c>finished</c>.
    /// </summary>
    public sealed class QuillAnimation : QuillNonVisual
    {
        /// <summary>The running job when this animation runs on its own.</summary>
        internal AnimJob Job;
        internal QuillEngine.ActiveJob Entry;

        public override void SeedDefaults()
        {
            Property("running").SetValue(false);
            Property("paused").SetValue(false);
            Property("loops").SetValue(1.0);
            Property("duration").SetValue(TypeName == "SmoothedAnimation" ? -1.0 : 250.0);
            Property("property").SetValue("");
            Property("properties").SetValue("");
            Property("target").SetValue(null);
            Property("easing.type").SetValue("Linear");
            Property("easing.amplitude").SetValue(1.0);
            Property("easing.period").SetValue(0.3);
            Property("easing.overshoot").SetValue(1.70158);

            switch (TypeName)
            {
                case "SpringAnimation":
                    Property("spring").SetValue(3.0);
                    Property("damping").SetValue(0.25);
                    Property("mass").SetValue(1.0);
                    Property("epsilon").SetValue(0.01);
                    Property("velocity").SetValue(0.0);   // max speed (units/s); 0 = unlimited
                    break;
                case "SmoothedAnimation":
                    Property("velocity").SetValue(200.0);  // units/s, used when duration is -1
                    break;
            }
        }

        public bool IsGroup => TypeName == "SequentialAnimation" || TypeName == "ParallelAnimation";

        public override bool TryInvokeMethod(QuillEngine engine, string name, object[] args, out object result)
        {
            result = null;
            switch (name)
            {
                case "start": Property("running").SetValue(true); return true;
                case "stop": Property("running").SetValue(false); return true;
                case "restart":
                    engine.StopAnimation(this, emit: false);
                    if (Flag("running", false)) engine.StartAnimation(this);
                    else Property("running").SetValue(true);
                    return true;
                case "pause": Property("paused").SetValue(true); return true;
                case "resume": Property("paused").SetValue(false); return true;
                case "complete": engine.CompleteAnimation(this); return true;
                default: return false;
            }
        }
    }

    // ============================================================================================
    //  Jobs — the running form of an animation.
    // ============================================================================================

    /// <summary>A running animation. <see cref="Begin"/> once, then <see cref="Tick"/> until it returns true.</summary>
    public abstract class AnimJob
    {
        public abstract void Begin();

        /// <summary>Advance by <paramref name="dtMs"/> milliseconds. Returns true when finished.</summary>
        public abstract bool Tick(double dtMs);

        /// <summary>Jump to the end state (<c>complete()</c>).</summary>
        public abstract void Finish();

        /// <summary>Time (ms) past the end on the tick that finished, carried into what comes next.</summary>
        public virtual double Overflow => 0;
    }

    /// <summary>A job whose target can move while it runs (springs, smoothed motion, behaviors).</summary>
    public interface IRetargetable
    {
        void Retarget(object to);
    }

    /// <summary>One property being animated: where it goes, and how its start/end values are found.</summary>
    internal sealed class Track
    {
        public QuillProperty Prop;
        public Func<object> From;   // evaluated at Begin
        public Func<object> To;     // evaluated at Begin
        public object FromValue, ToValue;
        public int Kind;            // 0 = number, 1 = colour, 2 = discrete (switch at the end)

        public void Capture()
        {
            FromValue = From();
            ToValue = To();
            Kind = Classify(FromValue, ToValue);
        }

        public static int Classify(object a, object b)
        {
            bool numA = a == null || a is double || a is bool;
            bool numB = b == null || b is double || b is bool;
            if (numA && numB) return 0;
            if (IsColorish(a) && IsColorish(b)) return 1;
            return 2;
        }

        public static bool IsColorish(object v)
            => v is Color || (v is string s && QuillConvert.TryParseColor(s, out _));

        public void Write(double k)
        {
            switch (Kind)
            {
                case 0:
                {
                    double a = QuillConvert.ToDouble(FromValue), b = QuillConvert.ToDouble(ToValue);
                    Prop.SetAnimated(k >= 1 ? b : a + (b - a) * k);
                    break;
                }
                case 1:
                    Prop.SetAnimated(k >= 1 && ToValue is string
                        ? ToValue
                        : (object)QuillColor.Lerp(QuillConvert.ToColor(FromValue), QuillConvert.ToColor(ToValue), k));
                    break;
                default:
                    if (k >= 1) Prop.SetAnimated(ToValue);
                    break;
            }
        }
    }

    /// <summary>NumberAnimation / ColorAnimation / PropertyAnimation: eased interpolation over a duration.</summary>
    internal sealed class TweenJob : AnimJob, IRetargetable
    {
        private readonly List<Track> _tracks;
        private readonly Func<double> _duration;
        private readonly Func<double, double> _ease;
        private double _elapsed, _dur;

        public TweenJob(List<Track> tracks, Func<double> duration, Func<double, double> ease)
        {
            _tracks = tracks;
            _duration = duration;
            _ease = ease;
        }

        public override void Begin()
        {
            _elapsed = 0;
            _dur = Math.Max(0, _duration());
            foreach (var t in _tracks) t.Capture();
        }

        public override bool Tick(double dtMs)
        {
            _elapsed += dtMs;
            double t = _dur <= 0 ? 1 : Math.Min(1, _elapsed / _dur);
            double k = t >= 1 ? 1 : _ease(t);
            foreach (var tr in _tracks) tr.Write(k);
            return t >= 1;
        }

        public override void Finish()
        {
            foreach (var tr in _tracks) tr.Write(1);
        }

        public override double Overflow => Math.Max(0, _elapsed - _dur);

        // A behavior re-aimed mid-flight: restart from wherever the property is now.
        public void Retarget(object to)
        {
            foreach (var tr in _tracks)
            {
                var now = tr.Prop.Raw;
                tr.From = () => now;
                tr.To = () => to;
            }
            Begin();
        }
    }

    /// <summary>
    /// SpringAnimation: damped spring physics toward the target, stepped at a fixed 16 ms like QML,
    /// so it feels the same at any frame rate. Retargeting keeps the current velocity.
    /// </summary>
    internal sealed class SpringJob : AnimJob, IRetargetable
    {
        private readonly QuillAnimation _el;
        private readonly List<Track> _tracks;
        private double[] _pos, _vel, _to;
        private double _acc;

        public SpringJob(QuillAnimation el, List<Track> tracks)
        {
            _el = el;
            _tracks = tracks;
        }

        public override void Begin()
        {
            int n = _tracks.Count;
            bool keep = _pos != null && _pos.Length == n;
            if (!keep) { _pos = new double[n]; _vel = new double[n]; _to = new double[n]; }
            for (int i = 0; i < n; i++)
            {
                _tracks[i].Capture();
                _pos[i] = QuillConvert.ToDouble(_tracks[i].Prop.Raw);
                _to[i] = QuillConvert.ToDouble(_tracks[i].ToValue);
                if (!keep) _vel[i] = 0;
            }
            _acc = 0;
        }

        public void Retarget(object to)
        {
            for (int i = 0; i < _tracks.Count; i++) _to[i] = QuillConvert.ToDouble(to);
        }

        public override bool Tick(double dtMs)
        {
            double spring = _el.Num("spring", 3), damping = _el.Num("damping", 0.25f);
            double mass = Math.Max(0.0001, _el.Num("mass", 1)), eps = Math.Max(1e-6, _el.Num("epsilon", 0.01f));
            double maxV = _el.Num("velocity", 0);

            _acc += Math.Min(dtMs, 250);
            bool settled = true;
            while (_acc >= 16)
            {
                _acc -= 16;
                settled = true;
                for (int i = 0; i < _pos.Length; i++)
                {
                    double diff = _to[i] - _pos[i];
                    if (spring <= 0)
                    {
                        // No spring: move at the max velocity (or jump).
                        double step = maxV > 0 ? maxV * 0.016 : Math.Abs(diff);
                        _pos[i] += Math.Sign(diff) * Math.Min(Math.Abs(diff), step);
                        _vel[i] = 0;
                    }
                    else
                    {
                        _vel[i] += (spring * diff - damping * _vel[i]) / mass;
                        if (maxV > 0) _vel[i] = Math.Max(-maxV, Math.Min(maxV, _vel[i]));
                        _pos[i] += _vel[i] * 0.016;
                    }
                    if (Math.Abs(_to[i] - _pos[i]) < eps && Math.Abs(_vel[i]) < eps) { _pos[i] = _to[i]; _vel[i] = 0; }
                    else settled = false;
                }
            }
            for (int i = 0; i < _pos.Length; i++) _tracks[i].Prop.SetAnimated(_pos[i]);
            return settled && _acc < 16 && AllAtTarget();
        }

        private bool AllAtTarget()
        {
            for (int i = 0; i < _pos.Length; i++) if (_pos[i] != _to[i]) return false;
            return true;
        }

        public override void Finish()
        {
            for (int i = 0; i < _pos.Length; i++) { _pos[i] = _to[i]; _vel[i] = 0; _tracks[i].Prop.SetAnimated(_to[i]); }
        }
    }

    /// <summary>
    /// SmoothedAnimation: eases toward the target at a set velocity (or over a set duration); a new
    /// target mid-flight splices on with an ease-out so the motion never stops dead.
    /// </summary>
    internal sealed class SmoothedJob : AnimJob, IRetargetable
    {
        private readonly QuillAnimation _el;
        private readonly List<Track> _tracks;
        private double _elapsed, _dur;
        private bool _moving;

        public SmoothedJob(QuillAnimation el, List<Track> tracks)
        {
            _el = el;
            _tracks = tracks;
        }

        public override void Begin()
        {
            foreach (var t in _tracks) t.Capture();
            Plan();
            _moving = false;
        }

        private void Plan()
        {
            _elapsed = 0;
            double duration = _el.Num("duration", -1), velocity = _el.Num("velocity", 200);
            double dist = 0;
            foreach (var t in _tracks)
                dist = Math.Max(dist, Math.Abs(QuillConvert.ToDouble(t.ToValue) - QuillConvert.ToDouble(t.FromValue)));
            double byVelocity = velocity > 0 ? dist / velocity * 1000.0 : 0;
            _dur = duration > 0 ? (velocity > 0 ? Math.Min(duration, byVelocity) : duration) : byVelocity;
        }

        public void Retarget(object to)
        {
            foreach (var t in _tracks)
            {
                t.FromValue = t.Prop.Raw;
                t.ToValue = to;
                t.Kind = 0;
            }
            Plan();
            _moving = true;   // splice with an ease-out
        }

        public override bool Tick(double dtMs)
        {
            _elapsed += dtMs;
            double t = _dur <= 0 ? 1 : Math.Min(1, _elapsed / _dur);
            double k = _moving ? Easing.Evaluate("OutQuad", t) : Easing.Evaluate("InOutQuad", t);
            foreach (var tr in _tracks) tr.Write(k);
            return t >= 1;
        }

        public override void Finish()
        {
            foreach (var tr in _tracks) tr.Write(1);
        }
    }

    internal sealed class PauseJob : AnimJob
    {
        private readonly Func<double> _duration;
        private double _elapsed, _dur;

        public PauseJob(Func<double> duration) { _duration = duration; }
        public override void Begin() { _elapsed = 0; _dur = _duration(); }
        public override bool Tick(double dtMs) { _elapsed += dtMs; return _elapsed >= _dur; }
        public override void Finish() { }
        public override double Overflow => Math.Max(0, _elapsed - _dur);
    }

    /// <summary>ScriptAction / PropertyAction: an instant step inside a group.</summary>
    internal sealed class ActionJob : AnimJob
    {
        private readonly Action _run;
        private bool _done;

        public ActionJob(Action run) { _run = run; }
        public override void Begin() { _done = false; }
        public override bool Tick(double dtMs)
        {
            if (!_done) { _done = true; _run?.Invoke(); }
            return true;
        }
        public override void Finish() { if (!_done) { _done = true; _run?.Invoke(); } }
    }

    internal sealed class SequenceJob : AnimJob
    {
        private readonly List<AnimJob> _steps;
        private int _index;

        public SequenceJob(List<AnimJob> steps) { _steps = steps; }

        public override void Begin()
        {
            _index = 0;
            _overflow = 0;
            if (_steps.Count > 0) _steps[0].Begin();
        }

        public override bool Tick(double dtMs)
        {
            // Instant steps (actions, zero-length tweens) chain within the same frame.
            int guard = 0;
            while (_index < _steps.Count && guard++ < 1000)
            {
                var step = _steps[_index];
                if (!step.Tick(dtMs)) return false;
                dtMs = step.Overflow;
                _overflow = dtMs;
                _index++;
                if (_index < _steps.Count) _steps[_index].Begin();
            }
            return true;
        }

        private double _overflow;
        public override double Overflow => _overflow;

        public override void Finish()
        {
            for (int i = _index; i < _steps.Count; i++)
            {
                if (i > _index) _steps[i].Begin();
                _steps[i].Finish();
            }
            _index = _steps.Count;
        }
    }

    internal sealed class ParallelJob : AnimJob
    {
        private readonly List<AnimJob> _parts;
        private bool[] _done;

        public ParallelJob(List<AnimJob> parts) { _parts = parts; }

        public override void Begin()
        {
            _done = new bool[_parts.Count];
            foreach (var p in _parts) p.Begin();
        }

        public override bool Tick(double dtMs)
        {
            bool all = true;
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_done[i]) continue;
                if (_parts[i].Tick(dtMs)) _done[i] = true;
                else all = false;
            }
            return all;
        }

        public override void Finish()
        {
            for (int i = 0; i < _parts.Count; i++)
                if (!_done[i]) { _parts[i].Finish(); _done[i] = true; }
        }

        public override double Overflow
        {
            get
            {
                double m = double.MaxValue;
                foreach (var p in _parts) m = Math.Min(m, p.Overflow);
                return _parts.Count == 0 ? 0 : m;
            }
        }
    }

    /// <summary>Repeats an inner job `loops` times (-1 = forever). Each loop re-captures its start values.</summary>
    internal sealed class LoopJob : AnimJob
    {
        private readonly AnimJob _inner;
        private readonly Func<int> _loops;
        private int _count, _done;

        public LoopJob(AnimJob inner, Func<int> loops)
        {
            _inner = inner;
            _loops = loops;
        }

        public override void Begin()
        {
            _count = _loops();
            _done = 0;
            _inner.Begin();
        }

        public override bool Tick(double dtMs)
        {
            if (!_inner.Tick(dtMs)) return false;
            _done++;
            if (_count >= 0 && _done >= _count) { _overflow = _inner.Overflow; return true; }
            double carry = _inner.Overflow;
            _inner.Begin();
            if (carry > 0 && _inner.Tick(carry))
            {
                // The carried time spans a whole cycle (very short loops): count it next frame.
                _inner.Begin();
            }
            return false;
        }

        private double _overflow;
        public override double Overflow => _overflow;

        public override void Finish()
        {
            if (_count < 0) { _inner.Finish(); return; }   // "complete" an infinite loop: end of this cycle
            _inner.Finish();
        }
    }

    // ============================================================================================
    //  Building jobs from elements.
    // ============================================================================================

    /// <summary>What an animation is animating when its element doesn't say: its container's context.</summary>
    internal sealed class AnimContext
    {
        public QuillEngine Engine;

        /// <summary>Value source (`on x`) or Behavior: the property animated by default.</summary>
        public QuillProperty DefaultProperty;

        /// <summary>Behavior: the value being written (the animation's default `to`).</summary>
        public bool HasTo;
        public object To;

        /// <summary>Transition: the properties the state change touched.</summary>
        public List<StateChange> Changes;
    }

    /// <summary>One property changed by a state change: its value before and after.</summary>
    internal sealed class StateChange
    {
        public QuillProperty Prop;
        public object From, To;
        public bool Claimed;   // picked up by an animation of the transition
    }

    internal static class AnimBuilder
    {
        public static AnimJob Build(QuillAnimation el, AnimContext ctx)
        {
            AnimJob job = BuildOnce(el, ctx);
            if (job == null) return null;

            // Loops apply to self-running animations and to steps of a group (not to a Behavior's or
            // a Transition's direct animation, which run once per change in QML).
            bool loopable = ctx.Changes == null && !ctx.HasTo;
            if (loopable && el.HasProperty("loops"))
            {
                int loops = (int)el.Num("loops", 1);
                if (loops != 1)
                    return new LoopJob(job, () => (int)el.Num("loops", 1));
            }
            return job;
        }

        private static AnimJob BuildOnce(QuillAnimation el, AnimContext ctx)
        {
            switch (el.TypeName)
            {
                case "SequentialAnimation":
                case "ParallelAnimation":
                {
                    var parts = new List<AnimJob>();
                    // Group children inherit the context but not the Behavior target value twice.
                    foreach (var child in el.Children)
                        if (child is QuillAnimation a)
                        {
                            var j = Build(a, ctx);
                            if (j != null) parts.Add(j);
                        }
                    return el.TypeName == "SequentialAnimation" ? new SequenceJob(parts) : (AnimJob)new ParallelJob(parts);
                }

                case "PauseAnimation":
                    return new PauseJob(() => el.Num("duration", 250));

                case "ScriptAction":
                    return new ActionJob(() => ctx.Engine.RunScript(el));

                case "PropertyAction":
                {
                    var tracks = Tracks(el, ctx, animatesAny: true);
                    return new ActionJob(() =>
                    {
                        foreach (var t in tracks) { t.Capture(); t.Kind = 2; t.Write(1); }
                    });
                }

                case "SpringAnimation":
                    return new SpringJob(el, Tracks(el, ctx, numericOnly: true));

                case "SmoothedAnimation":
                    return new SmoothedJob(el, Tracks(el, ctx, numericOnly: true));

                default: // NumberAnimation, PropertyAnimation, ColorAnimation
                {
                    var tracks = Tracks(el, ctx,
                        numericOnly: el.TypeName == "NumberAnimation",
                        colorOnly: el.TypeName == "ColorAnimation");
                    return new TweenJob(tracks, () => el.Num("duration", 250), t => Ease(el, t));
                }
            }
        }

        public static double Ease(QuillAnimation el, double t)
            => Easing.Evaluate(el.Str("easing.type", "Linear"), t,
                               el.Num("easing.amplitude", 1), el.Num("easing.period", 0.3f),
                               el.Num("easing.overshoot", 1.70158f));

        private static List<string> PropertyNames(QuillAnimation el)
        {
            var names = new List<string>();
            foreach (var key in new[] { "property", "properties" })
                foreach (var part in el.Str(key).Split(','))
                {
                    var n = part.Trim();
                    if (n.Length > 0 && !names.Contains(n)) names.Add(n);
                }
            return names;
        }

        private static List<QuillObject> Targets(QuillAnimation el)
        {
            var list = new List<QuillObject>();
            if (el.FindProperty("target")?.Raw is QuillObject t) list.Add(t);
            if (el.FindProperty("targets")?.Raw is List<object> many)
                foreach (var o in many) if (o is QuillObject q && !list.Contains(q)) list.Add(q);
            return list;
        }

        private static List<Track> Tracks(QuillAnimation el, AnimContext ctx,
                                          bool numericOnly = false, bool colorOnly = false, bool animatesAny = false)
        {
            var tracks = new List<Track>();
            var names = PropertyNames(el);
            var targets = Targets(el);
            bool hasFrom = el.HasProperty("from"), hasTo = el.HasProperty("to"), hasValue = el.HasProperty("value");

            Func<object> fromOf(QuillProperty p, Func<object> fallback)
                => hasFrom ? (Func<object>)(() => el.FindProperty("from").Raw) : fallback;
            Func<object> toOf(Func<object> fallback)
            {
                if (el.TypeName == "PropertyAction" && hasValue) return () => el.FindProperty("value").Raw;
                return hasTo ? (Func<object>)(() => el.FindProperty("to").Raw) : fallback;
            }

            if (ctx.Changes != null)
            {
                // Transition: animate the changed properties this animation matches.
                foreach (var ch in ctx.Changes)
                {
                    if (targets.Count > 0 && !targets.Contains(ch.Prop.Owner)) continue;
                    if (names.Count > 0 && !names.Contains(ch.Prop.Name)) continue;
                    int kind = Track.Classify(ch.From, ch.To);
                    if (numericOnly && kind != 0) continue;
                    if (colorOnly && kind != 1) continue;
                    if (!animatesAny && kind == 2 && el.TypeName != "PropertyAnimation") continue;
                    ch.Claimed = true;
                    var change = ch;
                    tracks.Add(new Track
                    {
                        Prop = ch.Prop,
                        From = fromOf(ch.Prop, () => change.From),
                        To = toOf(() => change.To),
                    });
                }
                return tracks;
            }

            // Explicit target(s) + property/properties, else the container's default property.
            var props = new List<QuillProperty>();
            if (names.Count > 0)
            {
                var owners = targets.Count > 0 ? targets
                    : ctx.DefaultProperty != null ? new List<QuillObject> { ctx.DefaultProperty.Owner } : targets;
                foreach (var o in owners)
                    foreach (var n in names) props.Add(o.Property(n));
            }
            else if (ctx.DefaultProperty != null)
            {
                props.Add(ctx.DefaultProperty);
            }

            foreach (var p in props)
            {
                var prop = p;
                object to = ctx.To;
                tracks.Add(new Track
                {
                    Prop = prop,
                    From = fromOf(prop, () => prop.Raw),
                    To = toOf(ctx.HasTo && prop == ctx.DefaultProperty ? (Func<object>)(() => to) : () => prop.Raw),
                });
            }
            return tracks;
        }
    }
}
