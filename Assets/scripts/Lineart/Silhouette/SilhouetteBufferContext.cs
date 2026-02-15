using System;
using System.Runtime.InteropServices;
using UnityEngine;

public sealed class SilhouetteBufferContext : IDisposable
{
    public int StrokeCount { get; }
    public int FaceCount { get; }
    public int DenseCapacity { get; }

    public ComputeBuffer Vertices { get; }
    public ComputeBuffer Indices { get; }
    public ComputeBuffer AdjIndices { get; }
    public ComputeBuffer Strokes { get; }

    public ComputeBuffer NextPointerSrc { get; private set; }
    public ComputeBuffer NextPointerDst { get; private set; }
    public ComputeBuffer RankSrc { get; private set; }
    public ComputeBuffer RankDst { get; private set; }

    public ComputeBuffer DenseNextPointerSrc { get; private set; }
    public ComputeBuffer DenseNextPointerDst { get; private set; }
    public ComputeBuffer DenseUStrokeSrc { get; private set; }
    public ComputeBuffer DenseUStrokeDst { get; private set; }

    public ComputeBuffer NumStrokesCounter { get; }
    public ComputeBuffer NumStrokePointsCounter { get; }

    public GraphicsBuffer DenseStrokes { get; }
    public GraphicsBuffer Color { get; }

    private SilhouetteBufferContext(
        int strokeCount,
        int faceCount,
        int denseCapacity,
        ComputeBuffer vertices,
        ComputeBuffer indices,
        ComputeBuffer adjIndices,
        ComputeBuffer strokes,
        ComputeBuffer nextPointerSrc,
        ComputeBuffer nextPointerDst,
        ComputeBuffer rankSrc,
        ComputeBuffer rankDst,
        ComputeBuffer denseNextPointerSrc,
        ComputeBuffer denseNextPointerDst,
        ComputeBuffer denseUStrokeSrc,
        ComputeBuffer denseUStrokeDst,
        ComputeBuffer numStrokesCounter,
        ComputeBuffer numStrokePointsCounter,
        GraphicsBuffer denseStrokes,
        GraphicsBuffer color)
    {
        StrokeCount = strokeCount;
        FaceCount = faceCount;
        DenseCapacity = denseCapacity;

        Vertices = vertices;
        Indices = indices;
        AdjIndices = adjIndices;
        Strokes = strokes;

        NextPointerSrc = nextPointerSrc;
        NextPointerDst = nextPointerDst;
        RankSrc = rankSrc;
        RankDst = rankDst;

        DenseNextPointerSrc = denseNextPointerSrc;
        DenseNextPointerDst = denseNextPointerDst;
        DenseUStrokeSrc = denseUStrokeSrc;
        DenseUStrokeDst = denseUStrokeDst;

        NumStrokesCounter = numStrokesCounter;
        NumStrokePointsCounter = numStrokePointsCounter;

        DenseStrokes = denseStrokes;
        Color = color;
    }

