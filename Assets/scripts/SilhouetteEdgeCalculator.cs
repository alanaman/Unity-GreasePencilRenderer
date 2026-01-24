using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using System.Runtime.InteropServices;
using DefaultNamespace;
using UnityEditor;
using System.Linq;

public class SilhouetteEdgeCalculator : MonoBehaviour, IGreasePencilEdgeCalculator
{

    // Constants
    private const int ADJ_NONE = -1;
    private static readonly int WorldSpaceCameraPos = Shader.PropertyToID("_WorldSpaceCameraPos");
    private static readonly int ObjectToWorldIt = Shader.PropertyToID("_ObjectToWorldIT");
    private static readonly int ObjectToWorld = Shader.PropertyToID("_ObjectToWorld");
    private static readonly int NextPointerSrc = Shader.PropertyToID("_nextPointerSrc");
    private static readonly int NextPointerDst = Shader.PropertyToID("_nextPointerDst");
    private static readonly int RadiusMultiplier = Shader.PropertyToID("_radiusMultiplier");

    // Public assets / parameters
    private ComputeShader _silhouetteEdgeFinder;
    private ComputeShader _edgesToStrokes;
    private ComputeShader _strokesToGreasePencilStrokes;
    
    private Mesh _sourceMesh;

    private int FaceCount => _sourceMesh.triangles.Length / 3;
    
    public Camera viewCamera;
    public float radiusMultiplier = 1.0f;


    // Compute buffers
    private ComputeBuffer _verticesBuffer;
    private ComputeBuffer _indicesBuffer;
    private ComputeBuffer _adjIndicesBuffer;
    private ComputeBuffer _strokesBuffer;
    private ComputeBuffer _nextPointerSrcBuffer;
    private ComputeBuffer _nextPointerDstBuffer;
    // Two 1-element buffers to be used as UAV atomic counters by the compute shader
    private ComputeBuffer _numStrokesCounterBuffer;
    private ComputeBuffer _numStrokePointsCounterBuffer;

    // Graphics buffers for GreasePencil output
    private GraphicsBuffer _denseStrokesBuffer;
    private GraphicsBuffer _colorBuffer;

    // Kernel indices for shaders
    private int _findSilhouetteEdge_Kernel;
    
    private int _initialize_Kernel;
    private int _findStrokeTail_Kernel;
    private int _resetNext_Kernel;
    private int _initDistances_Kernel;
    private int _listRank_Kernel;

    private int _setStrokeLengthAtTail_Kernel;
    private int _calcStrokeOffsets_Kernel;
    private int _invalidateEntries_Kernel;
    private int _sorter_Kernel;


    private const uint NUM_POINTER_JUMP_ITERATIONS = 8;
    private const float KERNEL_SIZE = 128.0f;

    private void Awake()
    {
        _silhouetteEdgeFinder = Instantiate(Resources.Load<ComputeShader>("Lineart/ComputeShaders/SilhouetteEdge"));
        _edgesToStrokes = Instantiate(Resources.Load<ComputeShader>("Lineart/ComputeShaders/EdgesToStrokes"));
        _strokesToGreasePencilStrokes = Instantiate(Resources.Load<ComputeShader>("Lineart/ComputeShaders/StrokesToGreasePencil"));
        
        _sourceMesh = GetComponent<MeshFilter>()?.sharedMesh;
        
        ResolveViewCamera();
    }

    void Start()
    {
        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.LogError("Compute shaders are not supported on this platform.");
            return;
        }

        if (_sourceMesh == null)
        {
            return;
        }

