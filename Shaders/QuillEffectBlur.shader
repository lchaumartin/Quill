// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
// Quill/Effect/Blur — a frosted backdrop blur. Blurs whatever is already on screen behind the
// item's rect, tints it, and rounds its corners. The base layer for glass-style panels.
//
// Convention for Quill ShaderEffect shaders:
//   * author clip-space positions directly and flip Y by _ProjectionParams.x (see vert);
//   * uv runs 0..1 across the item (origin top-left);
//   * the engine always provides _Rect, _ScreenSize, _Opacity (for time, use Unity's built-in _Time.y);
//   * any extra Quill property on the ShaderEffect arrives as a uniform of the same name;
//   * declare every one of those uniforms (and _Rect/_Opacity) in Properties, and on URP inside
//     CBUFFER_START(UnityPerMaterial). Bare globals make the SRP Batcher ignore per-material values,
//     so several effects on screen would all draw with one effect's _Rect and properties.
//
// Quill properties used here:
//   real  radius        blur radius in screen pixels        (0 = no blur)
//   real  cornerRadius  rounded-corner radius in pixels     (0 = square corners)
//   color tint          colour laid over the blur, by alpha (alpha 0 = untinted)
//
// Unset Quill properties arrive as 0, so every default above is the "off" state — an effect with
// no properties set is a plain pass-through of the backdrop.
//
// TWO SUBSHADERS, TWO SOURCES — this is the one behavioural difference to know about:
//
//   URP        samples _CameraOpaqueTexture, which is a copy of the OPAQUE scene taken before
//              transparents. It therefore contains the game world but NOT any Quill UI, so this
//              blurs the scene behind the whole surface and ignores Quill rectangles under it.
//              Requires "Opaque Texture" to be enabled on the URP asset — with it off the
//              texture is black and the effect renders as a flat dark panel.
//
//   Built-in   uses a GrabPass, which copies the live framebuffer at this render queue. Quill
//              rectangles are drawn at queue 4000, before effects, so on built-in the blur DOES
//              pick up Quill panels sitting behind it as well as the scene.
//
// Blurring an arbitrary Quill sub-tree on both pipelines needs ShaderEffectSource (render a
// sub-tree to a texture), which is not implemented yet — see SCOPE.md.
Shader "Quill/Effect/Blur"
{
    Properties
    {
        [HideInInspector] _Rect ("Rect (x, y, w, h px)", Vector) = (0, 0, 1, 1)
        [HideInInspector] _Opacity ("Opacity", Float) = 1
        radius ("Blur Radius (px)", Float) = 0
        cornerRadius ("Corner Radius (px)", Float) = 0
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
                float4 _Rect;       // x, y, w, h of the item in Quill pixels
                float  _Opacity;
                float  radius;
                float  cornerRadius;
                float4 tint;
            CBUFFER_END

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;
                float4 screen : TEXCOORD1;
            };

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
                float2 suv = i.screen.xy / i.screen.w;
                float2 texel = 1.0 / _ScreenParams.xy;
                float  r = max(radius, 0.0);

                // 16-tap golden-angle disc: even area coverage, no visible sample pattern.
                float3 acc = SampleSceneColor(suv);
                float  wsum = 1.0;
                [unroll]
                for (int k = 0; k < 16; k++)
                {
                    float fk = (float)k;
                    float ang = fk * 2.39996323;                 // golden angle, radians
                    float dist = sqrt((fk + 0.5) / 16.0);        // sqrt -> uniform over the disc
                    float2 off = float2(cos(ang), sin(ang)) * dist * r * texel;
                    float  w = 1.0 - dist * 0.5;                 // gentle centre weighting
                    acc += SampleSceneColor(suv + off) * w;
                    wsum += w;
                }
                acc /= wsum;

                float3 col = lerp(acc, tint.rgb, saturate(tint.a));

                // Rounded-rect mask in item-local pixels.
                float2 ext = _Rect.zw * 0.5;
                float2 p = (i.uv - 0.5) * _Rect.zw;
                float  cr = clamp(cornerRadius, 0.0, min(ext.x, ext.y));
                float2 b = ext - cr;
                float  d = length(max(abs(p) - b, 0.0)) - cr;
                float  mask = 1.0 - smoothstep(-1.0, 1.0, d);

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
            #include "UnityCG.cginc"

            sampler2D _QuillBackdrop;
            float4 _QuillBackdrop_TexelSize;

            float4 _Rect;
            float  _Opacity;

            float  radius;
            float  cornerRadius;
            float4 tint;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos  : SV_POSITION;
                float2 uv   : TEXCOORD0;
                float4 grab : TEXCOORD1;
            };

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
                float2 texel = _QuillBackdrop_TexelSize.xy;
                float  r = max(radius, 0.0);

                float3 acc = tex2D(_QuillBackdrop, guv).rgb;
                float  wsum = 1.0;
                [unroll]
                for (int k = 0; k < 16; k++)
                {
                    float fk = (float)k;
                    float ang = fk * 2.39996323;
                    float dist = sqrt((fk + 0.5) / 16.0);
                    float2 off = float2(cos(ang), sin(ang)) * dist * r * texel;
                    float  w = 1.0 - dist * 0.5;
                    acc += tex2D(_QuillBackdrop, guv + off).rgb * w;
                    wsum += w;
                }
                acc /= wsum;

                float3 col = lerp(acc, tint.rgb, saturate(tint.a));

                float2 ext = _Rect.zw * 0.5;
                float2 p = (i.uv - 0.5) * _Rect.zw;
                float  cr = clamp(cornerRadius, 0.0, min(ext.x, ext.y));
                float2 b = ext - cr;
                float  d = length(max(abs(p) - b, 0.0)) - cr;
                float  mask = 1.0 - smoothstep(-1.0, 1.0, d);

                return float4(col, saturate(_Opacity * mask));
            }
            ENDCG
        }
    }

    Fallback Off
}
