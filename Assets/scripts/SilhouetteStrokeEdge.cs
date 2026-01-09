using System.Runtime.InteropServices;
using UnityEngine;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SilhouetteStrokeEdge
{
    public Vector3 pos;
    public int adj;
    public Vector3 faceNormal;
    public uint minPoint;
    public uint rank;            // hop count to tail
    public uint flags;
    public float distFromTail;   // cumulative geometric distance to tail (0 at tail)

    public uint totalStrokeLength; // total length of the stroke that contains this point

    public uint strokePointsOffset; // Offset to the stroke points array

    public static int SizeOf => Marshal.SizeOf(typeof(SilhouetteStrokeEdge));
}
