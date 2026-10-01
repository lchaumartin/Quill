// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
// Quill/Surface
// Draws every Rectangle in one draw call: one quad per rectangle, all in a single mesh. Each quad's
// vertex shader reads its rectangle from a float data texture and grows the quad to the rectangle's
// bounds (plus the anti-aliasing / softness margin); the fragment shader evaluates that one rounded
// box's signed distance. A pixel is therefore shaded only by the rectangles that cover it, instead
// of every pixel looping over every rectangle. Quads draw in mesh order, which the GPU blends in
// order, so rectangles composite back-to-front exactly as the element tree lists them.
Shader "Quill/Surface"
{
    Properties { }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Overlay" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest Always
        // Premultiplied blend: the fragment returns its colour already multiplied by alpha.
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // target 3.5 for integer texel fetches (Texture2D.Load / GLSL texelFetch), here in the
            // vertex stage. The rect list lives in a float texture rather than a StructuredBuffer so
            // this pass also runs on WebGL 2 and on GLES3 GPUs without vertex-stage storage buffers.
            #pragma target 3.5

            #include "UnityCG.cginc"

            struct QuillRect
            {
                float4 bounds;      // xy = top-left px (y-down), zw = size px
                float4 color;       // rgba straight-alpha fill
                float4 prm;         // x = radius, y = opacity, z = border width, w = edge softness (px)
                float4 borderColor; // rgba straight-alpha border
            };

            // Four RGBA32F texels per rect, 256 rects per 1024-texel row (see QuillRectLayer).
            Texture2D<float4> _RectData;
            #define RECTS_PER_ROW_SHIFT 8
            #define RECTS_PER_ROW_MASK  255

            QuillRect LoadRect(int idx)
            {
                int x = (idx & RECTS_PER_ROW_MASK) * 4;
                int y = idx >> RECTS_PER_ROW_SHIFT;
                QuillRect r;
                r.bounds      = _RectData.Load(int3(x,     y, 0));
                r.color       = _RectData.Load(int3(x + 1, y, 0));
                r.prm         = _RectData.Load(int3(x + 2, y, 0));
                r.borderColor = _RectData.Load(int3(x + 3, y, 0));
                return r;
            }

            float4 _ScreenSize; // xy = pixel size, zw = 1/size

            // uv.xy = quad corner (0 or 1), uv.z = rect index. The vertex position is unused: the
            // quad is placed entirely from the rect data.
            struct appdata { float4 vertex : POSITION; float4 uv : TEXCOORD0; };

            struct v2f
            {
                float4 pos         : SV_POSITION;
                float2 px          : TEXCOORD0;   // this fragment's pixel position (top-left origin)
                float4 shape       : TEXCOORD1;   // xy = centre px, zw = half-size px
                float4 prm         : TEXCOORD2;   // radius (clamped), opacity, border width, softness
                float4 color       : TEXCOORD3;
                float4 borderColor : TEXCOORD4;
            };

            v2f vert(appdata v)
            {
                v2f o;
                QuillRect r = LoadRect((int)(v.uv.z + 0.5));

                float2 size  = r.bounds.zw;
                float2 half_ = size * 0.5;
                float  soft  = max(r.prm.w, 0.0);

                // Grow the quad past the shape by the width of its coverage ramp: 0.5 px of
                // anti-aliasing, or half the softness feather plus that.
                float  pad = 0.5 * soft + 1.0;
                float2 corner = r.bounds.xy - pad + v.uv.xy * (size + 2.0 * pad);

                // Empty or fully transparent rects collapse to a point: no fragments at all.
                if (size.x <= 0.0 || size.y <= 0.0 || r.prm.y <= 0.0) corner = r.bounds.xy;

                // Pixel (top-left origin, y-down) -> clip space. _ProjectionParams.x is -1 when
                // rendering into a flipped target (D3D-style), which corrects the Y orientation.
                float2 clip = float2(corner.x * _ScreenSize.z * 2.0 - 1.0, 1.0 - corner.y * _ScreenSize.w * 2.0);
                o.pos = float4(clip.x, clip.y * _ProjectionParams.x, 0.0, 1.0);

                o.px = corner;
                o.shape = float4(r.bounds.xy + half_, half_);
                o.prm = float4(min(r.prm.x, min(half_.x, half_.y)), r.prm.y, max(r.prm.z, 0.0), soft);
                o.color = r.color;
                o.borderColor = r.borderColor;
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
                float2 half_  = i.shape.zw;
                float  radius = i.prm.x;
                float  border = i.prm.z;
                float  soft   = i.prm.w;

                float2 p = i.px - i.shape.xy;
                float d = sdRoundBox(p, half_, radius);

                // `d` is a true signed distance in SCREEN PIXELS: rects are uploaded in screen pixels
                // and one fragment is one pixel, so |grad d| is 1 by construction and the 1px coverage
                // ramp needs no derivatives (fwidth would overshoot on diagonals — see history).
                // `softness` widens that ramp into a smooth feather centred on the outline.
                float coverage = soft > 0.0 ? 1.0 - smoothstep(-0.5 * soft - 0.5, 0.5 * soft + 0.5, d)
                                            : saturate(0.5 - d);

                // Inner shape (shrunk by the border): ~1 deep inside the fill, 0 in the border ring.
                float innerCov = 1.0;
                if (border > 0.0)
                {
                    float2 innerHalf = max(half_ - border, 0.0);
                    float  innerRad  = max(radius - border, 0.0);
                    innerCov = saturate(0.5 - sdRoundBox(p, innerHalf, innerRad));
                }

                float3 rgb = lerp(i.borderColor.rgb, i.color.rgb, innerCov);
                float  a   = lerp(i.borderColor.a,   i.color.a,   innerCov) * i.prm.y * coverage;

                // Premultiplied "over". Where the UI below is opaque this is exactly what the previous
                // full-screen pass produced; where translucent UI lies directly on the scene that pass
                // weighted the colour by alpha² (darker), which this corrects.
                return float4(rgb * a, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
