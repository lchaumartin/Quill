// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
// Quill/Samples/FrostOrb — glossy, self-lit orbs for the Frost sample backdrop.
// Fake key light + wrap shading between two colours, a fresnel rim and a tight specular spot:
// crisp silhouettes and highlights for the glass to refract. No scene lights needed.
//
// Colours come per renderer through a MaterialPropertyBlock (_ColorA lit, _ColorB shadow), set
// with SetVector so they stay display-referred like the rest of the maths (see Grade()).
// Globals shared with the wallpaper: _FrostExposure (0 = default 1), _FrostWarmth, _FrostCalm.
// Unlit and untagged: the same pass runs on URP (as SRPDefaultUnlit) and Built-in.
Shader "Quill/Samples/FrostOrb"
{
    Properties
    {
        _ColorA ("Lit Colour", Color) = (1.0, 0.5, 0.4, 1)
        _ColorB ("Shadow Colour", Color) = (0.4, 0.1, 0.5, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Geometry"  "RenderType" = "Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _ColorA;
            float4 _ColorB;
            float _FrostExposure;
            float _FrostWarmth;
            float _FrostCalm;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f
            {
                float4 pos    : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 view   : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.view = WorldSpaceViewDir(v.vertex);
                return o;
            }

            float3 Grade(float3 col)
            {
                float lum = dot(col, float3(0.2126, 0.7152, 0.0722));
                col = lerp(col, lum * float3(0.78, 0.84, 1.0) * 0.75, saturate(_FrostCalm) * 0.8);
                col *= lerp(float3(1.0, 1.0, 1.0), float3(1.12, 0.93, 0.72), saturate(_FrostWarmth));
                col *= _FrostExposure > 0.0 ? _FrostExposure : 1.0;
                // Colours here are authored display-referred; convert in Linear projects.
            #ifndef UNITY_COLORSPACE_GAMMA
                col = GammaToLinearSpace(max(col, 0.0));
            #endif
                return col;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 v = normalize(i.view);
                float3 l = normalize(float3(-0.45, 0.75, -0.5));     // key light: top-left, toward camera

                float wrap = saturate(dot(n, l) * 0.5 + 0.5);
                float3 col = lerp(_ColorB.rgb, _ColorA.rgb, smoothstep(0.08, 0.95, wrap));

                float fres = pow(1.0 - saturate(dot(n, v)), 3.0);
                col += fres * 0.45 * _ColorA.rgb;                    // coloured rim

                float3 h = normalize(l + v);
                col += pow(saturate(dot(n, h)), 90.0) * 0.9;         // glossy highlight

                return float4(Grade(col), 1.0);
            }
            ENDCG
        }

        // Depth for URP's depth prepass / depth priming and the camera depth texture.
        Pass
        {
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On  ColorMask 0

            CGPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #include "UnityCG.cginc"

            float4 vertDepth(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            half4 fragDepth() : SV_Target { return 0; }
            ENDCG
        }
    }

    Fallback Off
}
