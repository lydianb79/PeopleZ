using System.Collections.Generic;
using UnityEngine;

/// Tiny procedural mesh builder. Vertex colour = albedo (alpha = wind sway weight),
/// uv.x = team tint mask, uv.y = emission mask.
public class MB
{
    readonly List<Vector3> v = new List<Vector3>();
    readonly List<Color> c = new List<Color>();
    readonly List<Vector2> uv = new List<Vector2>();
    readonly List<int> t = new List<int>();
    public Color col = Color.white;
    public float team, emis, sway;
    public bool mirror;
    Vector3 M(Vector3 p) { return mirror ? new Vector3(-p.x, p.y, p.z) : p; }

    public MB Set(Color color, float teamMask = 0f, float emission = 0f) { col = color; team = teamMask; emis = emission; return this; }
    public MB Set(float r, float g, float b, float teamMask = 0f, float emission = 0f) { return Set(new Color(r, g, b, 0f), teamMask, emission); }

    void Add(Vector3 p) { v.Add(p); var k = col; k.a = sway; c.Add(k); uv.Add(new Vector2(team, emis)); }

    public void Tri(Vector3 a, Vector3 b, Vector3 d, Vector3 outward)
    {
        a = M(a); b = M(b); d = M(d); outward = M(outward);
        if (Vector3.Dot(Vector3.Cross(b - a, d - a), outward) < 0f) { var s = b; b = d; d = s; }
        int i = v.Count; Add(a); Add(b); Add(d);
        t.Add(i); t.Add(i + 1); t.Add(i + 2);
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Vector3 outward)
    {
        a = M(a); b = M(b); d = M(d); e = M(e); outward = M(outward);
        if (Vector3.Dot(Vector3.Cross(b - a, d - a), outward) < 0f) { var s = b; b = e; e = s; }
        int i = v.Count; Add(a); Add(b); Add(d); Add(e);
        t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
    }

    public MB Box(Vector3 center, Vector3 size)
    {
        Vector3 h = size * 0.5f;
        for (int axis = 0; axis < 3; axis++)
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 n = Vector3.zero; n[axis] = s;
                Vector3 u = Vector3.zero, w = Vector3.zero;
                u[(axis + 1) % 3] = 1; w[(axis + 2) % 3] = 1;
                Vector3 o = center + Vector3.Scale(n, h), du = Vector3.Scale(u, h), dw = Vector3.Scale(w, h);
                Quad(o - du - dw, o + du - dw, o + du + dw, o - du + dw, n);
            }
        return this;
    }

    public MB Cyl(Vector3 b, float r0, float r1, float height, int sides)
    {
        Vector3 top = b + Vector3.up * height;
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
            Vector3 o = (d0 + d1) * 0.5f + Vector3.up * 0.3f * Mathf.Sign(r0 - r1 + 1e-5f) * (r1 < r0 ? 1 : 0);
            if (r1 > 0.0001f) Quad(b + d0 * r0, b + d1 * r0, top + d1 * r1, top + d0 * r1, o);
            else Tri(b + d0 * r0, b + d1 * r0, top, o);
            if (r1 > 0.0001f) Tri(top + d0 * r1, top + d1 * r1, top, Vector3.up);
        }
        return this;
    }

    public MB Pyr(Vector3 b, float w, float d, float height)
    {
        Vector3 a0 = b + new Vector3(-w, 0, -d) * 0.5f, a1 = b + new Vector3(w, 0, -d) * 0.5f, a2 = b + new Vector3(w, 0, d) * 0.5f, a3 = b + new Vector3(-w, 0, d) * 0.5f;
        Vector3 top = b + Vector3.up * height;
        Tri(a0, a1, top, new Vector3(0, 0.5f, -1)); Tri(a1, a2, top, new Vector3(1, 0.5f, 0));
        Tri(a2, a3, top, new Vector3(0, 0.5f, 1)); Tri(a3, a0, top, new Vector3(-1, 0.5f, 0));
        return this;
    }

    /// Gable roof: ridge along z (or along x if alongX).
    public MB Gable(Vector3 b, float w, float height, float len, bool alongX = false, bool ends = true)
    {
        System.Func<float, float, float, Vector3> P = (x, y, z) => b + (alongX ? new Vector3(z, y, x) : new Vector3(x, y, z));
        float hw = w * 0.5f, hl = len * 0.5f;
        Vector3 a0 = P(-hw, 0, -hl), a1 = P(hw, 0, -hl), a2 = P(hw, 0, hl), a3 = P(-hw, 0, hl), r0 = P(0, height, -hl), r1 = P(0, height, hl);
        Quad(a0, a3, r1, r0, (a0 + a3 + r1 + r0) * 0.25f - b + Vector3.up);
        Quad(a1, a2, r1, r0, (a1 + a2 + r1 + r0) * 0.25f - b + Vector3.up);
        if (ends)
        {
            Tri(a0, a1, r0, (a0 + a1) * 0.5f - b);
            Tri(a3, a2, r1, (a3 + a2) * 0.5f - b);
        }
        return this;
    }

    public MB Ball(Vector3 center, float rx, float ry, int seg = 7, int rings = 4, float jitter = 0f, int seed = 1)
    {
        var rng = new System.Random(seed);
        var pts = new Vector3[rings + 1, seg];
        for (int r = 0; r <= rings; r++)
            for (int s = 0; s < seg; s++)
            {
                float phi = Mathf.PI * r / rings, th = Mathf.PI * 2f * s / seg;
                float j = 1f + ((float)rng.NextDouble() - 0.5f) * jitter;
                pts[r, s] = center + new Vector3(Mathf.Sin(phi) * Mathf.Cos(th) * rx, Mathf.Cos(phi) * ry, Mathf.Sin(phi) * Mathf.Sin(th) * rx) * ((r == 0 || r == rings) ? 1f : j);
            }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < seg; s++)
            {
                int s1 = (s + 1) % seg;
                Vector3 a = pts[r, s], b = pts[r, s1], d = pts[r + 1, s1], e = pts[r + 1, s];
                Vector3 o = (a + b + d + e) * 0.25f - center;
                if (r == 0) Tri(a, d, e, o);
                else if (r == rings - 1) Tri(a, b, d, o);
                else Quad(a, b, d, e, o);
            }
        return this;
    }

    /// Double-sided leaf / grass blade.
    public MB Blade(Vector3 b, float height, float width, float yaw, float lean)
    {
        Vector3 dir = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw)), right = new Vector3(-dir.z, 0, dir.x) * width * 0.5f;
        Vector3 mid = b + Vector3.up * height * 0.55f + dir * lean * 0.35f, tip = b + Vector3.up * height + dir * lean;
        Quad(b - right, b + right, mid + right * 0.7f, mid - right * 0.7f, dir); Quad(b - right, b + right, mid + right * 0.7f, mid - right * 0.7f, -dir);
        Tri(mid - right * 0.7f, mid + right * 0.7f, tip, dir); Tri(mid - right * 0.7f, mid + right * 0.7f, tip, -dir);
        return this;
    }

    /// Set sway weight (vertex alpha) by height.
    public MB SwayByHeight(float height)
    {
        for (int i = 0; i < c.Count; i++) { var k = c[i]; float y = Mathf.Clamp01(v[i].y / height); k.a = y * y; c[i] = k; }
        return this;
    }

    public Mesh Build(string name)
    {
        var m = new Mesh { name = name };
        m.SetVertices(v); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }
}

