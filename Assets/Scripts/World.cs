using System.Collections.Generic;
using UnityEngine;

/// Heightfield world: integer vertex heights with a max step of 1 between neighbours.
public class World
{
    public const int N = 128, V = N + 1, MaxH = 10, INF = 1 << 29;
    public const float Step = 0.6f, WaterY = 0.3f;

    public readonly int[] h = new int[V * V];
    public readonly float[] vy = new float[V * V];
    public readonly float[] scorch = new float[V * V];
    public readonly float[] fieldA = new float[V * V], fieldB = new float[V * V];
    public readonly bool[] swamp = new bool[N * N];
    public readonly byte[] tree = new byte[N * N];
    public readonly sbyte[] flat = new sbyte[N * N];   // flat land height, or -1
    public readonly bool[] walk = new bool[N * N];
    public bool meshDirty = true, treesDirty = true, colorsDirty = true;

    public int rx0 = 0, rz0 = 0, rx1 = N, rz1 = N;   // dirty vertex rectangle for the renderer
    bool partial;
    void Mark(int x0, int z0, int x1, int z1)
    {
        rx0 = Mathf.Max(0, Mathf.Min(rx0, x0)); rz0 = Mathf.Max(0, Mathf.Min(rz0, z0));
        rx1 = Mathf.Min(N, Mathf.Max(rx1, x1)); rz1 = Mathf.Min(N, Mathf.Max(rz1, z1));
    }
    public void ClearRect() { rx0 = rz0 = V; rx1 = rz1 = -1; }
    int bx0, bz0, bx1, bz1; bool seaChange;
    void Track(int x, int z, int before, int after)
    {
        if (x < bx0) bx0 = x; if (x > bx1) bx1 = x; if (z < bz0) bz0 = z; if (z > bz1) bz1 = z;
        if ((before == 0) != (after == 0)) seaChange = true;
    }
    void BeginEdit() { bx0 = bz0 = V; bx1 = bz1 = -1; seaChange = false; }
    void EndEdit() { int pad = seaChange ? 13 : 3; Mark(bx0 - pad, bz0 - pad, bx1 + pad, bz1 + pad); partial = true; Changed(); }

    readonly int[] queue = new int[V * V];
    readonly int[] sdist = new int[V * V];
    readonly Stack<int> st = new Stack<int>();

    public static int VI(int x, int z) { return z * V + x; }
    public static int CI(int x, int z) { return z * N + x; }
    public static bool InV(int x, int z) { return x >= 0 && z >= 0 && x < V && z < V; }
    public static bool InC(int x, int z) { return x >= 0 && z >= 0 && x < N && z < N; }
    static int Cap(int x, int z) { return Mathf.Min(MaxH, Mathf.Min(Mathf.Min(x, z), Mathf.Min(N - x, N - z))); }

    static float Fbm(float x, float z)
    {
        float a = 0.5f, s = 0f, n = 0f;
        for (int i = 0; i < 4; i++) { s += a * Mathf.PerlinNoise(x, z); n += a; x = x * 2.03f + 31.7f; z = z * 2.03f + 11.3f; a *= 0.5f; }
        return s / n;
    }

