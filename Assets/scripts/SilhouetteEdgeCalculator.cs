using System;
using System.Collections.Generic;
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

    public int displayInt=0;
    
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
        public Vector3 faceNormal; // Added: world-space face normal
        public uint minLeftPoint;
        public uint minRightPoint;
        
        // Helper to match HLSL float3[2] and uint[2] layout
        // Compute stride explicitly via Marshal.SizeOf to avoid mismatch/padding
        public static int SizeOf => Marshal.SizeOf(typeof(StrokeData));
    }

    private int _kernelHandle;
    private int _faceCount;

    // New: compute shader for head/tail finding
    public ComputeShader findHeadTailShader;

    // Buffers used by FindHeadTail pass
    private ComputeBuffer _labelsBuffer; // uint per stroke
    private ComputeBuffer _compMinXBuffer; // uint per stroke
    private ComputeBuffer _compMinYBuffer; // uint per stroke
    private ComputeBuffer _compMinZBuffer; // uint per stroke

    private int _initPjKernel;
    private int _findMinPjKernel;
    private int _findBuildPointersKernel;
    private int _findMinXKernel;
    private int _findMinYKernel;
    private int _findMinZKernel;
    private int _findAssignKernel;

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
        if (findHeadTailShader != null)
        {
            _initPjKernel = findHeadTailShader.FindKernel("InitPJ");
            _findMinPjKernel = findHeadTailShader.FindKernel("FindMinPJ");
            // _findBuildPointersKernel = findHeadTailShader.FindKernel("BuildPointers");
            // _findMinXKernel = findHeadTailShader.FindKernel("ComputeMinX");
            // _findMinYKernel = findHeadTailShader.FindKernel("ComputeMinY");
            // _findMinZKernel = findHeadTailShader.FindKernel("ComputeMinZ");
            // _findAssignKernel = findHeadTailShader.FindKernel("AssignEndpoints");
        }

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
        uint[] adjData = CalculateAdjacency(indices);
        
        _adjIndicesBuffer = new ComputeBuffer(adjData.Length, sizeof(uint));
        _adjIndicesBuffer.SetData(adjData);


        // --- 4. Output Stroke Buffer ---
        // We need one StrokeData element per *face*
        _strokesBuffer = new ComputeBuffer(_faceCount, StrokeData.SizeOf);

        // buffers for FindHeadTail
        _labelsBuffer = new ComputeBuffer(_faceCount, sizeof(uint));
        _compMinXBuffer = new ComputeBuffer(_faceCount, sizeof(uint));
        _compMinYBuffer = new ComputeBuffer(_faceCount, sizeof(uint));
        _compMinZBuffer = new ComputeBuffer(_faceCount, sizeof(uint));

        // --- 5. Set Buffers on Shader ---
        // (This only needs to be done once if buffers don't change)
        silhouetteShader.SetBuffer(_kernelHandle, "_Vertices", _verticesBuffer);
        silhouetteShader.SetBuffer(_kernelHandle, "_Indices", _indicesBuffer);
        silhouetteShader.SetBuffer(_kernelHandle, "_AdjIndices", _adjIndicesBuffer);
        silhouetteShader.SetBuffer(_kernelHandle, "_outStrokes", _strokesBuffer);
        silhouetteShader.SetInt("_NumFaces", _faceCount);

        if (findHeadTailShader != null)
        {
            findHeadTailShader.SetInt("_NumFaces", _faceCount);

            findHeadTailShader.SetBuffer(_initPjKernel, "_strokes", _strokesBuffer);
            findHeadTailShader.SetBuffer(_initPjKernel, "_next", _labelsBuffer);

            // Set buffers for other kernels as well
            findHeadTailShader.SetBuffer(_findMinPjKernel, "_next", _labelsBuffer);
            findHeadTailShader.SetBuffer(_findMinPjKernel, "_strokes", _strokesBuffer);
        }
        
        indices = new int[_faceCount*6];
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
        DebugDraw();

        DebugStrokes();
        // --- Run FindHeadTail GPU pass if available ---
        if (findHeadTailShader != null)
        {
            // Init labels
            findHeadTailShader.Dispatch(_initPjKernel, threadGroups, 1, 1);

            // Pointer jumping rounds: run a few times to converge
            // Build initial pointers from adjacency matches
            findHeadTailShader.Dispatch(_findBuildPointersKernel, threadGroups, 1, 1);
            DebugStrokes();
            for (int i = 0; i < 8; ++i)
            {
                findHeadTailShader.Dispatch(_findMinPjKernel, threadGroups, 1, 1);

                DebugStrokes();
            }
        }


        var matProps = new MaterialPropertyBlock();

        matProps.SetBuffer("_inEdges", _strokesBuffer);
        matProps.SetMatrix("_ObjectToWorld", sourceMeshFilter.transform.localToWorldMatrix);
    
        
        RenderParams rp = new RenderParams(material);
        rp.worldBounds = new Bounds(Vector3.zero, 1000*Vector3.one); // use tighter bounds
        rp.matProps = matProps;
        Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, _indices, _indices.count);
    }

    private void DebugStrokes()
    {
        StrokeData[] strokes = new StrokeData[_faceCount];
        _strokesBuffer.GetData(strokes);

        // Example: log the first valid stroke found
        for (int j = 0; j < strokes.Length; j++)
        {
            if (strokes[j].valid == 1)
            {
                Debug.Log($"Stroke[{j}] pos1={strokes[j].pos1} pos2={strokes[j].pos2} adj1={strokes[j].adj1} adj2={strokes[j].adj2} min={strokes[j].minLeftPoint},{strokes[j].minRightPoint}");
            }
        }
    }

    private void DebugDraw()
    {
        StrokeData[] debugStrokes = new StrokeData[_faceCount];
        _strokesBuffer.GetData(debugStrokes);

        const float EPS = 1e-6f;
        const uint INVALID = uint.MaxValue;
        
        for(int i=0; i < debugStrokes.Length; i++)
        {
            if (i != displayInt)
            {
                continue;
            }
            var adj1 = debugStrokes[i].adj1;
            var adj2 = debugStrokes[i].adj2;

            var pos1 = debugStrokes[i].pos1;
            var pos2 = debugStrokes[i].pos2; 
            
            // Defaults if no neighbor found
            Vector3 pos0 = pos1;
            Vector3 pos3 = pos2;
        
            // Find pos0 from adjacent face adj1:
            // If adj1 is valid, pick the adjacent face vertex that is NOT one of the current edge endpoints.
            if (adj1 != INVALID && adj1 < debugStrokes.Length)
            {
                var n = debugStrokes[(int)adj1];
                // neighbor has pos1/pos2 for the shared edge; choose the neighbor's endpoint that is NOT equal to pos1/pos2.
                if (Vector3.Distance(n.pos1, pos1) < EPS || Vector3.Distance(n.pos1, pos2) < EPS)
                {
                    pos0 = n.pos2;
                }
                else
                {
                    pos0 = n.pos1;
                }
            }
        
            // Find pos3 from adjacent face adj2:
            if (adj2 != INVALID && adj2 < debugStrokes.Length)
            {
                var n = debugStrokes[(int)adj2];
                if (Vector3.Distance(n.pos1, pos1) < EPS || Vector3.Distance(n.pos1, pos2) < EPS)
                {
                    pos3 = n.pos2;
                }
                else
                {
                    pos3 = n.pos1;
                }
            }
            
            if(debugStrokes[i].valid == 1)
            {
                // Draw the line in the editor (original edge)
                Debug.DrawLine(pos1, pos2, Color.red);
        
                // Optional: draw computed adjacent endpoints for visualization
                Debug.DrawLine(pos0, pos1, Color.green);
                Debug.DrawLine(pos2, pos3, Color.green);
            }
        }
    }

    void OnDestroy()
    {
        // Release buffers when the object is destroyed
        _verticesBuffer?.Release();
        _indicesBuffer?.Release();
        _adjIndicesBuffer?.Release();
        _strokesBuffer?.Release();
        _labelsBuffer?.Release();
        _compMinXBuffer?.Release();
        _compMinYBuffer?.Release();
        _compMinZBuffer?.Release();
        _indices?.Dispose();
    }
    
    private static uint[] CalculateAdjacency(int[] triangles)
    {
        if (triangles == null) throw new ArgumentNullException(nameof(triangles));
        if (triangles.Length % 3 != 0) throw new ArgumentException("Triangle array length must be a multiple of 3.", nameof(triangles));
    
        int faceCount = triangles.Length / 3;
        uint[] adj = new uint[triangles.Length];
        const uint INVALID = uint.MaxValue;
    
        // initialize all to INVALID
        for (int i = 0; i < adj.Length; i++) adj[i] = INVALID;
    
        // Map an undirected edge key -> list of face indices that contain that edge
        var edgeToFaces = new Dictionary<long, List<int>>(triangles.Length);
    
        for (int f = 0; f < faceCount; f++)
        {
            int baseIdx = f * 3;
            int v0 = triangles[baseIdx + 0];
            int v1 = triangles[baseIdx + 1];
            int v2 = triangles[baseIdx + 2];
    
            // three edges: (v0,v1), (v1,v2), (v2,v0)
            long[] keys = new long[3];
            keys[0] = ((long)Math.Min(v0, v1) << 32) | (uint)Math.Max(v0, v1);
            keys[1] = ((long)Math.Min(v1, v2) << 32) | (uint)Math.Max(v1, v2);
            keys[2] = ((long)Math.Min(v2, v0) << 32) | (uint)Math.Max(v2, v0);
    
            for (int e = 0; e < 3; e++)
            {
                if (!edgeToFaces.TryGetValue(keys[e], out var list))
                {
                    list = new List<int>(2);
                    edgeToFaces[keys[e]] = list;
                }
                list.Add(f);
            }
        }
    
        // Fill adjacency: for each face edge, pick the other face that shares the edge (if any)
        for (int f = 0; f < faceCount; f++)
        {
            int baseIdx = f * 3;
            int v0 = triangles[baseIdx + 0];
            int v1 = triangles[baseIdx + 1];
            int v2 = triangles[baseIdx + 2];
    
            long[] keys = new long[3];
            keys[0] = ((long)Math.Min(v0, v1) << 32) | (uint)Math.Max(v0, v1);
            keys[1] = ((long)Math.Min(v1, v2) << 32) | (uint)Math.Max(v1, v2);
            keys[2] = ((long)Math.Min(v2, v0) << 32) | (uint)Math.Max(v2, v0);
    
            for (int e = 0; e < 3; e++)
            {
                var faces = edgeToFaces[keys[e]];
                uint neighbor = INVALID;
    
                // find a face in the list that is not the current face
                for (int k = 0; k < faces.Count; k++)
                {
                    int other = faces[k];
                    if (other != f)
                    {
                        neighbor = (uint)other;
                        break; // for non-manifold edges with >2 faces, pick the first other face
                    }
                }
    
                adj[baseIdx + e] = neighbor;
            }
        }
    
        return adj;
    }
    
}
