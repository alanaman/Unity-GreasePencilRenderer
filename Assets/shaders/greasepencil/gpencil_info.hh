#pragma once

#include "gpencil_shader_shared.hh"
#include "gpencil_defines.hh"

//TODO: pack the below buffers into fewer buffers
struct GreasePencilStrokeVert
{
    /** Position and radius packed in the same attribute. */
    float3 pos;
    float radius;
    /** Material Index, Stroke Index, Point Index, Packed aspect + hardness + rotation. */
    // mat = -1 if this vert is padding at the start and end of a stroke
    // cyclic = signed_point_id<0;
    int mat, stroke_id, signed_point_id, packed_asp_hard_rot;
    /** UV and opacity packed in the same attribute. */
    float2 uv_fill;
    float u_stroke, opacity;
};

struct GreasePencilColorVert {
    float4 vcol; /* Vertex color */
    float4 fcol; /* Fill color */
};

StructuredBuffer<GreasePencilStrokeVert> _Pos;
uniform float4x4 _ObjectToWorld;
// /** Position and radius packed in the same attribute. */
// // float pos[3], radius;
// StructuredBuffer<float4> _Pos;
//
// /** Material Index, Stroke Index, Point Index, Packed aspect + hardness + rotation. */
// // int32_t mat, stroke_id, point_id, packed_asp_hard_rot;
// StructuredBuffer<int4> _Strokes;
//             
// /** UV and opacity packed in the same attribute. */
// // float uv_fill[2], u_stroke, opacity;
// StructuredBuffer<float4> _UvOpacity;


StructuredBuffer<GreasePencilColorVert> _Color;

// /* Vertex color */
// StructuredBuffer<float4> _Vcol;
// /* Fill color */
// StructuredBuffer<float4> _Fcol;

StructuredBuffer<gpMaterial> gp_materials;

//per object
bool gp_stroke_order3d=true;
int gp_material_offset=0;

//per layer
float gp_vertex_color_opacity = 1;
float4 gp_layer_tint = float4(1, 1, 1, 1);
float gp_layer_opacity = 1;

