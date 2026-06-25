// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. All rights reserved.
//
// Quill/Effect/Radial — a soft glowing halo ring. Example ShaderEffect shader.
//
// Convention for Quill ShaderEffect shaders:
//   * author clip-space positions directly and flip Y by _ProjectionParams.x (see vert);
//   * uv runs 0..1 across the item (origin top-left);
//   * the engine always provides _Rect, _ScreenSize, _Time, _Opacity;
//   * any extra Quill property on the ShaderEffect arrives as a uniform of the same name.
//
// Quill properties used here:  color glow;  float intensity;
Shader "Quill/Effect/Radial"
{
    Properties { }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Overlay" "IgnoreProjector" = "True" }
        Cull Off  ZWrite Off  ZTest Always
        Blend SrcAlpha One   // additive glow

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Rect;
            float4 _ScreenSize;
            //float  _Time;
            float  _Opacity;

            float4 glow;        // Quill: property color glow
            float  intensity;   // Quill: property real  intensity

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = float4(v.vertex.x, v.vertex.y * _ProjectionParams.x, 0.0, 1.0);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;       // -1..1
                float r = length(p);

                // Gaussian ring peaking around r = 0.6 (transparent centre so a knob shows through).
                float ring = exp(-pow((r - 0.6) * 4.0, 2.0));
                float pulse = 0.85 + 0.15 * sin(_Time * 3.0);

                float a = saturate(ring * max(intensity, 0.0) * pulse) * _Opacity * glow.a;
                return float4(glow.rgb, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