        InitializeKernels();
        InitializeBuffers();
        BindBuffersToShaders();
    }

    void OnDestroy()
    {
        _verticesBuffer?.Release();
        _indicesBuffer?.Release();
        _adjIndicesBuffer?.Release();
        _strokesBuffer?.Release();
        _nextPointerSrcBuffer?.Release();
        _nextPointerDstBuffer?.Release();
        _numStrokesCounterBuffer?.Release();
        _numStrokePointsCounterBuffer?.Release();
        _denseStrokesBuffer?.Release();
        _colorBuffer?.Release();

        if (_silhouetteEdgeFinder != null) Destroy(_silhouetteEdgeFinder);
        if (_edgesToStrokes != null) Destroy(_edgesToStrokes);
        if (_strokesToGreasePencilStrokes != null) Destroy(_strokesToGreasePencilStrokes);
    }

    void InitializeKernels()
    {
        _findSilhouetteEdge_Kernel = _silhouetteEdgeFinder.FindKernel("FindSilhouetteEdge");

        _initialize_Kernel = _edgesToStrokes.FindKernel("Initialize");
        _findStrokeTail_Kernel = _edgesToStrokes.FindKernel("FindStrokeTail");
        _resetNext_Kernel = _edgesToStrokes.FindKernel("ResetNextPointer");
        _initDistances_Kernel = _edgesToStrokes.FindKernel("InitializeRanksAndDistances");
        _listRank_Kernel = _edgesToStrokes.FindKernel("CalculateRanksAndDistances");

        _setStrokeLengthAtTail_Kernel = _strokesToGreasePencilStrokes.FindKernel("SetStrokeLengthAtTail");
        _calcStrokeOffsets_Kernel = _strokesToGreasePencilStrokes.FindKernel("CalculateArrayOffsets");
        _invalidateEntries_Kernel = _strokesToGreasePencilStrokes.FindKernel("InvalidateEntries");
        _sorter_Kernel = _strokesToGreasePencilStrokes.FindKernel("MoveToDenseArray");
    }

    void InitializeBuffers()
    {
        CreateBuffersForSilhouetteEdgeFinder();
        CreateBuffersForEdgesToStrokes();
        CreateBuffersForGreasePencilStrokes();
    }

    void CreateBuffersForSilhouetteEdgeFinder()
    {
        Vector3[] positions = _sourceMesh.vertices;
        Vector3[] normals = _sourceMesh.normals;
        SilhouetteSourceVertex[] vertexDataArray = new SilhouetteSourceVertex[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            vertexDataArray[i] = new SilhouetteSourceVertex { position = positions[i], normal = normals[i] };
        }

        _verticesBuffer = new ComputeBuffer(vertexDataArray.Length, Marshal.SizeOf(typeof(SilhouetteSourceVertex)));
        _verticesBuffer.SetData(vertexDataArray);
        
        int[] indices = _sourceMesh.triangles;
        _indicesBuffer = new ComputeBuffer(indices.Length, sizeof(int));
        _indicesBuffer.SetData(indices);

        int[] adjData = CalculateAdjacency(indices, positions);
        _adjIndicesBuffer = new ComputeBuffer(adjData.Length, sizeof(uint));
        _adjIndicesBuffer.SetData(adjData);
        
        _strokesBuffer = new ComputeBuffer(FaceCount, SilhouetteStrokeEdge.SizeOf);
    }

    void CreateBuffersForEdgesToStrokes()
    {
        _nextPointerSrcBuffer = new ComputeBuffer(FaceCount, sizeof(int));
        _nextPointerDstBuffer = new ComputeBuffer(FaceCount, sizeof(int));
    }

    void CreateBuffersForGreasePencilStrokes()
    {
        //TODO: find a tighter limit for these buffer sizes
        //output buffers
        _denseStrokesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2 * FaceCount, GreasePencilRenderer.GreasePencilStrokeVert.SizeOf);
        _colorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2 * FaceCount, GreasePencilRenderer.GreasePencilColorVert.SizeOf);

        //offset calculation buffers
        _numStrokesCounterBuffer = new ComputeBuffer(1, sizeof(uint));
        _numStrokePointsCounterBuffer = new ComputeBuffer(1, sizeof(uint));
    }

    [SuppressMessage("ReSharper", "Unity.PreferAddressByIdToGraphicsParams")]
    void BindBuffersToShaders()
    {
        _silhouetteEdgeFinder.SetInt("_NumFaces", FaceCount);
        _silhouetteEdgeFinder.SetBuffer(_findSilhouetteEdge_Kernel, "_Vertices", _verticesBuffer);
        _silhouetteEdgeFinder.SetBuffer(_findSilhouetteEdge_Kernel, "_Indices", _indicesBuffer);
        _silhouetteEdgeFinder.SetBuffer(_findSilhouetteEdge_Kernel, "_AdjIndices", _adjIndicesBuffer);
        _silhouetteEdgeFinder.SetBuffer(_findSilhouetteEdge_Kernel, "_outStrokes", _strokesBuffer);

        _edgesToStrokes.SetInt("_NumFaces", FaceCount);
        _edgesToStrokes.SetBuffer(_initialize_Kernel, "_strokes", _strokesBuffer);
        _edgesToStrokes.SetBuffer(_findStrokeTail_Kernel, "_strokes", _strokesBuffer);
        _edgesToStrokes.SetBuffer(_listRank_Kernel, "_strokes", _strokesBuffer);
        _edgesToStrokes.SetBuffer(_resetNext_Kernel, "_strokes", _strokesBuffer);
        _edgesToStrokes.SetBuffer(_initDistances_Kernel, "_strokes", _strokesBuffer);
        
        _strokesToGreasePencilStrokes.SetInt("_NumFaces", FaceCount);
        
        _strokesToGreasePencilStrokes.SetBuffer(_setStrokeLengthAtTail_Kernel, "_strokes", _strokesBuffer);
        
        _strokesToGreasePencilStrokes.SetBuffer(_calcStrokeOffsets_Kernel, "_strokes", _strokesBuffer);
        _strokesToGreasePencilStrokes.SetBuffer(_calcStrokeOffsets_Kernel, "numStrokesCounter", _numStrokesCounterBuffer);
        _strokesToGreasePencilStrokes.SetBuffer(_calcStrokeOffsets_Kernel, "numStrokePointsCounter", _numStrokePointsCounterBuffer);
        
        _strokesToGreasePencilStrokes.SetBuffer(_invalidateEntries_Kernel, "_denseArray", _denseStrokesBuffer);

        _strokesToGreasePencilStrokes.SetBuffer(_sorter_Kernel, "_strokes", _strokesBuffer);
        _strokesToGreasePencilStrokes.SetBuffer(_sorter_Kernel, "_denseArray", _denseStrokesBuffer);
        _strokesToGreasePencilStrokes.SetBuffer(_sorter_Kernel, "_colorArray", _colorBuffer);
    }

    private void BindNextPointers(int kernel)
    {
        _edgesToStrokes.SetBuffer(kernel, NextPointerSrc, _nextPointerSrcBuffer);
        _edgesToStrokes.SetBuffer(kernel, NextPointerDst, _nextPointerDstBuffer);
    }

    private void SwapNextPointers()
    {
        (_nextPointerSrcBuffer, _nextPointerDstBuffer) = (_nextPointerDstBuffer, _nextPointerSrcBuffer);
    }

    public void CalculateEdges()
    {
        if (_sourceMesh == null || viewCamera == null) return;

        _silhouetteEdgeFinder.SetVector(WorldSpaceCameraPos, viewCamera.transform.position);

        Matrix4x4 objectToWorld = transform.localToWorldMatrix;
        _silhouetteEdgeFinder.SetMatrix(ObjectToWorld, objectToWorld);
        _silhouetteEdgeFinder.SetMatrix(ObjectToWorldIt, objectToWorld.inverse.transpose);

        int threadGroups = Mathf.CeilToInt(FaceCount / KERNEL_SIZE);
        if (threadGroups > 0)
            _silhouetteEdgeFinder.Dispatch(_findSilhouetteEdge_Kernel, threadGroups, 1, 1);

        RunEdgesToStrokePasses(threadGroups);
        // DebugDrawSilhouetteEdges();

        RunStrokesToGreasePencilPass(threadGroups);
    }



    private void ResolveViewCamera()
    {
#if UNITY_EDITOR
        if (viewCamera == null)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            viewCamera = sceneView != null ? sceneView.camera : Camera.main;
        }
