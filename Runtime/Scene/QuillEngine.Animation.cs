// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;

namespace Quill
{
    // Time-driven behaviour: running animation jobs, Timers, Behaviors, and States & Transitions.
    public sealed partial class QuillEngine
    {
        internal sealed class ActiveJob
        {
            public AnimJob Job;
            public QuillAnimation Owner;   // for `paused`; null for behaviors and transitions
            public Action OnDone;
            public bool Removed;
        }

        private readonly List<ActiveJob> _jobs = new List<ActiveJob>();
        private readonly List<QuillTimer> _activeTimers = new List<QuillTimer>();
        private readonly Dictionary<QuillBehavior, BehaviorRunner> _behaviors = new Dictionary<QuillBehavior, BehaviorRunner>();
        private readonly Dictionary<QuillObject, StateGroup> _stateGroups = new Dictionary<QuillObject, StateGroup>();
        private readonly Dictionary<QuillPositioner, Positioners.Info> _positioners = new Dictionary<QuillPositioner, Positioners.Info>();

        /// <summary>Number of animations, behaviors and transitions currently running.</summary>
        public int RunningAnimationCount
        {
            get
            {
                int n = 0;
                foreach (var j in _jobs) if (!j.Removed) n++;
                return n;
            }
        }

        internal Positioners.Info PositionerInfo(QuillPositioner p)
        {
            if (!_positioners.TryGetValue(p, out var info))
                _positioners[p] = info = new Positioners.Info();
            return info;
        }

        private void ResetAnimationState()
        {
            _jobs.Clear();
            _activeTimers.Clear();
            _behaviors.Clear();
            _stateGroups.Clear();
        }

        internal ActiveJob AddJob(AnimJob job, QuillAnimation owner, Action onDone)
        {
            var entry = new ActiveJob { Job = job, Owner = owner, OnDone = onDone };
            _jobs.Add(entry);
            return entry;
        }

        internal void RemoveJob(ActiveJob entry)
        {
            if (entry != null) entry.Removed = true;
        }

        private void AdvanceTime(double dtSeconds)
        {
            double ms = Math.Max(0, dtSeconds) * 1000.0;

            // Timers — at most one trigger per timer per frame.
            if (_activeTimers.Count > 0)
            {
                foreach (var t in _activeTimers.ToArray())
                {
                    if (!t.Flag("running", false)) { _activeTimers.Remove(t); continue; }
                    double interval = Math.Max(1, t.Num("interval", 1000));
                    t.Elapsed += ms;
                    if (t.Elapsed < interval) continue;

                    if (t.Flag("repeat", false))
                    {
                        t.Elapsed -= interval;
                        if (t.Elapsed >= interval) t.Elapsed %= interval;
                    }
                    else
                    {
                        t.Elapsed = 0;
                        _activeTimers.Remove(t);
                        t.Property("running").SetAnimated(false);
                    }
                    t.Emit("onTriggered");
                }
            }

            // Animation jobs.
            if (_jobs.Count > 0)
            {
                foreach (var entry in _jobs.ToArray())
                {
                    if (entry.Removed) continue;
                    if (entry.Owner != null && entry.Owner.Flag("paused", false)) continue;

                    bool done;
                    try { done = entry.Job.Tick(ms); }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogWarning($"[Quill] Animation failed and was stopped: {e.Message}");
                        done = true;
                    }
                    if (done && !entry.Removed)
                    {
                        entry.Removed = true;
                        entry.OnDone?.Invoke();
                    }
                }
                _jobs.RemoveAll(e => e.Removed);
            }
        }

        // ---- Timers -----------------------------------------------------------------------------

        private void SetupTimer(QuillTimer t)
        {
            t.Property("running").Changed += () =>
            {
                if (t.Flag("running", false)) TimerStarted(t);
                else _activeTimers.Remove(t);
            };
            if (t.Flag("running", false)) TimerStarted(t);
        }

        internal void TimerStarted(QuillTimer t)
        {
            t.Elapsed = 0;
            if (!_activeTimers.Contains(t)) _activeTimers.Add(t);
            if (t.Flag("triggeredOnStart", false)) t.Emit("onTriggered");
        }

        // ---- Self-running animations -----------------------------------------------------------

        private static bool IsSelfRunning(QuillAnimation a)
            => !(a.Parent is QuillAnimation || a.Parent is QuillBehavior || a.Parent is QuillTransition);

