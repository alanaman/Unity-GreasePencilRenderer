using System;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;
using DefaultNamespace;
using UnityEditor;

public class SharpSilhouetteEdgeCalculator : MonoBehaviour, IGreasePencilEdgeCalculator
{
    private const int ADJ_NONE = -1;
    private const int ADJ_INVALID = -2;

    public ComputeShader silhouetteShader;
    public MeshFilter sourceMeshFilter;
    public Camera viewCamera;

    private ComputeBuffer _verticesBuffer;
    private ComputeBuffer _indicesBuffer;
    private ComputeBuffer _adjIndicesBuffer;

    private ComputeBuffer _strokesBuffer;

    
    public Material material;

    public int displayInt=0;

    private int _kernelHandle;
    private int _cornerCount;
    private int _faceCount;

    public ComputeShader findHeadTailShader;

    private ComputeBuffer _nextPointerSrcBuffer;
    private ComputeBuffer _nextPointerDstBuffer;


    private int _initPjKernel;
    private int _reduceKernel;
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
            _reduceKernel = findHeadTailShader.FindKernel("Reduce"); 
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

        // --- 2. Index Buffer ---
        int[] indices = mesh.triangles;
        _cornerCount = indices.Length;
        _faceCount = indices.Length / 3;
        _indicesBuffer = new ComputeBuffer(indices.Length, sizeof(int));
        _indicesBuffer.SetData(indices);

        // --- 3. Adjacency Buffer ---
        int[] adjData = CalculateAdjacency(indices, positions);
        
        _adjIndicesBuffer = new ComputeBuffer(adjData.Length, sizeof(uint));
        _adjIndicesBuffer.SetData(adjData);

