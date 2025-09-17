using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class MeshIndexToUV : MonoBehaviour
{
    [ContextMenu("Apply Vertex Indices To UVs")]
    void ApplyIndices()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        Mesh mesh = mf.sharedMesh;

        if (mesh == null)
        {
            Debug.LogError("No mesh found on MeshFilter!");
            return;
        }

        int vertexCount = mesh.vertexCount;
        Vector2[] uvs = new Vector2[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            // Store the vertex index in UV.x
            uvs[i] = new Vector2(i, 0);
        }
        Mesh meshCopy = Instantiate(mesh);
        meshCopy.SetUVs(2, new System.Collections.Generic.List<Vector2>(uvs));
        // Optional: rename so it’s clear this is a copy
        meshCopy.name = mesh.name + "_Copy";

        // Assign back so we are not editing the original
        mf.mesh = meshCopy;
    }
    
    [ContextMenu("Create and Bind Texture Attributes")]
    void CreateAndBindTextureAttributes()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        Mesh mesh = mf.sharedMesh;
        
        var material = GetComponent<Renderer>().sharedMaterial;
        
        int vertexCount = mesh.vertexCount;
        Texture2D attributeTex = new Texture2D(vertexCount, 1, TextureFormat.RGBAFloat, false, true);
        attributeTex.filterMode = FilterMode.Point;

// Fill it with your data
        Color[] data = new Color[vertexCount];
        for (int i = 0; i < vertexCount; i++) {
            Vector3 velocity;
            velocity.x = (i%3 == 0)?1:0;
            velocity.y = (i%3 == 1)?1:0;
            velocity.z = (i%3 == 2)?1:0;
            data[i] = new Color(velocity.x, velocity.y, velocity.z, 1);
            // data[i] = new Color(0, 0, 0, 1);
        }
        
        ComputeBuffer buffer = new ComputeBuffer(vertexCount, sizeof(float) * 4);
        buffer.SetData(data);
        material.SetBuffer("_AttrBuffer", buffer);
        material.SetInt("_VertexCount", mesh.vertexCount);
    }
}