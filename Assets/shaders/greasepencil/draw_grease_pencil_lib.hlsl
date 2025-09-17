#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "common_shader_util.hlsl"
#include "gpencil_info.hh"
#include "gpencil_shader_shared.hh"

float gpencil_stroke_round_cap_mask(
    float2 p1, float2 p2, float2 fragPos, float2 aspect, float thickness, float hardfac)
{
    /* We create our own uv space to avoid issues with triangulation and p12ar
     * interpolation artifacts. */
    float2 p12 = p2.xy - p1.xy;
    float2 pos = fragPos - p1.xy;
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