using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// Renders the heightfield as a smooth 3x-subdivided mesh, plus instanced flora and rocks.
public class TerrainView
{
    const int N = World.N, V = World.V, S = 3, SV = N * S + 1;
    public const int PropKinds = 10;   // 0 conifer 1 broadleaf 2 birch 3 palm 4 rock 5 bush 6 grass 7 flowersA 8 flowersB 9 reeds
    readonly World w;
    readonly Mesh mesh;
    readonly Vector3[] verts = new Vector3[SV * SV], norms = new Vector3[SV * SV];
    readonly float[] hy = new float[SV * SV];
    readonly Texture2D mask;
    readonly Color32[] maskPx = new Color32[V * V];
    readonly Mesh[] propMesh;
    readonly List<Matrix4x4>[] props = new List<Matrix4x4>[PropKinds];
    readonly Matrix4x4[] tmp = new Matrix4x4[1023];
    RenderParams rpShadow, rpNoShadow;

    public TerrainView(World world, Transform root, Material terrainMat, Material waterMat, Material treeMaterial)
    {
        w = world;
        mesh = new Mesh { name = "Terrain", indexFormat = IndexFormat.UInt32 };
        mesh.MarkDynamic();
        for (int z = 0; z < SV; z++)
            for (int x = 0; x < SV; x++)
            { verts[z * SV + x] = new Vector3(x / (float)S, 0, z / (float)S); norms[z * SV + x] = Vector3.up; }
        var tris = new int[(SV - 1) * (SV - 1) * 6];
        int k = 0;
        for (int z = 0; z < SV - 1; z++)
            for (int x = 0; x < SV - 1; x++)
            {
                int a = z * SV + x, b = a + 1, c = a + SV, d = c + 1;
                if (((x + z) & 1) == 0) { tris[k++] = a; tris[k++] = c; tris[k++] = d; tris[k++] = a; tris[k++] = d; tris[k++] = b; }
                else { tris[k++] = a; tris[k++] = c; tris[k++] = b; tris[k++] = b; tris[k++] = c; tris[k++] = d; }
            }
        mesh.vertices = verts; mesh.normals = norms; mesh.triangles = tris;
        mesh.bounds = new Bounds(new Vector3(N * 0.5f, 1.5f, N * 0.5f), new Vector3(N, 12f, N));

        var go = new GameObject("Terrain");
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = terrainMat;
        mr.shadowCastingMode = ShadowCastingMode.On;

        var wgo = new GameObject("Water");
        wgo.transform.SetParent(root, false);
        wgo.transform.position = new Vector3(0, World.WaterY, 0);
        wgo.AddComponent<MeshFilter>().sharedMesh = Models.WaterGrid(-500f, N + 500f, 64);
        var wr = wgo.AddComponent<MeshRenderer>();
        wr.sharedMaterial = waterMat;
        wr.shadowCastingMode = ShadowCastingMode.Off;

        mask = new Texture2D(V, V, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Shader.SetGlobalTexture("_PZMask", mask);

        propMesh = Models.Props();
        for (int i = 0; i < PropKinds; i++) props[i] = new List<Matrix4x4>();
        var b2 = new Bounds(new Vector3(N * 0.5f, 3f, N * 0.5f), new Vector3(N + 10, 30, N + 10));
        rpShadow = new RenderParams(treeMaterial) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, worldBounds = b2 };
        rpNoShadow = new RenderParams(treeMaterial) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, worldBounds = b2 };
    }

    public void Sync()
    {
        if (w.meshDirty) { RebuildMesh(); w.meshDirty = false; }
        if (w.colorsDirty) { RebuildMask(); w.colorsDirty = false; }
        if (w.treesDirty) { RebuildProps(); w.treesDirty = false; }
    }

    void RebuildMask()
    {
        for (int z = 0; z < V; z++)
            for (int x = 0; x < V; x++)
            {
                int i = World.VI(x, z);
                bool sw = false;
                for (int dz = -1; dz <= 0; dz++)
                    for (int dx = -1; dx <= 0; dx++)
                        if (World.InC(x + dx, z + dz) && w.swamp[World.CI(x + dx, z + dz)]) sw = true;
                maskPx[i] = new Color32((byte)(w.fieldA[i] * 255f), (byte)(sw ? 255 : 0), (byte)(w.fieldB[i] * 255f), (byte)(Mathf.Clamp01(w.scorch[i]) * 255f));
            }
        mask.SetPixels32(maskPx);
        mask.Apply(false);
    }

    void RebuildMesh()
    {
        if (w.rx1 < w.rx0) return;
        int x0 = Mathf.Max(0, w.rx0 * S), x1 = Mathf.Min(SV - 1, w.rx1 * S), z0 = Mathf.Max(0, w.rz0 * S), z1 = Mathf.Min(SV - 1, w.rz1 * S);
        w.ClearRect();
        const float inv = 1f / S;
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int i = z * SV + x;
                float y = w.Height(x * inv, z * inv);
                hy[i] = y; verts[i].y = y;
            }
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int i = z * SV + x;
                float l = hy[x > 0 ? i - 1 : i], r = hy[x < SV - 1 ? i + 1 : i], d = hy[z > 0 ? i - SV : i], u = hy[z < SV - 1 ? i + SV : i];
                norms[i] = new Vector3(l - r, 2f * inv, d - u).normalized;
            }
        mesh.vertices = verts; mesh.normals = norms;
    }

    static float Hash(int i, int salt)
    {
        uint n = (uint)(i * 374761393 + salt * 668265263);
        n = (n ^ (n >> 13)) * 1274126177u;
        return ((n ^ (n >> 16)) & 0xFFFF) / 65535f;
    }

    void Place(int kind, int c, int salt, float spread, float smin, float smax, bool align)
    {
        int x = c % N, z = c / N;
        float px = x + 0.5f + (Hash(c, salt + 1) - 0.5f) * spread, pz = z + 0.5f + (Hash(c, salt + 2) - 0.5f) * spread;
        float y = w.Height(px, pz);
        if (y < World.WaterY + 0.02f) return;
        float s = Mathf.Lerp(smin, smax, Hash(c, salt + 3));
        Quaternion q = Quaternion.Euler(0, Hash(c, salt + 4) * 360f, 0);
        if (align)
        {
            Vector3 n = new Vector3(w.Height(px - 0.2f, pz) - w.Height(px + 0.2f, pz), 0.4f, w.Height(px, pz - 0.2f) - w.Height(px, pz + 0.2f)).normalized;
            q = Quaternion.FromToRotation(Vector3.up, n) * q;
        }
        props[kind].Add(Matrix4x4.TRS(new Vector3(px, y - 0.02f, pz), q, new Vector3(s, s * (0.9f + Hash(c, salt + 5) * 0.3f), s)));
    }

    void RebuildProps()
    {
        for (int i = 0; i < PropKinds; i++) props[i].Clear();
        for (int c = 0; c < N * N; c++)
        {
            if (!w.walk[c]) continue;
            int x = c % N, z = c / N, vi = World.VI(x, z);
            int t = w.tree[c];
            if (t != 0) { Place(t - 1, c, 0, 0.6f, t == 4 ? 0.9f : 0.75f, t == 4 ? 1.3f : 1.35f, false); if (Hash(c, 40) < 0.5f) Place(6, c, 50, 0.8f, 0.8f, 1.2f, true); continue; }
            if (w.swamp[c]) { if (Hash(c, 9) < 0.7f) Place(9, c, 10, 0.7f, 0.7f, 1.3f, false); continue; }
            if (w.fieldA[vi] + w.fieldB[vi] + w.fieldA[vi + V + 1] + w.fieldB[vi + V + 1] > 0f) continue;
            if (w.scorch[vi] > 0.3f) continue;
            int sum = w.h[vi] + w.h[vi + 1] + w.h[vi + V] + w.h[vi + V + 1];
            bool flat = w.flat[c] >= 1;
            float r = Hash(c, 20);
            if ((!flat && r < 0.09f) || (sum >= 24 && r < 0.3f)) Place(4, c, 21, 0.7f, 0.5f, 1.5f, true);
            else if (sum <= 4) continue;                              // beach sand stays clean
            else if (r < 0.15f) Place(5, c, 22, 0.7f, 0.7f, 1.3f, true);
            else if (r < 0.55f) { Place(6, c, 23, 0.6f, 0.8f, 1.3f, true); if (r < 0.32f) Place(6, c, 33, 0.9f, 0.7f, 1.1f, true); }
            else if (r < 0.63f) Place(r < 0.59f ? 7 : 8, c, 24, 0.6f, 0.8f, 1.2f, true);
        }
    }

    public void Draw()
    {
        for (int t = 0; t < PropKinds; t++)
        {
            var list = props[t];
            for (int start = 0; start < list.Count; start += 1023)
            {
                int n = Mathf.Min(1023, list.Count - start);
                list.CopyTo(start, tmp, 0, n);
                Graphics.RenderMeshInstanced(t <= 5 ? rpShadow : rpNoShadow, propMesh[t], 0, tmp, n);
            }
        }
    }
}
