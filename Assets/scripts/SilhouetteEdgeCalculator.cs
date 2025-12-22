using System;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;
using UnityEditor;

public class SilhouetteEdgeCalculator : MonoBehaviour
{
    public ComputeShader silhouetteShader;
    public MeshFilter sourceMeshFilter;
    public Camera viewCamera;

    private ComputeBuffer _verticesBuffer;
    private ComputeBuffer _indicesBuffer;
    private ComputeBuffer _adjIndicesBuffer;

    // output by compute, input for stroke rendering
    private ComputeBuffer _strokesBuffer;

    
    // GraphicsBuffer _indices;
    
    public Material material;

    public int displayInt=0;
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct VertexData
    {
        public Vector3 position;
        public Vector3 normal;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct StrokeData
    {
        public Vector3 pos;
        public uint adj;
        public uint valid;
        public Vector3 faceNormal;
        public uint minPoint;
        public uint rank;            // hop count to tail
        public uint isCyclic;
        public float distFromTail;   // cumulative geometric distance to tail (0 at tail)
    
        public uint isChild; // 1 if this stroke point has a parent, 0 otherwise
        public uint totalStrokeLength; // total length of the stroke that contains this point
    
        public uint strokeIdx; // ID of the stroke this point belongs to
        public uint strokePointsOffset; // Offset to the stroke points array
        
        
        // Helper to match HLSL layout
        public static int SizeOf => Marshal.SizeOf(typeof(StrokeData));
    }

    private int _kernelHandle;
    private int _faceCount;

    public ComputeShader findHeadTailShader;

    private ComputeBuffer _nextPointerBuffer;


    private int _initPjKernel;
    private int _findMinPjKernel;
    private int _listRankKernel;
    private int _resetNextKernel;
    private int _initDistancesKernel;

    //TODO: this is supposed to be in GreasePencil format
    public GraphicsBuffer DenseStrokesBuffer;
    public GraphicsBuffer ColorBuffer;
    public ComputeShader sorterShader;
    
    private int _setStrokeLengthAtTailKernel;
    private int _calcStrokeOffsetsKernel;
    private int _invalidateEntriesKernel;
    private int _sorterKernel;

    // Add two 1-element buffers to be used as UAV atomic counters by the compute shader
    private ComputeBuffer _numStrokesCounterBuffer;
    private ComputeBuffer _numStrokePointsCounterBuffer;
    
    public float radiusMultiplier = 1.0f;
    
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
            _listRankKernel = findHeadTailShader.FindKernel("ListRankPJ");
            _resetNextKernel = findHeadTailShader.FindKernel("ResetNextPointer");
            _initDistancesKernel = findHeadTailShader.FindKernel("InitDistances");
        }