public static class Models
{
    static readonly Color Stone = new Color(0.56f, 0.56f, 0.55f), StoneDark = new Color(0.40f, 0.40f, 0.41f), Wood = new Color(0.30f, 0.20f, 0.12f),
        WoodLight = new Color(0.50f, 0.36f, 0.21f), Window = new Color(1f, 0.74f, 0.34f), Roof = new Color(0.95f, 0.95f, 0.95f),
        Thatch = new Color(0.66f, 0.54f, 0.26f), Dirt = new Color(0.40f, 0.32f, 0.22f), Hay = new Color(0.80f, 0.66f, 0.24f);
    static readonly Color[] Plaster = { new Color(0.90f, 0.85f, 0.73f), new Color(0.80f, 0.80f, 0.78f) };

    static Vector3 V3(float x, float y, float z) { return new Vector3(x, y, z); }

    static void Yard(MB m, float r) { m.Set(Dirt).Cyl(V3(0, -0.03f, 0), r, r * 0.92f, 0.045f, 14); }

    static void Wall(MB m, Vector3 c, float w, float h, float d, Color col, bool timber)
    {
        m.Set(StoneDark).Box(c + Vector3.up * 0.035f, V3(w + 0.035f, 0.07f, d + 0.035f));
        m.Set(col).Box(c + Vector3.up * (h * 0.5f + 0.03f), V3(w, h - 0.06f, d));
        if (!timber) return;
        m.Set(Wood);
        for (int i = 0; i < 4; i++)
            m.Box(c + V3(((i & 1) * 2 - 1) * w * 0.5f, h * 0.5f, ((i >> 1) * 2 - 1) * d * 0.5f), V3(0.04f, h, 0.04f));
        m.Box(c + Vector3.up * (h - 0.02f), V3(w + 0.03f, 0.04f, d + 0.03f));
        m.Box(c + Vector3.up * (h * 0.52f), V3(w + 0.014f, 0.025f, d + 0.014f));
    }

    static void RoofG(MB m, Vector3 c, float w, float h, float len, bool alongX, Color wall)
    {
        m.Set(wall).Gable(c, w, h, len, alongX);
        m.Set(Roof, 1f).Gable(c + Vector3.up * 0.015f - Vector3.up * 0.05f, w + 0.18f, h * (w + 0.18f) / w, len + 0.14f, alongX, false);
        m.Set(Roof * 0.8f, 1f).Box(c + Vector3.up * (h + 0.012f), alongX ? V3(len + 0.16f, 0.03f, 0.04f) : V3(0.04f, 0.03f, len + 0.16f));
    }

    static void Chimney(MB m, Vector3 p, float h)
    {
        m.Set(Stone).Box(p + Vector3.up * h * 0.5f, V3(0.09f, h, 0.09f));
        m.Set(StoneDark).Box(p + Vector3.up * (h + 0.012f), V3(0.12f, 0.025f, 0.12f));
    }

