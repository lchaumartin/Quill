// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
// Quill/Text
// Draws glyph quads whose vertices are authored directly in clip space (so no camera matrices are
// needed). Samples the dynamic-font atlas for coverage and tints with the per-vertex colour.
Shader "Quill/Text"
{
    Properties { _MainTex ("Font Atlas", 2D) = "white" {} }

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
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };
            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                // Already clip-space; _ProjectionParams.x (-1 on flipped targets) fixes Y.
                o.pos = float4(v.vertex.x, v.vertex.y * _ProjectionParams.x, 0.0, 1.0);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // Dynamic-font atlas stores coverage in the alpha channel.
                float coverage = tex2D(_MainTex, i.uv).a;
                return float4(i.color.rgb, i.color.a * coverage);
            }
            ENDCG
        }
    }
    Fallback Off
}
