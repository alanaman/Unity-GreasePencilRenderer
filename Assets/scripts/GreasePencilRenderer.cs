using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

public class GreasePencilRenderer : MonoBehaviour
{
    private const int GP_IS_STROKE_VERTEX_BIT = (1 << 30);
    
    public GreasePencilSO greasePencil;
    public int frameIdx = 0;

    GraphicsBuffer _gPencilIbo;
    GraphicsBuffer _verts;
    GraphicsBuffer _cols;
    ComputeBuffer _materialBuffer;
    
    public Material material;
    
    void Start()
    {
        CreateMesh();
    }

    
    void OnDestroy()
    {
        ReleaseBuffers();
    }

    void ReleaseBuffers()
    {
        _gPencilIbo?.Dispose();
        _gPencilIbo = null;
        _verts?.Dispose();
        _verts = null;
        _cols?.Dispose();
        _cols = null;
        _materialBuffer?.Dispose();
        _materialBuffer = null;
    }

    void Update()
    {
        // Create the MaterialPropertyBlock to hold our per-draw data.
        var matProps = new MaterialPropertyBlock();
    
        // Set your custom data buffers
        matProps.SetBuffer("_Pos", _verts);
        matProps.SetBuffer("_Color", _cols);
        matProps.SetBuffer("gp_materials", _materialBuffer);

        // Set the standard object-to-world matrix uniform.
        matProps.SetMatrix("_ObjectToWorld", transform.localToWorldMatrix);
        
        RenderParams rp = new RenderParams(material);
        rp.worldBounds = new Bounds(Vector3.zero, 1000*Vector3.one); // use tighter bounds
        rp.matProps = matProps;
        Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, _gPencilIbo, _gPencilIbo.count);
    }
    
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
    
    [StructLayout(LayoutKind.Sequential)]
    struct GreasePencilColorVert {
        public float4 vcol; /* Vertex color */
        public float4 fcol; /* Fill color */
    };
    
    [ContextMenu("Build Stroke Mesh")]
    void CreateMesh()
    {
        var vertsStartOffsetsPerLayer = CalculateOffsetsPerLayer(out var totalNumPoints, out var totalVertexOffset);
        
        // Add extra space at the end of the buffer because of quad load.
        GreasePencilStrokeVert[] verts = new GreasePencilStrokeVert[totalVertexOffset+2];
        GreasePencilColorVert[] cols = new GreasePencilColorVert[totalVertexOffset+2];
        
        // a quad for every strokePoint
        int[] triangleIbo = new int[totalNumPoints*2*3];
        int triangleIboIndex = 0;

        for (int layerIdx = 0; layerIdx < greasePencil.data.layers.Count; layerIdx++)
        {
            var layer = greasePencil.data.layers[layerIdx];
            var strokes = layer.frames[frameIdx].strokes;
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
                verts[vertsStartOffset + pointsCount + 1].mat = -1;
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
                    int vertIdxMarkedStroke = ((vertsStartOffset + idx) << 2) | GP_IS_STROKE_VERTEX_BIT;
                    triangleIbo[triangleIboIndex + 0] = vertIdxMarkedStroke + 0;
                    triangleIbo[triangleIboIndex + 1] = vertIdxMarkedStroke + 1;
                    triangleIbo[triangleIboIndex + 2] = vertIdxMarkedStroke + 2;
                    triangleIboIndex += 3;
                    triangleIbo[triangleIboIndex + 0] = vertIdxMarkedStroke + 2;
                    triangleIbo[triangleIboIndex + 1] = vertIdxMarkedStroke + 1;
                    triangleIbo[triangleIboIndex + 2] = vertIdxMarkedStroke + 3;
                    triangleIboIndex += 3;
                }
            }
        }
        
        verts[totalVertexOffset + 0].mat = -1;
        verts[totalVertexOffset + 1].mat = -1;
        
        ReleaseBuffers();
        _gPencilIbo = new GraphicsBuffer(GraphicsBuffer.Target.Structured, triangleIbo.Length, sizeof(int));
        _gPencilIbo.SetData(triangleIbo);
        
        _verts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, verts.Length, sizeof(float)*4*3);
        _verts.SetData(verts);
        _cols = new GraphicsBuffer(GraphicsBuffer.Target.Structured, verts.Length, sizeof(float)*4*2);
        _cols.SetData(cols);

        CreateMaterialBuffer();
    }
    
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
        _materialBuffer = new ComputeBuffer(materialDataList.Count, System.Runtime.InteropServices.Marshal.SizeOf(typeof(GpMaterialData)));
        _materialBuffer.SetData(materialDataList);
    }
    
    private List<int[]> CalculateOffsetsPerLayer(out int totalNumPoints, out int totalVertexOffset)
    {
        totalNumPoints = 0;
        totalVertexOffset = 0;
        var vertsStartOffsetsPerLayer = new List<int[]>();

        foreach (var layer in greasePencil.data.layers)
        {
            var strokes = layer.frames[frameIdx].strokes;
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
}
