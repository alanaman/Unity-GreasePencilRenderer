// #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
// #include "common_shader_util.hlsl"
// #include "gpencil_info.hh"
// #include "gpencil_shader_shared.hh"
//
// #pragma once
//
// #ifdef GPU_FRAGMENT_SHADER
// float gpencil_stroke_round_cap_mask(
//     float2 p1, float2 p2, float2 aspect, float thickness, float hardfac)
// {
//   /* We create our own uv space to avoid issues with triangulation and linear
//    * interpolation artifacts. */
//   float2 line = p2.xy - p1.xy;
//   float2 pos = gl_FragCoord.xy - p1.xy;
//   float line_len = length(line);
//   float half_line_len = line_len * 0.5f;
//   /* Normalize */
//   line = (line_len > 0.0f) ? (line / line_len) : float2(1.0f, 0.0f);
//   /* Create a uv space that englobe the whole segment into a capsule. */
//   float2 uv_end;
//   uv_end.x = max(abs(dot(line, pos) - half_line_len) - half_line_len, 0.0f);
//   uv_end.y = dot(float2(-line.y, line.x), pos);
//   /* Divide by stroke radius. */
//   uv_end /= thickness;
//   uv_end *= aspect;
//
//   float dist = clamp(1.0f - length(uv_end) * 2.0f, 0.0f, 1.0f);
//   if (hardfac > 0.999f) {
//     return step(1e-8f, dist);
//   }
//   else {
//     /* Modulate the falloff profile */
//     float hardness = 1.0f - hardfac;
//     dist = pow(dist, mix(0.01f, 10.0f, hardness));
//     return smoothstep(0.0f, 1.0f, dist);
//   }
// }
// #endif
//
// struct PointData {
//   bool cyclical;
//   int mat, stroke_id, point_id, packed_data;
// };
//
// PointData decode_ma(int4 ma)
// {
//   PointData data;
//
//   data.mat = ma.x;
//   data.stroke_id = ma.y;
//   /* Take the absolute because the sign is for cyclical. */
//   data.point_id = abs(ma.z);
//   /* Aspect, UV Rotation and Hardness. */
//   data.packed_data = ma.w;
//   /* Cyclical is stored in the sign of the point index. */
//   data.cyclical = ma.z < 0;
//
//   return data;
// }
//
// float2 gpencil_decode_aspect(int packed_data)
// {
//   float asp = float(uint(packed_data) & 0x1FFu) * (1.0f / 255.0f);
//   return (asp > 1.0f) ? float2(1.0f, (asp - 1.0f)) : float2(asp, 1.0f);
// }
//
// float gpencil_decode_uvrot(int packed_data)
// {
//   uint udata = uint(packed_data);
//   float uvrot = 1e-8f + float((udata & 0x1FE00u) >> 9u) * (1.0f / 255.0f);
//   return ((udata & 0x20000u) != 0u) ? -uvrot : uvrot;
// }
//
// float gpencil_decode_hardness(int packed_data)
// {
//   return float((uint(packed_data) & 0x3FC0000u) >> 18u) * (1.0f / 255.0f);
// }
//
// float2 gpencil_project_to_screenspace(float4 v, float4 viewport_res)
// {
//   return ((v.xy / v.w) * 0.5f + 0.5f) * viewport_res.xy;
// }
//
// float gpencil_stroke_thickness_modulate(float thickness, float4 ndc_pos, float4 viewport_res)
// {
//     // /* Modify stroke thickness by object scale. */
//     // thickness = length(to_float3x3(drw_modelmat()) * float3(thickness * M_SQRT1_3));
//     //
//     // /* World space point size. */
//     // thickness *= drw_view().winmat[1][1] * viewport_res.y;
//     //
//     // return thickness;
//     // sqrt(1/3)
//     const float M_SQRT1_3 = 0.57735026919;
//
//     // Object scale: take upper-left 3x3 of unity_ObjectToWorld
//     float3x3 obj3x3 = (float3x3)unity_ObjectToWorld;
//
//     // Apply isotropic vector with length = thickness
//     float3 scaled = mul(obj3x3, float3(thickness * M_SQRT1_3, thickness * M_SQRT1_3, thickness * M_SQRT1_3));
//     thickness = length(scaled);
//
//     // Projection scaling: UNITY_MATRIX_P[1][1] is the same as drw_view().winmat[1][1]
//     thickness *= UNITY_MATRIX_P._m11 * _ScreenParams.y;
//
//     return thickness;
// }
//
// int gpencil_stroke_point_id(uint vertexId)
// {
//   return (vertexId & ~GP_IS_STROKE_VERTEX_BIT) >> GP_VERTEX_ID_SHIFT;
// }
//
// bool gpencil_is_stroke_vertex(uint vertexId)
// {
//   return true;
//   // TODO:
//     // return flag_test(vertexId, (uint)GP_IS_STROKE_VERTEX_BIT);
// }
//
//
// /**
//  * Returns value of gl_Position.
//  *
//  * To declare in vertex shader.
//  * in ivec4 ma, ma1, ma2, ma3;
//  * in float4 pos, pos1, pos2, pos3, uv1, uv2, col1, col2, fcol1;
//  *
//  * All of these attributes are quad loaded the same way
//  * as GL_LINES_ADJACENCY would feed a geometry shader:
//  * - ma reference the previous adjacency point.
//  * - ma1 reference the current line first point.
//  * - ma2 reference the current line second point.
//  * - ma3 reference the next adjacency point.
//  * Note that we are rendering quad instances and not using any index buffer
//  *(except for fills).
//  *
//  * Material : x is material index, y is stroke_id, z is point_id,
//  *            w is aspect & rotation & hardness packed.
//  * Position : contains thickness in 4th component.
//  * UV : xy is UV for fills, z is U of stroke, w is strength.
//  *
//  *
//  * WARNING: Max attribute count is actually 14 because OSX OpenGL implementation
//  * considers gl_VertexID and gl_InstanceID as vertex attribute. (see #74536)
//  */
// float4 gpencil_vertex(
//     int vertexId,
//     float4 viewport_res,
//     gpMaterialFlag material_flags,
//     float2 alignment_rot,
//     /* World Position. */
//     out float3 out_P,
//     /* World Normal. */
//     out float3 out_N,
//     /* Vertex Color. */
//     out float4 out_color,
//     /* Stroke Strength. */
//     out float out_strength,
//     /* UV coordinates. */
//     out float2 out_uv,
//     /* Screen-Space segment endpoints. */
//     out float4 out_sspos,
//     /* Stroke aspect ratio. */
//     out float2 out_aspect,
//     /* Stroke thickness (x: clamped, y: unclamped). */
//     out float2 out_thickness,
//     /* Stroke hardness. */
//     out float out_hardness)
// {
//   int stroke_point_id = (vertexId & ~GP_IS_STROKE_VERTEX_BIT) >> GP_VERTEX_ID_SHIFT;
//
//   /* Attribute Loading. */
//   float3 pos = _Pos[stroke_point_id-1].pos;
//   float3 pos1 = _Pos[stroke_point_id].pos;
//   float3 pos2 = _Pos[stroke_point_id+1].pos;
//   float3 pos3 = _Pos[stroke_point_id+2].pos;
//
//   int4 ma = _Strokes[stroke_point_id-1];
//   int4 ma1 = _Strokes[stroke_point_id];
//   int4 ma2 = _Strokes[stroke_point_id+1];
//   int4 ma3 = _Strokes[stroke_point_id+2];
//
//   float4 uv1 = _UvOpacity[stroke_point_id];
//   float4 uv2 = _UvOpacity[stroke_point_id+1];
//
//   float4 col1 = _Vcol[stroke_point_id];
//   float4 col2 = _Vcol[stroke_point_id+1];
//   float4 fcol1 = _Fcol[stroke_point_id];
//
// #  define thickness1 pos1.w
// #  define thickness2 pos2.w
// #  define strength1 uv1.w
// #  define strength2 uv2.w
//
//   float4 out_ndc;
//
//   if (gpencil_is_stroke_vertex(vertexId)) {
//     bool is_dot = flag_test(material_flags, GP_STROKE_ALIGNMENT);
//     bool is_squares = !flag_test(material_flags, GP_STROKE_DOTS);
//
//     bool is_first = (ma.x == -1);
//     bool is_last = (ma3.x == -1);
//     bool is_single = is_first && (ma2.x == -1);
//
//     PointData point_data1 = decode_ma(ma1);
//     PointData point_data2 = decode_ma(ma2);
//
//     /* Join the first and last point if the curve is cyclical. */
//     if (point_data1.cyclical && !is_single) {
//       if (is_first) {
//         /* The first point will have the index of the last point. */
//         PointData point_data = decode_ma(ma);
//         int last_stroke_id = point_data.stroke_id;
//         ma = asint(_Strokes[last_stroke_id-2]);
//         pos = _Pos[last_stroke_id-2];
//       }
//
//       if (is_last) {
//         int first_stroke_id = point_data1.stroke_id;
//         ma3 = asint(_Strokes[first_stroke_id+2]);
//         pos3 = _Pos[first_stroke_id+2];
//       }
//     }
//
//     /* Special Case. Stroke with single vert are rendered as dots. Do not discard them. */
//     if (!is_dot && is_single) {
//       is_dot = true;
//       is_squares = false;
//     }
//
//     /* Endpoints, we discard the vertices. */
//     if (!is_dot && ma2.x == -1) {
//       /* We set the vertex at the camera origin to generate 0 fragments. */
//       out_ndc = float4(0.0f, 0.0f, -3e36f, 0.0f);
//       return out_ndc;
//     }
//
//     /* Avoid using a vertex attribute for quad positioning. */
//     float x = float(vertexId & 1) * 2.0f - 1.0f; /* [-1..1] */
//     float y = float(vertexId & 2) - 1.0f;        /* [-1..1] */
//
//     bool use_curr = is_dot || (x == -1.0f);
//
//     float3 wpos_adj = TransformObjectToWorld((use_curr) ? pos.xyz : pos3.xyz);
//     float3 wpos1 = TransformObjectToWorld(pos1.xyz);
//     float3 wpos2 = TransformObjectToWorld(pos2.xyz);
//     
//     float3 T;
//     if (is_dot) {
//       /* Shade as facing billboards. */
//       T = unity_CameraToWorld[0].xyz;
//     }
//     else if (use_curr && ma.x != -1) {
//       T = wpos1 - wpos_adj;
//     }
//     else {
//       T = wpos2 - wpos1;
//     }
//     T = safe_normalize(T);
//
//     float3 B = cross(T, unity_CameraToWorld[2].xyz);
//     out_N = normalize(cross(B, T));
//
//     float4 ndc_adj = TransformWorldToHClip(wpos_adj);
//     float4 ndc1 = TransformWorldToHClip(wpos1);
//     float4 ndc2 = TransformWorldToHClip(wpos2);
//
//     out_ndc = (use_curr) ? ndc1 : ndc2;
//     out_P = (use_curr) ? wpos1 : wpos2;
//     out_strength = abs((use_curr) ? strength1 : strength2);
//
//     float2 ss_adj = gpencil_project_to_screenspace(ndc_adj, viewport_res);
//     float2 ss1 = gpencil_project_to_screenspace(ndc1, viewport_res);
//     float2 ss2 = gpencil_project_to_screenspace(ndc2, viewport_res);
//     
//     /* Screen-space Lines tangents. */
//     float edge_len;
//     float2 edge_dir = safe_normalize_and_get_length(ss2 - ss1, edge_len);
//     float2 edge_adj_dir = safe_normalize((use_curr) ? (ss1 - ss_adj) : (ss_adj - ss2));
//
//     float thickness = abs((use_curr) ? thickness1 : thickness2);
//     thickness = gpencil_stroke_thickness_modulate(thickness, out_ndc, viewport_res);
//     /* The radius attribute can have negative values. Make sure that it's not negative by clamping
//      * to 0. */
//     float clamped_thickness = max(0.0f, thickness);
//
//     out_uv = float2(x, y) * 0.5f + 0.5f;
//     out_hardness = gpencil_decode_hardness(use_curr ? point_data1.packed_data :
//                                                       point_data2.packed_data);
//
//     if (is_dot) {
//       uint alignment_mode = material_flags & GP_STROKE_ALIGNMENT;
//
//       /* For one point strokes use object alignment. */
//       if (alignment_mode == GP_STROKE_ALIGNMENT_STROKE && is_single) {
//         alignment_mode = GP_STROKE_ALIGNMENT_OBJECT;
//       }
//
//       float2 x_axis;
//       if (alignment_mode == GP_STROKE_ALIGNMENT_STROKE) {
//         x_axis = (ma2.x == -1) ? edge_adj_dir : edge_dir;
//       }
//       else if (alignment_mode == GP_STROKE_ALIGNMENT_FIXED) {
//         /* Default for no-material drawing. */
//         x_axis = float2(1.0f, 0.0f);
//       }
//       else { /* GP_STROKE_ALIGNMENT_OBJECT */
//         float4 ndc_x = TransformWorldToHClip(wpos1 + unity_ObjectToWorld[0].xyz);
//         float2 ss_x = gpencil_project_to_screenspace(ndc_x, viewport_res);
//         x_axis = safe_normalize(ss_x - ss1);
//       }
//
//       /* Rotation: Encoded as Cos + Sin sign. */
//       float uv_rot = gpencil_decode_uvrot(point_data1.packed_data);
//       float rot_sin = sqrt(max(0.0f, 1.0f - uv_rot * uv_rot)) * sign(uv_rot);
//       float rot_cos = abs(uv_rot);
//       /* TODO(@fclem): Optimize these 2 matrix multiply into one by only having one rotation angle
//        * and using a cosine approximation. */
//       float2x2 rotMat = float2x2(rot_cos, -rot_sin, rot_sin,  rot_cos);
//       x_axis = mul(rotMat, x_axis);
//       float2x2 alignMat = float2x2(alignment_rot.x, -alignment_rot.y, alignment_rot.y,  alignment_rot.x);
//       x_axis = mul(alignMat, x_axis);
//       /* Rotate 90 degrees counter-clockwise. */
//       float2 y_axis = float2(-x_axis.y, x_axis.x);
//
//       out_aspect = gpencil_decode_aspect(point_data1.packed_data);
//
//       x *= out_aspect.x;
//       y *= out_aspect.y;
//
//       /* Invert for vertex shader. */
//       out_aspect = 1.0f / out_aspect;
//
//       out_ndc.xy += (x * x_axis + y * y_axis) * viewport_res.zw * clamped_thickness;
//
//       out_sspos.xy = ss1;
//       out_sspos.zw = ss1 + x_axis * 0.5f;
//       out_thickness.x = (is_squares) ? 1e18f : (clamped_thickness / out_ndc.w);
//       out_thickness.y = (is_squares) ? 1e18f : (thickness / out_ndc.w);
//     }
//     else {
//       bool is_stroke_start = (ma.x == -1 && x == -1);
//       bool is_stroke_end = (ma3.x == -1 && x == 1);
//
//       /* Mitter tangent vector. */
//       float2 miter_tan = safe_normalize(edge_adj_dir + edge_dir);
//       float miter_dot = dot(miter_tan, edge_adj_dir);
//       /* Break corners after a certain angle to avoid really thick corners. */
//       const float miter_limit = 0.5f; /* cos(60 degrees) */
//       bool miter_break = (miter_dot < miter_limit);
//       miter_tan = (miter_break || is_stroke_start || is_stroke_end) ? edge_dir :
//                                                                       (miter_tan / miter_dot);
//       /* Rotate 90 degrees counter-clockwise. */
//       float2 miter = float2(-miter_tan.y, miter_tan.x);
//
//       out_sspos.xy = ss1;
//       out_sspos.zw = ss2;
//       out_thickness.x = clamped_thickness / out_ndc.w;
//       out_thickness.y = thickness / out_ndc.w;
//       out_aspect = float2(1, 1);
//
//       float2 screen_ofs = miter * y;
//
//       /* Reminder: we packed the cap flag into the sign of strength and thickness sign. */
//       if ((is_stroke_start && strength1 > 0.0f) || (is_stroke_end && thickness1 > 0.0f) ||
//           (miter_break && !is_stroke_start && !is_stroke_end))
//       {
//         screen_ofs += edge_dir * x;
//       }
//
//       out_ndc.xy += screen_ofs * viewport_res.zw * clamped_thickness;
//
//       out_uv.x = (use_curr) ? uv1.z : uv2.z;
//     }
//
//     out_color = (use_curr) ? col1 : col2;
//   }
//   else {
//     out_P = TransformObjectToWorld(pos1.xyz);
//     out_ndc = TransformWorldToHClip(out_P);
//     out_uv = uv1.xy;
//     out_thickness.x = 1e18f;
//     out_thickness.y = 1e20f;
//     out_hardness = 1.0f;
//     out_aspect = float2(1.0f, 1.0f);
//     out_sspos = float4(0, 0, 0, 0);
//
//     /* Flat normal following camera and object bounds. */
//     float3 objScale = float3(
//         length(unity_ObjectToWorld._m00_m10_m20),
//         length(unity_ObjectToWorld._m01_m11_m21),
//         length(unity_ObjectToWorld._m02_m12_m22)
//     );
//     float3 objWorldPos = unity_ObjectToWorld[3].xyz;
//     float3 V = normalize(_WorldSpaceCameraPos - objWorldPos); //TODO: this wont work for orthographic view
//     float3 N = mul((float3x3)unity_WorldToObject, V);
//     N *= objScale;
//     N = mul((float3x3)unity_WorldToObject, N);
//     out_N = safe_normalize(N);
//
//     /* Decode fill opacity. */
//     out_color = float4(fcol1.rgb, floor(fcol1.a / 10.0f) / 10000.0f);
//
//     /* We still offset the fills a little to avoid overlaps */
//     out_ndc.z += 0.000002f;
//   }
//
// #  undef thickness1
// #  undef thickness2
// #  undef strength1
// #  undef strength2
//
//   return out_ndc;
// }
//
// float4 gpencil_vertex(
//   int vertexID,
//   float4 viewport_res,
//   out float3 out_P,
//   out float3 out_N,
//   out float4 out_color,
//   out float out_strength,
//   out float2 out_uv,
//   out float4 out_sspos,
//   out float2 out_aspect,
//   out float2 out_thickness,
// out float out_hardness)
// {
//   return gpencil_vertex(
//     vertexID,
//     viewport_res,
//     gpMaterialFlag(0u),
//     float2(1.0f, 0.0f),
//     out_P,
//     out_N,
//     out_color,
//     out_strength,
//     out_uv,
//     out_sspos,
//     out_aspect,
//     out_thickness,
//     out_hardness);
// }
//
