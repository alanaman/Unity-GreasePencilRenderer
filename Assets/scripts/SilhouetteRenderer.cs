using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Rendering;


[RequireComponent(typeof(SilhouetteEdgeCalculator))]
public class SilhouetteRenderer : MonoBehaviour
{
    
    static readonly ProfilerMarker debugMarker = new ProfilerMarker("SilhouetteRender");

    [StructLayout(LayoutKind.Sequential)]
	public struct VertexData
	{
		public Vector3 position;
		public Vector3 normal;
	}
    
    SilhouetteEdgeCalculator _edgeCalculator;
    
    GraphicsBuffer _indices;
    ComputeBuffer _materialBuffer;
    
    public Material material;
    
    
    public List<GreasePencilMaterial> greasePencilMaterials;
    
    [Range(0.0f, 1.0f)] public float opacity = 1.0f;
    public Color colorTint = new(1, 1, 1, 0);
    
    void Start()
    {
        _edgeCalculator = GetComponent<SilhouetteEdgeCalculator>();
        InitBuffers();
    }

    private void Update()
    {
        _edgeCalculator.CalculateEdges();
        // Create the MaterialPropertyBlock to hold our per-draw data.
        var matProps = new MaterialPropertyBlock();

        // Set your custom data buffers
        matProps.SetBuffer("_Pos", _edgeCalculator.DenseStrokesBuffer);
        matProps.SetBuffer("_Color", _edgeCalculator.ColorBuffer);
        matProps.SetBuffer("gp_materials", _materialBuffer);
        
        // Set the standard object-to-world matrix uniform.
        matProps.SetMatrix("_ObjectToWorld", transform.localToWorldMatrix);
        matProps.SetFloat("gp_layer_opacity", opacity);
        matProps.SetColor("gp_layer_tint", colorTint);
        // matProps.SetFloat("gp_layer_opacity", opacity);
        // matProps.SetColor("gp_layer_tint", colorTint);
    
        
        RenderParams rp = new RenderParams(material);
        rp.worldBounds = new Bounds(Vector3.zero, 1000*Vector3.one); // use tighter bounds
        rp.matProps = matProps;
        
        Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, _indices, _indices.count);
    }

    void OnDestroy()
    {
        ReleaseBuffers();
    }

    void ReleaseBuffers()
    {
        _indices?.Dispose();
        _indices = null;
    }

    void InitBuffers()
    {
        ReleaseBuffers();
        
        Mesh sourceMesh = _edgeCalculator.sourceMeshFilter.sharedMesh;

        var meshIndices = sourceMesh.GetIndices(0);
        var numFaces = meshIndices.Length / 3;
        var indices = new int[numFaces*6]; // 6 indices per triangle
        int triangleIboIndex = 0;
        for (int i = 0; i < numFaces; i++)
        {
            int vertIdxMarkedStroke = ((i) << 2) | GreasePencilRenderer.GP_IS_STROKE_VERTEX_BIT;
            indices[triangleIboIndex + 0] = vertIdxMarkedStroke + 0;
            indices[triangleIboIndex + 1] = vertIdxMarkedStroke + 1;
            indices[triangleIboIndex + 2] = vertIdxMarkedStroke + 2;
            triangleIboIndex += 3;
            indices[triangleIboIndex + 0] = vertIdxMarkedStroke + 2;
            indices[triangleIboIndex + 1] = vertIdxMarkedStroke + 1;
            indices[triangleIboIndex + 2] = vertIdxMarkedStroke + 3;
            triangleIboIndex += 3;
        }
        
        _indices = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Index, indices.Length, sizeof(int));
        _indices.SetData(indices);
        
        CreateMaterialBuffer();
    }
    
    private void CreateMaterialBuffer()
    {
        
        var materialDataList = new List<GreasePencilRenderer.GpMaterialData>();
        foreach (var mat in greasePencilMaterials)
        {
            var gpuMat = new GreasePencilRenderer.GpMaterialData
            {
                // Populate the C# struct from your GreasePencilSO data.
                stroke_color = new Vector4(mat.stroke_color[0], mat.stroke_color[1], mat.stroke_color[2], 1.0f),
                fill_color = new Vector4(mat.fill_color[0], mat.fill_color[1], mat.fill_color[2], 1.0f),
                fill_mix_color = new Vector4(mat.fill_mix_color[0], mat.fill_mix_color[1], mat.fill_mix_color[2], 1.0f),
                fill_uv_rot_scale = new Vector4(mat.fill_uv_rot_scale[0], mat.fill_uv_rot_scale[1], mat.fill_uv_rot_scale[2], mat.fill_uv_rot_scale[3]),
                // Pack fill_uv_offset and alignment_rot into a single Vector4
                fill_uv_offset_alignment_rot = new Vector4(mat.fill_uv_offset[0], mat.fill_uv_offset[1], mat.alignment_rot[0], mat.alignment_rot[1]),
                stroke_texture_mix = mat.stroke_texture_mix,
                stroke_u_scale = mat.stroke_u_scale,
                fill_texture_mix = mat.fill_texture_mix,
                flag = mat.flag
            };

            materialDataList.Add(gpuMat);
        }
    
        // Create the ComputeBuffer and upload the data.
        _materialBuffer = new ComputeBuffer(materialDataList.Count, GreasePencilRenderer.GpMaterialData.SizeOf);
        _materialBuffer.SetData(materialDataList);
    }
}
