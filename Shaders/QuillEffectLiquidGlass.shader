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
//   real  softness      edge fade in pixels, min 1 px                 (0 = crisp, antialiased)
//   real  radius        blur kernel radius in screen pixels           (0 = no blur)
//   color tint          colour laid over the backdrop, by alpha       (alpha 0 = untinted)
//
// Refraction: inside the lens band the backdrop sample is pulled toward the pane's centre,
// fully at the edge, fading out `refraction` pixels in — a convex, magnifying-edge look.
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

                float mask = saturate(-d / max(softness, 1.0));   // 1 inside, fades over `softness`
                if (mask <= 0.0) return float4(0.0, 0.0, 0.0, 0.0);

                // Refraction — pull the sample toward the centre inside the edge band.
                float  edge = saturate(-d / max(refraction, 1e-4));
                float  k    = sin(pow(edge, 0.25) * 1.5707963);   // 0 at edge -> 1 inside
                float2 uv   = lerp(cuv, suv, k);

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

                // Lighting — thin rim along the edge + soft top-down sheen (scaled to the height).
                float  dn   = d / max(_Rect.w, 1.0);
                float  rim  = smoothstep(0.03, 0.0, abs(dn)) * 0.5;
                float  grad = (0.5 + (0.5 - i.uv.y) * 0.5) * 0.10;
                col += rim + grad;

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

                float mask = saturate(-d / max(softness, 1.0));
                if (mask <= 0.0) return float4(0.0, 0.0, 0.0, 0.0);

                float  edge = saturate(-d / max(refraction, 1e-4));
                float  k    = sin(pow(edge, 0.25) * 1.5707963);
                float2 uv   = lerp(cuv, guv, k);

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

                float  dn   = d / max(_Rect.w, 1.0);
                float  rim  = smoothstep(0.03, 0.0, abs(dn)) * 0.5;
                float  grad = (0.5 + (0.5 - i.uv.y) * 0.5) * 0.10;
                col += rim + grad;

                return float4(col, saturate(_Opacity * mask));
            }
            ENDCG
        }
    }

    Fallback Off
}
