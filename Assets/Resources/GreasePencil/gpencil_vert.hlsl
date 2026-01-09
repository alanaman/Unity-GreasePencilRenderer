#pragma once

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "gpencil_info.hh"
#include "gpencil_attribs.hlsl"
#include "common_shader_util.hlsl"

bool g_pencil_is_stroke_vertex(uint vertexId)
{
    return flag_test(vertexId, (uint)GP_IS_STROKE_VERTEX_BIT);
}

inline bool is_cyclic(GreasePencilStrokeVert vert)
{
    return vert.signed_point_id < 0;
}

float2 g_pencil_project_to_screenspace(float4 v)
{
    return ((v.xy / v.w) * 0.5f + 0.5f) * _ScreenParams.xy;
}

float g_pencil_decode_hardness(int packed_data)
{
    return float((uint(packed_data) & 0x3FC0000u) >> 18u) * (1.0f / 255.0f);
}

float g_pencil_stroke_radius_modulate(float radius)
{
    float3x3 obj3x3 = (float3x3)unity_ObjectToWorld;
    float3 scaled = mul(obj3x3, float3(radius * 0.57735, radius * 0.57735, radius * 0.57735));
    radius = length(scaled);

    float screen_radius = radius * -(UNITY_MATRIX_P[1][1]) * _ScreenParams.y;
    
    return screen_radius;
}

