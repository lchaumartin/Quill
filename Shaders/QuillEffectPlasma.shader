// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. All rights reserved.
//
// Quill/Effect/Plasma — an animated plasma field. Example ShaderEffect shader (uses _Time).
// Quill properties used here:  float speed;  float scale.
Shader "Quill/Effect/Plasma"
{
    Properties { }
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

            float4 _Rect;
            float4 _ScreenSize;
            //float  _Time;
            float  _Opacity;

            float speed;   // Quill: property real speed   (defaults to 0 if unset)
            float scale;   // Quill: property real scale

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
                float t = _Time * max(speed, 0.0001);
                float s = max(scale, 0.0001);
                float2 uv = i.uv * s * 10.0;

                float v = sin(uv.x + t)
                        + sin(uv.y + t * 1.3)
                        + sin((uv.x + uv.y) * 0.7 + t * 0.7)
                        + sin(length(i.uv * 2.0 - 1.0) * 8.0 - t * 1.6);

                float3 col = 0.5 + 0.5 * cos(v + float3(0.0, 2.0, 4.0));
                return float4(col, _Opacity);
            }
            ENDCG
        }
    }
    Fallback Off
}