#else
        if (viewCamera == null) viewCamera = Camera.main;
#endif
    }

    public int GetMaximumBufferLength()
    {
        return FaceCount;
    }
    public GraphicsBuffer GetStrokeBuffer()
    {
        return _denseStrokesBuffer;
    }
    public GraphicsBuffer GetColorBuffer()
    {
        return _colorBuffer;
    }

    void RunEdgesToStrokePasses(int threadGroups)
    {
        BindNextPointers(_initialize_Kernel);
        _edgesToStrokes.Dispatch(_initialize_Kernel, threadGroups, 1, 1);
        SwapNextPointers();

        for (int i = 0; i < NUM_POINTER_JUMP_ITERATIONS; ++i)
        {
            BindNextPointers(_findStrokeTail_Kernel);
            _edgesToStrokes.Dispatch(_findStrokeTail_Kernel, threadGroups, 1, 1);
            SwapNextPointers();
        }

        //TODO: merge next two kernels
        BindNextPointers(_resetNext_Kernel);
        _edgesToStrokes.Dispatch(_resetNext_Kernel, threadGroups, 1, 1);
        SwapNextPointers();

        _edgesToStrokes.Dispatch(_initDistances_Kernel, threadGroups, 1, 1);

        for (int i = 0; i < NUM_POINTER_JUMP_ITERATIONS; ++i)
        {
            BindNextPointers(_listRank_Kernel);
            _edgesToStrokes.Dispatch(_listRank_Kernel, threadGroups, 1, 1);
            SwapNextPointers();
        }
    }

    private void DebugStrokes()
    {
        var strokes = new SilhouetteStrokeEdge[FaceCount];
        _strokesBuffer.GetData(strokes);

        int printCount = 0;
        for (int j = 0; j < strokes.Length && printCount < 10; j++)
        {
            if (strokes[j].adj != ADJ_NONE)
            {
                Debug.Log($"Stroke[{j}] pos={strokes[j].pos} adj={strokes[j].adj} minPoint={strokes[j].minPoint} rank={strokes[j].rank} dist={strokes[j].distFromTail:F4}");
                printCount++;
            }
        }
    }
    
    private void RunStrokesToGreasePencilPass(int threadGroups)
    {
        _numStrokesCounterBuffer.SetData(new[] { 0u });
        _numStrokePointsCounterBuffer.SetData(new[] { 0u });

        _strokesToGreasePencilStrokes.Dispatch(_setStrokeLengthAtTail_Kernel, threadGroups, 1, 1);
        _strokesToGreasePencilStrokes.Dispatch(_calcStrokeOffsets_Kernel, threadGroups, 1, 1);
        _strokesToGreasePencilStrokes.Dispatch(_invalidateEntries_Kernel, threadGroups, 1, 1);

        _strokesToGreasePencilStrokes.SetFloat(RadiusMultiplier, radiusMultiplier);
        _strokesToGreasePencilStrokes.Dispatch(_sorter_Kernel, threadGroups, 1, 1);
    }

    private void DebugGp()
    {
        var gpStrokes = new GreasePencilRenderer.GreasePencilStrokeVert[2 * FaceCount];
        _denseStrokesBuffer.GetData(gpStrokes);

        for (int j = 0; j < gpStrokes.Length; j++)
        {
            Debug.Log($"GP Stroke[{j}] pos={gpStrokes[j].pos} mat={gpStrokes[j].mat} strokePointIdx={gpStrokes[j].point_id}");
        }
    }

    private void DebugDrawSilhouetteEdges()
    {
        var debugStrokes = new SilhouetteStrokeEdge[FaceCount];
        _strokesBuffer.GetData(debugStrokes);
        
        for (int i = 0; i < debugStrokes.Length; i++)
        {
            var adj1 = debugStrokes[i].adj;

            var pos1 = debugStrokes[i].pos;
            if (!debugStrokes[i].IsInvalid() && adj1 >= 0 && adj1 < debugStrokes.Length)
            {
                var pos2 = debugStrokes[adj1].pos;
                Debug.DrawLine(pos1, pos2, Color.red);
            }
        }
    }

