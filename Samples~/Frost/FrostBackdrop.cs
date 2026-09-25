// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Quill.Samples
{
    /// <summary>
    /// The 3D scene behind the Frost sample: a full-screen animated aurora wallpaper with
    /// contour lines, and glossy orbs drifting in front of it. Frosted glass is only as interesting
    /// as what it bends, so the backdrop has colour, movement and crisp edges.
    ///
    /// Everything is opaque geometry, so it lands in URP's Opaque Texture (and in the Built-in
    /// GrabPass). Both shaders are unlit and pipeline-agnostic, so the scene looks the same on URP
    /// and Built-in without any material setup.
    ///
    /// <see cref="Exposure"/>, <see cref="Warmth"/> and <see cref="Calm"/> grade the scene; changes are
    /// eased, so they can be driven from UI (a brightness slider…) without popping.
    ///
    /// On URP it also turns on the main camera's Opaque Texture (a per-camera override), which the
    /// glass samples; with Built-in the glass uses a GrabPass and needs nothing.
    /// </summary>
    [AddComponentMenu("Quill/Samples/Frost Backdrop")]
    public sealed class FrostBackdrop : MonoBehaviour
    {
        public Shader WallpaperShader;
        public Shader OrbShader;

        [Range(0f, 2f)] public float Exposure = 1f;
        [Range(0f, 1f)] public float Warmth;
        [Range(0f, 1f)] public float Calm;

        [Tooltip("Orb drift speed multiplier.")]
        public float OrbSpeed = 1f;

        [Tooltip("How fast Exposure / Warmth / Calm changes settle (1/s).")]
        public float Smoothing = 3f;

        [Tooltip("Seed for the orb layout, so the composition is the same every run.")]
        public int Seed = 11;

        private static readonly int IdExposure = Shader.PropertyToID("_FrostExposure");
        private static readonly int IdWarmth = Shader.PropertyToID("_FrostWarmth");
        private static readonly int IdCalm = Shader.PropertyToID("_FrostCalm");
        private static readonly int IdColorA = Shader.PropertyToID("_ColorA");
        private static readonly int IdColorB = Shader.PropertyToID("_ColorB");

        // Lit / shadow colour pairs for the orbs.
        private static readonly Color[] Palette =
        {
            new Color(1.00f, 0.47f, 0.42f), new Color(0.55f, 0.08f, 0.30f),   // coral
            new Color(1.00f, 0.76f, 0.30f), new Color(0.80f, 0.25f, 0.10f),   // amber
            new Color(1.00f, 0.42f, 0.86f), new Color(0.40f, 0.07f, 0.55f),   // magenta
            new Color(0.66f, 0.52f, 1.00f), new Color(0.20f, 0.10f, 0.55f),   // violet
            new Color(0.45f, 0.90f, 1.00f), new Color(0.05f, 0.30f, 0.60f),   // cyan
            new Color(0.52f, 1.00f, 0.80f), new Color(0.05f, 0.45f, 0.45f),   // mint
            new Color(0.45f, 0.65f, 1.00f), new Color(0.10f, 0.15f, 0.50f),   // sky
        };

        private struct Orb
        {
            public Transform T;
            public Vector3 Home, Amplitude, Frequency, Phase;
        }

        private readonly List<Orb> _orbs = new List<Orb>();
        private readonly List<Object> _owned = new List<Object>();
        private float _exposure, _warmth, _calm;

        private void Start()
        {
            if (WallpaperShader == null) WallpaperShader = Shader.Find("Quill/Samples/FrostWallpaper");
            if (OrbShader == null) OrbShader = Shader.Find("Quill/Samples/FrostOrb");

            RequestOpaqueTexture(Camera.main);

            _exposure = Exposure; _warmth = Warmth; _calm = Calm;
            PushGlobals();

            if (WallpaperShader != null) BuildWallpaper();
            else Debug.LogWarning("[Quill] Frost sample: wallpaper shader missing.");

            if (OrbShader != null) BuildOrbs();
            else Debug.LogWarning("[Quill] Frost sample: orb shader missing.");
        }

        private void Update()
        {
            float k = 1f - Mathf.Exp(-Smoothing * Time.unscaledDeltaTime);
            _exposure = Mathf.Lerp(_exposure, Exposure, k);
            _warmth = Mathf.Lerp(_warmth, Warmth, k);
            _calm = Mathf.Lerp(_calm, Calm, k);
            PushGlobals();

            // Calm also slows the scene down.
            float t = Time.time * OrbSpeed * Mathf.Lerp(1f, 0.35f, _calm);
            for (int i = 0; i < _orbs.Count; i++)
            {
                var o = _orbs[i];
                o.T.localPosition = o.Home + Vector3.Scale(o.Amplitude, new Vector3(
                    Mathf.Sin(t * o.Frequency.x + o.Phase.x),
                    Mathf.Sin(t * o.Frequency.y + o.Phase.y),
                    Mathf.Sin(t * o.Frequency.z + o.Phase.z)));
            }
        }

        private void PushGlobals()
        {
            Shader.SetGlobalFloat(IdExposure, _exposure);
            Shader.SetGlobalFloat(IdWarmth, _warmth);
            Shader.SetGlobalFloat(IdCalm, _calm);
        }

        // A clip-space quad: the shader places it over the whole screen, behind everything.
        private void BuildWallpaper()
        {
            var go = new GameObject("Wallpaper");
            go.transform.SetParent(transform, false);

            var mesh = new Mesh { name = "Frost Wallpaper" };
            mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);   // never frustum-culled

            var mat = new Material(WallpaperShader) { name = "Frost Wallpaper" };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _owned.Add(mesh);
            _owned.Add(mat);
        }

        private void BuildOrbs()
        {
            var mat = new Material(OrbShader) { name = "Frost Orb" };
            _owned.Add(mat);

            var rng = new System.Random(Seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // Hand-placed anchors spread across the frame (camera at z = -10, 50° FOV), so there is
            // always something passing behind the panels.
            Vector3[] homes =
            {
                new Vector3(-6.2f,  2.6f, 3.0f), new Vector3(-2.4f, -2.8f, 1.0f), new Vector3( 1.2f,  2.9f, 4.5f),
                new Vector3( 4.6f, -0.8f, 0.5f), new Vector3( 6.8f,  3.0f, 5.0f), new Vector3(-4.0f,  0.2f, 6.0f),
                new Vector3( 2.8f, -3.4f, 3.5f), new Vector3( 8.5f, -2.8f, 6.5f), new Vector3(-8.2f, -2.6f, 5.5f),
                new Vector3( 0.2f,  0.4f, 8.0f),
            };
            float[] sizes = { 2.2f, 1.5f, 1.8f, 1.2f, 2.7f, 1.0f, 2.0f, 3.0f, 2.5f, 1.6f };

            var block = new MaterialPropertyBlock();
            for (int i = 0; i < homes.Length; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Orb " + i;
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                go.transform.SetParent(transform, false);
                go.transform.localScale = Vector3.one * sizes[i];

                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                int c = (i * 3) % (Palette.Length / 2);
                // SetVector, not SetColor: keep the colours display-referred (no linear conversion);
                // the shader converts its final result for Linear projects.
                block.SetVector(IdColorA, Palette[c * 2]);
                block.SetVector(IdColorB, Palette[c * 2 + 1]);
                mr.SetPropertyBlock(block);

                _orbs.Add(new Orb
                {
                    T = go.transform,
                    Home = homes[i],
                    Amplitude = new Vector3(R(1.2f, 2.6f), R(0.8f, 1.6f), R(0.5f, 1.5f)),
                    Frequency = new Vector3(R(0.12f, 0.3f), R(0.15f, 0.35f), R(0.1f, 0.25f)),
                    Phase = new Vector3(R(0f, 6.28f), R(0f, 6.28f), R(0f, 6.28f)),
                });
            }
        }

        /// <summary>
        /// URP only: ask this camera for the Opaque Texture (UniversalAdditionalCameraData
        /// .requiresColorOption = On). Done by reflection so the sample compiles without URP.
        /// </summary>
        private static void RequestOpaqueTexture(Camera cam)
        {
            if (cam == null || GraphicsSettings.currentRenderPipeline == null) return;

            var type = Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (type == null) return;

            var data = cam.GetComponent(type);
            if (data == null) data = cam.gameObject.AddComponent(type);

            var prop = type.GetProperty("requiresColorOption");
            if (prop == null || !prop.CanWrite || !prop.PropertyType.IsEnum) return;
            try { prop.SetValue(data, Enum.Parse(prop.PropertyType, "On")); }
            catch (Exception e) { Debug.LogWarning($"[Quill] Could not enable the camera Opaque Texture: {e.Message}"); }
        }

        private void OnDestroy()
        {
            foreach (var o in _owned)
                if (o != null) Destroy(o);
        }
    }
}