    public void Generate(int seed)
    {
        var rng = new System.Random(seed);
        float ox = rng.Next(1000), oz = rng.Next(1000);
        for (int z = 0; z < V; z++)
            for (int x = 0; x < V; x++)
            {
                float u = x / (float)N * 2f - 1f, w = z / (float)N * 2f - 1f;
                float d = Mathf.Sqrt(u * u + w * w) * 0.7f + Mathf.Max(Mathf.Abs(u), Mathf.Abs(w)) * 0.3f;
                float fall = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.60f, 0.97f, d));
                float n = Fbm(x * 0.03f + ox, z * 0.03f + oz);
                float e = fall * 0.60f + (n - 0.5f) * 1.5f + (Fbm(x * 0.12f + oz, z * 0.12f + ox) - 0.5f) * 0.55f;
                int hv = Mathf.Clamp(Mathf.RoundToInt((e - 0.24f) * 8f), 0, 8);
                h[VI(x, z)] = Mathf.Min(hv, Cap(x, z));
            }
        RelaxDown();

        // forests
        float fx = rng.Next(1000), fz = rng.Next(1000);
        for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                int c = CI(x, z);
                tree[c] = 0;
                int sum = h[VI(x, z)] + h[VI(x + 1, z)] + h[VI(x, z + 1)] + h[VI(x + 1, z + 1)];
                if (sum < 4) continue;
                float f = Fbm(x * 0.07f + fx, z * 0.07f + fz);
                float r = (float)rng.NextDouble();
                if (sum <= 5 && r < 0.10f) tree[c] = 4;
                else if (f > 0.5f && r < (f - 0.45f) * 3.2f) tree[c] = (byte)(sum >= 16 || rng.NextDouble() < 0.3 ? 1 : (rng.NextDouble() < 0.3 ? 3 : 2));
                else if (r < 0.015f) tree[c] = (byte)(rng.NextDouble() < 0.4 ? 3 : 2);
            }
        Changed();
    }

    void RelaxDown()
    {
        bool ch = true; int guard = 0;
        while (ch && guard++ < 64)
        {
            ch = false;
            for (int pass = 0; pass < 2; pass++)
                for (int k = 0; k < V * V; k++)
                {
                    int i = pass == 0 ? k : V * V - 1 - k;
                    int x = i % V, z = i / V, m = h[i];
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int ax = x + dx, az = z + dz;
                            if (!InV(ax, az)) continue;
                            int v = h[VI(ax, az)] + 1;
                            if (v < m) m = v;
                        }
                    if (m < h[i]) { h[i] = m; ch = true; }
                }
        }
    }

    void RelaxUp()
    {
        bool ch = true; int guard = 0;
        while (ch && guard++ < 64)
        {
            ch = false;
            for (int pass = 0; pass < 2; pass++)
                for (int k = 0; k < V * V; k++)
                {
                    int i = pass == 0 ? k : V * V - 1 - k;
                    int x = i % V, z = i / V, m = h[i];
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int ax = x + dx, az = z + dz;
                            if (!InV(ax, az)) continue;
                            int v = h[VI(ax, az)] - 1;
                            if (v > m) m = v;
                        }
                    m = Mathf.Min(m, Cap(x, z));
                    if (m > h[i]) { h[i] = m; ch = true; }
                }
        }
    }

    /// Empty sea with a single plateau island (used for the app-icon render).
    public void MakeIsland(int cx, int cz, int r, int height)
    {
        System.Array.Clear(h, 0, h.Length); System.Array.Clear(tree, 0, tree.Length);
        FlattenArea(cx, cz, r, height);
    }

    public void FlattenArea(int cx, int cz, int r, int height)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            for (int z = cz - r; z <= cz + r; z++)
                for (int x = cx - r; x <= cx + r; x++)
                    if (InV(x, z) && (x - cx) * (x - cx) + (z - cz) * (z - cz) <= r * r + 1)
                        h[VI(x, z)] = Mathf.Min(height, Cap(x, z));
            if (pass == 0) RelaxDown(); else RelaxUp();
        }
        Changed();
    }

    public int Raise(int x, int z)
    {
        if (!InV(x, z)) return 0;
        int i = VI(x, z);
        if (h[i] >= Cap(x, z)) return 0;
        int n = 1;
        BeginEdit();
        h[i]++; Track(x, z, h[i] - 1, h[i]);
        st.Push(i);
        while (st.Count > 0)
        {
            int q = st.Pop(); int qx = q % V, qz = q / V;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ax = qx + dx, az = qz + dz;
                    if (!InV(ax, az)) continue;
                    int a = VI(ax, az);
                    if (h[a] < h[q] - 1) { Track(ax, az, h[a], h[q] - 1); h[a] = h[q] - 1; n++; st.Push(a); }
                }
        }
        EndEdit();
        return n;
    }

    public int Lower(int x, int z)
    {
        if (!InV(x, z)) return 0;
        int i = VI(x, z);
        if (h[i] <= 0) return 0;
        int n = 1;
        BeginEdit();
        h[i]--; Track(x, z, h[i] + 1, h[i]);
        st.Push(i);
        while (st.Count > 0)
        {
            int q = st.Pop(); int qx = q % V, qz = q / V;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ax = qx + dx, az = qz + dz;
                    if (!InV(ax, az)) continue;
                    int a = VI(ax, az);
                    if (h[a] > h[q] + 1) { Track(ax, az, h[a], h[q] + 1); h[a] = h[q] + 1; n++; st.Push(a); }
                }
        }
        EndEdit();
        return n;
    }

    public void Flood()
    {
        for (int i = 0; i < V * V; i++) if (h[i] > 0) h[i]--;
        Changed();
    }

    /// Recompute everything derived from the height grid.
    public void Changed()
    {
        if (!partial) Mark(0, 0, N, N);
        partial = false;
        // seabed depth: BFS distance from land
        int qh = 0, qt = 0;
        for (int i = 0; i < V * V; i++)
        {
            if (h[i] > 0) { sdist[i] = 0; queue[qt++] = i; } else sdist[i] = INF;
        }
        while (qh < qt)
        {
            int q = queue[qh++]; int x = q % V, z = q / V, d = sdist[q] + 1;
            if (d > 10) continue;
            if (x > 0 && sdist[q - 1] > d) { sdist[q - 1] = d; queue[qt++] = q - 1; }
            if (x < V - 1 && sdist[q + 1] > d) { sdist[q + 1] = d; queue[qt++] = q + 1; }
            if (z > 0 && sdist[q - V] > d) { sdist[q - V] = d; queue[qt++] = q - V; }
            if (z < V - 1 && sdist[q + V] > d) { sdist[q + V] = d; queue[qt++] = q + V; }
        }
        for (int i = 0; i < V * V; i++)
            vy[i] = h[i] > 0 ? h[i] * Step : -Mathf.Min(sdist[i], 10) * 0.32f;

        for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                int c = CI(x, z);
                int a = h[VI(x, z)], b = h[VI(x + 1, z)], cc = h[VI(x, z + 1)], d = h[VI(x + 1, z + 1)];
                bool fl = a == b && a == cc && a == d && a >= 1;
                flat[c] = (sbyte)(fl ? a : -1);
                walk[c] = a + b + cc + d >= 2;
                if (!fl) swamp[c] = false;
                if (!walk[c]) tree[c] = 0;
            }
        meshDirty = treesDirty = colorsDirty = true;
    }

    // Monotone cubic (PCHIP) interpolation: flats stay exactly flat, slopes blend smoothly.
    static float Tan(float a, float b, float c)
    {
        float d0 = b - a, d1 = c - b;
        return d0 * d1 <= 0f ? 0f : 2f * d0 * d1 / (d0 + d1);
    }
    static float Herm(float p0, float p1, float p2, float p3, float t)
    {
        float m1 = Tan(p0, p1, p2), m2 = Tan(p1, p2, p3), t2 = t * t, t3 = t2 * t;
        return (2f * t3 - 3f * t2 + 1f) * p1 + (t3 - 2f * t2 + t) * m1 + (-2f * t3 + 3f * t2) * p2 + (t3 - t2) * m2;
    }
    float Row(int z, int ix, float fx)
    {
        z = z < 0 ? 0 : (z > N ? N : z);
        int i = z * V;
        int x0 = ix > 0 ? ix - 1 : 0, x3 = ix + 2 > N ? N : ix + 2;
        return Herm(vy[i + x0], vy[i + ix], vy[i + ix + 1], vy[i + x3], fx);
    }
    public float Height(float x, float z)
    {
        x = Mathf.Clamp(x, 0f, N - 0.0001f); z = Mathf.Clamp(z, 0f, N - 0.0001f);
        int ix = (int)x, iz = (int)z; float fx = x - ix, fz = z - iz;
        return Herm(Row(iz - 1, ix, fx), Row(iz, ix, fx), Row(iz + 1, ix, fx), Row(iz + 2, ix, fx), fz);
    }

    public bool Inside(Vector3 p) { return p.x > 0.5f && p.z > 0.5f && p.x < N - 0.5f && p.z < N - 0.5f; }

    public bool Raycast(Ray ray, out Vector3 hit)
    {
        hit = Vector3.zero;
        float t = 0f, top = MaxH * Step + 0.5f;
        if (ray.origin.y > top)
        {
            if (ray.direction.y >= -1e-4f) return false;
            t = (top - ray.origin.y) / ray.direction.y;
        }
        float prev = t;
        for (int i = 0; i < 3000; i++)
        {
            Vector3 p = ray.GetPoint(t);
            if (p.y <= Ground(p))
            {
                float lo = prev, hi = t;
                for (int k = 0; k < 10; k++)
                {
                    float mid = (lo + hi) * 0.5f; Vector3 m = ray.GetPoint(mid);
                    if (m.y <= Ground(m)) hi = mid; else lo = mid;
                }
                hit = ray.GetPoint(hi);
                return true;
            }
            prev = t; t += 0.2f;
            if (p.y < -1f) break;
        }
        return false;
    }

    float Ground(Vector3 p)
    {
        if (p.x < 0 || p.z < 0 || p.x > N || p.z > N) return WaterY;
        return Mathf.Max(Height(p.x, p.z), WaterY);
    }

    static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 }, DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

    /// Multi-source BFS over walkable cells.
    public void Flow(List<int> src, int[] dist)
    {
        for (int i = 0; i < N * N; i++) dist[i] = INF;
        int qh = 0, qt = 0;
        for (int i = 0; i < src.Count; i++)
        {
            int s = src[i];
            if (dist[s] != 0) { dist[s] = 0; queue[qt++] = s; }
        }
        while (qh < qt)
        {
            int c = queue[qh++]; int cx = c % N, cz = c / N, d = dist[c] + 1;
            for (int k = 0; k < 8; k++)
            {
                int nx = cx + DX[k], nz = cz + DZ[k];
                if (!InC(nx, nz)) continue;
                int n = CI(nx, nz);
                if (dist[n] != INF || !walk[n]) continue;
                if (k >= 4 && (!walk[CI(nx, cz)] || !walk[CI(cx, nz)])) continue;
                dist[n] = d; queue[qt++] = n;
            }
        }
    }
}
