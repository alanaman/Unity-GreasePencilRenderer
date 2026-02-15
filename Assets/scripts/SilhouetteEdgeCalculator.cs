using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using DefaultNamespace;
using UnityEditor;

public class SilhouetteEdgeCalculator : MonoBehaviour, IGreasePencilEdgeCalculator
{
    private ComputeShader _silhouetteEdgeFinder;
    private ComputeShader _edgesToStrokes;
    private ComputeShader _strokesToGreasePencilStrokes;

    private Mesh _sourceMesh;
    private int FaceCount => _sourceMesh.triangles.Length / 3;

    private SilhouetteBufferContext _buffers;

    public Camera viewCamera;
    public float radiusMultiplier = 1.0f;
    public float lateralShiftFactor = 1.0f;
    public float lateralShiftConstant;

    private int _findSilhouetteEdge_Kernel;

    private int _initialize_Kernel;
    private int _findStrokeTail_Kernel;
    private int _resetNext_Kernel;
    private int _initRanks_Kernel;
    private int _listRank_Kernel;
    private int _finalizeRanks_Kernel;
    private int _setStrokeLengthAtTail_Kernel;
    private int _calcStrokeOffsets_Kernel;
    private int _invalidateEntries_Kernel;
    private int _sorter_Kernel;
    private int _buildDenseNextPointers_Kernel;
    private int _offsetDenseScreenPositions_Kernel;
    private int _initDenseScreenDistances_Kernel;
    private int _densePointerJumpDistances_Kernel;
    private int _finalizeDenseScreenDistances_Kernel;

    private const uint NUM_POINTER_JUMP_ITERATIONS = 8;
    private const float KERNEL_SIZE = 128.0f;

