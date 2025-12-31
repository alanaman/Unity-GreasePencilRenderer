using System;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;
using DefaultNamespace;
using UnityEditor;

public class SilhouetteEdgeCalculator : MonoBehaviour, IGreasePencilEdgeCalculator
{
    // Constants
    private const int ADJ_NONE = -1;
    private const int ADJ_INVALID = -2;

    // Public assets / parameters
    public ComputeShader silhouetteShader;
    public ComputeShader findHeadTailShader;
    public ComputeShader sorterShader;
    public MeshFilter sourceMeshFilter;
    public Camera viewCamera;
    public float radiusMultiplier = 1.0f;

    // Debug
    public int displayInt = 0;

    // Internal state
    private int _kernelHandle;
    private int _faceCount;

    // Compute buffers
    private ComputeBuffer _verticesBuffer;
    private ComputeBuffer _indicesBuffer;
    private ComputeBuffer _adjIndicesBuffer;
    private ComputeBuffer _strokesBuffer;
    private ComputeBuffer _nextPointerSrcBuffer;
    private ComputeBuffer _nextPointerDstBuffer;
    private ComputeBuffer _numStrokesCounterBuffer;
    private ComputeBuffer _numStrokePointsCounterBuffer;

    // Graphics buffers for GreasePencil output
    public GraphicsBuffer DenseStrokesBuffer;
    public GraphicsBuffer ColorBuffer;

    // Kernel indices for auxiliary shaders
    private int _initPjKernel;
    private int _findMinPjKernel;
    private int _listRankKernel;
    private int _resetNextKernel;
    private int _initDistancesKernel;

    private int _setStrokeLengthAtTailKernel;
    private int _calcStrokeOffsetsKernel;
    private int _invalidateEntriesKernel;
    private int _sorterKernel;

    // Vertex / Stroke data used for compute buffers
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
        public int adj;
        public Vector3 faceNormal;
        public uint minPoint;
        public uint rank;            // hop count to tail
        public uint isCyclic;
        public float distFromTail;   // cumulative geometric distance to tail (0 at tail)

        public uint isChild; // 1 if this stroke point has a parent, 0 otherwise
        public uint totalStrokeLength; // total length of the stroke that contains this point

        public uint strokeIdx; // ID of the stroke this point belongs to
        public uint strokePointsOffset; // Offset to the stroke points array