    public static SilhouetteBufferContext CreateForSharp(Mesh mesh)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));

        int cornerCount = mesh.triangles.Length;
        int faceCount = mesh.triangles.Length / 3;
        int denseCapacity = cornerCount;

        Vector3[] positions = mesh.vertices;
        Vector3[] normals = mesh.normals;
        var vertexDataArray = new SilhouetteSourceVertex[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            vertexDataArray[i] = new SilhouetteSourceVertex { position = positions[i], normal = normals[i] };
        }

        var verticesBuffer = new ComputeBuffer(vertexDataArray.Length, Marshal.SizeOf(typeof(SilhouetteSourceVertex)));
        verticesBuffer.SetData(vertexDataArray);

        int[] indices = mesh.triangles;
        var indicesBuffer = new ComputeBuffer(indices.Length, sizeof(int));
        indicesBuffer.SetData(indices);

        int[] adjData = SilhouetteMeshUtility.CalculateCornerAdjacency(indices, positions);
        var adjIndicesBuffer = new ComputeBuffer(adjData.Length, sizeof(uint));
        adjIndicesBuffer.SetData(adjData);

        var strokesBuffer = new ComputeBuffer(cornerCount, SilhouetteStrokeEdge.SizeOf);

        var nextPointerSrcBuffer = new ComputeBuffer(cornerCount, sizeof(int));
        var nextPointerDstBuffer = new ComputeBuffer(cornerCount, sizeof(int));
        var rankSrcBuffer = new ComputeBuffer(cornerCount, sizeof(uint));
        var rankDstBuffer = new ComputeBuffer(cornerCount, sizeof(uint));

        var denseStrokesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, denseCapacity, GreasePencilRenderer.GreasePencilStrokeVert.SizeOf);
        var colorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, denseCapacity, GreasePencilRenderer.GreasePencilColorVert.SizeOf);

        var numStrokesCounterBuffer = new ComputeBuffer(1, sizeof(uint));
        var numStrokePointsCounterBuffer = new ComputeBuffer(1, sizeof(uint));

        var denseNextPointerSrcBuffer = new ComputeBuffer(denseCapacity, sizeof(int));
        var denseNextPointerDstBuffer = new ComputeBuffer(denseCapacity, sizeof(int));
        var denseUStrokeSrcBuffer = new ComputeBuffer(denseCapacity, sizeof(float));
        var denseUStrokeDstBuffer = new ComputeBuffer(denseCapacity, sizeof(float));

        return new SilhouetteBufferContext(
            cornerCount,
            faceCount,
            denseCapacity,
            verticesBuffer,
            indicesBuffer,
            adjIndicesBuffer,
            strokesBuffer,
            nextPointerSrcBuffer,
            nextPointerDstBuffer,
            rankSrcBuffer,
            rankDstBuffer,
            denseNextPointerSrcBuffer,
            denseNextPointerDstBuffer,
            denseUStrokeSrcBuffer,
            denseUStrokeDstBuffer,
            numStrokesCounterBuffer,
            numStrokePointsCounterBuffer,
            denseStrokesBuffer,
            colorBuffer);
    }

    public static SilhouetteBufferContext CreateForSmooth(Mesh mesh)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));

        int faceCount = mesh.triangles.Length / 3;
        int denseCapacity = 2 * faceCount;

        Vector3[] positions = mesh.vertices;
        Vector3[] normals = mesh.normals;
        var vertexDataArray = new SilhouetteSourceVertex[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            vertexDataArray[i] = new SilhouetteSourceVertex { position = positions[i], normal = normals[i] };
        }

        var verticesBuffer = new ComputeBuffer(vertexDataArray.Length, Marshal.SizeOf(typeof(SilhouetteSourceVertex)));
        verticesBuffer.SetData(vertexDataArray);

        int[] indices = mesh.triangles;
        var indicesBuffer = new ComputeBuffer(indices.Length, sizeof(int));
        indicesBuffer.SetData(indices);

        int[] adjData = SilhouetteMeshUtility.CalculateAdjacency(indices, positions);
        var adjIndicesBuffer = new ComputeBuffer(adjData.Length, sizeof(uint));
        adjIndicesBuffer.SetData(adjData);

        var strokesBuffer = new ComputeBuffer(faceCount, SilhouetteStrokeEdge.SizeOf);

        var nextPointerSrcBuffer = new ComputeBuffer(faceCount, sizeof(int));
        var nextPointerDstBuffer = new ComputeBuffer(faceCount, sizeof(int));
        var rankSrcBuffer = new ComputeBuffer(faceCount, sizeof(uint));
        var rankDstBuffer = new ComputeBuffer(faceCount, sizeof(uint));

        var denseStrokesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, denseCapacity, GreasePencilRenderer.GreasePencilStrokeVert.SizeOf);
        var colorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, denseCapacity, GreasePencilRenderer.GreasePencilColorVert.SizeOf);

        var numStrokesCounterBuffer = new ComputeBuffer(1, sizeof(uint));
        var numStrokePointsCounterBuffer = new ComputeBuffer(1, sizeof(uint));

        var denseNextPointerSrcBuffer = new ComputeBuffer(denseCapacity, sizeof(int));
        var denseNextPointerDstBuffer = new ComputeBuffer(denseCapacity, sizeof(int));
        var denseUStrokeSrcBuffer = new ComputeBuffer(denseCapacity, sizeof(float));
        var denseUStrokeDstBuffer = new ComputeBuffer(denseCapacity, sizeof(float));

        return new SilhouetteBufferContext(
            faceCount,
            faceCount,
            denseCapacity,
            verticesBuffer,
            indicesBuffer,
            adjIndicesBuffer,
            strokesBuffer,
            nextPointerSrcBuffer,
            nextPointerDstBuffer,
            rankSrcBuffer,
            rankDstBuffer,
            denseNextPointerSrcBuffer,
            denseNextPointerDstBuffer,
            denseUStrokeSrcBuffer,
            denseUStrokeDstBuffer,
            numStrokesCounterBuffer,
            numStrokePointsCounterBuffer,
            denseStrokesBuffer,
            colorBuffer);
    }

    public void SwapNextPointers()
    {
        (NextPointerSrc, NextPointerDst) = (NextPointerDst, NextPointerSrc);
    }

    public void SwapRanks()
    {
        (RankSrc, RankDst) = (RankDst, RankSrc);
    }

    public void SwapDenseNextPointers()
    {
        (DenseNextPointerSrc, DenseNextPointerDst) = (DenseNextPointerDst, DenseNextPointerSrc);
    }

    public void SwapDenseUStrokeBuffers()
    {
        (DenseUStrokeSrc, DenseUStrokeDst) = (DenseUStrokeDst, DenseUStrokeSrc);
    }

    public void Dispose()
    {
        Vertices?.Release();
        Indices?.Release();
        AdjIndices?.Release();
        Strokes?.Release();
        NextPointerSrc?.Release();
        NextPointerDst?.Release();
        RankSrc?.Release();
        RankDst?.Release();
        DenseNextPointerSrc?.Release();
        DenseNextPointerDst?.Release();
        DenseUStrokeSrc?.Release();
        DenseUStrokeDst?.Release();
        NumStrokesCounter?.Release();
        NumStrokePointsCounter?.Release();
        DenseStrokes?.Release();
        Color?.Release();
    }
}