        private void SetupAnimation(QuillAnimation a)
        {
            if (!IsSelfRunning(a)) return;

            var running = a.Property("running");
            // A value source (`NumberAnimation on x`) runs unless told otherwise; a standalone
            // animation waits for `running: true` or start(), as in QML.
            if (!string.IsNullOrEmpty(a.OnProperty) && !running.HasBinding) running.SetAnimated(true);

            running.Changed += () =>
            {
                bool r = a.Flag("running", false);
                if (r && a.Job == null) StartAnimation(a);
                else if (!r && a.Job != null) StopAnimation(a, emit: true);
            };
            if (a.Flag("running", false)) StartAnimation(a);
        }

        internal void StartAnimation(QuillAnimation a)
        {
            if (a.Job != null) return;
            var ctx = new AnimContext { Engine = this };
            if (!string.IsNullOrEmpty(a.OnProperty) && a.Parent != null)
                ctx.DefaultProperty = a.Parent.Property(a.OnProperty);

            var job = AnimBuilder.Build(a, ctx);
            if (job == null) return;

            a.Job = job;
            job.Begin();
            var entry = AddJob(job, a, null);
            a.Entry = entry;
            entry.OnDone = () =>
            {
                if (a.Job != job) return;
                a.Job = null;
                a.Entry = null;
                a.Property("running").SetAnimated(false);
                a.Emit("onFinished");
                a.Emit("onStopped");
            };
            a.Emit("onStarted");
        }

        internal void StopAnimation(QuillAnimation a, bool emit)
        {
            if (a.Job == null) return;
            RemoveJob(a.Entry);
            a.Job = null;
            a.Entry = null;
            if (emit) a.Emit("onStopped");
        }

        internal void CompleteAnimation(QuillAnimation a)
        {
            if (a.Job == null) return;
            var job = a.Job;
            StopAnimation(a, emit: false);
            job.Finish();
            a.Property("running").SetAnimated(false);
            a.Emit("onFinished");
            a.Emit("onStopped");
        }

        // ---- Behavior ---------------------------------------------------------------------------

        private void InstallBehavior(QuillBehavior b)
        {
            if (string.IsNullOrEmpty(b.OnProperty) || b.Parent == null)
            {
                UnityEngine.Debug.LogWarning("[Quill] Behavior needs the `on` form: `Behavior on x { NumberAnimation { } }`.");
                return;
            }
            var prop = b.Parent.Property(b.OnProperty);
            var runner = new BehaviorRunner(this, b, prop);
            _behaviors[b] = runner;
            prop.Interceptor = runner;
        }

        private void RemoveBehavior(QuillBehavior b)
        {
            if (!_behaviors.TryGetValue(b, out var r)) return;
            r.Cancel();
            if (r.Prop.Interceptor == r) r.Prop.Interceptor = null;
            _behaviors.Remove(b);
        }

        /// <summary>Turns writes to a property into animations toward the written value.</summary>
        private sealed class BehaviorRunner : IPropertyInterceptor
        {
            private readonly QuillEngine _engine;
            private readonly QuillBehavior _behavior;
            public readonly QuillProperty Prop;
            private ActiveJob _active;
            private object _target;

            public BehaviorRunner(QuillEngine engine, QuillBehavior behavior, QuillProperty prop)
            {
                _engine = engine;
                _behavior = behavior;
                Prop = prop;
            }

            public void Cancel()
            {
                _engine.RemoveJob(_active);
                _active = null;
            }

            public bool Intercept(QuillProperty property, object newValue)
            {
                if (!_behavior.Flag("enabled", true)) { Cancel(); return false; }

                QuillAnimation anim = null;
                foreach (var c in _behavior.Children)
                    if (c is QuillAnimation a) { anim = a; break; }
                if (anim == null) return false;

                bool running = _active != null && !_active.Removed;
                if (running)
                {
                    if (QuillProperty.ValuesEqual(_target, newValue)) return true;   // already heading there
                    _target = newValue;
                    if (_active.Job is SpringJob || _active.Job is SmoothedJob)
                    {
                        ((IRetargetable)_active.Job).Retarget(newValue);        // keep momentum
                        return true;
                    }
                    Cancel();
                }

                if (QuillProperty.ValuesEqual(property.Raw, newValue)) return false;   // nothing to animate

                _target = newValue;
                var job = AnimBuilder.Build(anim, new AnimContext
                {
                    Engine = _engine, DefaultProperty = property, HasTo = true, To = newValue
                });
                if (job == null) return false;

                job.Begin();
                ActiveJob entry = null;
                entry = _engine.AddJob(job, null, () => { if (_active == entry) _active = null; });
                _active = entry;
                return true;
            }
        }

