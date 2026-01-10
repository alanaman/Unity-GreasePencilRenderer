using System;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;
using DefaultNamespace;
using UnityEditor;

public class SharpSilhouetteEdgeCalculator : MonoBehaviour, IGreasePencilEdgeCalculator
{
    private static readonly int WorldSpaceCameraPos = Shader.PropertyToID("_WorldSpaceCameraPos");
    private static readonly int ObjectToWorld = Shader.PropertyToID("_ObjectToWorld");
    private static readonly int ObjectToWorldIt = Shader.PropertyToID("_ObjectToWorldIT");
    private static readonly int NextPointerDst = Shader.PropertyToID("_nextPointerDst");
    private static readonly int NextPointerSrc = Shader.PropertyToID("_nextPointerSrc");
    private static readonly int RadiusMultiplier = Shader.PropertyToID("_radiusMultiplier");
    private const int ADJ_NONE = -1;
    private const int INVALID = -2;

    private ComputeShader _sharpSilhouetteEdgeFinder;
    private ComputeShader _sharpEdgesToStrokes;
    private ComputeShader _strokesToGreasePencilStrokes;
    
    private Mesh _sourceMesh;
    private int CornerCount => _sourceMesh.triangles.Length;
    private int FaceCount => _sourceMesh.triangles.Length / 3;
    

    public Camera viewCamera;
    public float radiusMultiplier = 1.0f;

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
    private int _findSharpSilhouetteEdge_Kernel;

    private int _initialize_Kernel;
    private int _reduce_Kernel;
    private int _findStrokeTail_Kernel;
    private int _resetNext_Kernel;
    private int _initDistances_Kernel;
    private int _listRank_Kernel;

    private int _setStrokeLengthAtTail_Kernel;
    private int _calcStrokeOffsets_Kernel;
    private int _invalidateEntries_Kernel;
    private int _sorter_Kernel;

