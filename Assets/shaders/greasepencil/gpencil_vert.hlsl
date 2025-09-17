#pragma once

#include "draw_grease_pencil_lib.hlsl"
#include "gpencil_info.hh"
#include "gpencil_attribs.hlsl"

float gpencil_stroke_thickness_modulate(float thickness, float4 ndc_pos)
{
    // sqrt(1/3)
    const float M_SQRT1_3 = 0.57735026919;

    // Object scale: take upper-left 3x3 of unity_ObjectToWorld
    float3x3 obj3x3 = (float3x3)unity_ObjectToWorld;

    // Apply isotropic vector with length = thickness
    float3 scaled = mul(obj3x3, float3(thickness * M_SQRT1_3, thickness * M_SQRT1_3, thickness * M_SQRT1_3));
    thickness = length(scaled);

    // Projection scaling: UNITY_MATRIX_P[1][1] is the same as drw_view().winmat[1][1]
    thickness *= UNITY_MATRIX_P._m11 * _ScreenParams.y;

    return thickness;
}

void gpencil_color_output(out float4 color_mul, out float4 color_add, float4 stroke_col, float4 vert_col, float vert_strength, float mix_tex)
{
    /* Mix stroke with other colors. */
    float4 mixed_col = stroke_col;
    mixed_col.rgb = lerp(mixed_col.rgb, vert_col.rgb, vert_col.a * gp_vertex_color_opacity);
    mixed_col.rgb = lerp(mixed_col.rgb, gp_layer_tint.rgb, gp_layer_tint.a);
    mixed_col.a *= vert_strength * gp_layer_opacity;
    /**
    * This is what the fragment shader looks like.
    * out = col * gp_interp.color_mul + col.a * gp_interp.color_add.
    * gp_interp.color_mul is how much of the texture color to keep.
    * gp_interp.color_add is how much of the mixed color to add.
    * Note that we never add alpha. This is to keep the texture act as a stencil.
    * We do however, modulate the alpha (reduce it).
    */
    /* We add the mixed color. This is 100% mix (no texture visible). */
    color_mul = float4(mixed_col.aaa, mixed_col.a);
    color_add = float4(mixed_col.rgb * mixed_col.a, 0.0f);
    /* Then we blend according to the texture mix factor.
    * Note that we keep the alpha modulation. */
    color_mul.rgb *= mix_tex;
    color_add.rgb *= 1.0f - mix_tex;
}

Varyings vert(Attributes IN)
{
    Varyings OUT;
    float vert_strength;
    float4 vert_color;
    float3 vert_N;

    int4 ma1 = _Strokes[gpencil_stroke_point_id(IN.vertexId)];
    PointData point_data1 = decode_ma(ma1);
    gpMaterial gp_mat = gp_materials[point_data1.mat + gp_material_offset];
    gpMaterialFlag gp_flag = gpMaterialFlag(asuint(gp_mat.flag));

    OUT.positionHCS = gpencil_vertex(
        IN.vertexId,
        _ScreenParams,
        gp_flag,
        gp_mat.alignment_rot,
        OUT.pos,
        vert_N,
        vert_color,
        vert_strength,
        OUT.uv,
        OUT.sspos,
        OUT.aspect,
        OUT.thickness,
        OUT.hardness);

    if (gpencil_is_stroke_vertex(IN.vertexId))
    {
        if (!flag_test(gp_flag, GP_STROKE_ALIGNMENT)) {
            OUT.uv.x *= gp_mat.stroke_u_scale;
        }

        /* Special case: We don't use vertex color if material Holdout. */
        if (flag_test(gp_flag, GP_STROKE_HOLDOUT)) {
            vert_color = float4(0,0,0,0);
        }

        gpencil_color_output(OUT.color_mul, OUT.color_add, gp_mat.stroke_color, vert_color, vert_strength, gp_mat.stroke_texture_mix);

        OUT.mat_flag = gp_flag & ~GP_FILL_FLAGS;

        if (gp_stroke_order3d) {
          /* Use the fragment depth (see fragment shader). */
            OUT.depth = -1.0f;
        }
        else if (flag_test(gp_flag, GP_STROKE_OVERLAP)) {
          /* Use the index of the point as depth.
           * This means the stroke can overlap itself. */
            OUT.depth = (point_data1.point_id + gp_stroke_index_offset + 2.0f) * 0.0000002f;
        }
        else {
            /* Use the index of first point of the stroke as depth.
            * We render using a greater depth test this means the stroke
            * cannot overlap itself.
            * We offset by one so that the fill can be overlapped by its stroke.
            * The offset is ok since we pad the strokes data because of adjacency infos. */
            OUT.depth = (point_data1.stroke_id + gp_stroke_index_offset + 2.0f) * 0.0000002f;
        }
    }
    else {
        int stroke_point_id = gpencil_stroke_point_id(IN.vertexId);
        float4 uv1 = _UvOpacity[stroke_point_id];
        float4 fcol1 = _Fcol[stroke_point_id];
        float4 fill_col = gp_mat.fill_color;

        /* Special case: We don't modulate alpha in gradient mode. */
        if (flag_test(gp_flag, GP_FILL_GRADIENT_USE)) {
            fill_col.a = 1.0f;
        }

        /* Decode fill opacity. */
        float4 fcol_decode = float4(fcol1.rgb, floor(fcol1.a / 10.0f));
        float fill_opacity = fcol1.a - (fcol_decode.a * 10);
        fcol_decode.a /= 10000.0f;

        /* Special case: We don't use vertex color if material Holdout. */
        if (flag_test(gp_flag, GP_FILL_HOLDOUT)) {
            fcol_decode = float4(0,0,0,0);
        }

        /* Apply opacity. */
        fill_col.a *= fill_opacity;
        /* If factor is > 1 force opacity. */
        if (fill_opacity > 1.0f) {
            fill_col.a += fill_opacity - 1.0f;
        }

        fill_col.a = clamp(fill_col.a, 0.0f, 1.0f);

        gpencil_color_output(OUT.color_mul, OUT.color_add, fill_col, fcol_decode, 1.0f, gp_mat.fill_texture_mix);

        OUT.mat_flag = gp_flag & GP_FILL_FLAGS;
        OUT.mat_flag |= uint(point_data1.mat + gp_material_offset) << GPENCIl_MATID_SHIFT;

        float2x2 rotMat = float2x2(gp_mat.fill_uv_rot_scale.xy, gp_mat.fill_uv_rot_scale.zw);
        OUT.uv = mul(rotMat, uv1.xy) + gp_mat.fill_uv_offset;

        if (gp_stroke_order3d) {
            /* Use the fragment depth (see fragment shader). */
            OUT.depth = -1.0f;
        }
        else {
            /* Use the index of first point of the stroke as depth. */
            OUT.depth = (point_data1.stroke_id + gp_stroke_index_offset + 1.0f) * 0.0000002f;
        }
    }
    return OUT;        
}