        public static int SizeOf => Marshal.SizeOf(typeof(StrokeData));
    }

    // Lifecycle -------------------------------------------------------------

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

        InitializeKernels();
        InitializeBuffers();
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
        DenseStrokesBuffer?.Release();
        ColorBuffer?.Release();
    }

    // Initialization helpers -----------------------------------------------

    void InitializeKernels()
    {
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
    }

    void InitializeBuffers()
    {
        Mesh mesh = sourceMeshFilter.sharedMesh;

        CreateVertexBuffer(mesh);
        CreateIndexAndAdjacencyBuffers(mesh);
        CreateStrokeAndAuxBuffers();
        BindBuffersToShaders();
    }

    void CreateVertexBuffer(Mesh mesh)
    {
        Vector3[] positions = mesh.vertices;
        Vector3[] normals = mesh.normals;
        VertexData[] vertexDataArray = new VertexData[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            vertexDataArray[i] = new VertexData { position = positions[i], normal = normals[i] };
        }

        _verticesBuffer = new ComputeBuffer(vertexDataArray.Length, Marshal.SizeOf(typeof(VertexData)));
        _verticesBuffer.SetData(vertexDataArray);
    }

    void CreateIndexAndAdjacencyBuffers(Mesh mesh)
    {
        int[] indices = mesh.triangles;
        _faceCount = indices.Length / 3;

        _indicesBuffer = new ComputeBuffer(indices.Length, sizeof(int));
        _indicesBuffer.SetData(indices);

        uint[] adjData = CalculateAdjacency(indices);
        _adjIndicesBuffer = new ComputeBuffer(adjData.Length, sizeof(uint));
        _adjIndicesBuffer.SetData(adjData);
    }

    void CreateStrokeAndAuxBuffers()
    {
        _strokesBuffer = new ComputeBuffer(_faceCount, StrokeData.SizeOf);

        // Buffer sizes mirror original code
        DenseStrokesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2 * _faceCount, GreasePencilRenderer.GreasePencilStrokeVert.SizeOf);
        ColorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2 * _faceCount, GreasePencilRenderer.GreasePencilColorVert.SizeOf);

        _nextPointerSrcBuffer = new ComputeBuffer(_faceCount, sizeof(uint));
        _nextPointerDstBuffer = new ComputeBuffer(_faceCount, sizeof(uint));

        if (sorterShader != null)
        {
            _numStrokesCounterBuffer = new ComputeBuffer(1, sizeof(uint));
            _numStrokePointsCounterBuffer = new ComputeBuffer(1, sizeof(uint));
            _numStrokesCounterBuffer.SetData(new uint[] { 0u });
            _numStrokePointsCounterBuffer.SetData(new uint[] { 0u });
        }
    }

    void BindBuffersToShaders()
    {
        silhouetteShader.SetBuffer(_kernelHandle, "_Vertices", _verticesBuffer);
        silhouetteShader.SetBuffer(_kernelHandle, "_Indices", _indicesBuffer);
        silhouetteShader.SetBuffer(_kernelHandle, "_AdjIndices", _adjIndicesBuffer);
        silhouetteShader.SetBuffer(_kernelHandle, "_outStrokes", _strokesBuffer);
        silhouetteShader.SetInt("_NumFaces", _faceCount);

        if (findHeadTailShader != null)
            findHeadTailShader.SetInt("_NumFaces", _faceCount);

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
    }

    // Core functionality --------------------------------------------------

    private void BindNextPointers(int kernel)
    {
        findHeadTailShader.SetBuffer(kernel, "_nextPointerSrc", _nextPointerSrcBuffer);
        findHeadTailShader.SetBuffer(kernel, "_nextPointerDst", _nextPointerDstBuffer);
    }

    private void SwapNextPointers()
    {
        var tmp = _nextPointerSrcBuffer;
        _nextPointerSrcBuffer = _nextPointerDstBuffer;
        _nextPointerDstBuffer = tmp;
    }

    public void CalculateEdges()
    {
#if UNITY_EDITOR
        if (viewCamera == null)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
                viewCamera = sceneView.camera;
            else
                viewCamera = Camera.main;
        }
#else
        if (viewCamera == null) viewCamera = Camera.main;