        if (sorterShader != null)
        {
            _setStrokeLengthAtTailKernel = sorterShader.FindKernel("SetStrokeLengthAtTail");
            _calcStrokeOffsetsKernel = sorterShader.FindKernel("CalculateArrayOffsets");
            _invalidateEntriesKernel = sorterShader.FindKernel("InvalidateEntries");
            _sorterKernel = sorterShader.FindKernel("MoveToDenseArray");
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
        
        silhouetteShader.SetInt("_NumFaces", _faceCount);
        

        // --- 3. Adjacency Buffer ---
        uint[] adjData = CalculateAdjacency(indices);
        
        _adjIndicesBuffer = new ComputeBuffer(adjData.Length, sizeof(uint));
        _adjIndicesBuffer.SetData(adjData);

        // --- 4. Output Stroke Buffer ---
        _strokesBuffer = new ComputeBuffer(_faceCount, StrokeData.SizeOf);
        //TODO: find a tighter limit for these buffer sizes
        DenseStrokesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2*_faceCount, GreasePencilRenderer.GreasePencilStrokeVert.SizeOf);
        ColorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2*_faceCount, GreasePencilRenderer.GreasePencilColorVert.SizeOf);

        // buffers for FindHeadTail
        _nextPointerBuffer = new ComputeBuffer(_faceCount, sizeof(uint));

        // --- 5. Create 1-element atomic counter buffers used by the sorter compute shader ---
        if (sorterShader != null)
        {
            // 1 element uint buffers used as UAV atomic counters
            _numStrokesCounterBuffer = new ComputeBuffer(1, sizeof(uint));
            _numStrokePointsCounterBuffer = new ComputeBuffer(1, sizeof(uint));
            // initialize to zero
            _numStrokesCounterBuffer.SetData(new uint[] { 0u });
            _numStrokePointsCounterBuffer.SetData(new uint[] { 0u });
        }

        // --- 6. Set Buffers on Shader ---
        silhouetteShader.SetBuffer(_kernelHandle, "_Vertices", _verticesBuffer);
        silhouetteShader.SetBuffer(_kernelHandle, "_Indices", _indicesBuffer);
        silhouetteShader.SetBuffer(_kernelHandle, "_AdjIndices", _adjIndicesBuffer);
        silhouetteShader.SetBuffer(_kernelHandle, "_outStrokes", _strokesBuffer);
        silhouetteShader.SetInt("_NumFaces", _faceCount);

        if (findHeadTailShader != null)
        {
            findHeadTailShader.SetInt("_NumFaces", _faceCount);
        
            findHeadTailShader.SetBuffer(_initPjKernel, "_strokes", _strokesBuffer);
            findHeadTailShader.SetBuffer(_initPjKernel, "_nextPointer", _nextPointerBuffer);
        
            findHeadTailShader.SetBuffer(_findMinPjKernel, "_nextPointer", _nextPointerBuffer);
            findHeadTailShader.SetBuffer(_findMinPjKernel, "_strokes", _strokesBuffer);
        
            findHeadTailShader.SetBuffer(_listRankKernel, "_nextPointer", _nextPointerBuffer);
            findHeadTailShader.SetBuffer(_listRankKernel, "_strokes", _strokesBuffer);
            
            findHeadTailShader.SetBuffer(_resetNextKernel, "_nextPointer", _nextPointerBuffer);
            findHeadTailShader.SetBuffer(_resetNextKernel, "_strokes", _strokesBuffer);
            
            findHeadTailShader.SetBuffer(_initDistancesKernel, "_strokes", _strokesBuffer);
            
        }

        if (sorterShader != null)
        {
            sorterShader.SetInt("_NumFaces", _faceCount);
            
            sorterShader.SetBuffer(_setStrokeLengthAtTailKernel, "_strokes", _strokesBuffer);
            sorterShader.SetBuffer(_calcStrokeOffsetsKernel, "_strokes", _strokesBuffer);
            sorterShader.SetBuffer(_calcStrokeOffsetsKernel, "numStrokesCounter", _numStrokesCounterBuffer);
            sorterShader.SetBuffer(_calcStrokeOffsetsKernel, "numStrokePointsCounter", _numStrokePointsCounterBuffer);
            sorterShader.SetBuffer(_invalidateEntriesKernel, "_denseArray", DenseStrokesBuffer);

            sorterShader.SetBuffer(_sorterKernel, "_strokes", _strokesBuffer);
            sorterShader.SetBuffer(_sorterKernel, "_denseArray", DenseStrokesBuffer);
            sorterShader.SetBuffer(_sorterKernel, "_colorArray", ColorBuffer);
        }
        
        // indices = new int[_faceCount*6];
        // for (int i = 0; i < indices.Length; i++)
        // {
        //     indices[i] = i;
        // }
        //
        // _indices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, indices.Length, sizeof(int));
        // _indices.SetData(indices);
    }

    public void CalculateEdges()
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
        int threadGroups = Mathf.CeilToInt(_faceCount / 64.0f);
        if (threadGroups > 0)
        {
            silhouetteShader.Dispatch(_kernelHandle, threadGroups, 1, 1);
        }
        DebugDraw();

        if (findHeadTailShader != null)
        {
            // Initialize minPoint + next pointers + initial distances
            findHeadTailShader.Dispatch(_initPjKernel, threadGroups, 1, 1);

            // Propagate minima only (using pointer jumping on nextPointer). This modifies _nextPointer.
            for (int i = 0; i < 6; ++i)
            {
                findHeadTailShader.Dispatch(_findMinPjKernel, threadGroups, 1, 1);
            }
            DebugStrokes();

            // Restore original successor links before computing ranking so ranking is based on original adjacency.
            findHeadTailShader.Dispatch(_resetNextKernel, threadGroups, 1, 1);

            // Reset only distances/rank, keep minPoint
            findHeadTailShader.Dispatch(_initDistancesKernel, threadGroups, 1, 1);
            DebugStrokes();

            // Perform list ranking pointer jumping passes.
            for (int i = 0; i < 8; ++i)
            {
                findHeadTailShader.Dispatch(_listRankKernel, threadGroups, 1, 1);
            }
            DebugStrokes(); 
        }

        if (sorterShader != null)
        {
            if (_numStrokesCounterBuffer != null && _numStrokePointsCounterBuffer != null)
            {
                _numStrokesCounterBuffer.SetData(new uint[] { 0u });
                _numStrokePointsCounterBuffer.SetData(new uint[] { 0u });
            }
            
            sorterShader.Dispatch(_setStrokeLengthAtTailKernel, threadGroups, 1, 1);
            sorterShader.Dispatch(_calcStrokeOffsetsKernel, threadGroups, 1, 1);
            DebugStrokes();
            sorterShader.Dispatch(_invalidateEntriesKernel, threadGroups, 1, 1);
            
            sorterShader.SetFloat("_radiusMultiplier", radiusMultiplier);
            sorterShader.Dispatch(_sorterKernel, threadGroups, 1, 1);
            DebugGp();
        }

        var matProps = new MaterialPropertyBlock();

        matProps.SetBuffer("_inEdges", _strokesBuffer);
        matProps.SetMatrix("_ObjectToWorld", sourceMeshFilter.transform.localToWorldMatrix);
    
        
        RenderParams rp = new RenderParams(material);
        rp.worldBounds = new Bounds(Vector3.zero, 1000*Vector3.one); // use tighter bounds
        rp.matProps = matProps;
        // Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, _indices, _indices.count);
    }

    private void DebugStrokes()
    {
        StrokeData[] strokes = new StrokeData[_faceCount];
        _strokesBuffer.GetData(strokes);

        // Example: log a subset
        int printCount = 0;
        for (int j = 0; j < strokes.Length && printCount < 10; j++)
        {
            if (strokes[j].valid == 1)
            {
                Debug.Log($"Stroke[{j}] pos={strokes[j].pos} adj={strokes[j].adj} minPoint={strokes[j].minPoint} rank={strokes[j].rank} dist={strokes[j].distFromTail:F4}");
                printCount++;
            }
        }
        ValidateRanking(strokes);
    }

    private void DebugGp()
    {
        var gpStrokes = new GreasePencilRenderer.GreasePencilStrokeVert[2*_faceCount];
        DenseStrokesBuffer.GetData(gpStrokes);
        
        for (int j = 0; j < gpStrokes.Length; j++)
        {
            Debug.Log($"GP Stroke[{j}] pos={gpStrokes[j].pos} mat={gpStrokes[j].mat} strokePointIdx={gpStrokes[j].point_id}");
        }
    }

    private void ValidateRanking(StrokeData[] strokes)
    {
        if (strokes == null || strokes.Length == 0) return;
        int countValid = 0;
        int countTail = 0;
        float maxDist = 0f;
        int orderingViolations = 0;
        const uint INVALID = uint.MaxValue;

        for (int i = 0; i < strokes.Length; i++)
        {
            if (strokes[i].valid == 0) continue;
            countValid++;
            if (strokes[i].distFromTail > maxDist) maxDist = strokes[i].distFromTail;
            if (i == strokes[i].minPoint)
            {
                countTail++;
                continue;
            }
            uint succ = strokes[i].adj;
            if (succ != INVALID && succ < strokes.Length && strokes[succ].valid == 1)
            {
                // Expect successor to be closer or equal to tail (strictly smaller distance unless equal in degenerate case)
                if (!(strokes[i].distFromTail >= strokes[succ].distFromTail))
                {
                    orderingViolations++;
                }
            }
        }

        Debug.Log($"[Ranking Validation] valid={countValid} tails={countTail} maxDist={maxDist:F4} orderingViolations={orderingViolations}");
    }

    private void DebugDraw()
    {
        StrokeData[] debugStrokes = new StrokeData[_faceCount];
        _strokesBuffer.GetData(debugStrokes);

        const uint INVALID = uint.MaxValue;
        
        for(int i=0; i < debugStrokes.Length; i++)
        {
            if (i != displayInt)
            {
                continue;
            }
            var adj1 = debugStrokes[i].adj;

            var pos1 = debugStrokes[i].pos;
            var pos2 = pos1;
            var pos3 = pos2;
            if (adj1 != INVALID && adj1 < debugStrokes.Length)
            {
                pos2 = debugStrokes[adj1].pos;
                var adj2 = debugStrokes[adj1].adj;

                pos3 = pos2;
                if (adj2 != INVALID && adj2 < debugStrokes.Length)
                {
                    pos3 = debugStrokes[adj2].pos;
                }
            }
            
            if(debugStrokes[i].valid == 1)
            {
                Debug.DrawLine(pos1, pos2, Color.red);

                // Debug.DrawLine(pos0, pos1, Color.green);
                Debug.DrawLine(pos2, pos3, Color.green);
            }
        }
    }

    void OnDestroy()
    {
        _verticesBuffer?.Release();
        _indicesBuffer?.Release();
        _adjIndicesBuffer?.Release();
        _strokesBuffer?.Release();
        _nextPointerBuffer?.Release();
        _numStrokesCounterBuffer?.Release();
        _numStrokePointsCounterBuffer?.Release();
        DenseStrokesBuffer?.Release();
        ColorBuffer?.Release();
        // _indices?.Dispose();
    }
    
    private static uint[] CalculateAdjacency(int[] triangles)
    {
        if (triangles == null) throw new ArgumentNullException(nameof(triangles));
        if (triangles.Length % 3 != 0) throw new ArgumentException("Triangle array length must be a multiple of 3.", nameof(triangles));
    
        int faceCount = triangles.Length / 3;
        uint[] adj = new uint[triangles.Length];
        const uint INVALID = uint.MaxValue;
    
        for (int i = 0; i < adj.Length; i++) adj[i] = INVALID;
    
        var edgeToFaces = new Dictionary<long, List<int>>(triangles.Length);
        // Map an undirected edge key -> list of face indices that contain that edge
    
        for (int f = 0; f < faceCount; f++)
        {
            int baseIdx = f * 3;
            int v0 = triangles[baseIdx + 0];
            int v1 = triangles[baseIdx + 1];
            int v2 = triangles[baseIdx + 2];
    
            long[] keys = new long[3];
            // three edges: (v0,v1), (v1,v2), (v2,v0)
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
    
        for (int f = 0; f < faceCount; f++)
        // Fill adjacency: for each face edge, pick the other face that shares the edge (if any)
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

                for (int k = 0; k < faces.Count; k++)
                    // find a face in the list that is not the current face
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