    /// Window that punches through a wall of the given thickness along the given axis.
    static void Win(MB m, Vector3 c, float thick, bool alongX, float w = 0.075f, float h = 0.09f)
    {
        m.Set(Wood).Box(c, alongX ? V3(thick + 0.012f, h + 0.03f, w + 0.03f) : V3(w + 0.03f, h + 0.03f, thick + 0.012f));
        m.Set(Window, 0, 1f).Box(c, alongX ? V3(thick + 0.022f, h, w) : V3(w, h, thick + 0.022f));
        m.Set(Wood).Box(c, alongX ? V3(thick + 0.026f, h, 0.012f) : V3(0.012f, h, thick + 0.026f));
    }

    static void Door(MB m, Vector3 p, bool alongX, float w = 0.11f, float h = 0.2f)
    {
        m.Set(Wood).Box(p + Vector3.up * (h * 0.5f + 0.01f), alongX ? V3(0.03f, h + 0.03f, w + 0.035f) : V3(w + 0.035f, h + 0.03f, 0.03f));
        m.Set(WoodLight).Box(p + Vector3.up * (h * 0.5f), alongX ? V3(0.04f, h, w) : V3(w, h, 0.04f));
        m.Set(Stone).Box(p + Vector3.up * 0.012f, alongX ? V3(0.12f, 0.025f, w + 0.06f) : V3(w + 0.06f, 0.025f, 0.12f));
    }

    static void Barrel(MB m, Vector3 p) { m.Set(WoodLight).Cyl(p, 0.045f, 0.045f, 0.1f, 8); m.Set(Wood).Cyl(p + Vector3.up * 0.04f, 0.048f, 0.048f, 0.015f, 8); }
    static void Crate(MB m, Vector3 p) { m.Set(WoodLight).Box(p + Vector3.up * 0.04f, V3(0.08f, 0.08f, 0.08f)); }
    static void HayStack(MB m, Vector3 p) { m.Set(Hay).Cyl(p, 0.1f, 0.085f, 0.1f, 9); m.Cyl(p + Vector3.up * 0.1f, 0.085f, 0f, 0.09f, 9); }
    static void Fence(MB m, Vector3 a, Vector3 b)
    {
        m.Set(WoodLight);
        int n = Mathf.Max(2, Mathf.RoundToInt(Vector3.Distance(a, b) / 0.14f));
        for (int i = 0; i <= n; i++) m.Box(Vector3.Lerp(a, b, i / (float)n) + Vector3.up * 0.05f, V3(0.022f, 0.1f, 0.022f));
        Vector3 d = b - a; bool ax = Mathf.Abs(d.x) > Mathf.Abs(d.z);
        for (int k = 0; k < 2; k++)
            m.Box((a + b) * 0.5f + Vector3.up * (0.045f + k * 0.04f), ax ? V3(Mathf.Abs(d.x), 0.014f, 0.012f) : V3(0.012f, 0.014f, Mathf.Abs(d.z)));
    }
    static void Well(MB m, Vector3 p)
    {
        m.Set(Stone).Cyl(p, 0.075f, 0.075f, 0.08f, 8);
        m.Set(0.05f, 0.12f, 0.2f).Cyl(p + Vector3.up * 0.06f, 0.055f, 0.055f, 0.012f, 8);
        m.Set(Wood).Box(p + V3(0.07f, 0.12f, 0), V3(0.015f, 0.24f, 0.015f)).Box(p + V3(-0.07f, 0.12f, 0), V3(0.015f, 0.24f, 0.015f));
        m.Set(Roof * 0.85f, 1f).Pyr(p + Vector3.up * 0.22f, 0.22f, 0.16f, 0.07f);
    }
    static void Banner(MB m, Vector3 b, float h, float size = 1f)
    {
        m.Set(Wood).Box(b + Vector3.up * h * 0.5f, V3(0.022f, h, 0.022f));
        m.Set(Hay).Ball(b + Vector3.up * (h + 0.012f), 0.02f, 0.02f, 5, 3);
        m.Set(Roof, 1f); m.sway = 0.25f;
        float w = 0.2f * size, fh = 0.12f * size;
        for (int i = 0; i < 3; i++)
        {
            float x0 = 0.012f + w * i / 3f, x1 = 0.012f + w * (i + 1) / 3f, dr = i * 0.008f;
            m.sway = 0.15f + i * 0.35f;
            Vector3 a = b + V3(x0, h - 0.01f - dr, 0), a2 = b + V3(x1, h - 0.01f - dr - 0.008f, 0), c = b + V3(x1, h - 0.01f - fh + dr, 0), c2 = b + V3(x0, h - 0.01f - fh + dr * 0.5f, 0);
            m.Quad(a, a2, c, c2, Vector3.forward); m.Quad(a, a2, c, c2, Vector3.back);
        }
        m.sway = 0f;
    }
    static void Merlons(MB m, Vector3 c, float hw, float hd, int n, float s)
    {
        for (int i = 0; i < n; i++)
        {
            float f = n == 1 ? 0f : i / (float)(n - 1) * 2f - 1f;
            m.Box(c + V3(f * hw, s * 0.5f, hd), V3(s, s, s)); m.Box(c + V3(f * hw, s * 0.5f, -hd), V3(s, s, s));
            if (i > 0 && i < n - 1) { m.Box(c + V3(hw, s * 0.5f, f * hd), V3(s, s, s)); m.Box(c + V3(-hw, s * 0.5f, f * hd), V3(s, s, s)); }
        }
    }
    static void RoundTower(MB m, Vector3 p, float r, float h, float roofH, int merlons)
    {
        m.Set(StoneDark).Cyl(p, r + 0.02f, r + 0.01f, 0.08f, 10);
        m.Set(Stone).Cyl(p, r, r * 0.92f, h, 10);
        m.Set(Stone * 1.06f).Cyl(p + Vector3.up * (h - 0.02f), r * 1.12f, r * 1.12f, 0.07f, 10);
        m.Set(Stone * 0.95f);
        for (int i = 0; i < merlons; i++)
        {
            float a = i * Mathf.PI * 2f / merlons;
            m.Box(p + V3(Mathf.Cos(a) * r * 1.02f, h + 0.075f, Mathf.Sin(a) * r * 1.02f), V3(0.055f, 0.06f, 0.055f));
        }
        if (roofH > 0f) m.Set(Roof, 1f).Cyl(p + Vector3.up * (h + 0.04f), r * 0.98f, 0f, roofH, 10);
    }

