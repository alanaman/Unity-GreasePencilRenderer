#pragma once

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "draw_grease_pencil_lib.hlsl"
#include "gpencil_info.hh"
#include "gpencil_attribs.hlsl"

Varyings vert(Attributes IN)
{
    unity_ObjectToWorld = _ObjectToWorld;
    uint vertexId = IN.vertexId;
    
    int stroke_point_id = IN.vertexId >> GP_VERTEX_ID_SHIFT;
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
    
    float4 out_ndc;
    // float4 pos = _Pos[stroke_point_id-1];
    // float4 pos2 = _Pos[stroke_point_id+1];
    // float4 pos3 = _Pos[stroke_point_id+2];
    // int x= IN.vertexId % 4;
    // if (x==0)
    //     OUT.positionHCS = TransformObjectToHClip(pos);
    // if (x==1)
    //     OUT.positionHCS = TransformObjectToHClip(pos1);
    // if (x==2)
    //     OUT.positionHCS = TransformObjectToHClip(pos2);
    // if (x==3)
    //     OUT.positionHCS = TransformObjectToHClip(pos3);
    // if (gpencil_is_stroke_vertex(vertexId))
    // {
    //     // bool is_dot = flag_test(material_flags, GP_STROKE_ALIGNMENT);
    //     // bool is_squares = !flag_test(material_flags, GP_STROKE_DOTS);
    //     // bool is_first = (ma.x == -1);
    //     // bool is_last = (ma3.x == -1);
    //     // bool is_single = is_first && (ma2.x == -1);
    //     // if (is_last)
    //     // IN.positionOS.y += 1;
    // }
    // IN.positionOS += pos1;
    // OUT.positionHCS = TransformObjectToHClip(IN.positionOS);

    // float3 outp = float3(stroke_point_id,0,0);
    float3 pos1 = _Pos[stroke_point_id].pos;
    float3 outp = pos1;
    if (vertexId%4==0)
    {
        outp.y+=1;
        outp.z+=1;
    }
    else if (vertexId%4==1)
    {
        outp.y-=1;
        outp.z+=1;
    }
    else if (vertexId%4==2)
    {
        outp.y+=1;
        outp.z-=1;
    }
    else
    {
        outp.y-=1;
        outp.z-=1;
    }
    OUT.positionHCS = TransformObjectToHClip(outp.xyz);
    // Manually transform from object space to world space using your matrix
    // float4 worldPos = mul(_ObjectToWorld, float4(outp.xyz, 1.0));
    //
    // // Then transform from world space to clip space
    // OUT.positionHCS = mul(UNITY_MATRIX_VP, worldPos);
    return OUT;        
}
