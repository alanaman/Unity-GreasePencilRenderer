#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float4 color  : COLOR;
    uint vertexId : SV_VertexID;
    float2 uv     : TEXCOORD0;
    float2 uv1    : TEXCOORD1;
};

struct Varyings
{
    float4 positionHCS : SV_POSITION;
    float3 normal : NORMAL;
    float3 wPosition : WORLD_POSITION;
    float2 uv : TEXCOORD0;
    // float4 color_mul : TEXCOORD0;
    // float4 color_add : TEXCOORD1;
    // float3 pos : TEXCOORD2;
    // //flat
    float2 aspect : ASPECT;
    // float4 sspos : TEXCOORD5;
    // uint mat_flag : TEXCOORD6;
    // float depth : TEXCOORD7;
    // //no perspective
    float2 thickness : RADIUS;
    float hardness : HARDNESS;
    // float index;
    float opacity : OPACITY;
};

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
float4 _BaseMap_ST;
CBUFFER_END