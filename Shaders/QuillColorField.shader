// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
// Quill/ColorField — the colour surfaces of the ColorPicker control, drawn analytically so the
// gradients are smooth at any size. One ShaderEffect per part, picked by `mode`:
//
//   mode 0  saturation (x) / value (y) square for `hue`, with a ring marker at (saturation, value)
//   mode 1  hue strip (left to right), with a marker at `hue`
//   mode 2  alpha strip for the colour (hue, saturation, value) over a checkerboard, marker at `alpha`
//   mode 3  swatch: the colour with its alpha over a checkerboard
//
// Quill properties (all 0..1; unset = 0): mode, hue, saturation, value, alpha, cornerRadius (px).
// Colours are produced as raw values, like Quill rectangles, so a swatch matches a Rectangle
// filled with the same colour. The quad is authored in clip space by QuillShaderEffectLayer; it
// needs no scene sampling, so this single SubShader serves URP and the built-in pipeline alike.
Shader "Quill/ColorField"
{
    Properties
    {
        [HideInInspector] _Rect ("Rect (x, y, w, h px)", Vector) = (0, 0, 1, 1)
        [HideInInspector] _Opacity ("Opacity", Float) = 1
        mode ("Mode", Float) = 0
        hue ("Hue", Float) = 0
        saturation ("Saturation", Float) = 0
        value ("Value", Float) = 0
        alpha ("Alpha", Float) = 0
        cornerRadius ("Corner Radius (px)", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Overlay" "IgnoreProjector" = "True" }
        Cull Off  ZWrite Off  ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // Per-material (not global) so every field on screen keeps its own values under URP's
            // SRP Batcher.
            CBUFFER_START(UnityPerMaterial)
                float4 _Rect;
                float  _Opacity;
                float  mode;
                float  hue;
                float  saturation;
                float  value;
                float  alpha;
                float  cornerRadius;
            CBUFFER_END

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = float4(v.vertex.x, v.vertex.y * _ProjectionParams.x, 0.0, 1.0);
                o.uv = v.uv;   // (0,0) top-left
                return o;
            }

            float3 hsv2rgb(float h, float s, float v)
            {
                float3 k = saturate(abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
                return v * lerp(float3(1.0, 1.0, 1.0), k, s);
            }

            float sdRoundBox(float2 p, float2 halfSize, float r)
            {
                float2 q = abs(p) - halfSize + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            float3 checker(float2 px)
            {
                float2 c = floor(px / 6.0);
                return fmod(c.x + c.y, 2.0) < 1.0 ? float3(0.42, 0.42, 0.45) : float3(0.28, 0.28, 0.31);
            }

            // White ring with a soft dark edge, radius r, centred at c.
            float3 ring(float3 col, float2 px, float2 c, float r)
            {
                float d = abs(length(px - c) - r);
                col = lerp(col, float3(0.0, 0.0, 0.0), saturate(2.8 - d) * 0.35);
                return lerp(col, float3(1.0, 1.0, 1.0), saturate(1.6 - d));
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 size = max(_Rect.zw, 1.0);
                float2 px = i.uv * size;                      // item-local px, +y down
                float  d  = sdRoundBox(px - size * 0.5, size * 0.5, clamp(cornerRadius, 0.0, min(size.x, size.y) * 0.5));
                float  mask = saturate(0.5 - d);
                if (mask <= 0.0) return float4(0.0, 0.0, 0.0, 0.0);

                float3 col;
                float  a = 1.0;
                int m = (int)round(mode);

                if (m == 0)
                {
                    col = hsv2rgb(hue, i.uv.x, 1.0 - i.uv.y);
                    float2 c = float2(saturate(saturation), 1.0 - saturate(value)) * size;
                    col = ring(col, px, c, 7.0);
                }
                else if (m == 1)
                {
                    col = hsv2rgb(i.uv.x, 1.0, 1.0);
                    float r = size.y * 0.5 - 2.0;
                    float2 c = float2(clamp(saturate(hue) * size.x, r + 2.0, size.x - r - 2.0), size.y * 0.5);
                    col = ring(col, px, c, r);
                }
                else if (m == 2)
                {
                    col = lerp(checker(px), hsv2rgb(hue, saturation, value), i.uv.x);
                    float r = size.y * 0.5 - 2.0;
                    float2 c = float2(clamp(saturate(alpha) * size.x, r + 2.0, size.x - r - 2.0), size.y * 0.5);
                    col = ring(col, px, c, r);
                }
                else
                {
                    col = lerp(checker(px), hsv2rgb(hue, saturation, value), saturate(alpha));
                }

                return float4(col, a * mask * saturate(_Opacity));
            }
            ENDCG
        }
    }

    Fallback Off
}