    static Mesh House(int lvl, int v)
    {
        var m = new MB { mirror = v == 1 };
        Color pl = Plaster[v];
        bool timber = v == 0;
        switch (lvl)
        {
            case 0: // camp
                Yard(m, 0.36f);
                m.Set(0.86f, 0.80f, 0.66f).Pyr(V3(-0.06f, 0, 0.05f), 0.44f, 0.46f, 0.38f);
                m.Set(Roof, 1f).Pyr(V3(-0.06f, 0.27f, 0.05f), 0.14f, 0.15f, 0.125f);
                m.Set(0.14f, 0.10f, 0.08f).Box(V3(-0.06f, 0.08f, -0.14f), V3(0.11f, 0.16f, 0.06f));
                if (v == 1) m.Set(0.80f, 0.74f, 0.60f).Pyr(V3(0.24f, 0, 0.22f), 0.26f, 0.26f, 0.24f);
                for (int i = 0; i < 6; i++) { float a = i * 1.047f; m.Set(StoneDark).Box(V3(0.2f + Mathf.Cos(a) * 0.065f, 0.015f, -0.18f + Mathf.Sin(a) * 0.065f), V3(0.035f, 0.03f, 0.035f)); }
                m.Set(new Color(1f, 0.45f, 0.1f), 0, 2.2f).Pyr(V3(0.2f, 0.01f, -0.18f), 0.07f, 0.07f, 0.09f);
                m.Set(Wood).Box(V3(0.08f, 0.025f, -0.3f), V3(0.16f, 0.045f, 0.045f));
                Banner(m, V3(0.27f, 0, 0.0f), 0.5f, 0.8f);
                break;
            case 1: // hut
                Yard(m, 0.42f);
                m.Set(StoneDark).Cyl(V3(0, 0, 0), 0.27f, 0.27f, 0.05f, 10);
                m.Set(v == 0 ? new Color(0.62f, 0.50f, 0.36f) : new Color(0.70f, 0.66f, 0.56f)).Cyl(V3(0, 0.04f, 0), 0.25f, 0.24f, 0.22f, 10);
                m.Set(Thatch).Cyl(V3(0, 0.23f, 0), 0.36f, 0f, 0.36f, 10);
                m.Set(Roof, 1f).Cyl(V3(0, 0.235f, 0), 0.365f, 0.30f, 0.065f, 10);
                m.Set(Thatch * 0.8f).Cyl(V3(0, 0.52f, 0), 0.05f, 0f, 0.1f, 6);
                Door(m, V3(0, 0, -0.245f), false, 0.1f, 0.17f);
                Win(m, V3(0, 0.16f, 0), 0.49f, true, 0.06f, 0.06f);
                Barrel(m, V3(0.3f, 0, -0.16f)); HayStack(m, V3(-0.32f, 0, -0.12f));
                Fence(m, V3(-0.4f, 0, 0.2f), V3(-0.4f, 0, -0.02f));
                break;
            case 2: // house
                Yard(m, 0.5f);
                Wall(m, V3(0, 0, 0.02f), 0.54f, 0.36f, 0.68f, pl, timber);
                RoofG(m, V3(0, 0.36f, 0.02f), 0.54f, 0.3f, 0.68f, false, pl);
                Chimney(m, V3(0.15f, 0.36f, 0.2f), 0.36f);
                Door(m, V3(0, 0, -0.33f), false);
                Win(m, V3(0, 0.2f, -0.14f), 0.54f, true); Win(m, V3(0, 0.2f, 0.18f), 0.54f, true);
                Win(m, V3(0, 0.5f, 0.02f), 0.68f, false, 0.06f, 0.07f);
                Barrel(m, V3(0.33f, 0, -0.3f)); Crate(m, V3(0.34f, 0, -0.18f));
                Fence(m, V3(-0.44f, 0, -0.36f), V3(-0.44f, 0, 0.3f)); Fence(m, V3(-0.44f, 0, -0.36f), V3(-0.2f, 0, -0.36f));
                if (v == 1) HayStack(m, V3(-0.34f, 0, 0.34f));
                break;
            case 3: // manor
                Yard(m, 0.62f);
                Wall(m, V3(0.02f, 0, 0.12f), 0.92f, 0.46f, 0.48f, pl, timber);
                RoofG(m, V3(0.02f, 0.46f, 0.12f), 0.48f, 0.3f, 0.92f, true, pl);
                Wall(m, V3(-0.2f, 0, -0.28f), 0.42f, 0.4f, 0.42f, pl, timber);
                RoofG(m, V3(-0.2f, 0.4f, -0.22f), 0.42f, 0.26f, 0.56f, false, pl);
                Chimney(m, V3(0.32f, 0.5f, 0.12f), 0.36f); Chimney(m, V3(-0.3f, 0.5f, 0.18f), 0.3f);
                Door(m, V3(-0.2f, 0, -0.49f), false, 0.12f, 0.22f);
                for (int i = 0; i < 3; i++) { Win(m, V3(-0.24f + i * 0.27f, 0.27f, 0.12f), 0.48f, false); }
                Win(m, V3(-0.2f, 0.24f, -0.3f), 0.42f, true); Win(m, V3(0.02f, 0.6f, 0.12f), 0.92f, true, 0.07f, 0.07f);
                Well(m, V3(0.3f, 0, -0.34f)); HayStack(m, V3(0.5f, 0, -0.1f)); Barrel(m, V3(0.06f, 0, -0.18f));
                Fence(m, V3(0.56f, 0, -0.5f), V3(0.05f, 0, -0.5f)); Fence(m, V3(0.56f, 0, -0.5f), V3(0.56f, 0, -0.22f));
                break;
            case 4: // keep
                Yard(m, 0.66f);
                RoundTower(m, V3(-0.18f, 0, 0.14f), 0.27f, 1.0f, 0.42f, 8);
                Win(m, V3(-0.18f, 0.72f, 0.14f), 0.5f, false, 0.05f, 0.1f); Win(m, V3(-0.18f, 0.44f, 0.14f), 0.52f, true, 0.05f, 0.1f);
                Wall(m, V3(0.27f, 0, -0.06f), 0.5f, 0.4f, 0.62f, pl, timber);
                RoofG(m, V3(0.27f, 0.4f, -0.06f), 0.5f, 0.28f, 0.62f, false, pl);
                Door(m, V3(0.27f, 0, -0.37f), false, 0.13f, 0.22f);
                Win(m, V3(0.27f, 0.23f, -0.18f), 0.5f, true); Win(m, V3(0.27f, 0.23f, 0.08f), 0.5f, true);
                Chimney(m, V3(0.4f, 0.42f, 0.1f), 0.3f);
                m.Set(Stone * 0.95f).Box(V3(-0.2f, 0.11f, -0.46f), V3(0.6f, 0.22f, 0.07f));
                Merlons(m, V3(-0.2f, 0.22f, -0.46f), 0.27f, 0f, 5, 0.05f);
                Banner(m, V3(-0.18f, 1.44f, 0.14f), 0.42f, 1.2f);
                Barrel(m, V3(0.56f, 0, -0.3f)); Crate(m, V3(-0.5f, 0, -0.2f));
                break;
            default: // castle
                {
                    float R = 0.68f;
                    m.Set(new Color(0.46f, 0.42f, 0.36f)).Box(V3(0, -0.005f, 0), V3(R * 2, 0.03f, R * 2));
                    m.Set(Stone * 0.96f);
                    m.Box(V3(0, 0.21f, R), V3(R * 2, 0.42f, 0.13f)); m.Box(V3(R, 0.21f, 0), V3(0.13f, 0.42f, R * 2)); m.Box(V3(-R, 0.21f, 0), V3(0.13f, 0.42f, R * 2));
                    m.Box(V3(-0.42f, 0.21f, -R), V3(0.52f, 0.42f, 0.13f)); m.Box(V3(0.42f, 0.21f, -R), V3(0.52f, 0.42f, 0.13f));
                    m.Set(Stone * 0.9f);
                    for (int i = 0; i < 9; i++)
                    {
                        float f = (i / 8f * 2f - 1f) * (R - 0.2f);
                        m.Box(V3(f, 0.45f, R + 0.035f), V3(0.07f, 0.07f, 0.05f)); m.Box(V3(R + 0.035f, 0.45f, f), V3(0.05f, 0.07f, 0.07f)); m.Box(V3(-R - 0.035f, 0.45f, f), V3(0.05f, 0.07f, 0.07f));
                        if (Mathf.Abs(f) > 0.2f) m.Box(V3(f, 0.45f, -R - 0.035f), V3(0.07f, 0.07f, 0.05f));
                    }
                    // gatehouse
                    m.Set(Stone * 1.04f).Box(V3(-0.17f, 0.3f, -R), V3(0.1f, 0.6f, 0.2f)).Box(V3(0.17f, 0.3f, -R), V3(0.1f, 0.6f, 0.2f)).Box(V3(0, 0.5f, -R), V3(0.34f, 0.2f, 0.2f));
                    m.Set(Wood).Box(V3(0, 0.2f, -R + 0.02f), V3(0.24f, 0.4f, 0.04f));
                    m.Set(Roof, 1f).Gable(V3(0, 0.6f, -R), 0.3f, 0.16f, 0.5f, true);
                    for (int i = 0; i < 4; i++)
                        RoundTower(m, V3(((i & 1) * 2 - 1) * R, 0, ((i >> 1) * 2 - 1) * R), 0.17f, 0.74f, 0.36f, 6);
                    // keep
                    m.Set(StoneDark).Box(V3(0, 0.04f, 0.1f), V3(0.68f, 0.08f, 0.68f));
                    m.Set(Stone * 1.07f).Box(V3(0, 0.5f, 0.1f), V3(0.62f, 1.0f, 0.62f));
                    m.Set(Stone * 0.98f).Box(V3(0, 1.02f, 0.1f), V3(0.7f, 0.06f, 0.7f));
                    m.Set(Stone * 0.92f); Merlons(m, V3(0, 1.05f, 0.1f), 0.31f, 0.31f, 5, 0.075f);
                    m.Set(Stone * 1.07f).Box(V3(0, 1.2f, 0.1f), V3(0.36f, 0.34f, 0.36f));
                    m.Set(Roof, 1f).Pyr(V3(0, 1.37f, 0.1f), 0.48f, 0.48f, 0.5f);
                    for (int k = 0; k < 2; k++)
                    {
                        Win(m, V3(-0.14f + k * 0.28f, 0.72f, 0.1f), 0.62f, false, 0.05f, 0.12f);
                        Win(m, V3(0, 0.72f, -0.04f + k * 0.28f), 0.62f, true, 0.05f, 0.12f);
                        Win(m, V3(-0.14f + k * 0.28f, 0.4f, 0.1f), 0.62f, false, 0.05f, 0.1f);
                    }
                    Win(m, V3(0, 1.22f, 0.1f), 0.36f, false, 0.06f, 0.1f); Win(m, V3(0, 1.22f, 0.1f), 0.36f, true, 0.06f, 0.1f);
                    Door(m, V3(0, 0.02f, -0.215f), false, 0.14f, 0.24f);
                    Banner(m, V3(0, 1.85f, 0.1f), 0.5f, 1.5f);
                    Banner(m, V3(-0.17f, 0.6f, -R - 0.06f), 0.3f, 0.8f); Banner(m, V3(0.17f, 0.6f, -R - 0.06f), 0.3f, 0.8f);
                    Well(m, V3(0.44f, 0.01f, -0.36f)); Barrel(m, V3(-0.46f, 0.01f, -0.4f)); Crate(m, V3(-0.44f, 0.01f, -0.28f)); HayStack(m, V3(-0.46f, 0.01f, 0.42f));
                }
                break;
        }
        return m.Build("House" + lvl + "_" + v);
    }