void g_pencil_color_output(out float4 color_mul, out float4 color_add, float4 stroke_col, float4 vert_col, float opacity, float mix_tex)
{
    /* Mix stroke with other colors. */
    float4 mixed_col = stroke_col;
    mixed_col.rgb = lerp(mixed_col.rgb, vert_col.rgb, vert_col.a * gp_vertex_color_opacity);
    mixed_col.rgb = lerp(mixed_col.rgb, gp_layer_tint.rgb, gp_layer_tint.a);
    mixed_col.a *= opacity * gp_layer_opacity;
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
    unity_ObjectToWorld = _ObjectToWorld;
    uint vertexId = IN.vertexId;
    
    int stroke_point_id = (vertexId & ~GP_IS_STROKE_VERTEX_BIT) >> GP_VERTEX_ID_SHIFT;

    GreasePencilStrokeVert p0 = _Pos[stroke_point_id - 1];
    GreasePencilStrokeVert p1 = _Pos[stroke_point_id + 0];
    GreasePencilStrokeVert p2 = _Pos[stroke_point_id + 1];
    GreasePencilStrokeVert p3 = _Pos[stroke_point_id + 2];
    
    // if p2.mat == -1 && p0.mat == -1 then it's a dot, we don't discard
    if (p1.mat == -1 || (p2.mat == -1 && p0.mat != -1))
    {
        /* Degenerate point, output nothing. */
        Varyings OUT;
        OUT.positionHCS = float4(0.0f, 0.0f, -3e36f, 0.0f);
        return OUT;
    }
    
    /* Attribute Loading. */
    float3 pos0 = p0.pos;
    float3 pos1 = p1.pos;
    float3 pos2 = p2.pos;
    float3 pos3 = p3.pos;
    
    float4 col1 = _Color[stroke_point_id + 0].vcol;
    float4 col2 = _Color[stroke_point_id + 1].vcol;
    float4 fcol1 = _Color[stroke_point_id + 0].fcol;
    
    gpMaterial gp_mat = gp_materials[p1.mat + gp_material_offset];
    gpMaterialFlag material_flags = gpMaterialFlag(asuint(gp_mat.flag));
    // int4 ma = _Strokes[stroke_point_id-1];
    // int4 ma1 = _Strokes[stroke_point_id];
    // int4 ma2 = _Strokes[stroke_point_id+1];
    // int4 ma3 = _Strokes[stroke_point_id+2];
    //
    // float4 uv1 = _UvOpacity[stroke_point_id];
    // float4 uv2 = _UvOpacity[stroke_point_id+1];
    //
    // float4 col1 = _Vcol[stroke_point_id];
    // float4 col2 = _Vcol[stroke_point_id+1];
    // float4 fcol1 = _Fcol[stroke_point_id];
    // int4 ma1 = _Strokes[stroke_point_id];
    // PointData point_data1 = decode_ma(ma1);
    // gpMaterial gp_mat = gp_materials[point_data1.mat + gp_material_offset];
    // gpMaterialFlag gp_flag = gpMaterialFlag(asuint(gp_mat.flag));
    // gpMaterialFlag material_flags = gp_mat.flag;
    
    Varyings OUT;
    // if (stroke_point_id == 1)
    // {
    //     IN.positionOS.y += 1;
    // }

    // int4 ma = _Strokes[stroke_point_id-1];
    // int4 ma2 = _Strokes[stroke_point_id+1];
    // int4 ma3 = _Strokes[stroke_point_id+2];
    
    float3 outp = pos1;
    if (g_pencil_is_stroke_vertex(vertexId))
    {
        bool is_dot = flag_test(material_flags, GP_STROKE_ALIGNMENT);
        bool is_squares = !flag_test(material_flags, GP_STROKE_DOTS);

        bool is_first = (p0.mat == -1);
        bool is_last = (p3.mat == -1);
        bool is_single = is_first && (p2.mat == -1);

        /* Join the first and last point if the curve is cyclical. */
        if (is_cyclic(p1) && !is_single) {
            if (is_first) {
                /* The first point will have the index of the last point. */
                int last_stroke_id = p0.stroke_id;
                p0 = _Pos[last_stroke_id-2];
                pos0 = p0.pos;
            }

            if (is_last) {
                int first_stroke_id = p1.stroke_id;
                p3 = _Pos[first_stroke_id+2];
                pos3 = p3.pos;
            }
        }

        /* Special Case. Stroke with single vert are rendered as dots. Do not discard them. */
        if (!is_dot && is_single) {
            is_dot = true;
            is_squares = false;
        }

        /* Endpoints, we discard the vertices. */
        if (!is_dot && p2.mat == -1) {
            /* We set the vertex at the camera origin to generate 0 fragments. */
            OUT.positionHCS = float4(0.0f, 0.0f, -3e36f, 0.0f);
            return OUT;
        }

        //quad positioning
        float x = float(vertexId & 1) * 2.0f - 1.0f; /* [-1..1] */
        float y = float(vertexId & 2) - 1.0f;        /* [-1..1] */
        
        bool is_on_p1 = is_dot || (x == -1.0f);
        
        

        float3 wpos_adj = TransformObjectToWorld((is_on_p1) ? pos0.xyz : pos3.xyz);
        float3 wpos1 = TransformObjectToWorld(pos1.xyz);
        float3 wpos2 = TransformObjectToWorld(pos2.xyz);

        // float3 tangent;
        // if (is_dot) {
        //     /* Shade as facing billboards. */
        //     tangent = unity_CameraToWorld[0].xyz;
        // }
        // else if (is_on_p1 && p0.mat != -1) {
        //     tangent = wpos1 - wpos_adj;
        // }
        // else {
        //     tangent = wpos2 - wpos1;
        // }
        // tangent = safe_normalize(tangent);

        // //TODO remove
        // wpos1.y += y * 0.1;
        // wpos2.y += y * 0.1;
        
        float4 ndc_adj = TransformWorldToHClip(wpos_adj);
        float4 ndc1 = TransformWorldToHClip(wpos1.xyz);
        float4 ndc2 = TransformWorldToHClip(wpos2.xyz);
        
        OUT.positionHCS = (is_on_p1) ? ndc1 : ndc2;
        OUT.wPosition = (is_on_p1) ? wpos1 : wpos2;
        OUT.opacity = abs((is_on_p1) ? p1.opacity : p2.opacity);
        

        float2 ss_adj = g_pencil_project_to_screenspace(ndc_adj);
        float2 ss1 = g_pencil_project_to_screenspace(ndc1);
        float2 ss2 = g_pencil_project_to_screenspace(ndc2);
        
        /* Screen-space Lines tangents. */
        float edge_len;
        float2 edge_dir = safe_normalize_and_get_length(ss2 - ss1, edge_len);
        float2 edge_adj_dir = safe_normalize((is_on_p1) ? (ss1 - ss_adj) : (ss_adj - ss2));
        
        float radius = abs((is_on_p1) ? p1.radius : p2.radius);
        radius = g_pencil_stroke_radius_modulate(radius);
        /* The radius attribute can have negative values. Make sure that it's not negative by clamping
         * to 0. */
        float clamped_radius = max(0.0f, radius);
        
        OUT.uv = float2(x, y) * 0.5f + 0.5f;

        //TODO: uncomment
        // OUT.hardness = g_pencil_decode_hardness(is_on_p1 ? p1.packed_asp_hard_rot : p2.packed_asp_hard_rot);
        OUT.hardness = 1;
        
        //TODO dot
        bool is_stroke_start = (p0.mat == -1 && x == -1);
        bool is_stroke_end = (p3.mat == -1 && x == 1);
        
        /* Mitter tangent vector. */
        float2 miter_tan = safe_normalize(edge_adj_dir + edge_dir);
        float miter_dot = dot(miter_tan, edge_adj_dir);
        /* Break corners after a certain angle to avoid really thick corners. */
        const float miter_limit = 0.5f; /* cos(60 degrees) */
        bool miter_break = (miter_dot < miter_limit);
        miter_tan = (miter_break || is_stroke_start || is_stroke_end) ? edge_dir :
                                                                        (miter_tan / miter_dot);
        /* Rotate 90 degrees counter-clockwise. */
        float2 miter = float2(-miter_tan.y, miter_tan.x);
        
        OUT.sspos.xy = ss1;
        OUT.sspos.zw = ss2;
        OUT.thickness.x = clamped_radius / OUT.positionHCS.w;
        OUT.thickness.y = radius / OUT.positionHCS.w;
        OUT.aspect = float2(1, 1);
        
        float2 screen_ofs = miter * y;
        
        /* Reminder: we packed the cap flag into the sign of strength and thickness sign. */
        if ((is_stroke_start && p1.opacity > 0.0f) || (is_stroke_end && p1.radius > 0.0f) ||
            (miter_break && !is_stroke_start && !is_stroke_end))
        {
            screen_ofs += edge_dir * x;
        }
        // screen_ofs = float2(0, y);
        float2 clip_space_per_pixel = float2(1.0 / _ScreenParams.x, 1.0 / _ScreenParams.y);
        OUT.positionHCS.xy += screen_ofs * clip_space_per_pixel * clamped_radius;
        // OUT.positionHCS.xy += screen_ofs * _ScreenParams.zw * 0.1;
        
        OUT.uv.x = (is_on_p1) ? p1.u_stroke : p2.u_stroke;

        //end stroke


        g_pencil_color_output(OUT.color_mul, OUT.color_add, gp_mat.stroke_color, is_on_p1? col1 : col2, is_on_p1? p1.opacity : p2.opacity, gp_mat.stroke_texture_mix);
        
        OUT.mat_flag = asuint(material_flags) & ~GP_FILL_FLAGS;

        if (gp_stroke_order3d) {
            /* Use the fragment depth (see fragment shader). */
            OUT.depth = -1.0f;
        }
        else if (flag_test(material_flags, GP_STROKE_OVERLAP)) {
            /* Use the index of the point as depth.
             * This means the stroke can overlap itself. */
            OUT.depth = (abs(p1.signed_point_id) + 2.0f) * 0.0000002f;
        }
        else {
            /* Use the index of first point of the stroke as depth.
            * We render using a greater depth test this means the stroke
            * cannot overlap itself.
            * We offset by one so that the fill can be overlapped by its stroke.
            * The offset is ok since we pad the strokes data because of adjacency infos. */
            OUT.depth = (abs(p1.signed_point_id) + 2.0f) * 0.0000002f;
        }
        // out_color = (use_curr) ? col1 : col2;
    }
    else
    {
        
        OUT.mat_flag = asuint(material_flags) & GP_FILL_FLAGS;
        OUT.mat_flag |= uint(p1.mat + gp_material_offset) << GPENCIl_MATID_SHIFT;
    }
    // Manually transform from object space to world space using your matrix
    // float4 worldPos = mul(_ObjectToWorld, float4(outp.xyz, 1.0));
    //
    // // Then transform from world space to clip space
    // OUT.positionHCS = mul(UNITY_MATRIX_VP, worldPos);

    
    return OUT;        
}