        // ---- States & Transitions ------------------------------------------------------------------

        private sealed class SavedValue
        {
            public Func<object> Expression;   // the binding that drove the property, or null
            public object Value;
        }

        private sealed class StateGroup
        {
            public QuillObject Owner;
            public readonly List<QuillState> States = new List<QuillState>();
            public readonly List<QuillTransition> Transitions = new List<QuillTransition>();
            public readonly Dictionary<QuillProperty, SavedValue> Saved = new Dictionary<QuillProperty, SavedValue>();
            public string Current = "";
            public bool WhenActive, Initializing;
            public ActiveJob Transition;
            public QuillTransition RunningTransition;
        }

        private void SetupStates(QuillItem obj)
        {
            if (_stateGroups.ContainsKey(obj)) return;
            StateGroup g = null;
            foreach (var c in obj.Children)
            {
                if (c is QuillState s) (g ??= new StateGroup { Owner = obj }).States.Add(s);
                else if (c is QuillTransition t) (g ??= new StateGroup { Owner = obj }).Transitions.Add(t);
            }
            if (g == null || g.States.Count == 0) return;
            _stateGroups[obj] = g;

            var stateProp = obj.Property("state");
            if (stateProp.Raw == null) stateProp.SetAnimated("");
            stateProp.Changed += () => OnStateChanged(g);
            foreach (var s in g.States)
                if (s.HasProperty("when")) s.Property("when").Changed += () => EvaluateWhen(g);

            g.Initializing = true;
            EvaluateWhen(g);
            OnStateChanged(g);
            g.Initializing = false;
        }

        private void RemoveStates(QuillObject o)
        {
            if (!_stateGroups.TryGetValue(o, out var g)) return;
            RemoveJob(g.Transition);
            _stateGroups.Remove(o);
        }

        private void EvaluateWhen(StateGroup g)
        {
            string pick = null;
            foreach (var s in g.States)
                if (s.HasProperty("when") && s.Flag("when", false)) { pick = s.Str("name"); break; }

            var sp = g.Owner.Property("state");
            if (pick != null)
            {
                g.WhenActive = true;
                if (QuillConvert.ToStr(sp.Raw) != pick) sp.SetAnimated(pick);
            }
            else if (g.WhenActive)
            {
                g.WhenActive = false;
                object back = sp.Driver != null && !sp.Driver.IsDetached ? sp.Driver.Expression() : "";
                sp.SetAnimated(QuillConvert.ToStr(back));
            }
        }

        private void OnStateChanged(StateGroup g)
        {
            string target = QuillConvert.ToStr(g.Owner.Property("state").Raw);
            if (target == g.Current) return;
            ApplyState(g, g.Current, target, animate: !g.Initializing);
        }