        // --- 4. Output Stroke Buffer ---
        _strokesBuffer = new ComputeBuffer(_cornerCount, SilhouetteStrokeEdge.SizeOf);
        //TODO: find a tighter limit for these buffer sizes
        DenseStrokesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2*_faceCount, GreasePencilRenderer.GreasePencilStrokeVert.SizeOf);
        ColorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2*_faceCount, GreasePencilRenderer.GreasePencilColorVert.SizeOf);

        // buffers for FindHeadTail
        _nextPointerSrcBuffer = new ComputeBuffer(_cornerCount, sizeof(uint));
        _nextPointerDstBuffer = new ComputeBuffer(_cornerCount, sizeof(uint));

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
        silhouetteShader.SetInt("_NumVerts", indices.Length);

        if (findHeadTailShader != null)
        {
            findHeadTailShader.SetInt("_NumVerts", _cornerCount);
        
            findHeadTailShader.SetBuffer(_initPjKernel, "_strokes", _strokesBuffer);
            findHeadTailShader.SetBuffer(_initPjKernel, "_nextPointerSrc", _nextPointerSrcBuffer);
            findHeadTailShader.SetBuffer(_initPjKernel, "_nextPointerDst", _nextPointerDstBuffer);
        
            findHeadTailShader.SetBuffer(_reduceKernel, "_nextPointerSrc", _nextPointerSrcBuffer);
            findHeadTailShader.SetBuffer(_reduceKernel, "_nextPointerDst", _nextPointerDstBuffer);
            findHeadTailShader.SetBuffer(_reduceKernel, "_strokes", _strokesBuffer);
            
            findHeadTailShader.SetBuffer(_findMinPjKernel, "_strokes", _strokesBuffer);
            findHeadTailShader.SetBuffer(_listRankKernel, "_strokes", _strokesBuffer);
            findHeadTailShader.SetBuffer(_resetNextKernel, "_strokes", _strokesBuffer);
            findHeadTailShader.SetBuffer(_initDistancesKernel, "_strokes", _strokesBuffer);
            
        }

        if (sorterShader != null)
        {
            sorterShader.SetInt("_NumFaces", _cornerCount);
            
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

    private void BindNextPointers(int kernel)
    {
        findHeadTailShader.SetBuffer(kernel, "_nextPointerSrc", _nextPointerSrcBuffer);
        findHeadTailShader.SetBuffer(kernel, "_nextPointerDst", _nextPointerDstBuffer);
    }

    private void SwapNextPointers()
    {
        (_nextPointerSrcBuffer, _nextPointerDstBuffer) = (_nextPointerDstBuffer, _nextPointerSrcBuffer);
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
            BindNextPointers(_initPjKernel);
            findHeadTailShader.Dispatch(_initPjKernel, threadGroups, 1, 1);
            SwapNextPointers();

            for (int i = 0; i < 6; ++i)
            {
                BindNextPointers(_reduceKernel);
                findHeadTailShader.Dispatch(_reduceKernel, threadGroups, 1, 1);
                SwapNextPointers();
            }
            DebugStrokes();
            
            // Restore original successor links before computing ranking so ranking is based on original adjacency.
            BindNextPointers(_resetNextKernel);
            findHeadTailShader.Dispatch(_resetNextKernel, threadGroups, 1, 1);
            SwapNextPointers();
            
            // Propagate minima only (using pointer jumping on nextPointer). This modifies _nextPointer.
            for (int i = 0; i < 6; ++i)
            {
                BindNextPointers(_findMinPjKernel);
                findHeadTailShader.Dispatch(_findMinPjKernel, threadGroups, 1, 1);
                SwapNextPointers();
            }
            DebugStrokes();

            // Restore original successor links before computing ranking so ranking is based on original adjacency.
            BindNextPointers(_resetNextKernel);
            findHeadTailShader.Dispatch(_resetNextKernel, threadGroups, 1, 1);
            SwapNextPointers();

            // Reset only distances/rank, keep minPoint
            findHeadTailShader.SetBuffer(_initDistancesKernel, "_strokes", _strokesBuffer);
            findHeadTailShader.Dispatch(_initDistancesKernel, threadGroups, 1, 1);

            // Perform list ranking pointer jumping passes.
            for (int i = 0; i < 8; ++i)
            {
                BindNextPointers(_listRankKernel);
                findHeadTailShader.SetBuffer(_listRankKernel, "_strokes", _strokesBuffer);
                findHeadTailShader.Dispatch(_listRankKernel, threadGroups, 1, 1);
                SwapNextPointers();
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
        SilhouetteStrokeEdge[] strokes = new SilhouetteStrokeEdge[_cornerCount];
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
        var gpStrokes = new GreasePencilRenderer.GreasePencilStrokeVert[2*_faceCount];
        DenseStrokesBuffer.GetData(gpStrokes);
        
        for (int j = 0; j < gpStrokes.Length; j++)
        {
            Debug.Log($"GP Stroke[{j}] pos={gpStrokes[j].pos} mat={gpStrokes[j].mat} strokePointIdx={gpStrokes[j].point_id}");
        }
    }

    private void ValidateRanking(SilhouetteStrokeEdge[] strokes)
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
        SilhouetteStrokeEdge[] debugStrokes = new SilhouetteStrokeEdge[_cornerCount];
        _strokesBuffer.GetData(debugStrokes);

        const int INVALID = ADJ_INVALID;
        
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
        // _indices?.Dispose();
    }
    
    private static int[] CalculateAdjacency(int[] triangles, Vector3[] positions)
    {
        if (triangles == null) throw new ArgumentNullException(nameof(triangles));
        if (positions == null) throw new ArgumentNullException(nameof(positions));
        if (triangles.Length % 3 != 0) throw new ArgumentException("Triangle array length must be a multiple of 3.", nameof(triangles));

        int faceCount = triangles.Length / 3;
        int[] adj = new int[triangles.Length];
        const int INVALID = -1;
        for (int i = 0; i < adj.Length; i++) adj[i] = INVALID;

        var edgeMap = new Dictionary<EdgeKey, List<EdgeCorner>>(triangles.Length);

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
                list.Add(new EdgeCorner(baseIdx + c, p0));
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
        public EdgeCorner(int cornerIndex, Vector3 startPos)
        {
            CornerIndex = cornerIndex;
            StartPos = startPos;
        }

        public int CornerIndex { get; }
        public Vector3 StartPos { get; }
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
        return sourceMeshFilter.sharedMesh.triangles.Length;
    }
    public GraphicsBuffer GetStrokeBuffer()
    {
        return DenseStrokesBuffer;
    }
    public GraphicsBuffer GetColorBuffer()
    {
        return ColorBuffer;
    }
}
