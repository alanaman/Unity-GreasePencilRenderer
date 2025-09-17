using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Converts a mesh so its index buffer encodes adjacency information
/// for use with geometry shaders that declare `triangleadj`.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
public class MeshAdjacencyBuilder : MonoBehaviour
{
    [ContextMenu("Build Adjacency Mesh")]
    public void BuildAdjacencyMesh()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        Mesh original = mf.sharedMesh;
        if (original == null)
        {
            Debug.LogError("No mesh found!");
            return;
        }

        Mesh adjMesh = BuildAdjacency(original);
        mf.sharedMesh = adjMesh;
    }

    Mesh BuildAdjacency(Mesh mesh)
    {
        // Get data
        Vector3[] verts = mesh.vertices;
        int[] indices = mesh.triangles;

        // Build edge → opposite vertex map
        var edgeToVertex = new Dictionary<(int, int), int>();
        for (int i = 0; i < indices.Length; i += 3)
        {
            int v0 = indices[i];
            int v1 = indices[i + 1];
            int v2 = indices[i + 2];

            AddEdge(edgeToVertex, v0, v1, v2);
            AddEdge(edgeToVertex, v1, v2, v0);
            AddEdge(edgeToVertex, v2, v0, v1);
        }

        // New index buffer: 6 indices per triangle
        List<int> newIndices = new List<int>();
        for (int i = 0; i < indices.Length; i += 3)
        {
            int v0 = indices[i];
            int v1 = indices[i + 1];
            int v2 = indices[i + 2];

            int a0 = FindAdj(edgeToVertex, v0, v1);
            int a1 = FindAdj(edgeToVertex, v1, v2);
            int a2 = FindAdj(edgeToVertex, v2, v0);

            newIndices.Add(v0); newIndices.Add(a0);
            newIndices.Add(v1); newIndices.Add(a1);
            newIndices.Add(v2); newIndices.Add(a2);
        }

        // Build new mesh
        Mesh adjMesh = new Mesh();
        adjMesh.name = mesh.name + "_Adjacency";
        adjMesh.vertices = verts;
        adjMesh.normals = mesh.normals;
        adjMesh.uv = mesh.uv;
        adjMesh.uv2 = mesh.uv2;
        adjMesh.colors = mesh.colors;
        adjMesh.tangents = mesh.tangents;
        adjMesh.SetIndices(newIndices.ToArray(), MeshTopology.Triangles, 0);
        return adjMesh;
    }

    void AddEdge(Dictionary<(int, int), int> map, int v0, int v1, int opposite)
    {
        var key = (Mathf.Min(v0, v1), Mathf.Max(v0, v1));
        if (!map.ContainsKey(key))
            map[key] = opposite;
    }

    int FindAdj(Dictionary<(int, int), int> map, int v0, int v1)
    {
        var key = (Mathf.Min(v0, v1), Mathf.Max(v0, v1));
        if (map.TryGetValue(key, out int adj))
            return adj;

        // No adjacent triangle (boundary edge) → duplicate one of the edge vertices
        return v0;
    }
}