        private QuillState FindState(StateGroup g, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var s in g.States) if (s.Str("name") == name) return s;
            return null;
        }

        private List<(QuillProperty prop, QuillPropertyChanges pc, string name)> CollectChanges(StateGroup g, string stateName)
        {
            var result = new List<(QuillProperty, QuillPropertyChanges, string)>();
            var index = new Dictionary<QuillProperty, int>();

            void Add(QuillState s, int depth)
            {
                if (s == null || depth > 8) return;
                Add(FindState(g, s.Str("extend")), depth + 1);   // a base state's changes first

                foreach (var child in s.Children)
                {
                    if (!(child is QuillPropertyChanges pc)) continue;
                    var target = pc.FindProperty("target")?.Raw as QuillObject;

                    foreach (var pname in new List<string>(pc.PropertyNames))
                    {
                        if (pname == "target" || pname == "restoreEntryValues" || pname == "explicit") continue;

                        QuillObject obj = target;
                        string propName = pname;
                        int dot = pname.IndexOf('.');
                        if (dot > 0)
                        {
                            // `rect.color: "red"` names its target by id; `border.color` is a group of the target.
                            string first = pname.Substring(0, dot);
                            bool isGroupOfTarget = target != null && target.HasGroup(first);
                            var byId = isGroupOfTarget ? null : ResolveObject(pc, first);
                            if (byId != null) { obj = byId; propName = pname.Substring(dot + 1); }
                        }
                        if (obj == null)
                        {
                            UnityEngine.Debug.LogWarning($"[Quill] PropertyChanges in state '{s.Str("name")}' has no target for '{pname}'.");
                            continue;
                        }

                        var qp = obj.Property(propName);
                        if (index.TryGetValue(qp, out int at)) result[at] = (qp, pc, pname);
                        else { index[qp] = result.Count; result.Add((qp, pc, pname)); }
                    }
                }
            }

            Add(FindState(g, stateName), 0);
            return result;
        }

        private void ApplyState(StateGroup g, string fromName, string toName, bool animate)
        {
            if (!string.IsNullOrEmpty(toName) && FindState(g, toName) == null)
                UnityEngine.Debug.LogWarning($"[Quill] Unknown state '{toName}'.");

            var oldChanges = CollectChanges(g, fromName);
            var newChanges = CollectChanges(g, toName);

            var affected = new List<QuillProperty>();
            var before = new Dictionary<QuillProperty, object>();
            foreach (var c in oldChanges) if (!before.ContainsKey(c.prop)) { before[c.prop] = c.prop.Raw; affected.Add(c.prop); }
            foreach (var c in newChanges) if (!before.ContainsKey(c.prop)) { before[c.prop] = c.prop.Raw; affected.Add(c.prop); }

            // A transition still running is interrupted where it is.
            if (g.Transition != null)
            {
                RemoveJob(g.Transition);
                g.Transition = null;
                g.RunningTransition?.Property("running").SetAnimated(false);
            }

            var stillChanged = new HashSet<QuillProperty>();
            foreach (var c in newChanges) stillChanged.Add(c.prop);

            // Leave the old state: restore what it overrode (unless the new state overrides it too).
            foreach (var c in oldChanges)
            {
                if (stillChanged.Contains(c.prop) || !g.Saved.TryGetValue(c.prop, out var saved)) continue;
                g.Saved.Remove(c.prop);
                if (!c.pc.Flag("restoreEntryValues", true)) { c.prop.ClearBinding(); continue; }
                if (saved.Expression != null) c.prop.SetBinding(new Binding(this, saved.Expression));
                else c.prop.SetValue(saved.Value);
            }

            // Enter the new state: its values are live bindings to the PropertyChanges.
            foreach (var c in newChanges)
            {
                if (!g.Saved.ContainsKey(c.prop))
                {
                    var d = c.prop.Driver;
                    g.Saved[c.prop] = new SavedValue
                    {
                        Expression = d != null && !d.IsDetached ? d.Expression : null,
                        Value = c.prop.Raw
                    };
                }
                var pc = c.pc;
                string name = c.name;
                if (pc.Flag("explicit", false)) c.prop.SetValue(pc.Property(name).Raw);
                else c.prop.SetBinding(new Binding(this, () => pc.Property(name).Get()));
            }

            g.Current = toName;
            if (!animate) return;

            var tr = FindTransition(g, fromName, toName);
            if (tr == null || !tr.Flag("enabled", true)) return;

            var changes = new List<StateChange>();
            foreach (var p in affected)
                if (!QuillProperty.ValuesEqual(before[p], p.Raw))
                    changes.Add(new StateChange { Prop = p, From = before[p], To = p.Raw });
            if (changes.Count == 0) return;

            var ctx = new AnimContext { Engine = this, Changes = changes };
            var parts = new List<AnimJob>();
            foreach (var c in tr.Children)
                if (c is QuillAnimation a)
                {
                    var j = AnimBuilder.Build(a, ctx);
                    if (j != null) parts.Add(j);
                }
            if (parts.Count == 0) return;

            // Animated properties start from where they were; the rest simply jump.
            foreach (var ch in changes) if (ch.Claimed) ch.Prop.SetAnimated(ch.From);

            var job = parts.Count == 1 ? parts[0] : new ParallelJob(parts);
            job.Begin();
            g.RunningTransition = tr;
            tr.Property("running").SetAnimated(true);
            ActiveJob entry = null;
            entry = AddJob(job, null, () =>
            {
                if (g.Transition == entry) g.Transition = null;
                tr.Property("running").SetAnimated(false);
            });
            g.Transition = entry;
        }

        private static QuillTransition FindTransition(StateGroup g, string from, string to)
        {
            bool Match(string pattern, string name)
            {
                foreach (var part in (pattern ?? "*").Split(','))
                {
                    var p = part.Trim();
                    if (p == "*" || p == name) return true;
                }
                return false;
            }

            foreach (var t in g.Transitions)   // an exact match wins over wildcards
                if (t.Str("from", "*") == from && t.Str("to", "*") == to) return t;
            foreach (var t in g.Transitions)
                if (Match(t.Str("from", "*"), from) && Match(t.Str("to", "*"), to)) return t;
            foreach (var t in g.Transitions)
                if (t.Flag("reversible", false) && Match(t.Str("from", "*"), to) && Match(t.Str("to", "*"), from)) return t;
            return null;
        }
    }
}
