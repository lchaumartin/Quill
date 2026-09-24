// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
// Quill/Samples/LiquidGlassWallpaper — the animated backdrop of the Liquid Glass sample.
// Soft drifting colour fields over a deep gradient, with thin contour lines that follow them:
// smooth gradients alone hide refraction, the lines make every lens edge readable.
//
// Drawn as a clip-space quad on the far plane in the Background queue, without writing depth, so it
// sits behind all scene geometry on any camera (and survives URP depth priming's Equal depth test).
// It is opaque, so URP copies it into the Opaque Texture.
// Globals (set by LiquidGlassBackdrop.cs): _LGExposure (0 = default 1), _LGWarmth, _LGCalm.
// Unlit and untagged: the same pass runs on URP (as SRPDefaultUnlit) and Built-in.
Shader "Quill/Samples/LiquidGlassWallpaper"
{
    Properties { }

    SubShader
    {
        Tags { "Queue" = "Background"  "RenderType" = "Opaque"  "IgnoreProjector" = "True"  "PreviewType" = "Plane" }
        Cull Off  ZWrite Off  ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float _LGExposure;
            float _LGWarmth;
            float _LGCalm;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = float4(v.uv * 2.0 - 1.0, 0.0, 1.0);
                o.pos.y *= _ProjectionParams.x;
            #if defined(UNITY_REVERSED_Z)
                o.pos.z = 0.0;          // far plane, reversed-Z
            #else
                o.pos.z = o.pos.w;      // far plane
            #endif
                o.uv = v.uv;
                return o;
            }

            float Blob(float2 p, float2 c, float r)
            {
                float2 d = p - c;
                return exp(-dot(d, d) / (r * r));
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            float3 Grade(float3 col)
            {
                float lum = dot(col, float3(0.2126, 0.7152, 0.0722));
                col = lerp(col, lum * float3(0.78, 0.84, 1.0) * 0.75, saturate(_LGCalm) * 0.8);
                col *= lerp(float3(1.0, 1.0, 1.0), float3(1.12, 0.93, 0.72), saturate(_LGWarmth));
                col *= _LGExposure > 0.0 ? _LGExposure : 1.0;
                // Colours here are authored display-referred; convert in Linear projects.
            #ifndef UNITY_COLORSPACE_GAMMA
                col = GammaToLinearSpace(max(col, 0.0));
            #endif
                return col;
            }

            float4 frag(v2f i) : SV_Target
            {
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 p = float2((i.uv.x - 0.5) * aspect, i.uv.y - 0.5);
                float t = _Time.y * (0.07 * lerp(1.0, 0.4, saturate(_LGCalm)));

                // Deep base gradient: navy at the top, plum at the bottom.
                float3 col = lerp(float3(0.10, 0.04, 0.17), float3(0.04, 0.06, 0.17), i.uv.y);

                // Drifting colour fields.
                float b0 = Blob(p, float2(sin(t * 1.10) * 0.55 * aspect, cos(t * 0.90) * 0.28), 0.42);
                float b1 = Blob(p, float2(cos(t * 0.70 + 1.0) * 0.50 * aspect, sin(t * 1.30 + 2.0) * 0.30), 0.36);
                float b2 = Blob(p, float2(sin(t * 0.50 + 4.0) * 0.45 * aspect, sin(t * 0.80 + 0.5) * 0.32), 0.50);
                float b3 = Blob(p, float2(cos(t * 1.00 + 3.0) * 0.40 * aspect, cos(t * 0.60 + 5.0) * 0.26), 0.30);
                float b4 = Blob(p, float2(sin(t * 0.90 + 2.5) * 0.60 * aspect, cos(t * 1.10 + 1.5) * 0.34), 0.38);

                float3 glow = b0 * float3(0.95, 0.25, 0.62)    // magenta
                            + b1 * float3(1.00, 0.52, 0.18)    // orange
                            + b2 * float3(0.18, 0.40, 1.00)    // blue
                            + b3 * float3(0.10, 0.85, 0.88)    // cyan
                            + b4 * float3(0.52, 0.28, 1.00);   // violet
                col += (1.0 - exp(-glow * 1.15)) * 0.8;

                // Contour lines of the same field — they bend visibly through the glass.
                float field = (b0 + b1 + b2 + b3 + b4) * 6.0 + p.y * 2.0;
                float fw = max(fwidth(field), 1e-4);
                float fl = frac(field);
                float contour = 1.0 - smoothstep(0.0, 1.4 * fw, min(fl, 1.0 - fl));
                col += contour * 0.07 * (0.4 + 0.6 * saturate(b0 + b1 + b2 + b3 + b4));

                // Vignette + a whisper of grain against banding.
                col *= 1.0 - 0.45 * dot(p, p);
                col += (Hash(i.uv * _ScreenParams.xy + frac(_Time.y) * 61.0) - 0.5) * (2.0 / 255.0);

                return float4(Grade(col), 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
