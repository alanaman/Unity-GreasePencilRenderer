using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public class SilhouetteRenderer : MonoBehaviour
{
    [StructLayout(LayoutKind.Sequential)]
	public struct VertexData
	{
		public Vector3 position;
		public Vector3 normal;
	}
    [SerializeField] private Mesh sourceMesh;

    GraphicsBuffer _indices;
    GraphicsBuffer _meshIndices;
    GraphicsBuffer _adjTriIndices;
    GraphicsBuffer _verts;
    
    public Material material;
    

    void Start()
    {
        if (!sourceMesh)
        {
            Debug.LogError("Source Mesh is not assigned.", this);
            Destroy(this);
            return;
        }
        InitBuffers();
    }

    private void Update()
    {
        // Create the MaterialPropertyBlock to hold our per-draw data.
        var matProps = new MaterialPropertyBlock();

        // Set your custom data buffers
        matProps.SetBuffer("_Vertices", _verts);
        matProps.SetBuffer("_MeshIndices", _meshIndices);
        // matProps.SetBuffer("_Color", _cols);
        // matProps.SetBuffer("gp_materials", _materialBuffer);

        // Set the standard object-to-world matrix uniform.
        matProps.SetMatrix("_ObjectToWorld", transform.localToWorldMatrix);
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
        _verts?.Dispose();
        _verts = null;
        _adjTriIndices?.Dispose();
        _adjTriIndices = null;
    }

    void InitBuffers()
    {
        ReleaseBuffers();

        var meshIndices = sourceMesh.GetIndices(0);
        var indices = new int[meshIndices.Length/3*6]; // 6 indices per triangle (2 triangles per original triangle for thick line)
        // for(int i=0; i<meshIndices.Length; i+=3)
        // {
        //     
        //     indices[i*2+0] = (meshIndices[i+0] << 1);
        //     indices[i*2+1] = (meshIndices[i+1] << 1);
        //     indices[i*2+2] = (meshIndices[i+2] << 1);
        //     indices[i*2+3] = (meshIndices[i+0] << 1) | 1;
        //     indices[i*2+4] = (meshIndices[i+1] << 1) | 1;
        //     indices[i*2+5] = (meshIndices[i+2] << 1) | 1;
        // }

        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }
        
        _indices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, indices.Length, sizeof(int));
        _indices.SetData(indices);
        
        
        _meshIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, meshIndices.Length, sizeof(int));
        _meshIndices.SetData(meshIndices);
        
        // 2. Set up the vertex data buffer (positions and normals)
        Vector3[] vertices = sourceMesh.vertices;
        Vector3[] normals = sourceMesh.normals;
        
        // We need to interleave the vertex data into our custom struct
        var vertexData = new VertexData[sourceMesh.vertexCount];
        for (int i = 0; i < sourceMesh.vertexCount; i++)
        {
            vertexData[i] = new VertexData
            {
                position = vertices[i],
                normal = normals[i]
            };
        }
        
        _verts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertexData.Length, sizeof(float) * 6); // Vector3 + Vector3 = 6 floats
        _verts.SetData(vertexData);

        // 3. Calculate and set up the adjacency index buffer
        int[] adjIndices = CalculateAdjacency(indices);
        _adjTriIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, adjIndices.Length, sizeof(int));
        _adjTriIndices.SetData(adjIndices);
    }

    /// <summary>
    /// Calculates the adjacency information for a mesh.
    /// For each triangle, it finds the neighboring triangle for each of its three edges.
    /// </summary>
    /// <param name="indices">The mesh's index buffer.</param>
    /// <returns>An array containing adjacency data.</returns>
    private int[] CalculateAdjacency(int[] indices)
    {
        // Key: An edge represented by its two vertex indices (always ordered min, max to be unique).
        // Value: A list of triangle indices that share this edge.
        var edgeToTriangleMap = new Dictionary<(int, int), List<int>>();
        
        // --- PASS 1: Build the edge-to-triangle map ---
        // Iterate through each triangle in the mesh
        for (int i = 0; i < indices.Length; i += 3)
        {
            int triIndex = i / 3;
            int v0 = indices[i];
            int v1 = indices[i + 1];
            int v2 = indices[i + 2];
            
            // Define the three edges of the triangle
            (int, int) edgeA = (Mathf.Min(v0, v1), Mathf.Max(v0, v1));
            (int, int) edgeB = (Mathf.Min(v1, v2), Mathf.Max(v1, v2));
            (int, int) edgeC = (Mathf.Min(v2, v0), Mathf.Max(v2, v0));

            // Add the current triangle to the list for each of its edges
            if (!edgeToTriangleMap.ContainsKey(edgeA)) edgeToTriangleMap[edgeA] = new List<int>();
            edgeToTriangleMap[edgeA].Add(triIndex);
            
            if (!edgeToTriangleMap.ContainsKey(edgeB)) edgeToTriangleMap[edgeB] = new List<int>();
            edgeToTriangleMap[edgeB].Add(triIndex);
            
            if (!edgeToTriangleMap.ContainsKey(edgeC)) edgeToTriangleMap[edgeC] = new List<int>();
            edgeToTriangleMap[edgeC].Add(triIndex);
        }

        // --- PASS 2: Use the map to find adjacent triangles ---
        var adjacencyIndices = new int[indices.Length];
        
        // Iterate through each triangle again
        for (int i = 0; i < indices.Length; i += 3)
        {
            int triIndex = i / 3;
            int v0 = indices[i];
            int v1 = indices[i + 1];
            int v2 = indices[i + 2];

            // Define the edges again
            (int, int) edgeA = (Mathf.Min(v0, v1), Mathf.Max(v0, v1)); // Edge opposite to v2
            (int, int) edgeB = (Mathf.Min(v1, v2), Mathf.Max(v1, v2)); // Edge opposite to v0
            (int, int) edgeC = (Mathf.Min(v2, v0), Mathf.Max(v2, v0)); // Edge opposite to v1
            
            // Find the adjacent triangle for each edge.
            // The adjacent triangle index is stored in the position of the opposite vertex.
            // This is a common convention for geometry shaders.
            adjacencyIndices[i + 2] = FindAdjacentTriangle(edgeToTriangleMap[edgeA], triIndex); // For edge v0-v1
            adjacencyIndices[i] = FindAdjacentTriangle(edgeToTriangleMap[edgeB], triIndex);     // For edge v1-v2
            adjacencyIndices[i + 1] = FindAdjacentTriangle(edgeToTriangleMap[edgeC], triIndex); // For edge v2-v0
        }
        
        return adjacencyIndices;
    }

    /// <summary>
    /// Helper to find the "other" triangle in a list of triangles sharing an edge.
    /// </summary>
    /// <param name="sharedTriangles">List of triangles sharing an edge (usually 1 or 2).</param>
    /// <param name="currentTriangleIndex">The index of the triangle we are processing.</param>
    /// <returns>The index of the adjacent triangle, or -1 if none exists (i.e., it's a border edge).</returns>
    private int FindAdjacentTriangle(List<int> sharedTriangles, int currentTriangleIndex)
    {
        foreach (int tri in sharedTriangles)
        {
            if (tri != currentTriangleIndex)
            {
                return tri;
            }
        }
        // If no other triangle is found, it's a boundary edge.
        return -1;
    }
}