private static int[] CalculateAdjacency(int[] triangles, Vector3[] vertices, float epsilon = 0.0001f)
{
    if (triangles == null) throw new ArgumentNullException(nameof(triangles));
    int faceCount = triangles.Length / 3;
    int[] adj = new int[triangles.Length];
    for (int i = 0; i < adj.Length; i++) adj[i] = -1; // ADJ_NONE

    // 1. Create a map where every vertex points to a "master" vertex at the same position
    int[] vertexMap = new int[vertices.Length];
    // Use a dictionary to group vertices by position bucket/hash
    var posDict = new Dictionary<Vector3, int>(vertices.Length); 
    
    // Note: Vector3 equality usually has issues with floats, but Unity/System.Numerics
    // Structs often implement decent hash codes. For high robustness, you might need
    // to Quantize the position or use a specialized comparer.
    for (int i = 0; i < vertices.Length; i++)
    {
        if (posDict.TryGetValue(vertices[i], out int masterIndex))
        {
            vertexMap[i] = masterIndex;
        }
        else
        {
            posDict[vertices[i]] = i;
            vertexMap[i] = i;
        }
    }

    var edgeToFaces = new Dictionary<long, List<int>>(triangles.Length);

    // 2. Build Edge Dictionary using the REMAPPED indices
    for (int f = 0; f < faceCount; f++)
    {
        int baseIdx = f * 3;
        // Get original indices
        int v0 = triangles[baseIdx + 0];
        int v1 = triangles[baseIdx + 1];
        int v2 = triangles[baseIdx + 2];

        // Convert to unique spatial indices
        int u0 = vertexMap[v0];
        int u1 = vertexMap[v1];
        int u2 = vertexMap[v2];

        // Define edges using unique indices
        long[] keys = new long[3];
        keys[0] = MakeEdgeKey(u0, u1);
        keys[1] = MakeEdgeKey(u1, u2);
        keys[2] = MakeEdgeKey(u2, u0);

        for (int e = 0; e < 3; e++)
        {
            if (!edgeToFaces.TryGetValue(keys[e], out var list))
            {
                list = new List<int>();
                edgeToFaces[keys[e]] = list;
            }
            list.Add(f);
        }
    }

    // 3. Resolve Adjacency
    for (int f = 0; f < faceCount; f++)
    {
        int baseIdx = f * 3;
        int v0 = triangles[baseIdx + 0];
        int v1 = triangles[baseIdx + 1];
        int v2 = triangles[baseIdx + 2];

        // Must use the same mapping here
        int u0 = vertexMap[v0];
        int u1 = vertexMap[v1];
        int u2 = vertexMap[v2];

        long[] keys = new long[3];
        keys[0] = MakeEdgeKey(u0, u1);
        keys[1] = MakeEdgeKey(u1, u2);
        keys[2] = MakeEdgeKey(u2, u0);

        for (int e = 0; e < 3; e++)
        {
            var faces = edgeToFaces[keys[e]];
            // With split vertices, an edge might have more than 2 faces touching it
            // if the geometry is non-manifold. But for standard adjacency,
            // we just want the "other" face.
            foreach (var other in faces)
            {
                if (other != f)
                {
                    adj[baseIdx + e] = other;
                    break;
                }
            }
        }
    }

    return adj;
}

private static long MakeEdgeKey(int i1, int i2)
{
    return ((long)Math.Min(i1, i2) << 32) | (uint)Math.Max(i1, i2);
}
}
