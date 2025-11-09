using UnityEngine;
using System.Runtime.InteropServices;
using UnityEditor;

public class SilhouetteEdgeCalculator : MonoBehaviour
{
    // Assign in the inspector
    public ComputeShader silhouetteShader;
    public MeshFilter sourceMeshFilter; // Assign a GameObject with a MeshFilter
    public Camera viewCamera;

    // --- Compute Buffers ---
    private ComputeBuffer _verticesBuffer;
    private ComputeBuffer _indicesBuffer;
    private ComputeBuffer _adjIndicesBuffer;

    // output by compute, input for stroke rendering
    private ComputeBuffer _strokesBuffer;
    
    // stroke rendering
    GraphicsBuffer _indices;
    
    public Material material;
    
    // --- Struct Definitions (must match HLSL) ---
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct VertexData
    {
        public Vector3 position;
        public Vector3 normal;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct StrokeData
    {
        public Vector3 pos1; // pos[0]
        public Vector3 pos2; // pos[1]
        public uint adj1;    // adj[0]
        public uint adj2;    // adj[1]
        public uint valid;
        
        // Helper to match HLSL float3[2] and uint[2] layout
        // Compute stride explicitly via Marshal.SizeOf to avoid mismatch/padding
        public static int SizeOf => Marshal.SizeOf(typeof(StrokeData));
    }

    private int _kernelHandle;
    private int _faceCount;

    void Start()
    {
        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.LogError("Compute shaders are not supported on this platform.");
            return;
        }

        if (sourceMeshFilter == null || sourceMeshFilter.sharedMesh == null || silhouetteShader == null)
        {
            Debug.LogError("Source MeshFilter (with a Mesh) or Silhouette Shader is not assigned.");
            return;
        }



        // Get kernel handle
        _kernelHandle = silhouetteShader.FindKernel("CSMain");

        // Initialize buffers
        InitializeBuffers();
    }

    void InitializeBuffers()
    {
        // --- 1. Vertex Buffer ---
        Mesh mesh = sourceMeshFilter.sharedMesh;
        Vector3[] positions = mesh.vertices;
        Vector3[] normals = mesh.normals;
        VertexData[] vertexDataArray = new VertexData[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            vertexDataArray[i] = new VertexData
            {
                position = positions[i],
                normal = normals[i]
            };
        }
        _verticesBuffer = new ComputeBuffer(vertexDataArray.Length, Marshal.SizeOf(typeof(VertexData)));
        _verticesBuffer.SetData(vertexDataArray);

        // --- 2. Index Buffer ---
        int[] indices = mesh.triangles;
        _faceCount = indices.Length / 3;
        _indicesBuffer = new ComputeBuffer(indices.Length, sizeof(int));
        _indicesBuffer.SetData(indices);

        // --- 3. Adjacency Buffer ---
        // !!! IMPORTANT !!!
        // You must calculate adjacency information yourself.
        // Unity's Mesh class does not provide this by default.
        // This is a placeholder and WILL NOT WORK without a real
        // adjacency calculation algorithm.
        uint[] adjData = new uint[indices.Length]; 
        // Populate adjData with your adjacency logic...
        // For example: adjData[faceIdx * 3 + 0] = adjacent face to edge v1-v2
        //              adjData[faceIdx * 3 + 1] = adjacent face to edge v2-v0
        //              adjData[faceIdx * 3 + 2] = adjacent face to edge v0-v1
        // (Following the access pattern in your shader snippet)
        
        // Placeholder: setting all to 0 (invalid)
        for(int i=0; i<adjData.Length; i++) adjData[i] = 0; 
        
        _adjIndicesBuffer = new ComputeBuffer(adjData.Length, sizeof(uint));
        _adjIndicesBuffer.SetData(adjData);


        // --- 4. Output Stroke Buffer ---
        // We need one StrokeData element per *face*
        _strokesBuffer = new ComputeBuffer(_faceCount, StrokeData.SizeOf);

        // --- 5. Set Buffers on Shader ---
        // (This only needs to be done once if buffers don't change)
        silhouetteShader.SetBuffer(_kernelHandle, "_Vertices", _verticesBuffer);
        silhouetteShader.SetBuffer(_kernelHandle, "_Indices", _indicesBuffer);
        silhouetteShader.SetBuffer(_kernelHandle, "_AdjIndices", _adjIndicesBuffer);
        silhouetteShader.SetBuffer(_kernelHandle, "_outStrokes", _strokesBuffer);
        silhouetteShader.SetInt("_NumFaces", _faceCount);
        
        indices = new int[_faceCount*6]; // 6 indices per triangle (2 triangles per original triangle for thick line)

        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }
        
        _indices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, indices.Length, sizeof(int));
        _indices.SetData(indices);
    }

    void Update()
    {
        if(viewCamera == null)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if(sceneView != null)
            {
                viewCamera = sceneView.camera;
            }
            else
            {
                viewCamera = Camera.main;
            }
        }
        if (_strokesBuffer == null || viewCamera == null) return;

        // --- 1. Set Per-Frame Uniforms ---
        silhouetteShader.SetVector("_WorldSpaceCameraPos", viewCamera.transform.position);

        // Set object->world and inverse-transpose for normals (use the selected MeshFilter's transform)
        Matrix4x4 objectToWorld = sourceMeshFilter.transform.localToWorldMatrix;
        Matrix4x4 objectToWorldIT = objectToWorld.inverse.transpose;
        silhouetteShader.SetMatrix("_ObjectToWorld", objectToWorld);
        silhouetteShader.SetMatrix("_ObjectToWorldIT", objectToWorldIT);

        // --- 2. Dispatch the Shader ---
        // Calculate thread groups. We need one thread per face.
        // Divide face count by thread group size (64, from HLSL)
        int threadGroups = Mathf.CeilToInt(_faceCount / 64.0f);
        if (threadGroups > 0)
        {
            silhouetteShader.Dispatch(_kernelHandle, threadGroups, 1, 1);
        }

        // --- 3. (Optional) Get Data Back ---
        // You can now use _strokesBuffer on the GPU for rendering (e.g., with DrawProcedural).
        // Or, you can read it back to the CPU for debugging.
        // Reading back every frame is slow!
        
        // Example: Read back for debugging
        StrokeData[] debugStrokes = new StrokeData[_faceCount];
        _strokesBuffer.GetData(debugStrokes);

        for(int i=0; i < debugStrokes.Length; i++)
        {
            if(debugStrokes[i].valid == 1)
            {
                // Draw the line in the editor
                Debug.DrawLine(
                    debugStrokes[i].pos1, 
                    debugStrokes[i].pos2, 
                    Color.red
                );
            }
        }
        
        var matProps = new MaterialPropertyBlock();

        matProps.SetBuffer("_inEdges", _strokesBuffer);
        matProps.SetMatrix("_ObjectToWorld", sourceMeshFilter.transform.localToWorldMatrix);
    
        
        RenderParams rp = new RenderParams(material);
        rp.worldBounds = new Bounds(Vector3.zero, 1000*Vector3.one); // use tighter bounds
        rp.matProps = matProps;
        // Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, _indices, _indices.count);
    }

    void OnDestroy()
    {
        // Release buffers when the object is destroyed
        _verticesBuffer?.Release();
        _indicesBuffer?.Release();
        _adjIndicesBuffer?.Release();
        _strokesBuffer?.Release();
        _indices?.Dispose();
    }
}