    private const uint NUM_POINTER_JUMP_ITERATIONS = 8;
    
    
    private void Awake()
    {
        _sharpSilhouetteEdgeFinder = Instantiate(Resources.Load<ComputeShader>("Lineart/ComputeShaders/SharpSilhouetteEdge"));
        _sharpEdgesToStrokes = Instantiate(Resources.Load<ComputeShader>("Lineart/ComputeShaders/SharpEdgesToStrokes"));
        _strokesToGreasePencilStrokes = Instantiate(Resources.Load<ComputeShader>("Lineart/ComputeShaders/StrokesToGreasePencil"));
        
        _sourceMesh = GetComponent<MeshFilter>()?.sharedMesh;
        
        ResolveViewCamera();
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

    void InitializeKernels()
    {
        _findSharpSilhouetteEdge_Kernel = _sharpSilhouetteEdgeFinder.FindKernel("FindSilhouetteEdge");

        _initialize_Kernel = _sharpEdgesToStrokes.FindKernel("Initialize");
        _reduce_Kernel = _sharpEdgesToStrokes.FindKernel("Reduce"); 
        _findStrokeTail_Kernel = _sharpEdgesToStrokes.FindKernel("FindStrokeTail");
        _resetNext_Kernel = _sharpEdgesToStrokes.FindKernel("ResetNextPointer");
        _initDistances_Kernel = _sharpEdgesToStrokes.FindKernel("InitializeRanksAndDistances");
        _listRank_Kernel = _sharpEdgesToStrokes.FindKernel("CalculateRanksAndDistances");

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

    private void CreateBuffersForSilhouetteEdgeFinder()
    {
        Vector3[] positions = _sourceMesh.vertices;
        Vector3[] normals = _sourceMesh.normals;
        SilhouetteSourceVertex[] vertexDataArray = new SilhouetteSourceVertex[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            vertexDataArray[i] = new SilhouetteSourceVertex
            {
                position = positions[i],
                normal = normals[i]
            };
        }
        _verticesBuffer = new ComputeBuffer(vertexDataArray.Length, Marshal.SizeOf(typeof(SilhouetteSourceVertex)));
        _verticesBuffer.SetData(vertexDataArray);

        int[] indices = _sourceMesh.triangles;
        _indicesBuffer = new ComputeBuffer(indices.Length, sizeof(int));
        _indicesBuffer.SetData(indices);

        int[] adjData = CalculateCornerAdjacency(indices, positions);
        _adjIndicesBuffer = new ComputeBuffer(adjData.Length, sizeof(uint));
        _adjIndicesBuffer.SetData(adjData);

        _strokesBuffer = new ComputeBuffer(CornerCount, SilhouetteStrokeEdge.SizeOf);
    }

    void CreateBuffersForEdgesToStrokes()
    {
        _nextPointerSrcBuffer = new ComputeBuffer(CornerCount, sizeof(int));
        _nextPointerDstBuffer = new ComputeBuffer(CornerCount, sizeof(int));
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
    
    void BindBuffersToShaders()
    {
        _sharpSilhouetteEdgeFinder.SetBuffer(_findSharpSilhouetteEdge_Kernel, "_Vertices", _verticesBuffer);
        _sharpSilhouetteEdgeFinder.SetBuffer(_findSharpSilhouetteEdge_Kernel, "_Indices", _indicesBuffer);
        _sharpSilhouetteEdgeFinder.SetBuffer(_findSharpSilhouetteEdge_Kernel, "_AdjIndices", _adjIndicesBuffer);
        _sharpSilhouetteEdgeFinder.SetBuffer(_findSharpSilhouetteEdge_Kernel, "_outStrokes", _strokesBuffer);
        _sharpSilhouetteEdgeFinder.SetInt("_NumVerts", CornerCount);

        _sharpEdgesToStrokes.SetInt("_NumVerts", CornerCount);
    
        _sharpEdgesToStrokes.SetBuffer(_initialize_Kernel, "_strokes", _strokesBuffer);
        _sharpEdgesToStrokes.SetBuffer(_reduce_Kernel, "_strokes", _strokesBuffer);
        _sharpEdgesToStrokes.SetBuffer(_findStrokeTail_Kernel, "_strokes", _strokesBuffer);
        _sharpEdgesToStrokes.SetBuffer(_listRank_Kernel, "_strokes", _strokesBuffer);
        _sharpEdgesToStrokes.SetBuffer(_resetNext_Kernel, "_strokes", _strokesBuffer);
        _sharpEdgesToStrokes.SetBuffer(_initDistances_Kernel, "_strokes", _strokesBuffer);
            
        _strokesToGreasePencilStrokes.SetInt("_NumFaces", CornerCount);
        
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
        _sharpEdgesToStrokes.SetBuffer(kernel, NextPointerSrc, _nextPointerSrcBuffer);
        _sharpEdgesToStrokes.SetBuffer(kernel, NextPointerDst, _nextPointerDstBuffer);
    }

    private void SwapNextPointers()
    {
        (_nextPointerSrcBuffer, _nextPointerDstBuffer) = (_nextPointerDstBuffer, _nextPointerSrcBuffer);
    }

    public void CalculateEdges()
    {
        if (_sourceMesh == null || viewCamera == null) return;

        _sharpSilhouetteEdgeFinder.SetVector(WorldSpaceCameraPos, viewCamera.transform.position);

        Matrix4x4 objectToWorld = transform.localToWorldMatrix;
        _sharpSilhouetteEdgeFinder.SetMatrix(ObjectToWorld, objectToWorld);
        _sharpSilhouetteEdgeFinder.SetMatrix(ObjectToWorldIt, objectToWorld.inverse.transpose);

        int threadGroups = Mathf.CeilToInt(FaceCount / 64.0f);
        if (threadGroups > 0)
        {
            _sharpSilhouetteEdgeFinder.Dispatch(_findSharpSilhouetteEdge_Kernel, threadGroups, 1, 1);
        }

        RunEdgesToStrokePasses(threadGroups);
        RunStrokesToGreasePencilPass(threadGroups);
    }

    private void RunEdgesToStrokePasses(int threadGroups)
    {
        BindNextPointers(_initialize_Kernel);
        _sharpEdgesToStrokes.Dispatch(_initialize_Kernel, threadGroups, 1, 1);
        SwapNextPointers();

        for (int i = 0; i < NUM_POINTER_JUMP_ITERATIONS; ++i)
        {
            BindNextPointers(_reduce_Kernel);
            _sharpEdgesToStrokes.Dispatch(_reduce_Kernel, threadGroups, 1, 1);
            SwapNextPointers();
        }
            
        BindNextPointers(_resetNext_Kernel);
        _sharpEdgesToStrokes.Dispatch(_resetNext_Kernel, threadGroups, 1, 1);
        SwapNextPointers();
        
        for (int i = 0; i < NUM_POINTER_JUMP_ITERATIONS; ++i)
        {
            BindNextPointers(_findStrokeTail_Kernel);
            _sharpEdgesToStrokes.Dispatch(_findStrokeTail_Kernel, threadGroups, 1, 1);
            SwapNextPointers();
        }

        BindNextPointers(_resetNext_Kernel);
        _sharpEdgesToStrokes.Dispatch(_resetNext_Kernel, threadGroups, 1, 1);
        SwapNextPointers();

        _sharpEdgesToStrokes.Dispatch(_initDistances_Kernel, threadGroups, 1, 1);

        for (int i = 0; i < 8; ++i)
        {
            BindNextPointers(_listRank_Kernel);
            _sharpEdgesToStrokes.Dispatch(_listRank_Kernel, threadGroups, 1, 1);
            SwapNextPointers();
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

    private void DebugStrokes()
    {
        SilhouetteStrokeEdge[] strokes = new SilhouetteStrokeEdge[CornerCount];
        _strokesBuffer.GetData(strokes);

        int printCount = 0;
        for (int j = 0; j < strokes.Length && printCount < 10; j++)
        {
            if (strokes[j].adj != INVALID && strokes[j].adj != ADJ_NONE)
            {
                Debug.Log($"Stroke[{j}] pos={strokes[j].pos} adj={strokes[j].adj} minPoint={strokes[j].minPoint} rank={strokes[j].rank} dist={strokes[j].distFromTail:F4}");
                printCount++;
            }
        }
    }

    private void DebugGp()
    {
        var gpStrokes = new GreasePencilRenderer.GreasePencilStrokeVert[2*FaceCount];
        _denseStrokesBuffer.GetData(gpStrokes);
        
        for (int j = 0; j < gpStrokes.Length; j++)
        {
            Debug.Log($"GP Stroke[{j}] pos={gpStrokes[j].pos} mat={gpStrokes[j].mat} strokePointIdx={gpStrokes[j].point_id}");
        }
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

        if (_sharpSilhouetteEdgeFinder != null) Destroy(_sharpSilhouetteEdgeFinder);
        if (_sharpEdgesToStrokes != null) Destroy(_sharpEdgesToStrokes);
        if (_strokesToGreasePencilStrokes != null) Destroy(_strokesToGreasePencilStrokes);
    }
    
    /// <summary>
    /// For each corner, finds the index of its adjacent corner. The adjacent corner of corner C is the corner at the same position as C and part of the face that is adjacent to the edge that is clockwise from C.
    /// </summary>
    /// <param name="triangles"></param>
    /// <param name="positions"></param>
    /// <returns>array of length same as <paramref name="triangles"/> containing indices of the adjacent corners</returns>
    private static int[] CalculateCornerAdjacency(int[] triangles, Vector3[] positions)
    {
        if (triangles == null) throw new ArgumentNullException(nameof(triangles));
        if (positions == null) throw new ArgumentNullException(nameof(positions));
        var cornerCount = triangles.Length;
        if (cornerCount % 3 != 0) throw new ArgumentException("Triangle array length must be a multiple of 3.", nameof(triangles));

        var faceCount = cornerCount / 3;
        int[] adj = new int[cornerCount];
        for (int i = 0; i < adj.Length; i++) adj[i] = INVALID;

        var edgeMap = new Dictionary<EdgeKey, List<EdgeCorner>>(cornerCount);

        for (int f = 0; f < faceCount; f++)
        {
            int baseIdx = f * 3;
            for (int c = 0; c < 3; c++)
            {
                int currIdx = triangles[baseIdx + c];
                int nextIdx = triangles[baseIdx + ((c + 1) % 3)];
                Vector3 p0 = positions[currIdx];
                Vector3 p1 = positions[nextIdx];
                var key = new EdgeKey(p0, p1);
                if (!edgeMap.TryGetValue(key, out var list))
                {
                    list = new List<EdgeCorner>(2);
                    edgeMap[key] = list;
                }
                list.Add(new EdgeCorner(baseIdx + c));
            }
        }

        foreach (var kvp in edgeMap)
        {
            var corners = kvp.Value;
            if (corners.Count < 1) continue;
            if (corners.Count == 1)
            {
                adj[corners[0].CornerIndex] = INVALID;
                continue;
            }
            if (corners.Count > 2)
            {
                Debug.LogWarning("Non Manifold edges found in mesh");
                foreach (var corner in corners)
                {
                    adj[corner.CornerIndex] = INVALID;
                }
                continue;
            }
            
            // corners.Count == 2
            int c1 = corners[0].CornerIndex;
            int c2 = corners[1].CornerIndex;

            adj[c1] = (c2 + 1) % 3 + c2 / 3 * 3;
            adj[c2] = (c1 + 1) % 3 + c1 / 3 * 3;
        }

        return adj;
    }

    private readonly struct EdgeCorner
    {
        public EdgeCorner(int cornerIndex)
        {
            CornerIndex = cornerIndex;
        }

        public int CornerIndex { get; }
    }

    private readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        private readonly Vector3 _a;
        private readonly Vector3 _b;

        public EdgeKey(Vector3 p0, Vector3 p1)
        {
            if (ComparePos(p0, p1) <= 0)
            {
                _a = p0;
                _b = p1;
            }
            else
            {
                _a = p1;
                _b = p0;
            }
        }

        public bool Equals(EdgeKey other) => PositionsEqual(_a, other._a) && PositionsEqual(_b, other._b);
        public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h1 = HashVector(_a);
                int h2 = HashVector(_b);
                return (h1 * 397) ^ h2;
            }
        }

        private static int HashVector(Vector3 v)
        {
            unchecked
            {
                int hx = v.x.GetHashCode();
                int hy = v.y.GetHashCode();
                int hz = v.z.GetHashCode();
                return ((hx * 397) ^ hy) * 397 ^ hz;
            }
        }

        private static int ComparePos(Vector3 a, Vector3 b)
        {
            if (a.x < b.x) return -1; if (a.x > b.x) return 1;
            if (a.y < b.y) return -1; if (a.y > b.y) return 1;
            if (a.z < b.z) return -1; if (a.z > b.z) return 1;
            return 0;
        }
    }

    private static bool PositionsEqual(Vector3 a, Vector3 b)
    {
        const float epsilon = 1e-6f;
        return Mathf.Abs(a.x - b.x) <= epsilon && Mathf.Abs(a.y - b.y) <= epsilon && Mathf.Abs(a.z - b.z) <= epsilon;
    }

    public int GetMaximumBufferLength()
    {
        return CornerCount;
    }
    public GraphicsBuffer GetStrokeBuffer()
    {
        return _denseStrokesBuffer;
    }
    public GraphicsBuffer GetColorBuffer()
    {
        return _colorBuffer;
    }
}
