Shader "Custom/indexer"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white"
        _AttrTex("Attribute Texture", 2D) = "black"
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float2 uv3 : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float index : TEXCOORD1;
                float index2 : TEXCOORD2;
                float4 vcolor : COLOR2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_AttrTex);
            SAMPLER(sampler_AttrTex);
            StructuredBuffer<float4> _AttrBuffer;

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                int _VertexCount;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.index = IN.uv2.x;
                OUT.index2 = IN.uv3.x;
                // float2 uvAttr = float2((IN.uv2.x + 0.5) / _VertexCount, 0.5);
                // float4 attr = SAMPLE_TEXTURE2D_LOD(_AttrTex, sampler_AttrTex, uvAttr, 0);
                float4 attr = _AttrBuffer[IN.uv3.x];
                // float4 attr;
                // if (IN.uv3.x == 0)
                // {
                //     attr = float4(1,1,1,1);
                // }
                // else
                // {
                //     attr = float4(0,0,0,0);
                // }
                OUT.vcolor = attr;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                // half4 color = IN.index2/_VertexCount;
                half4 color = half4(IN.vcolor.r, IN.vcolor.g, IN.vcolor.b, IN.vcolor.a);
                // half4 color = half4(IN.vcolor.r, 0, 0, IN.vcolor.a);
                return color;
            }
            ENDHLSL
        }
    }
}
