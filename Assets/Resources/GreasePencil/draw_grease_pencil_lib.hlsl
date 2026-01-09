#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "common_shader_util.hlsl"
#include "gpencil_info.hh"
#include "gpencil_shader_shared.hh"

#define MITER_LIMIT_TYPE_BEVEL -1.0f
#define MITER_LIMIT_TYPE_ROUND -2.0f

float2 gpencil_decode_aspect(int packed_data)
{
    float asp = float(uint(packed_data) & 0x1FFu) * (1.0f / 255.0f);
    return (asp > 1.0f) ? float2(1.0f, (asp - 1.0f)) : float2(asp, 1.0f);
}

float gpencil_decode_uvrot(int packed_data)
{
    uint udata = uint(packed_data);
    float uvrot = 1e-8f + float((udata & 0x1FE00u) >> 9u) * (1.0f / 255.0f);
    return ((udata & 0x20000u) != 0u) ? -uvrot : uvrot;
}

float gpencil_decode_hardness(int packed_data)
{
    return float((uint(packed_data) & 0x3FC0000u) >> 18u) * (1.0f / 255.0f);
}



float gpencil_decode_miter_limit(int packed_data)
{
    uint miter_data = (uint(packed_data) & 0xFC000000u) >> 26u;
    if (miter_data == GP_CORNER_TYPE_ROUND_BITS) {
        return MITER_LIMIT_TYPE_ROUND;
    }
    else if (miter_data == GP_CORNER_TYPE_BEVEL_BITS) {
        return MITER_LIMIT_TYPE_BEVEL;
    }
    float miter_angle = float(miter_data) * (PI / GP_CORNER_TYPE_MITER_NUMBER);
    return cos(miter_angle);
}


float gpencil_stroke_round_cap_mask(
    float2 p1, float2 p2, float2 fragPos, float2 aspect, float thickness, float hardfac)
{
    /* We create our own uv space to avoid issues with triangulation and p12ar
     * interpolation artifacts. */
    
    //unity has flipped y axis
    if (_ProjectionParams.x == -1)
    {
        p1.y = _ScreenParams.y - p1.y;
        p2.y = _ScreenParams.y - p2.y;
    }
    
    float2 p12 = p2.xy - p1.xy;
    float2 pos = float2(fragPos.x, fragPos.y) - p1.xy;
    float pos_len = length(pos);
    float p12_len = length(p12);
    float half_p12_len = p12_len * 0.5f;
    /* Normalize */
    p12 = (p12_len > 0.0f) ? (p12 / p12_len) : float2(1.0f, 0.0f);
    /* Create a uv space that englobe the whole segment into a capsule. */
    float2 uv_end;
    uv_end.x = max(abs(dot(p12, pos) - half_p12_len) - half_p12_len, 0.0f);
    uv_end.y = dot(float2(-p12.y, p12.x), pos);
    /* Divide by stroke radius. */
    uv_end /= thickness;
    uv_end *= aspect;

    float dist = clamp(1.0f - length(uv_end) * 2.0f, 0.0f, 1.0f);
    if (hardfac > 0.999f) {
        return step(1e-8f, dist);
    }
    else {
        /* Modulate the falloff profile */
        float hardness = 1.0f - hardfac;
        dist = pow(dist, lerp(0.01f, 10.0f, hardness));
        return smoothstep(0.0f, 1.0f, dist);
    }
}