    /// 6 levels x 2 variants; index = level * 2 + variant.
    public static Mesh[] Houses()
    {
        var res = new Mesh[12];
        for (int l = 0; l < 6; l++) for (int v = 0; v < 2; v++) res[l * 2 + v] = House(l, v);
        return res;
    }

    public static Mesh Figure()
    {
        var m = new MB();
        m.Set(0.12f, 0.09f, 0.08f).Box(V3(-0.055f, 0.03f, 0.02f), V3(0.08f, 0.06f, 0.14f)).Box(V3(0.055f, 0.03f, 0.02f), V3(0.08f, 0.06f, 0.14f));
        m.Set(0.30f, 0.24f, 0.18f).Box(V3(-0.055f, 0.15f, 0), V3(0.075f, 0.2f, 0.085f)).Box(V3(0.055f, 0.15f, 0), V3(0.075f, 0.2f, 0.085f));
        m.Set(Roof, 1f).Cyl(V3(0, 0.22f, 0), 0.165f, 0.12f, 0.36f, 8);
        m.Set(Wood).Cyl(V3(0, 0.33f, 0), 0.15f, 0.145f, 0.035f, 8);
        m.Set(Roof * 0.9f, 1f).Box(V3(-0.165f, 0.44f, 0), V3(0.07f, 0.24f, 0.08f)).Box(V3(0.165f, 0.44f, 0), V3(0.07f, 0.24f, 0.08f));
        m.Set(0.93f, 0.72f, 0.56f).Box(V3(-0.165f, 0.3f, 0), V3(0.06f, 0.06f, 0.07f)).Box(V3(0.165f, 0.3f, 0), V3(0.06f, 0.06f, 0.07f));
        m.Ball(V3(0, 0.70f, 0), 0.115f, 0.12f, 7, 5);
        m.Set(0.22f, 0.14f, 0.08f).Ball(V3(0, 0.735f, -0.018f), 0.122f, 0.105f, 7, 4);
        m.Set(Hay).Box(V3(0.2f, 0.42f, 0.03f), V3(0.02f, 0.7f, 0.02f));   // staff
        return m.Build("Figure");
    }