    private void Awake()
    {
        _silhouetteEdgeFinder = Instantiate(Resources.Load<ComputeShader>("Lineart/ComputeShaders/SilhouetteEdge"));
        _edgesToStrokes = Instantiate(Resources.Load<ComputeShader>("Lineart/ComputeShaders/EdgesToStrokes"));
        _strokesToGreasePencilStrokes = Instantiate(Resources.Load<ComputeShader>("Lineart/ComputeShaders/StrokesToGreasePencil"));

        _sourceMesh = GetComponent<MeshFilter>()?.sharedMesh;

        ResolveViewCamera();
            
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
        _buffers = SilhouetteBufferContext.CreateForSmooth(_sourceMesh);
        BindBuffersToShaders();
        
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


    void InitializeKernels()
    {
        _findSilhouetteEdge_Kernel = _silhouetteEdgeFinder.FindKernel("FindSilhouetteEdge");

        _initialize_Kernel = _edgesToStrokes.FindKernel("Initialize");
        _findStrokeTail_Kernel = _edgesToStrokes.FindKernel("FindStrokeTail");
        _resetNext_Kernel = _edgesToStrokes.FindKernel("ResetNextPointer");
        _initRanks_Kernel = _edgesToStrokes.FindKernel("InitializeRanks");
        _listRank_Kernel = _edgesToStrokes.FindKernel("CalculateRanks");
        _finalizeRanks_Kernel = _edgesToStrokes.FindKernel("FinalizeRanks");
        _setStrokeLengthAtTail_Kernel = _strokesToGreasePencilStrokes.FindKernel("SetStrokeLengthAtTail");
        _calcStrokeOffsets_Kernel = _strokesToGreasePencilStrokes.FindKernel("CalculateArrayOffsets");
        _invalidateEntries_Kernel = _strokesToGreasePencilStrokes.FindKernel("InvalidateEntries");
        _sorter_Kernel = _strokesToGreasePencilStrokes.FindKernel("MoveToDenseArray");
        _buildDenseNextPointers_Kernel = _strokesToGreasePencilStrokes.FindKernel("BuildDenseNextPointers");
        _offsetDenseScreenPositions_Kernel = _strokesToGreasePencilStrokes.FindKernel("OffsetDenseScreenPositions");
        _initDenseScreenDistances_Kernel = _strokesToGreasePencilStrokes.FindKernel("InitDenseScreenDistances");
        _densePointerJumpDistances_Kernel = _strokesToGreasePencilStrokes.FindKernel("DensePointerJumpDistances");
        _finalizeDenseScreenDistances_Kernel = _strokesToGreasePencilStrokes.FindKernel("FinalizeDenseScreenDistances");
    }

    [SuppressMessage("ReSharper", "Unity.PreferAddressByIdToGraphicsParams")]
    void BindBuffersToShaders()
    {
        _silhouetteEdgeFinder.SetInt("_NumFaces", _buffers.FaceCount);
        _silhouetteEdgeFinder.SetBuffer(_findSilhouetteEdge_Kernel, "_Vertices", _buffers.Vertices);
        _silhouetteEdgeFinder.SetBuffer(_findSilhouetteEdge_Kernel, "_Indices", _buffers.Indices);
        _silhouetteEdgeFinder.SetBuffer(_findSilhouetteEdge_Kernel, "_AdjIndices", _buffers.AdjIndices);
        _silhouetteEdgeFinder.SetBuffer(_findSilhouetteEdge_Kernel, "_outStrokes", _buffers.Strokes);

        _edgesToStrokes.SetInt("_NumFaces", _buffers.FaceCount);
        _edgesToStrokes.SetBuffer(_initialize_Kernel, "_strokes", _buffers.Strokes);
        _edgesToStrokes.SetBuffer(_findStrokeTail_Kernel, "_strokes", _buffers.Strokes);
        _edgesToStrokes.SetBuffer(_listRank_Kernel, "_strokes", _buffers.Strokes);
        _edgesToStrokes.SetBuffer(_resetNext_Kernel, "_strokes", _buffers.Strokes);
        _edgesToStrokes.SetBuffer(_initRanks_Kernel, "_strokes", _buffers.Strokes);
        _edgesToStrokes.SetBuffer(_finalizeRanks_Kernel, "_strokes", _buffers.Strokes);

        _strokesToGreasePencilStrokes.SetInt("_NumFaces", _buffers.FaceCount);

        _strokesToGreasePencilStrokes.SetBuffer(_setStrokeLengthAtTail_Kernel, "_strokes", _buffers.Strokes);

        _strokesToGreasePencilStrokes.SetBuffer(_calcStrokeOffsets_Kernel, "_strokes", _buffers.Strokes);
        _strokesToGreasePencilStrokes.SetBuffer(_calcStrokeOffsets_Kernel, "numStrokesCounter", _buffers.NumStrokesCounter);
        _strokesToGreasePencilStrokes.SetBuffer(_calcStrokeOffsets_Kernel, "numStrokePointsCounter", _buffers.NumStrokePointsCounter);

        _strokesToGreasePencilStrokes.SetBuffer(_invalidateEntries_Kernel, "_denseArray", _buffers.DenseStrokes);

        _strokesToGreasePencilStrokes.SetBuffer(_sorter_Kernel, "_strokes", _buffers.Strokes);
        _strokesToGreasePencilStrokes.SetBuffer(_sorter_Kernel, "_denseArray", _buffers.DenseStrokes);
        _strokesToGreasePencilStrokes.SetBuffer(_sorter_Kernel, "_colorArray", _buffers.Color);

        _strokesToGreasePencilStrokes.SetBuffer(_buildDenseNextPointers_Kernel, "_denseArray", _buffers.DenseStrokes);
        _strokesToGreasePencilStrokes.SetBuffer(_buildDenseNextPointers_Kernel, SilhouetteShaderIDs.DenseNextPointerDst, _buffers.DenseNextPointerDst);

        _strokesToGreasePencilStrokes.SetBuffer(_offsetDenseScreenPositions_Kernel, "_denseArray", _buffers.DenseStrokes);

        _strokesToGreasePencilStrokes.SetBuffer(_initDenseScreenDistances_Kernel, "_denseArray", _buffers.DenseStrokes);
        _strokesToGreasePencilStrokes.SetBuffer(_initDenseScreenDistances_Kernel, SilhouetteShaderIDs.DenseUStrokeDst, _buffers.DenseUStrokeDst);

        _strokesToGreasePencilStrokes.SetBuffer(_densePointerJumpDistances_Kernel, "_denseArray", _buffers.DenseStrokes);
        _strokesToGreasePencilStrokes.SetBuffer(_densePointerJumpDistances_Kernel, SilhouetteShaderIDs.DenseNextPointerSrc, _buffers.DenseNextPointerSrc);
        _strokesToGreasePencilStrokes.SetBuffer(_densePointerJumpDistances_Kernel, SilhouetteShaderIDs.DenseNextPointerDst, _buffers.DenseNextPointerDst);
        _strokesToGreasePencilStrokes.SetBuffer(_densePointerJumpDistances_Kernel, SilhouetteShaderIDs.DenseUStrokeSrc, _buffers.DenseUStrokeSrc);
        _strokesToGreasePencilStrokes.SetBuffer(_densePointerJumpDistances_Kernel, SilhouetteShaderIDs.DenseUStrokeDst, _buffers.DenseUStrokeDst);

        _strokesToGreasePencilStrokes.SetBuffer(_finalizeDenseScreenDistances_Kernel, "_denseArray", _buffers.DenseStrokes);
        _strokesToGreasePencilStrokes.SetBuffer(_finalizeDenseScreenDistances_Kernel, SilhouetteShaderIDs.DenseUStrokeSrc, _buffers.DenseUStrokeSrc);
    }

    private void BindNextPointers(int kernel)
    {
        _edgesToStrokes.SetBuffer(kernel, SilhouetteShaderIDs.NextPointerSrc, _buffers.NextPointerSrc);
        _edgesToStrokes.SetBuffer(kernel, SilhouetteShaderIDs.NextPointerDst, _buffers.NextPointerDst);
    }

    private void BindRankBuffers(int kernel)
    {
        _edgesToStrokes.SetBuffer(kernel, SilhouetteShaderIDs.RankSrc, _buffers.RankSrc);
        _edgesToStrokes.SetBuffer(kernel, SilhouetteShaderIDs.RankDst, _buffers.RankDst);
    }

    private void BindDenseNextPointers(int kernel)
    {
        _strokesToGreasePencilStrokes.SetBuffer(kernel, SilhouetteShaderIDs.DenseNextPointerSrc, _buffers.DenseNextPointerSrc);
        _strokesToGreasePencilStrokes.SetBuffer(kernel, SilhouetteShaderIDs.DenseNextPointerDst, _buffers.DenseNextPointerDst);
    }

    private void BindDenseUStrokeBuffers(int kernel)
    {
        _strokesToGreasePencilStrokes.SetBuffer(kernel, SilhouetteShaderIDs.DenseUStrokeSrc, _buffers.DenseUStrokeSrc);
        _strokesToGreasePencilStrokes.SetBuffer(kernel, SilhouetteShaderIDs.DenseUStrokeDst, _buffers.DenseUStrokeDst);
    }

    public void CalculateEdges()
    {
        if (!IsSetupValid()) return;

        UpdateEdgeDetectionUniforms();

        int threadGroups = Mathf.CeilToInt(FaceCount / KERNEL_SIZE);
        if (threadGroups > 0)
            _silhouetteEdgeFinder.Dispatch(_findSilhouetteEdge_Kernel, threadGroups, 1, 1);

        RunEdgesToStrokePasses(threadGroups);
        RunStrokesToGreasePencilPass(threadGroups);
    }

    private bool IsSetupValid()
    {
        return _sourceMesh != null && viewCamera != null && _buffers != null;
    }

    private void UpdateEdgeDetectionUniforms()
    {
        _silhouetteEdgeFinder.SetVector(SilhouetteShaderIDs.WorldSpaceCameraPos, viewCamera.transform.position);

        Matrix4x4 objectToWorld = transform.localToWorldMatrix;
        _silhouetteEdgeFinder.SetMatrix(SilhouetteShaderIDs.ObjectToWorld, objectToWorld);
        _silhouetteEdgeFinder.SetMatrix(SilhouetteShaderIDs.ObjectToWorldIt, objectToWorld.inverse.transpose);
    }

    void RunEdgesToStrokePasses(int threadGroups)
    {
        BindNextPointers(_initialize_Kernel);
        _edgesToStrokes.Dispatch(_initialize_Kernel, threadGroups, 1, 1);
        _buffers.SwapNextPointers();

        for (int i = 0; i < NUM_POINTER_JUMP_ITERATIONS; ++i)
        {
            BindNextPointers(_findStrokeTail_Kernel);
            _edgesToStrokes.Dispatch(_findStrokeTail_Kernel, threadGroups, 1, 1);
            _buffers.SwapNextPointers();
        }

        BindNextPointers(_resetNext_Kernel);
        _edgesToStrokes.Dispatch(_resetNext_Kernel, threadGroups, 1, 1);
        _buffers.SwapNextPointers();

        BindRankBuffers(_initRanks_Kernel);
        _edgesToStrokes.Dispatch(_initRanks_Kernel, threadGroups, 1, 1);
        _buffers.SwapRanks();

        for (int i = 0; i < NUM_POINTER_JUMP_ITERATIONS; ++i)
        {
            BindNextPointers(_listRank_Kernel);
            BindRankBuffers(_listRank_Kernel);
            _edgesToStrokes.Dispatch(_listRank_Kernel, threadGroups, 1, 1);
            _buffers.SwapNextPointers();
            _buffers.SwapRanks();
        }

        BindRankBuffers(_finalizeRanks_Kernel);
        _edgesToStrokes.Dispatch(_finalizeRanks_Kernel, threadGroups, 1, 1);
    }

    private void RunStrokesToGreasePencilPass(int threadGroups)
    {
        _buffers.NumStrokesCounter.SetData(new[] { 0u });
        _buffers.NumStrokePointsCounter.SetData(new[] { 0u });

        _strokesToGreasePencilStrokes.Dispatch(_setStrokeLengthAtTail_Kernel, threadGroups, 1, 1);
        _strokesToGreasePencilStrokes.Dispatch(_calcStrokeOffsets_Kernel, threadGroups, 1, 1);
        _strokesToGreasePencilStrokes.Dispatch(_invalidateEntries_Kernel, threadGroups, 1, 1);

        Matrix4x4 view = viewCamera.worldToCameraMatrix;
        Matrix4x4 proj = GL.GetGPUProjectionMatrix(viewCamera.projectionMatrix, true);
        Matrix4x4 viewProj = proj * view;
        Matrix4x4 invViewProj = viewProj.inverse;

        Matrix4x4 objectToWorld = transform.localToWorldMatrix;
        Matrix4x4 worldToObject = transform.worldToLocalMatrix;

        _strokesToGreasePencilStrokes.SetMatrix(SilhouetteShaderIDs.GpObjectToWorld, objectToWorld);
        _strokesToGreasePencilStrokes.SetMatrix(SilhouetteShaderIDs.GpWorldToObject, worldToObject);
        _strokesToGreasePencilStrokes.SetMatrix(SilhouetteShaderIDs.GpProj, proj);
        _strokesToGreasePencilStrokes.SetMatrix(SilhouetteShaderIDs.GpViewProj, viewProj);
        _strokesToGreasePencilStrokes.SetMatrix(SilhouetteShaderIDs.GpInvViewProj, invViewProj);
        _strokesToGreasePencilStrokes.SetVector(SilhouetteShaderIDs.GpScreenParams, new Vector4(viewCamera.pixelWidth, viewCamera.pixelHeight, 1.0f + 1.0f / viewCamera.pixelWidth, 1.0f + 1.0f / viewCamera.pixelHeight));

        _strokesToGreasePencilStrokes.SetFloat(SilhouetteShaderIDs.RadiusMultiplier, radiusMultiplier);
        _strokesToGreasePencilStrokes.SetFloat(SilhouetteShaderIDs.LateralShiftFactor, lateralShiftFactor);
        _strokesToGreasePencilStrokes.SetFloat(SilhouetteShaderIDs.LateralShiftConstant, lateralShiftConstant);
        _strokesToGreasePencilStrokes.Dispatch(_sorter_Kernel, threadGroups, 1, 1);

        int denseThreadGroups = Mathf.CeilToInt((2 * FaceCount) / KERNEL_SIZE);
        if (denseThreadGroups <= 0) return;

        BindDenseNextPointers(_buildDenseNextPointers_Kernel);
        _strokesToGreasePencilStrokes.Dispatch(_buildDenseNextPointers_Kernel, denseThreadGroups, 1, 1);
        _buffers.SwapDenseNextPointers();

        _strokesToGreasePencilStrokes.Dispatch(_offsetDenseScreenPositions_Kernel, denseThreadGroups, 1, 1);

        _strokesToGreasePencilStrokes.Dispatch(_initDenseScreenDistances_Kernel, denseThreadGroups, 1, 1);
        _buffers.SwapDenseUStrokeBuffers();

        for (int i = 0; i < NUM_POINTER_JUMP_ITERATIONS; ++i)
        {
            BindDenseNextPointers(_densePointerJumpDistances_Kernel);
            BindDenseUStrokeBuffers(_densePointerJumpDistances_Kernel);
            _strokesToGreasePencilStrokes.Dispatch(_densePointerJumpDistances_Kernel, denseThreadGroups, 1, 1);
            _buffers.SwapDenseNextPointers();
            _buffers.SwapDenseUStrokeBuffers();
        }

        BindDenseUStrokeBuffers(_finalizeDenseScreenDistances_Kernel);
        _strokesToGreasePencilStrokes.Dispatch(_finalizeDenseScreenDistances_Kernel, denseThreadGroups, 1, 1);
    }

    void OnDestroy()
    {
        _buffers?.Dispose();
        _buffers = null;

        if (_silhouetteEdgeFinder != null) Destroy(_silhouetteEdgeFinder);
        if (_edgesToStrokes != null) Destroy(_edgesToStrokes);
        if (_strokesToGreasePencilStrokes != null) Destroy(_strokesToGreasePencilStrokes);
    }

    public int GetMaximumBufferLength()
    {
        return _buffers?.FaceCount ?? 0;
    }
    public GraphicsBuffer GetStrokeBuffer()
    {
        return _buffers?.DenseStrokes;
    }
    public GraphicsBuffer GetColorBuffer()
    {
        return _buffers?.Color;
    }
}