#endif

        if (_strokesBuffer == null || viewCamera == null) return;

        silhouetteShader.SetVector("_WorldSpaceCameraPos", viewCamera.transform.position);

        Matrix4x4 objectToWorld = sourceMeshFilter.transform.localToWorldMatrix;
        Matrix4x4 objectToWorldIT = objectToWorld.inverse.transpose;
        silhouetteShader.SetMatrix("_ObjectToWorld", objectToWorld);
        silhouetteShader.SetMatrix("_ObjectToWorldIT", objectToWorldIT);

        int threadGroups = Mathf.CeilToInt(_faceCount / 64.0f);
        if (threadGroups > 0)
            silhouetteShader.Dispatch(_kernelHandle, threadGroups, 1, 1);

        DebugDraw();

        if (findHeadTailShader != null)
        {
            RunFindHeadTailPasses(threadGroups);
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
    }

    public int GetMaximumBufferLength()
    {
        return sourceMeshFilter.sharedMesh.triangles.Length/3;
    }
    public GraphicsBuffer GetStrokeBuffer()
    {
        return DenseStrokesBuffer;
    }
    public GraphicsBuffer GetColorBuffer()
    {
        return ColorBuffer;
    }

    void RunFindHeadTailPasses(int threadGroups)
    {
        BindNextPointers(_initPjKernel);
        findHeadTailShader.SetBuffer(_initPjKernel, "_strokes", _strokesBuffer);
        findHeadTailShader.Dispatch(_initPjKernel, threadGroups, 1, 1);
        SwapNextPointers();

        for (int i = 0; i < 6; ++i)
        {
            BindNextPointers(_findMinPjKernel);
            findHeadTailShader.SetBuffer(_findMinPjKernel, "_strokes", _strokesBuffer);
            findHeadTailShader.Dispatch(_findMinPjKernel, threadGroups, 1, 1);
            SwapNextPointers();
        }
        DebugStrokes();

        BindNextPointers(_resetNextKernel);
        findHeadTailShader.SetBuffer(_resetNextKernel, "_strokes", _strokesBuffer);
        findHeadTailShader.Dispatch(_resetNextKernel, threadGroups, 1, 1);
        SwapNextPointers();

        findHeadTailShader.SetBuffer(_initDistancesKernel, "_strokes", _strokesBuffer);
        findHeadTailShader.Dispatch(_initDistancesKernel, threadGroups, 1, 1);
        DebugStrokes();

        for (int i = 0; i < 8; ++i)
        {
            BindNextPointers(_listRankKernel);
            findHeadTailShader.SetBuffer(_listRankKernel, "_strokes", _strokesBuffer);
            findHeadTailShader.Dispatch(_listRankKernel, threadGroups, 1, 1);
            SwapNextPointers();
        }
        DebugStrokes();
    }

    // Debug / validation helpers ------------------------------------------

    private void DebugStrokes()
    {
        StrokeData[] strokes = new StrokeData[_faceCount];
        _strokesBuffer.GetData(strokes);

        int printCount = 0;
        for (int j = 0; j < strokes.Length && printCount < 10; j++)
        {
            if (strokes[j].adj != ADJ_INVALID && strokes[j].adj != ADJ_NONE)
            {
                Debug.Log($"Stroke[{j}] pos={strokes[j].pos} adj={strokes[j].adj} minPoint={strokes[j].minPoint} rank={strokes[j].rank} dist={strokes[j].distFromTail:F4}");
                printCount++;
            }
        }
    }

    private void DebugGp()
    {
        var gpStrokes = new GreasePencilRenderer.GreasePencilStrokeVert[2 * _faceCount];
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
        const int INVALID_ADJ = ADJ_INVALID;

        for (int i = 0; i < strokes.Length; i++)
        {
            if (strokes[i].adj == INVALID_ADJ) continue;
            countValid++;
            if (strokes[i].distFromTail > maxDist) maxDist = strokes[i].distFromTail;
            if (i == strokes[i].minPoint)
            {
                countTail++;
                continue;
            }
            int succ = strokes[i].adj;
            if (succ != INVALID_ADJ && succ >= 0 && succ < strokes.Length && strokes[succ].adj != INVALID_ADJ)
            {
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

        const int INVALID = ADJ_INVALID;

        for (int i = 0; i < debugStrokes.Length; i++)
        {
            if (i != displayInt)
                continue;

            var adj1 = debugStrokes[i].adj;

            var pos1 = debugStrokes[i].pos;
            var pos2 = pos1;
            var pos3 = pos2;
            if (adj1 != INVALID && adj1 >= 0 && adj1 < debugStrokes.Length)
            {
                pos2 = debugStrokes[adj1].pos;
                var adj2 = debugStrokes[adj1].adj;

                pos3 = pos2;
                if (adj2 != INVALID && adj2 >= 0 && adj2 < debugStrokes.Length)
                {
                    pos3 = debugStrokes[adj2].pos;
                }
            }

            if (debugStrokes[i].adj != ADJ_INVALID)
            {
                Debug.DrawLine(pos1, pos2, Color.red);
                Debug.DrawLine(pos2, pos3, Color.green);
            }
        }
    }

    // Utility -------------------------------------------------------------

    private static uint[] CalculateAdjacency(int[] triangles)
    {
        if (triangles == null) throw new ArgumentNullException(nameof(triangles));
        if (triangles.Length % 3 != 0) throw new ArgumentException("Triangle array length must be a multiple of 3.", nameof(triangles));

        int faceCount = triangles.Length / 3;
        uint[] adj = new uint[triangles.Length];
        const uint INVALID = uint.MaxValue;

        for (int i = 0; i < adj.Length; i++) adj[i] = INVALID;

        var edgeToFaces = new Dictionary<long, List<int>>(triangles.Length);

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
                if (!edgeToFaces.TryGetValue(keys[e], out var list))
                {
                    list = new List<int>(2);
                    edgeToFaces[keys[e]] = list;
                }
                list.Add(f);
            }
        }

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

                for (int k = 0; k < faces.Count; k++)
                {
                    int other = faces[k];
                    if (other != f)
                    {
                        neighbor = (uint)other;
                        break;
                    }
                }
                adj[baseIdx + e] = neighbor;
            }
        }

        return adj;
    }
}
