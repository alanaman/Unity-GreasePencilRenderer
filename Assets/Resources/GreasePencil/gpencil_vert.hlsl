#pragma once

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "gpencil_info.hh"
#include "gpencil_attribs.hlsl"
#include "Assets/Resources/common/common_shader_util.hlsl"
#include "Assets/Resources/GreasePencil/draw_grease_pencil_lib.hlsl"

Varyings vert(Attributes IN)
{
    Varyings OUT;
    
    unity_ObjectToWorld = _ObjectToWorld;
    uint vertexId = IN.vertexId;
    
    int stroke_point_id = (vertexId & ~GP_IS_STROKE_VERTEX_BIT) >> GP_VERTEX_ID_SHIFT;

    /*
     *      10------11
     *      |\ \   |
     *      | \ \  |
     *      |  \ \ |
     *      |   \ \|
     *      00-----01
     */
    if (vertexId & 1)
    {
        stroke_point_id++;
    }
    
    GreasePencilStrokeVert p0;
    GreasePencilStrokeVert p1;
    GreasePencilStrokeVert p2;

    p0 = _Pos[stroke_point_id - 1];
    p1 = _Pos[stroke_point_id];
    p2 = _Pos[stroke_point_id + 1];
    
    // if p2.mat == -1 && p0.mat == -1 then it's a dot, we don't discard
    // if (p1.mat == -1 || !(p2.mat == -1 && p0.mat == -1))
    if (p1.mat == -1 || stroke_point_id==-1)
    {
        /* Degenerate point, output nothing. */
        Varyings OUT;
        OUT.positionHCS = float4(0.0f, 0.0f, -3e36f, 0.0f);
        return OUT;
    }
    
    float x = float(vertexId & 1) * 2.0f - 1.0f; /* [-1..1] */
    float y = float(vertexId & 2) - 1.0f;        /* [-1..1] */
    
    bool is_first = (p0.mat == -1);
    bool is_last = (p2.mat == -1);
    bool is_single = is_first && is_last;
    if (is_cyclic(p1) && !is_single) {
        if (is_first) {
            /* The first point will have the index of the last point. */
            int last_stroke_id = p0.stroke_id;
            p0 = _Pos[last_stroke_id-2];
            is_first = false;
        }
    
        if (is_last) {
            int first_stroke_id = p1.stroke_id;
            p2 = _Pos[first_stroke_id+2];
            is_last = false;
        }
    }
    
    float3 pos0 = p0.pos;
    float3 pos1 = p1.pos;
    float3 pos2 = p2.pos;
    float2 ss0 = p0.ss_pos;
    float2 ss1 = p1.ss_pos;
    float2 ss2 = p2.ss_pos;

    float3 wpos0 = pos0;
    float3 wpos1 = pos1;
    float3 wpos2 = pos2;

    float4 ndc0 = TransformWorldToHClip(wpos0.xyz);
    float4 ndc1 = TransformWorldToHClip(wpos1.xyz);
    float4 ndc2 = TransformWorldToHClip(wpos2.xyz);
    
    OUT.positionHCS = ndc1;
    OUT.wPosition = wpos1;
    OUT.opacity = abs(p1.opacity);
    
    //TODO: uncomment
    // OUT.hardness = g_pencil_decode_hardness(is_on_p1 ? p1.packed_asp_hard_rot : p2.packed_asp_hard_rot);
    OUT.hardness = 1;
    
    float radius = g_pencil_stroke_radius_modulate(p1.radius);
    /* The radius attribute can have negative values. Make sure that it's not negative by clamping to 0. */
    float clamped_radius = max(0.0f, radius);
    
    OUT.thickness.x = clamped_radius / OUT.positionHCS.w;
    OUT.thickness.y = radius / OUT.positionHCS.w;
    OUT.aspect = float2(1, 1);
    
    /* Screen-space Lines tangents. */
    float2 edge01_dir;
    float2 edge12_dir;
    if (is_single)
    {
        edge01_dir = float2(1, 0);
        edge12_dir = float2(1, 0);
    }
    else if (is_first)
    {
        edge12_dir = safe_normalize(ss2 - ss1);
        edge01_dir = edge12_dir;
    }
    else if (is_last)
    {
        edge01_dir = safe_normalize(ss1 - ss0);
        edge12_dir = edge01_dir;
    }
    else
    {
        edge01_dir = safe_normalize(ss1 - ss0);
        edge12_dir = safe_normalize(ss2 - ss1);
    }
    
    // edge01_dir = safe_normalize(ss1 - ss0);
    // edge12_dir = safe_normalize(ss2 - ss1);
    
    float2 miter;
    // float2 miter2;
    float2 miter_tan = safe_normalize(edge01_dir + edge12_dir);
    float miter_dot = dot(miter_tan, edge01_dir);
    /* Rotate 90 degrees counter-clockwise. */
    miter = float2(-miter_tan.y, miter_tan.x);
    /* Break corners after a certain angle to avoid really thick corners. */
    const float miter_limit = 0.5f; /* cos(60 degrees) */
    bool miter_break = (miter_dot < miter_limit);
    miter = miter / miter_dot;
    
    float2 screen_ofs;
    // bool is_stroke_start = (p0.mat == -1 && x == -1);
    // bool is_stroke_end = (p2.mat == -1 && x == 1);    
    if (!miter_break)
    {
        screen_ofs = miter * y;
    }
    else
    {
        if (x==1)
        {
            screen_ofs = float2(-edge01_dir.y, edge01_dir.x) * y;
            screen_ofs += edge01_dir;
        }
        else
        {
            screen_ofs = float2(-edge12_dir.y, edge12_dir.x) * y;
            screen_ofs -= edge12_dir;
        }
    }
    
    float2 clip_space_per_pixel = float2(1.0 / _ScreenParams.x, 1.0 / _ScreenParams.y);
    OUT.positionHCS.xy += screen_ofs * clip_space_per_pixel * clamped_radius;
    OUT.sspos.xy = ss1;
    if (x==1)
    {
        OUT.sspos.zw = ss0;
    }
    else
    {
        OUT.sspos.zw = ss2;
    }
    
    OUT.uv.x = p1.u_stroke;
    OUT.uv.y = y * 0.5f + 0.5f;


    gpMaterial gp_mat = gp_materials[p1.mat + gp_material_offset];
    gpMaterialFlag material_flags = gpMaterialFlag(asuint(gp_mat.flag));
    float4 col1 = _Color[stroke_point_id + 0].vcol;
    g_pencil_color_output(OUT.color_mul, OUT.color_add, gp_mat.stroke_color, col1, p1.opacity, gp_mat.stroke_texture_mix);
    
    return OUT;


    // OUT.mat_flag = asuint(material_flags) & ~GP_FILL_FLAGS;
    //
    // if (gp_stroke_order3d) {
    //     /* Use the fragment depth (see fragment shader). */
    //     OUT.depth = -1.0f;
    // }
    // else if (flag_test(material_flags, GP_STROKE_OVERLAP)) {
    //     /* Use the index of the point as depth.
    //      * This means the stroke can overlap itself. */
    //     OUT.depth = (abs(p1.signed_point_id) + 2.0f) * 0.0000002f;
    // }
    // else {
    //     /* Use the index of first point of the stroke as depth.
    //     * We render using a greater depth test this means the stroke
    //     * cannot overlap itself.
    //     * We offset by one so that the fill can be overlapped by its stroke.
    //     * The offset is ok since we pad the strokes data because of adjacency infos. */
    //     OUT.depth = (abs(p1.signed_point_id) + 2.0f) * 0.0000002f;
    // }
    // // out_color = (use_curr) ? col1 : col2;

    
    return OUT;        
}
