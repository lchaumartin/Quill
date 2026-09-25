// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
// Quill/Effect/LiquidGlass — the whole ShaderEffect rect becomes a rounded pane of glass that
// refracts and blurs what is behind it, with a thin rim highlight and a soft top-down sheen.
// Port of the LiquidGlass Shader Graph Custom Function to the Quill ShaderEffect convention: the
// quad size comes from _Rect (no SizeX/SizeY inputs), and every length is in screen pixels.
//
// Quill properties used here (unset properties arrive as 0, so 0 is always a sane state):
//   real  cornerRadius  rounded-corner radius in pixels               (0 = square corners)
//   real  refraction    width in pixels of the lens band at the edge  (0 = no refraction)
//   real  softness      width in pixels of the rounded glass edge     (0 = sharp, flat edge)
//   real  radius        blur kernel radius in screen pixels           (0 = no blur)
//   color tint          colour laid over the backdrop, by alpha       (alpha 0 = untinted)
//
// Refraction: inside the lens band the backdrop sample is pulled toward the pane's centre,
// fully at the edge, fading out `refraction` pixels in — a convex, magnifying-edge look.
//
// Softness: the silhouette is always crisp (1 px antialiasing). `softness` is the width of a
// quarter-round bevel along it: the surface curves from vertical at the silhouette to flat
// `softness` pixels in. The bevel's normals refract the backdrop (Snell, glass IOR 1.5, on top of
// the lens band) and are lit: a key highlight from the top-left, a fainter bounce from the
// bottom-right and a grazing reflection toward the edge. At 0 the pane is a flat sheet with a thin
// rim line; a few pixels read as a polished edge; tens of pixels as a thick, rounded slab.
//
// Every forwarded uniform is declared in Properties and, on URP, in the UnityPerMaterial cbuffer.
// That is what keeps values per material: with bare globals the SRP Batcher treats the shader as
// having no material state, and every pane on screen ends up drawn with one pane's _Rect/tint.
//
// Backdrop source differs by pipeline, exactly as in Quill/Effect/Blur:
//   URP        _CameraOpaqueTexture — the opaque scene only, no Quill UI. Needs "Opaque Texture"
//              enabled on the URP asset, otherwise the pane renders dark.
//   Built-in   GrabPass — the live framebuffer, so Quill rectangles behind the pane show through.
Shader "Quill/Effect/LiquidGlass"
{
    Properties
    {
        [HideInInspector] _Rect ("Rect (x, y, w, h px)", Vector) = (0, 0, 1, 1)
        [HideInInspector] _Opacity ("Opacity", Float) = 1
        cornerRadius ("Corner Radius (px)", Float) = 0
        refraction ("Refraction Band (px)", Float) = 0
        softness ("Edge Softness (px)", Float) = 0
        radius ("Blur Radius (px)", Float) = 0
        tint ("Tint", Color) = (0, 0, 0, 0)
    }

    // ---------------------------------------------------------------------------------------
    // Universal Render Pipeline — samples the camera opaque texture.
    // ---------------------------------------------------------------------------------------
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Overlay"  "RenderType" = "Overlay"  "IgnoreProjector" = "True"
        }
        Cull Off  ZWrite Off  ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Rect;        // x, y, w, h of the item in Quill pixels (= screen pixels)
                float  _Opacity;
                float  cornerRadius;
                float  refraction;
                float  softness;
                float  radius;
                float4 tint;
            CBUFFER_END

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;
                float4 screen : TEXCOORD1;
            };

            // LOD 0 sample: safe inside dynamic loops/branches (no derivatives needed).
            float3 Backdrop(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture,
                                              UnityStereoTransformScreenSpaceTex(uv), 0).rgb;
            }

            // Signed distance to a rounded box, exact inside (<0) and out.
            float sdRoundBox(float2 p, float2 halfSize, float r)
            {
                float2 q = abs(p) - halfSize + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            // Outward unit direction of that SDF's gradient (item px, +y down). Straight edges
            // meet at a mitre when the corner radius is smaller than the bevel, like cut glass.
            float2 sdRoundBoxDir(float2 p, float2 halfSize, float r)
            {
                float2 q = abs(p) - halfSize + r;
                float2 g = (q.x > 0.0 && q.y > 0.0) ? normalize(q)
                         : (q.x > q.y ? float2(1.0, 0.0) : float2(0.0, 1.0));
                return g * float2(p.x < 0.0 ? -1.0 : 1.0, p.y < 0.0 ? -1.0 : 1.0);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = float4(v.vertex.x, v.vertex.y * _ProjectionParams.x, 0.0, 1.0);
                o.uv = v.uv;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // Screen UV of this pixel and of the rect centre. The quad is axis-aligned, so the
                // screen UV is affine in the item UV; the derivative ratio gives the exact per-axis
                // scale (and sign, whatever the platform's Y flip). Taken before any branch.
                float2 suv = i.screen.xy / i.screen.w;
                float2 s2u = float2(ddx(suv.x) / ddx(i.uv.x), ddy(suv.y) / ddy(i.uv.y));
                float2 cuv = suv + (0.5 - i.uv) * s2u;

                float2 ext = _Rect.zw * 0.5;
                float2 p   = (i.uv - 0.5) * _Rect.zw;              // item-local px, +y down
                float  cr  = clamp(cornerRadius, 0.0, min(ext.x, ext.y));
                float  d   = sdRoundBox(p, ext, cr);

                // Crisp, antialiased silhouette at every softness: the bevel shapes the edge, it
                // never fades it.
                float mask = saturate(-d);
                if (mask <= 0.0) return float4(0.0, 0.0, 0.0, 0.0);

                // Shading reads the SDF at least 1 px in, so the antialiased silhouette pixel matches
                // its neighbour instead of showing the steepest point of the lens as a hard line.
                float ds = min(d, -1.0);

                // Lens band — pull the sample toward the centre inside the edge band.
                float  edge = saturate(-ds / max(refraction, 1e-4));
                float  k    = sin(pow(edge, 0.25) * 1.5707963);   // 0 at edge -> 1 inside
                float2 uv   = lerp(cuv, suv, k);

                // Rounded edge — a quarter-round bevel `softness` px wide (see header). Its normal
                // refracts the view ray; the ray's drift across the bevel's depth shifts the sample.
                float  bevel = clamp(softness, 0.0, min(ext.x, ext.y));
                float3 n     = float3(0.0, 0.0, 1.0);
                if (bevel > 0.0)
                {
                    float u = 1.0 - saturate(-ds / bevel);            // 1 at the silhouette -> 0 on top
                    n = float3(sdRoundBoxDir(p, ext, cr) * u, sqrt(saturate(1.0 - u * u)));
                    float3 t = refract(float3(0.0, 0.0, -1.0), n, 1.0 / 1.5);
                    float2 bendPx = t.xy * (bevel / max(-t.z, 0.2));
                    uv += bendPx / max(_Rect.zw, 1.0) * s2u;
                }

                // 9x9 box blur spanning +/- `radius` pixels.
                float3 acc = Backdrop(uv);
                float  r   = max(radius, 0.0);
                if (r > 0.0)
                {
                    float2 stp = (r * 0.25) / _ScreenParams.xy;
                    acc = 0.0;
                    [loop] for (int bx = -4; bx <= 4; bx++)
                    [loop] for (int by = -4; by <= 4; by++)
                        acc += Backdrop(uv + float2(bx, by) * stp);
                    acc /= 81.0;
                }

                float3 col = lerp(acc, tint.rgb, saturate(tint.a));

                // Lighting — soft top-down sheen, plus a thin rim line on a flat edge. The rim fades
                // out as the bevel grows, where its own highlights take over.
                float  dn   = d / max(_Rect.w, 1.0);
                float  rim  = smoothstep(0.03, 0.0, abs(dn)) * 0.5 * saturate(1.0 - bevel / 3.0);
                float  grad = (0.5 + (0.5 - i.uv.y) * 0.5) * 0.10;
                col += rim + grad;
                if (bevel > 0.0)
                {
                    float3 keyH    = normalize(float3(-0.566, -0.755, 1.330));  // light up-left, half-vector
                    float3 bounceH = normalize(float3( 0.566,  0.755, 1.330));  // light down-right
                    float  spec = pow(saturate(dot(n, keyH)), 20.0) * 0.9
                                + pow(saturate(dot(n, bounceH)), 20.0) * 0.35;
                    float  graze = (1.0 - n.z) * (1.0 - n.z) * 0.35;           // reflection toward the edge
                    col += spec + graze;
                }

                return float4(col, saturate(_Opacity * mask));
            }
            ENDHLSL
        }
    }

    // ---------------------------------------------------------------------------------------
    // Built-in render pipeline — grabs the live framebuffer.
    // ---------------------------------------------------------------------------------------
    SubShader
    {
        Tags { "Queue" = "Overlay"  "RenderType" = "Overlay"  "IgnoreProjector" = "True" }
        Cull Off  ZWrite Off  ZTest Always

        GrabPass { "_QuillBackdrop" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _QuillBackdrop;
            float4 _QuillBackdrop_TexelSize;

            float4 _Rect;
            float  _Opacity;

            float  cornerRadius;
            float  refraction;
            float  softness;
            float  radius;
            float4 tint;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;
                float4 grab   : TEXCOORD1;
            };

            float3 Backdrop(float2 uv) { return tex2Dlod(_QuillBackdrop, float4(uv, 0.0, 0.0)).rgb; }

            float sdRoundBox(float2 p, float2 halfSize, float r)
            {
                float2 q = abs(p) - halfSize + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            // Outward unit direction of that SDF's gradient (item px, +y down). Straight edges
            // meet at a mitre when the corner radius is smaller than the bevel, like cut glass.
            float2 sdRoundBoxDir(float2 p, float2 halfSize, float r)
            {
                float2 q = abs(p) - halfSize + r;
                float2 g = (q.x > 0.0 && q.y > 0.0) ? normalize(q)
                         : (q.x > q.y ? float2(1.0, 0.0) : float2(0.0, 1.0));
                return g * float2(p.x < 0.0 ? -1.0 : 1.0, p.y < 0.0 ? -1.0 : 1.0);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = float4(v.vertex.x, v.vertex.y * _ProjectionParams.x, 0.0, 1.0);
                o.uv = v.uv;
                o.grab = ComputeGrabScreenPos(o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 guv = i.grab.xy / i.grab.w;
                float2 s2u = float2(ddx(guv.x) / ddx(i.uv.x), ddy(guv.y) / ddy(i.uv.y));
                float2 cuv = guv + (0.5 - i.uv) * s2u;

                float2 ext = _Rect.zw * 0.5;
                float2 p   = (i.uv - 0.5) * _Rect.zw;
                float  cr  = clamp(cornerRadius, 0.0, min(ext.x, ext.y));
                float  d   = sdRoundBox(p, ext, cr);

                // Crisp, antialiased silhouette at every softness: the bevel shapes the edge, it
                // never fades it.
                float mask = saturate(-d);
                if (mask <= 0.0) return float4(0.0, 0.0, 0.0, 0.0);

                // Shading reads the SDF at least 1 px in, so the antialiased silhouette pixel matches
                // its neighbour instead of showing the steepest point of the lens as a hard line.
                float ds = min(d, -1.0);

                // Lens band — pull the sample toward the centre inside the edge band.
                float  edge = saturate(-ds / max(refraction, 1e-4));
                float  k    = sin(pow(edge, 0.25) * 1.5707963);   // 0 at edge -> 1 inside
                float2 uv   = lerp(cuv, guv, k);

                // Rounded edge — a quarter-round bevel `softness` px wide (see header). Its normal
                // refracts the view ray; the ray's drift across the bevel's depth shifts the sample.
                float  bevel = clamp(softness, 0.0, min(ext.x, ext.y));
                float3 n     = float3(0.0, 0.0, 1.0);
                if (bevel > 0.0)
                {
                    float u = 1.0 - saturate(-ds / bevel);            // 1 at the silhouette -> 0 on top
                    n = float3(sdRoundBoxDir(p, ext, cr) * u, sqrt(saturate(1.0 - u * u)));
                    float3 t = refract(float3(0.0, 0.0, -1.0), n, 1.0 / 1.5);
                    float2 bendPx = t.xy * (bevel / max(-t.z, 0.2));
                    uv += bendPx / max(_Rect.zw, 1.0) * s2u;
                }

                float3 acc = Backdrop(uv);
                float  r   = max(radius, 0.0);
                if (r > 0.0)
                {
                    float2 stp = (r * 0.25) * _QuillBackdrop_TexelSize.xy;
                    acc = 0.0;
                    [loop] for (int bx = -4; bx <= 4; bx++)
                    [loop] for (int by = -4; by <= 4; by++)
                        acc += Backdrop(uv + float2(bx, by) * stp);
                    acc /= 81.0;
                }

                float3 col = lerp(acc, tint.rgb, saturate(tint.a));

                // Lighting — soft top-down sheen, plus a thin rim line on a flat edge. The rim fades
                // out as the bevel grows, where its own highlights take over.
                float  dn   = d / max(_Rect.w, 1.0);
                float  rim  = smoothstep(0.03, 0.0, abs(dn)) * 0.5 * saturate(1.0 - bevel / 3.0);
                float  grad = (0.5 + (0.5 - i.uv.y) * 0.5) * 0.10;
                col += rim + grad;
                if (bevel > 0.0)
                {
                    float3 keyH    = normalize(float3(-0.566, -0.755, 1.330));  // light up-left, half-vector
                    float3 bounceH = normalize(float3( 0.566,  0.755, 1.330));  // light down-right
                    float  spec = pow(saturate(dot(n, keyH)), 20.0) * 0.9
                                + pow(saturate(dot(n, bounceH)), 20.0) * 0.35;
                    float  graze = (1.0 - n.z) * (1.0 - n.z) * 0.35;           // reflection toward the edge
                    col += spec + graze;
                }

                return float4(col, saturate(_Opacity * mask));
            }
            ENDCG
        }
    }

    Fallback Off
}
