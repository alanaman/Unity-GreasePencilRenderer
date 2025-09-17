using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class GreasePencilRendererTrashed : MonoBehaviour
{
    public GreasePencilSO greasePencil;
    public int frameidx = 0;

    // ComputeBuffers
    ComputeBuffer posBuffer;
    ComputeBuffer strokesBuffer;
    ComputeBuffer uvOpacityBuffer;
    ComputeBuffer vcolBuffer;
    ComputeBuffer fcolBuffer;
    ComputeBuffer materialBuffer;

    // Keep references so we can dispose
    bool buffersCreated = false;

    Mesh mesh;
    
    // static void grease_pencil_geom_batch_ensure(Object &object,
    // const GreasePencil &grease_pencil,
    // const Scene &scene)
    [StructLayout(LayoutKind.Sequential)]
    public struct GreasePencilStrokeVert
    {
        // float3 pos;
        public Vector3 pos;

        // float radius;
        public float radius;

        // int4 mat, stroke_id, point_id, packed_asp_hard_rot;
        public int mat;
        public int stroke_id;
        public int point_id;
        public int packed_asp_hard_rot;

        // float2 uv_fill;
        public Vector2 uv_fill;

        // float u_stroke, opacity;
        public float u_stroke;
        public float opacity;
    };
    
    struct GreasePencilColorVert {
        public float4 vcol; /* Vertex color */
        public float4 fcol; /* Fill color */
    };
    
    [ContextMenu("Build Stroke Mesh")]
    void CreateMesh()
    {
        var vertsStartOffsetsPerLayer = CalculateOffsets(out var totalNumPoints, out var totalVertexOffset);
        // struct GreasePencilStrokeVert {
        //     /** Position and radius packed in the same attribute. */
        //     float pos[3], radius;
        //     /** Material Index, Stroke Index, Point Index, Packed aspect + hardness + rotation. */
        //     int32_t mat, stroke_id, point_id, packed_asp_hard_rot;
        //     /** UV and opacity packed in the same attribute. */
        //     float uv_fill[2], u_stroke, opacity;
        // };
        
        // Add extra space at the end of the buffer because of quad load.
        GreasePencilStrokeVert[] verts = new GreasePencilStrokeVert[totalVertexOffset+2];
        GreasePencilColorVert[] cols = new GreasePencilColorVert[totalVertexOffset+2];
        
        // a quad for every strokePoint
        int[] triangleIbo = new int[totalNumPoints*2*3];
        var triVbo = new Vector3[totalVertexOffset*4];
        int triangleIboIndex = 0;

        for (int layerIdx = 0; layerIdx < greasePencil.data.layers.Count; layerIdx++)
        {
            var layer = greasePencil.data.layers[layerIdx];
            var strokes = layer.frames[frameidx].strokes;
            if (strokes.Count == 0)
            {
                continue;
            }

            var vertsStartOffSets = vertsStartOffsetsPerLayer[layerIdx];
            for (int strokeIdx = 0; strokeIdx < strokes.Count; strokeIdx++)
            {
                var stroke = strokes[strokeIdx];
                var pointsCount = stroke.points.Count;
                bool isCyclic = stroke.cyclic && pointsCount >= 3;
                var vertsStartOffset = vertsStartOffSets[strokeIdx];
                var numVerts = 1 + pointsCount + (isCyclic ? 1 : 0) + 1;

                // First vertex is not drawn
                verts[vertsStartOffset].mat = -1;
                // The first vertex will have the index of the last vertex.
                verts[vertsStartOffset].stroke_id = vertsStartOffset + numVerts - 1;
                //
                // // If the stroke has more than 2 points, add the triangle indices to the index buffer.
                // if (pointsCount >= 3)
                // {
                //     var trisSlice = new Span<int3>(triangles.ToArray(), trisStartOffset, numVerts);
                //     foreach (var tri in trisSlice)
                //     {
                //         triangleIbo[triangleIboIndex + 0] = (vertsStartOffset + 1 + tri.x) << 2;
                //         triangleIbo[triangleIboIndex + 1] = (vertsStartOffset + 1 + tri.y) << 2;
                //         triangleIbo[triangleIboIndex + 2] = (vertsStartOffset + 1 + tri.z) << 2;
                //         triangleIboIndex += 3;
                //     }
                // }

                // Write all the point attributes to the vertex buffers. Create a quad for each point. 
                for (int pointIdx = 0; pointIdx < pointsCount; pointIdx++)
                {
                    var strokePoint = stroke.points[pointIdx];
                    var idx = vertsStartOffset + pointIdx + 1;
                    PopulatePoint(strokePoint, out verts[idx], out cols[idx], idx, isCyclic);
                }

                void PopulatePoint(PointData strokePoint, out GreasePencilStrokeVert sVert, out GreasePencilColorVert cVert, int idx, bool cyclic)
                {
                    sVert.pos = strokePoint.Position;
                    // sVert.pos = new float3(idx, 0, 0);
                    var posNext = stroke.points[(idx) % pointsCount].Position;
                    sVert.radius = strokePoint.radius;
                    sVert.opacity = strokePoint.opacity;
                    var offset = vertsStartOffset + idx + 1;
                    // Store if the curve is cyclic in the sign of the point index.
                    sVert.point_id = cyclic ? -offset : offset;
                    sVert.stroke_id = vertsStartOffset;

                    /* The material index is allowed to be negative as it's stored as a generic attribute. To
                     * ensure the material used by the shader is valid this needs to be clamped to zero. */
                    sVert.mat = Math.Max(stroke.material_index, 0) % 256;

                    sVert.packed_asp_hard_rot = 0; //todo
                    sVert.u_stroke = 0; //todo
                    sVert.uv_fill = float2.zero; //todo

                    cVert.vcol = strokePoint.VertexColor;
                    cVert.fcol = Vector4.one;

                    // quad
                    triangleIbo[triangleIboIndex + 0] = ((vertsStartOffset + idx) << 2) + 0;
                    triangleIbo[triangleIboIndex + 1] = ((vertsStartOffset + idx) << 2) + 1;
                    triangleIbo[triangleIboIndex + 2] = ((vertsStartOffset + idx) << 2) + 2;
                    triangleIboIndex += 3;
                    triangleIbo[triangleIboIndex + 0] = ((vertsStartOffset + idx) << 2) + 2;
                    triangleIbo[triangleIboIndex + 1] = ((vertsStartOffset + idx) << 2) + 1;
                    triangleIbo[triangleIboIndex + 2] = ((vertsStartOffset + idx) << 2) + 3;
                    triangleIboIndex += 3;
                    triVbo[((vertsStartOffset + idx) << 2) + 0] = strokePoint.Position + Vector3.up;
                    triVbo[((vertsStartOffset + idx) << 2) + 1] = strokePoint.Position - Vector3.up;
                    triVbo[((vertsStartOffset + idx) << 2) + 2] = posNext + Vector3.up;
                    triVbo[((vertsStartOffset + idx) << 2) + 3] = posNext - Vector3.up;
                }
            }
        }
        
        ReleaseBuffers();
        
        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = "GreasePencilMesh";
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }
        else
        {
            mesh.Clear();
        }
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = triVbo;
        mesh.SetIndices(triangleIbo, MeshTopology.Triangles, 0);
        posBuffer = new ComputeBuffer(verts.Length, sizeof(float) * 4 * 3);
        // var posArray = new Vector4[verts.Length];
        // for (int i = 0; i < verts.Length; i++)
        // {
        //     posArray[i] = new Vector4(verts[i].pos.x, verts[i].pos.y, verts[i].pos.z, verts[i].radius);
        // }
        // posBuffer.SetData(posArray);
        posBuffer.SetData(verts);
        vcolBuffer = new ComputeBuffer(cols.Length, sizeof(float) * 4 * 2);
        vcolBuffer.SetData(cols);
        CreateMaterialBuffer();
        buffersCreated = true;
        
        var targetMaterial = GetComponent<MeshRenderer>().sharedMaterial; 
        // 4) Bind to material
        if (targetMaterial != null)
        {
            targetMaterial.SetBuffer("_Pos", posBuffer);
            targetMaterial.SetBuffer("_Color", vcolBuffer);
            targetMaterial.SetBuffer("gp_materials", materialBuffer);
        }
        else
        {
            Debug.LogWarning("targetMaterial not assigned; buffers created but not bound.");
        }

    }

    private List<int[]> CalculateOffsets(out int totalNumPoints, out int totalVertexOffset)
    {
        totalNumPoints = 0;
        totalVertexOffset = 0;
        var vertsStartOffsetsPerLayer = new List<int[]>();

        foreach (var layer in greasePencil.data.layers)
        {
            var strokes = layer.frames[frameidx].strokes;
            int numStrokes = strokes.Count;
            int[] vertsStartOffsets = new int[numStrokes];
            
            // Calculate the triangle and vertex offsets for all the strokes
            int numCyclic = 0;
            int numPoints = 0;
            for (int strokeIdx = 0; strokeIdx < numStrokes; strokeIdx++)
            {
                var stroke = strokes[strokeIdx];
                var pointsCount = stroke.points.Count;
                bool isCyclic = stroke.cyclic && pointsCount >= 3;
                if (isCyclic) numCyclic++;
                vertsStartOffsets[strokeIdx] = totalVertexOffset;
                // One vertex is stored before and after as padding. Cyclic strokes have one extra vertex.
                totalVertexOffset += 1 + pointsCount + (isCyclic ? 1 : 0) + 1;
                numPoints += pointsCount;
            }

            // the strokes
            totalNumPoints += (numPoints + numCyclic);
            
            vertsStartOffsetsPerLayer.Add(vertsStartOffsets);
        }

        return vertsStartOffsetsPerLayer;
    }

    // BLI_INLINE int32_t pack_rotation_aspect_hardness(float rot, float asp, float softness)
    // {
    //     int32_t packed = 0;
    //     /* Aspect uses 9 bits */
    //     float asp_normalized = (asp > 1.0f) ? (1.0f / asp) : asp;
    //     packed |= int32_t(unit_float_to_uchar_clamp(asp_normalized));
    //     /* Store if inverted in the 9th bit. */
    //     if (asp > 1.0f) {
    //         packed |= 1 << 8;
    //     }
    //     /* Rotation uses 9 bits */
    //     /* Rotation are in [-90..90] degree range, so we can encode the sign of the angle + the cosine
    //      * because the cosine will always be positive. */
    //     packed |= int32_t(unit_float_to_uchar_clamp(cosf(rot))) << 9;
    //     /* Store sine sign in 9th bit. */
    //     if (rot < 0.0f) {
    //         packed |= 1 << 17;
    //     }
    //     /* Hardness uses 8 bits */
    //     packed |= int32_t(unit_float_to_uchar_clamp(1.0f - softness)) << 18;
    //     return packed;
    // }
    
    void BuildMesh()
    {
        // This method remains largely the same as the mesh structure is separate from the buffer packing.
        if (greasePencil == null)
        {
            Debug.LogError("No GreasePencil asset assigned.");
            return;
        }

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();   // stroke index, point index
        var uvs2 = new List<Vector2>();  // could hold vertex ID for buffer lookup
        var colors = new List<Color>();
        var indices = new List<int>();

        int vertBase = 0;

        int strokeIndex = 0;
        foreach (var layer in greasePencil.data.layers)
        {
            foreach (var stroke in layer.frames[frameidx].strokes)
            {
                if (stroke.points == null || stroke.points.Count < 2)
                    continue;

                for (int i = 0; i < stroke.points.Count - 1; i++)
                {
                    PointData p0 = stroke.points[i];
                    PointData p1 = stroke.points[i + 1];

                    // build a quad segment between p0 and p1
                    Vector3 dir = (p1.Position - p0.Position).normalized;
                    Vector3 normal = Vector3.up; // assume Z-up camera; adjust later
                    Vector3 side = Vector3.Cross(dir, normal).normalized;

                    float r0 = p0.radius;
                    float r1 = p1.radius;

                    // 4 vertices for the quad
                    vertices.Add(p0.Position + side * r0);
                    vertices.Add(p0.Position - side * r0);
                    vertices.Add(p1.Position + side * r1);
                    vertices.Add(p1.Position - side * r1);

                    // UVs (store strokeIndex + pointIndex)
                    uvs.Add(new Vector2(strokeIndex, i));
                    uvs.Add(new Vector2(strokeIndex, i));
                    uvs.Add(new Vector2(strokeIndex, i + 1));
                    uvs.Add(new Vector2(strokeIndex, i + 1));

                    // UV2 (for vertex id if you later map to buffers)
                    uvs2.Add(new Vector2(vertices.Count - 4, 0));
                    uvs2.Add(new Vector2(vertices.Count - 3, 0));
                    uvs2.Add(new Vector2(vertices.Count - 2, 0));
                    uvs2.Add(new Vector2(vertices.Count - 1, 0));

                    // Color (pack opacity or stroke color if you have one)
                    Color c0 = new Color(1, 1, 1, p0.opacity);
                    Color c1 = new Color(1, 1, 1, p1.opacity);
                    colors.Add(c0);
                    colors.Add(c0);
                    colors.Add(c1);
                    colors.Add(c1);

                    // Indices (two triangles)
                    indices.Add(vertBase + 0);
                    indices.Add(vertBase + 2);
                    indices.Add(vertBase + 1);
                    indices.Add(vertBase + 2);
                    indices.Add(vertBase + 3);
                    indices.Add(vertBase + 1);

                    vertBase += 4;
                }
                strokeIndex++;
            }
        }

        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = "GreasePencilMesh";
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }
        else
        {
            mesh.Clear();
        }

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uvs2);
        mesh.SetColors(colors);
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateBounds();
    }
    // Define the C# struct that matches the GPU struct layout.
    // The GPU struct is packed, so we must be careful with alignment.
    // The C# struct must have a size of 88 bytes to match the GPU side (22 floats * 4 bytes/float).
    // Unity's GPU struct packing might differ slightly, but this is a close approximation.
    struct GpMaterialData
    {
        public Vector4 stroke_color;
        public Vector4 fill_color;
        public Vector4 fill_mix_color;
        public Vector4 fill_uv_rot_scale;
        public Vector4 fill_uv_offset_alignment_rot; // Combined for packing
        public float stroke_texture_mix;
        public float stroke_u_scale;
        public float fill_texture_mix;
        public int flag;
    }
    [ContextMenu("pack Attributes")]
    void ApplyIndices()
    {
        if (greasePencil == null)
        {
            Debug.LogError("GreasePencil asset not assigned.");
            return;
        }

        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogError("MeshFilter or sharedMesh missing.");
            return;
        }
        
        var vertsStartOffsetsPerLayer = CalculateOffsets(out var totalNumPoints, out var totalVertexOffset);

        // 1) write vertex ids into uv2.x (so shader can index)
        var uvs2 = new List<Vector2>(totalVertexOffset);
        for (int i = 0; i < totalVertexOffset; i++)
            uvs2.Add(new Vector2(i, 0));
        mesh.SetUVs(1, uvs2); // TEXCOORD1 / uv2

        // 2) Flatten grease pencil points and create stroke metadata
        var posData = new Vector4[totalVertexOffset]; // (x,y,z,radius)
        var strokeData = new Vector4[totalVertexOffset]; // (mat_idx, stroke_id, point_id, packed_data)
        var uvOpacityData = new Vector4[totalVertexOffset]; // (uv_fill.x, uv_fill.y, u_stroke, opacity)
        var vcolData = new Vector4[totalVertexOffset]; // (r, g, b, a)
        var fcolData = new Vector4[totalVertexOffset]; // (r, g, b, a)

        int runningPointIndex = 0;
        int runningStrokeIndex = 0;

        foreach (var layer in greasePencil.data.layers)
        {
            foreach (var stroke in layer.frames[frameidx].strokes)
            {
                bool is_cyclic = stroke.cyclic && (stroke.points.Count > 2);
                // First point is not drawn
                strokeData[runningPointIndex][0] = -1;
                // The first vertex will have the index of the last vertex.
                strokeData[runningPointIndex][1] = runningPointIndex + 1 + stroke.points.Count + (is_cyclic ? 1 : 0);
                runningPointIndex++;    
                if (stroke.points != null)
                {
                    int pointIndex = 0;
                    foreach (var p in stroke.points)
                    {
                        // Populating the new buffers
                        posData[runningPointIndex] = new Vector4(p.Position.x, p.Position.y, p.Position.z, p.radius);
                        
                        // We need more data from the grease pencil asset to properly pack this,
                        // for now, use placeholders. Assuming mat_idx, stroke_id, and point_id.
                        int matIdx = stroke.material_index;
                        if (pointIndex == 0 || pointIndex == stroke.points.Count - 1)
                        {
                            matIdx = -1;
                        }
                        strokeData[runningPointIndex] = new Vector4(matIdx, runningStrokeIndex, pointIndex, 0f);
                        
                        // Assuming you have uv_fill and u_stroke data in your PointData or you need to generate it
                        // Here's a placeholder, you'll need to adjust based on your data source.
                        uvOpacityData[runningPointIndex] = new Vector4(0f, 0f, (float)pointIndex / (stroke.points.Count - 1), p.opacity); 
                        
                        // Assuming you have vertex colors and fill colors
                        // For this example, we'll use placeholder colors.
                        vcolData[runningPointIndex] = new Vector4(1f, 1f, 1f, 1f);
                        fcolData[runningPointIndex] = new Vector4(1f, 1f, 1f, 1f);

                        runningPointIndex++;
                        pointIndex++;
                    }
                }
                runningStrokeIndex++;
                runningPointIndex++;
            }
        }

        // 3) Create / update ComputeBuffers
        ReleaseBuffers();

        if (posData.Length == 0)
        {
            Debug.LogWarning("No points in grease pencil asset.");
            return;
        }

        posBuffer = new ComputeBuffer(posData.Length, sizeof(float) * 4);
        posBuffer.SetData(posData);

        strokesBuffer = new ComputeBuffer(strokeData.Length, sizeof(int) * 4);
        // Cast Vector4 to int4, as the shader expects ints
        var strokeInts = new int[strokeData.Length * 4];
        for (int i = 0; i < strokeData.Length; i++)
        {
            strokeInts[i * 4 + 0] = (int)strokeData[i].x;
            strokeInts[i * 4 + 1] = (int)strokeData[i].y;
            strokeInts[i * 4 + 2] = (int)strokeData[i].z;
            strokeInts[i * 4 + 3] = (int)strokeData[i].w;
        }
        strokesBuffer.SetData(strokeInts);
        



        CreateMaterialBuffer();

        uvOpacityBuffer = new ComputeBuffer(uvOpacityData.Length, sizeof(float) * 4);
        uvOpacityBuffer.SetData(uvOpacityData);

        vcolBuffer = new ComputeBuffer(vcolData.Length, sizeof(float) * 4);
        vcolBuffer.SetData(vcolData);

        fcolBuffer = new ComputeBuffer(fcolData.Length, sizeof(float) * 4);
        fcolBuffer.SetData(fcolData);
        
        var targetMaterial = GetComponent<MeshRenderer>().sharedMaterial; 
        // 4) Bind to material
        if (targetMaterial != null)
        {
            targetMaterial.SetBuffer("_Pos", posBuffer);
            targetMaterial.SetBuffer("_Strokes", strokesBuffer);
            targetMaterial.SetBuffer("_UvOpacity", uvOpacityBuffer);
            targetMaterial.SetBuffer("_Vcol", vcolBuffer);
            targetMaterial.SetBuffer("_Fcol", fcolBuffer);
            targetMaterial.SetBuffer("gp_materials", materialBuffer);
        }
        else
        {
            Debug.LogWarning("targetMaterial not assigned; buffers created but not bound.");
        }

        buffersCreated = true;

        Debug.Log($"GreasePencilRenderer: uploaded {posData.Length} points, {strokeData.Length} strokes.");
    }

    private void CreateMaterialBuffer()
    {
        var materialDataList = new List<GpMaterialData>();
        foreach (var mat in greasePencil.data.materials)
        {
            GpMaterialData gpuMat = new GpMaterialData();
            
            // Populate the C# struct from your GreasePencilSO data.
            gpuMat.stroke_color = new Vector4(mat.stroke_color[0], mat.stroke_color[1], mat.stroke_color[2], 1.0f);
            gpuMat.fill_color = new Vector4(mat.fill_color[0], mat.fill_color[1], mat.fill_color[2], 1.0f);
            gpuMat.fill_mix_color = new Vector4(mat.fill_mix_color[0], mat.fill_mix_color[1], mat.fill_mix_color[2], 1.0f);
            gpuMat.fill_uv_rot_scale = new Vector4(mat.fill_uv_rot_scale[0], mat.fill_uv_rot_scale[1], mat.fill_uv_rot_scale[2], mat.fill_uv_rot_scale[3]);
            
            // Pack fill_uv_offset and alignment_rot into a single Vector4
            gpuMat.fill_uv_offset_alignment_rot = new Vector4(mat.fill_uv_offset[0], mat.fill_uv_offset[1], mat.alignment_rot[0], mat.alignment_rot[1]);

            gpuMat.stroke_texture_mix = mat.stroke_texture_mix;
            gpuMat.stroke_u_scale = mat.stroke_u_scale;
            gpuMat.fill_texture_mix = mat.fill_texture_mix;
            gpuMat.flag = mat.flag;
            
            materialDataList.Add(gpuMat);
        }
    
        // Create the ComputeBuffer and upload the data.
        materialBuffer = new ComputeBuffer(materialDataList.Count, System.Runtime.InteropServices.Marshal.SizeOf(typeof(GpMaterialData)));
        materialBuffer.SetData(materialDataList);
    }

    void ReleaseBuffers()
    {
        if (posBuffer != null)
        {
            posBuffer.Release();
            posBuffer = null;
        }
        if (strokesBuffer != null)
        {
            strokesBuffer.Release();
            strokesBuffer = null;
        }
        if (uvOpacityBuffer != null)
        {
            uvOpacityBuffer.Release();
            uvOpacityBuffer = null;
        }
        if (vcolBuffer != null)
        {
            vcolBuffer.Release();
            vcolBuffer = null;
        }
        if (fcolBuffer != null)
        {
            fcolBuffer.Release();
            fcolBuffer = null;
        }

        if (materialBuffer != null)
        {
            materialBuffer.Release();
            materialBuffer = null;
        }
        buffersCreated = false;
    }

    void OnDestroy()
    {
        ReleaseBuffers();
    }

    void OnDisable()
    {
        var targetMaterial = GetComponent<MeshRenderer>().sharedMaterial; 
        
        // Unbind buffers from material to avoid dangling references
        if (targetMaterial != null)
        {
            targetMaterial.SetBuffer("_Pos", (ComputeBuffer)null);
            targetMaterial.SetBuffer("_Strokes", (ComputeBuffer)null);
            targetMaterial.SetBuffer("_UvOpacity", (ComputeBuffer)null);
            targetMaterial.SetBuffer("_Vcol", (ComputeBuffer)null);
            targetMaterial.SetBuffer("_Fcol", (ComputeBuffer)null);
            targetMaterial.SetBuffer("gp_materials", (ComputeBuffer)null);
        }
        ReleaseBuffers();
    }
}