    static void Leaves(MB m, Vector3 c, float rx, float ry, Color col, int seed)
    {
        var rng = new System.Random(seed);
        m.Set(col * (0.85f + (float)rng.NextDouble() * 0.3f)).Ball(c, rx, ry, 7, 5, 0.3f, seed);
    }

    /// 0 conifer 1 broadleaf 2 birch 3 palm 4 rock 5 bush 6 grass 7 flowersA 8 flowersB 9 reeds
    public static Mesh[] Props()
    {
        var res = new Mesh[10];
        var rng = new System.Random(11);
        System.Func<float, float, float> R = (a, b) => a + (float)rng.NextDouble() * (b - a);

        var m = new MB();
        m.Set(0.26f, 0.17f, 0.10f).Cyl(Vector3.zero, 0.065f, 0.035f, 0.5f, 6);
        for (int i = 0; i < 5; i++)
        {
            float f = i / 4f;
            m.Set(Color.Lerp(new Color(0.04f, 0.19f, 0.09f), new Color(0.12f, 0.36f, 0.17f), f)).Cyl(V3(R(-.015f, .015f), 0.2f + f * 0.78f, R(-.015f, .015f)), Mathf.Lerp(0.36f, 0.12f, f), 0f, Mathf.Lerp(0.42f, 0.3f, f), 8);
        }
        res[0] = m.SwayByHeight(1.3f).Build("Conifer");

        m = new MB();
        m.Set(0.30f, 0.20f, 0.12f).Cyl(Vector3.zero, 0.08f, 0.05f, 0.5f, 7);
        m.Cyl(V3(0.02f, 0.4f, 0), 0.035f, 0.02f, 0.3f, 5);
        Leaves(m, V3(0, 0.72f, 0), 0.36f, 0.28f, new Color(0.16f, 0.38f, 0.09f), 3);
        Leaves(m, V3(0.2f, 0.86f, 0.08f), 0.24f, 0.2f, new Color(0.24f, 0.48f, 0.11f), 5);
        Leaves(m, V3(-0.17f, 0.9f, -0.1f), 0.24f, 0.2f, new Color(0.20f, 0.44f, 0.10f), 7);
        Leaves(m, V3(0.02f, 1.04f, 0.04f), 0.22f, 0.18f, new Color(0.30f, 0.54f, 0.13f), 9);
        res[1] = m.SwayByHeight(1.2f).Build("Broadleaf");

        m = new MB();
        m.Set(0.88f, 0.87f, 0.82f).Cyl(Vector3.zero, 0.045f, 0.025f, 0.95f, 6);
        m.Set(0.15f, 0.14f, 0.13f);
        for (int i = 0; i < 4; i++) m.Box(V3(R(-.03f, .03f), 0.15f + i * 0.2f, R(-.03f, .03f)), V3(0.05f, 0.02f, 0.05f));
        for (int i = 0; i < 5; i++)
            Leaves(m, V3(R(-.14f, .14f), 0.72f + i * 0.1f, R(-.14f, .14f)), R(.14f, .2f), R(.12f, .17f), new Color(0.46f, 0.60f, 0.16f), 20 + i);
        res[2] = m.SwayByHeight(1.3f).Build("Birch");

        m = new MB();
        Vector3 tp = Vector3.zero;
        for (int i = 0; i < 6; i++)
        {
            m.Set(Color.Lerp(new Color(0.42f, 0.32f, 0.2f), new Color(0.52f, 0.42f, 0.27f), i / 5f)).Cyl(tp, 0.055f - i * 0.004f, 0.05f - i * 0.004f, 0.19f, 6);
            tp += V3(0.022f * i, 0.18f, 0.008f * i);
        }
        m.Set(0.36f, 0.24f, 0.12f).Ball(tp + V3(0, -0.03f, 0), 0.06f, 0.06f, 5, 3);
        for (int i = 0; i < 9; i++)
        {
            float a = i * Mathf.PI * 2f / 9f + R(-.15f, .15f);
            Vector3 d = V3(Mathf.Cos(a), 0, Mathf.Sin(a)), rt = V3(-d.z, 0, d.x) * 0.075f;
            Vector3 p1 = tp + d * 0.26f + Vector3.up * 0.12f, p2 = tp + d * 0.52f - Vector3.up * R(0.02f, 0.14f);
            m.Set(R(0.14f, 0.2f), R(0.42f, 0.52f), 0.12f);
            m.Quad(tp - rt * 0.4f, tp + rt * 0.4f, p1 + rt, p1 - rt, Vector3.up); m.Quad(tp - rt * 0.4f, tp + rt * 0.4f, p1 + rt, p1 - rt, Vector3.down);
            m.Tri(p1 - rt, p1 + rt, p2, Vector3.up); m.Tri(p1 - rt, p1 + rt, p2, Vector3.down);
        }
        res[3] = m.SwayByHeight(1.25f).Build("Palm");

        m = new MB();
        m.Set(0.47f, 0.46f, 0.45f).Ball(V3(0, 0.07f, 0), 0.2f, 0.14f, 7, 4, 0.5f, 31);
        m.Set(0.40f, 0.39f, 0.39f).Ball(V3(0.17f, 0.04f, 0.08f), 0.11f, 0.08f, 6, 4, 0.5f, 33);
        m.Set(0.52f, 0.51f, 0.48f).Ball(V3(-0.12f, 0.03f, -0.13f), 0.08f, 0.06f, 6, 3, 0.5f, 35);
        res[4] = m.Build("Rock");

        m = new MB();
        Leaves(m, V3(0, 0.1f, 0), 0.17f, 0.13f, new Color(0.13f, 0.33f, 0.09f), 41);
        Leaves(m, V3(0.13f, 0.08f, 0.06f), 0.12f, 0.1f, new Color(0.18f, 0.40f, 0.10f), 43);
        Leaves(m, V3(-0.1f, 0.07f, -0.09f), 0.11f, 0.09f, new Color(0.16f, 0.36f, 0.12f), 45);
        m.Set(0.75f, 0.1f, 0.12f);
        for (int i = 0; i < 6; i++) m.Ball(V3(R(-.16f, .16f), R(.1f, .2f), R(-.14f, .14f)), 0.016f, 0.016f, 4, 3);
        res[5] = m.SwayByHeight(0.5f).Build("Bush");

        m = new MB();
        for (int i = 0; i < 22; i++)
        {
            float a = R(0, 6.28f), r = R(0f, 0.38f);
            m.Set(R(0.16f, 0.32f), R(0.40f, 0.58f), R(0.08f, 0.16f));
            m.Blade(V3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r), R(0.09f, 0.2f), R(0.025f, 0.04f), R(0, 6.28f), R(0.02f, 0.09f));
        }
        res[6] = m.SwayByHeight(0.2f).Build("Grass");

