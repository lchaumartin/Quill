// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
// Quill/Surface
// Renders the entire UI in a single full-screen pass. Each Rectangle is composited as a rounded-box
// signed-distance field, back-to-front, with analytic anti-aliasing. No per-element draw calls, no
// Canvas/uGUI — the whole element tree is uploaded as uniform arrays and resolved per pixel.
Shader "Quill/Surface"
{
    Properties { }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Overlay" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // target 4.5 for StructuredBuffer access in the fragment stage. This lets a single pass
            // composite thousands of rectangles (great for Repeater stress tests).
            #pragma target 4.5

            #include "UnityCG.cginc"

            struct QuillRect
            {
                float4 bounds;      // xy = top-left px (y-down), zw = size px
                float4 color;       // rgba straight-alpha fill
                float4 prm;         // x = radius, y = opacity, z = border width, w = unused
                float4 borderColor; // rgba straight-alpha border
            };

            StructuredBuffer<QuillRect> _Rects;
            int    _RectCount;
            float4 _ScreenSize; // xy = pixel size, zw = 1/size

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                // Mesh vertices are authored directly in clip space (-1..1), so the quad always
                // fills the screen regardless of camera/transform. _ProjectionParams.x is -1 when
                // rendering into a flipped target (D3D-style), which corrects the Y orientation.
                o.pos = float4(v.vertex.x, v.vertex.y * _ProjectionParams.x, 0.0, 1.0);
                o.uv = v.uv;
                return o;
            }

            // Signed distance to a rounded box centred at the origin. b = half-size, r = radius.
            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            float4 frag(v2f i) : SV_Target
            {
                // Pixel coordinate with top-left origin (UV y is bottom-up in Unity).
                float2 px = float2(i.uv.x * _ScreenSize.x, (1.0 - i.uv.y) * _ScreenSize.y);

                // Accumulate straight-alpha colour using the painter's "over" operator.
                float3 rgb = 0.0;
                float  a   = 0.0;

                int count = _RectCount;
                // [loop] forces a real runtime loop instead of an unroll (which would generate a
                // giant shader and minute-long compiles).
                [loop]
                for (int idx = 0; idx < count; idx++)
                {
                    QuillRect rect = _Rects[idx];

                    float4 b = rect.bounds;
                    float2 size = b.zw;
                    if (size.x <= 0.0 || size.y <= 0.0) continue;

                    float2 center = b.xy + size * 0.5;
                    float2 half_  = size * 0.5;
                    float  radius = min(rect.prm.x, min(half_.x, half_.y));
                    float  opacity = rect.prm.y;
                    float  border  = max(rect.prm.z, 0.0);

                    float2 p = px - center;
                    float d = sdRoundBox(p, half_, radius);

                    // `d` is a true signed distance in SCREEN PIXELS: px is built from _ScreenSize,
                    // which QuillSurface fills with Screen.width/height, and the quad is authored in
                    // clip space, so one fragment is exactly one pixel. |grad d| is therefore 1 by
                    // construction and the 1px coverage ramp needs no derivative at all.
                    //
                    // This was fwidth(d), which was wrong twice over:
                    //   1. fwidth is the Manhattan sum |ddx| + |ddy|, which overshoots the true
                    //      gradient length by up to sqrt(2) on a 45-degree edge. Straight sides got
                    //      a 1px AA band and corner arcs got up to 1.41px, so a thin border ring
                    //      went soft and dim on the corners while the sides stayed crisp.
                    //   2. HLSL leaves derivatives undefined after divergent control flow, and the
                    //      per-pixel `continue` below diverges inside this loop.
                    float coverage = saturate(0.5 - d);
                    if (coverage <= 0.0) continue;

                    float4 fill = rect.color;
                    float4 brd  = rect.borderColor;

                    // Inner shape (shrunk by the border). innerCov ~ 1 deep inside the fill, 0 in
                    // the border ring. With no border the whole shape is fill.
                    float innerCov = 1.0;
                    if (border > 0.0)
                    {
                        float2 innerHalf = max(half_ - border, 0.0);
                        float  innerRad  = max(radius - border, 0.0);
                        float  di = sdRoundBox(p, innerHalf, innerRad);
                        innerCov = saturate(0.5 - di);   // same 1px-per-fragment reasoning
                    }

                    float3 crgb = lerp(brd.rgb, fill.rgb, innerCov);
                    float  ca   = lerp(brd.a,   fill.a,   innerCov);
                    float  src  = ca * opacity * coverage;

                    // over: result = src over dst
                    rgb = crgb * src + rgb * (1.0 - src);
                    a   = src + a * (1.0 - src);
                }

                return float4(rgb, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
