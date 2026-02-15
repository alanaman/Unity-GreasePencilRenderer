using UnityEngine;
//helpful when debugging silhouette strokes.
// ReSharper disable once RedundantUsingDirective
using System.Linq;

public static class SilhouetteDebug
{
    public static void LogStrokeEdges(ComputeBuffer strokesBuffer, int strokeCount, int adjNone, int invalidAdj)
    {
        var strokes = new SilhouetteStrokeEdge[strokeCount];
        strokesBuffer.GetData(strokes);

        int printCount = 0;
        for (int j = 0; j < strokes.Length && printCount < 10; j++)
        {
            if (strokes[j].adj != adjNone && strokes[j].adj != invalidAdj)
            {
                Debug.Log($"Stroke[{j}] pos={strokes[j].pos} adj={strokes[j].adj} minPoint={strokes[j].minPoint} rank={strokes[j].rank}");
                printCount++;
            }
        }
    }

    public static void LogGpStrokes(GraphicsBuffer denseStrokesBuffer, int count)
    {
        var gpStrokes = new GreasePencilRenderer.GreasePencilStrokeVert[count];
        denseStrokesBuffer.GetData(gpStrokes);

        for (int j = 0; j < gpStrokes.Length; j++)
        {
            Debug.Log($"GP Stroke[{j}] pos={gpStrokes[j].pos} mat={gpStrokes[j].mat} strokePointIdx={gpStrokes[j].point_id}");
        }
    }

    public static void DrawSilhouetteEdges(ComputeBuffer strokesBuffer, int strokeCount, int adjNone)
    {
        var debugStrokes = new SilhouetteStrokeEdge[strokeCount];
        strokesBuffer.GetData(debugStrokes);

        for (int i = 0; i < debugStrokes.Length; i++)
        {
            var adj1 = debugStrokes[i].adj;
            var pos1 = debugStrokes[i].pos;
            if (!debugStrokes[i].IsInvalid() && adj1 >= 0 && adj1 < debugStrokes.Length && adj1 != adjNone)
            {
                var pos2 = debugStrokes[adj1].pos;
                Debug.DrawLine(pos1, pos2, Color.red);
            }
        }
    }
}
