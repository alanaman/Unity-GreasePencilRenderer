using System;
using System.Collections.Generic;
using UnityEngine;

public static class SilhouetteMeshUtility
{
    public const int AdjNone = -1;
    public const int InvalidAdj = -2;

    public static int[] CalculateCornerAdjacency(int[] triangles, Vector3[] positions)
    {
        if (triangles == null) throw new ArgumentNullException(nameof(triangles));
        if (positions == null) throw new ArgumentNullException(nameof(positions));
        var cornerCount = triangles.Length;
        if (cornerCount % 3 != 0) throw new ArgumentException("Triangle array length must be a multiple of 3.", nameof(triangles));

        var faceCount = cornerCount / 3;
        int[] adj = new int[cornerCount];
        for (int i = 0; i < adj.Length; i++) adj[i] = InvalidAdj;

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
                adj[corners[0].CornerIndex] = InvalidAdj;
                continue;
            }
            if (corners.Count > 2)
            {
                Debug.LogWarning("Non Manifold edges found in mesh");
                foreach (var corner in corners)
                {
                    adj[corner.CornerIndex] = InvalidAdj;
                }
                continue;
            }

            int c1 = corners[0].CornerIndex;
            int c2 = corners[1].CornerIndex;

            adj[c1] = (c2 + 1) % 3 + c2 / 3 * 3;
            adj[c2] = (c1 + 1) % 3 + c1 / 3 * 3;
        }

        return adj;
    }

    public static int[] CalculateAdjacency(int[] triangles, Vector3[] vertices, float epsilon = 0.0001f)
    {
        if (triangles == null) throw new ArgumentNullException(nameof(triangles));
        int faceCount = triangles.Length / 3;
        int[] adj = new int[triangles.Length];
        for (int i = 0; i < adj.Length; i++) adj[i] = AdjNone;

        int[] vertexMap = new int[vertices.Length];
        var posDict = new Dictionary<Vector3, int>(vertices.Length);

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

        for (int f = 0; f < faceCount; f++)
        {
            int baseIdx = f * 3;
            int v0 = triangles[baseIdx + 0];
            int v1 = triangles[baseIdx + 1];
            int v2 = triangles[baseIdx + 2];

            int u0 = vertexMap[v0];
            int u1 = vertexMap[v1];
            int u2 = vertexMap[v2];

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

        for (int f = 0; f < faceCount; f++)
        {
            int baseIdx = f * 3;
            int v0 = triangles[baseIdx + 0];
            int v1 = triangles[baseIdx + 1];
            int v2 = triangles[baseIdx + 2];

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
}
