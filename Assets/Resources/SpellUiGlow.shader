// 주문 연출·배경용 UI 셰이더. 정점 색을 그대로 쓰되 uv0.x(0~1)로 가산 혼합 정도를 정점마다 고른다.
// 0이면 보통의 알파 혼합, 1이면 빛처럼 더해져 어두운 바탕 위에서 번지는 빛으로 보인다.
Shader "Spell/UI Glow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float4 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float additive : TEXCOORD0;
            };

            // 캔버스가 정점 색을 감마 값으로 넘기는 설정이면 1이다(UI 기본 셰이더와 같은 규칙).
            int _UIVertexColorAlwaysGammaSpace;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                float4 color = v.color;
                if (_UIVertexColorAlwaysGammaSpace)
                {
                    if (!IsGammaSpace()) color.rgb = GammaToLinearSpace(color.rgb);
                }
                o.color = color;
                o.additive = saturate(v.texcoord.x);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = i.color;
                c.rgb *= c.a;
                c.a *= 1 - i.additive;
                return c;
            }
            ENDCG
        }
    }
}
