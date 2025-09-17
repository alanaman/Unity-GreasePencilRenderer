#pragma once

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
    float3 wPosition : WORLD_POSITION;
    float2 uv : TEXCOORD0;
    float4 color_mul : COLOR;
    float4 color_add : COLOR1;
    // float3 pos : TEXCOORD2;
    // //flat
    nointerpolation float2 aspect : ASPECT;
    nointerpolation float4 sspos : SSPOS;
    nointerpolation uint mat_flag : MAT_FLAG;
    nointerpolation  float depth : DEPTH;
    // //no perspective
    noperspective float2 thickness : RADIUS;
    noperspective float hardness : HARDNESS;
    // float index;
    float opacity : OPACITY;
};

struct FragOutput
{
    float4 color : SV_Target;
    // float4 revealColor : REVEAL_COLOR;
    // float depth : SV_Depth;
};

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
float4 _BaseMap_ST;
CBUFFER_END