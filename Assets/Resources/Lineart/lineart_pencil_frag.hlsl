/* SPDX-FileCopyrightText: 2020-2023 Blender Authors
 *
 * SPDX-License-Identifier: GPL-2.0-or-later */

// #include "gpencil_info.hh"
// #include "gpencil_attribs.hlsl"
// #include "Assets/Resources/common/common_shader_util.hlsl"
// #include "draw_grease_pencil_lib.hlsl"
#include "Assets/Resources/common/common.hh"
#include "Assets/Resources/common/fractal_noise.hh"

float stroke_round_cap_mask_pencil(
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
    
    float scatter_dist =0.3;
    uv_end *=1+scatter_dist;
    uv_end += scatter_dist * (SimpleNoise_float(fragPos, 0.1)-0.5)*2;

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

// #include "draw_colormanagement_lib.glsl"
// #include "draw_grease_pencil_lib.glsl"
// #include "gpu_shader_math_vector_lib.glsl"

// float3 gpencil_lighting()
// {
//   float3 light_accum = float3(0.0f);
//   for (int i = 0; i < GPENCIL_LIGHT_BUFFER_LEN; i++) {
//     if (float3(gp_lights[i]._color).x == -1.0f) {
//       break;
//     }
//     float3 L = gp_lights[i]._position - IN.pos;
//     float vis = 1.0f;
//     gpLightType type = gpLightType(floatBitsToUint(gp_lights[i]._type));
//     /* Spot Attenuation. */
//     if (type == GP_LIGHT_TYPE_SPOT) {
//       float3x3 rot_scale = float3x3(gp_lights[i]._right, gp_lights[i]._up, gp_lights[i]._forward);
//       float3 local_L = rot_scale * L;
//       local_L /= abs(local_L.z);
//       float ellipse = inversesqrt(length_squared(local_L));
//       vis *= smoothstep(
//           0.0f, 1.0f, (ellipse - gp_lights[i]._spot_size) / gp_lights[i]._spot_blend);
//       /* Also mask +Z cone. */
//       vis *= step(0.0f, local_L.z);
//     }
//     /* Inverse square decay. Skip for suns. */
//     float L_len_sqr = length_squared(L);
//     if (type < GP_LIGHT_TYPE_SUN) {
//       vis /= L_len_sqr;
//     }
//     else {
//       L = gp_lights[i]._forward;
//       L_len_sqr = 1.0f;
//     }
//     /* Lambertian falloff */
//     if (type != GP_LIGHT_TYPE_AMBIENT) {
//       L /= sqrt(L_len_sqr);
//       vis *= clamp(dot(gp_normal, L), 0.0f, 1.0f);
//     }
//     light_accum += vis * gp_lights[i]._color;
//   }
//   /* Clamp to avoid NaNs. */
//   return clamp(light_accum, 0.0f, 1e10f);
// }

FragOutput frag(Varyings IN)
{
    FragOutput OUT;
    OUT.color = float4(1,1,1,1);

    float4 col;
    // if (flag_test(IN.mat_flag, GP_STROKE_TEXTURE_USE)) {
    //   bool premul = flag_test(IN.mat_flag, GP_STROKE_TEXTURE_PREMUL);
    //   col = texture_read_as_linearrgb(gp_stroke_tx, premul, IN.uv);
    // }
    // else if (flag_test(IN.mat_flag, GP_FILL_TEXTURE_USE)) {
    //   bool use_clip = flag_test(IN.mat_flag, GP_FILL_TEXTURE_CLIP);
    //   float2 uvs = (use_clip) ? clamp(IN.uv, 0.0f, 1.0f) : IN.uv;
    //   bool premul = flag_test(IN.mat_flag, GP_FILL_TEXTURE_PREMUL);
    //   col = texture_read_as_linearrgb(gp_fill_tx, premul, uvs);
    // }
    // else if (flag_test(IN.mat_flag, GP_FILL_GRADIENT_USE))
    // {
    //     bool radial = flag_test(IN.mat_flag, GP_FILL_GRADIENT_RADIAL);
    //     float fac = clamp(radial ? length(IN.uv * 2.0f - 1.0f) : IN.uv.x, 0.0f, 1.0f);
    //     uint matid = IN.mat_flag >> GPENCIl_MATID_SHIFT;
    //     col = lerp(gp_materials[matid].fill_color, gp_materials[matid].fill_mix_color, fac);
    // }
    // else /* SOLID */
    // {
        col = float4(1,1,1,1);
    // }

    /* Composite all other colors on top of texture color.
     * Everything is pre-multiply by `col.a` to have the stencil effect. */
	// if(mat
    OUT.color = col * IN.color_mul + col.a * IN.color_add;
    //OUT.color.a = 1;
    // OUT.color.rgb *= gpencil_lighting();
    OUT.color *= stroke_round_cap_mask_pencil(IN.sspos.xy,
                                            IN.sspos.zw,
                                            IN.positionHCS.xy,
                                            IN.aspect,
                                            IN.thickness.x,
                                            IN.hardness);
    /* To avoid aliasing artifacts, we reduce the opacity of small strokes. */
    OUT.color *= smoothstep(0.0f, 1.0f, IN.thickness.y);
    // OUT.color *= SimpleNoise_float(IN.positionHCS.xy, 0.5);
    if (SimpleNoise_float(IN.positionHCS.xy, 1)<0.2)
    {
        OUT.color.a = 0;
    }
    if (OUT.color.a < 0.001f)
    {
        discard;
    }
    return OUT;

    // float2 fb_size = max(float2(textureSize(gp_scene_depth_tx, 0).xy),
    //                      float2(textureSize(gp_mask_tx, 0).xy));
    // float2 uvs = IN.positionHCS.xy / fb_size;
    // /* Manual depth test */
    // float scene_depth = texture(gp_scene_depth_tx, uvs).r;
    // if (gl_FragCoord.z > scene_depth)
    // {
    //     gpu_discard_fragment();
    //     return;
    // }

    // /* FIXME(fclem): Grrr. This is bad for performance but it's the easiest way to not get
    //  * depth written where the mask obliterate the layer. */
    // float mask = texture(gp_mask_tx, uvs).r;
    // if (mask < 0.001f)
    // {
    //     gpu_discard_fragment();
    //     return;
    // }

    /* We override the fragment depth using the fragment shader to ensure a constant value.
     * This has a cost as the depth test cannot happen early.
     * We could do this in the vertex shader but then perspective interpolation of uvs and
     * fragment clipping gets really complicated. */
    // if (IN.depth >= 0.0f)
    // {
    //     OUT.depth = IN.depth;
    // }
    // else
    // {
    //     OUT.depth = IN.positionHCS.z;
    // }
    return OUT;
}