        for (int k = 0; k < 2; k++)
        {
            m = new MB();
            Color[] pal = k == 0 ? new[] { new Color(1f, 0.95f, 0.85f), new Color(1f, 0.8f, 0.12f) } : new[] { new Color(0.95f, 0.3f, 0.5f), new Color(0.6f, 0.35f, 0.9f) };
            for (int i = 0; i < 12; i++)
            {
                float a = R(0, 6.28f), r = R(0f, 0.34f), hgt = R(0.1f, 0.19f);
                Vector3 b = V3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                m.Set(0.2f, 0.45f, 0.12f).Blade(b, hgt, 0.02f, R(0, 6.28f), 0.01f);
                if (i % 3 != 2) m.Set(pal[i & 1]).Ball(b + Vector3.up * (hgt + 0.01f), 0.028f, 0.02f, 5, 3);
            }
            res[7 + k] = m.SwayByHeight(0.2f).Build("Flowers" + k);
        }

        m = new MB();
        for (int i = 0; i < 12; i++)
        {
            float a = R(0, 6.28f), r = R(0f, 0.34f), hgt = R(0.3f, 0.55f);
            Vector3 b = V3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            m.Set(R(0.25f, 0.35f), R(0.38f, 0.46f), 0.14f).Blade(b, hgt, 0.03f, R(0, 6.28f), R(0.02f, 0.1f));
            if (i % 3 == 0) m.Set(0.3f, 0.18f, 0.08f).Cyl(b + Vector3.up * hgt * 0.8f, 0.016f, 0.016f, 0.1f, 5);
        }
        res[9] = m.SwayByHeight(0.6f).Build("Reeds");
        return res;
    }

    public static Mesh Bird()
    {
        var m = new MB();
        m.Set(0.92f, 0.93f, 0.95f);
        Vector3 b0 = V3(0, 0, 0.12f), b1 = V3(0, 0, -0.12f), wl = V3(-0.42f, 1f, -0.06f), wr = V3(0.42f, 1f, -0.06f);
        m.Tri(b0, b1, wl, Vector3.up); m.Tri(b0, b1, wl, Vector3.down);
        m.Tri(b0, b1, wr, Vector3.up); m.Tri(b0, b1, wr, Vector3.down);
        return m.Build("Bird");
    }

    public static Mesh Beacon()
    {
        var m = new MB();
        m.Set(StoneDark).Cyl(Vector3.zero, 0.3f, 0.26f, 0.06f, 10);
        m.Set(Stone).Cyl(V3(0, 0.06f, 0), 0.22f, 0.18f, 0.1f, 10);
        m.Set(new Color(0.6f, 0.85f, 1f), 0, 2.5f).Cyl(V3(0, 0.16f, 0), 0.09f, 0.05f, 0.9f, 4);
        m.Cyl(V3(0, 1.06f, 0), 0.05f, 0f, 0.2f, 4);
        return m.Build("Beacon");
    }

    public static Mesh WaterGrid(float min, float max, int seg)
    {
        var v = new Vector3[(seg + 1) * (seg + 1)]; var t = new int[seg * seg * 6];
        for (int z = 0; z <= seg; z++)
            for (int x = 0; x <= seg; x++)
                v[z * (seg + 1) + x] = new Vector3(Mathf.Lerp(min, max, x / (float)seg), 0, Mathf.Lerp(min, max, z / (float)seg));
        int k = 0;
        for (int z = 0; z < seg; z++)
            for (int x = 0; x < seg; x++)
            {
                int a = z * (seg + 1) + x, b = a + 1, c = a + seg + 1, d = c + 1;
                t[k++] = a; t[k++] = c; t[k++] = b; t[k++] = b; t[k++] = c; t[k++] = d;
            }
        var m = new Mesh { name = "Water", vertices = v, triangles = t };
        m.RecalculateBounds();
        return m;
    }
}
