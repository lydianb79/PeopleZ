using System.Collections.Generic;
using UnityEngine;

/// Lightweight CPU particle / line renderer (two dynamic meshes: additive and alpha-blended).
public class Fx
{
    struct P { public Vector3 p, v; public float life, max, size, grow, grav; public Color c; public bool add; }
    struct Bolt { public Vector3 a, b; public float life, width; }

    readonly List<P> ps = new List<P>(2048);
    readonly List<Bolt> bolts = new List<Bolt>();
    readonly Mesh[] mesh = new Mesh[2];
    readonly Material[] mat = new Material[2];
    readonly List<Vector3>[] v = { new List<Vector3>(), new List<Vector3>() };
    readonly List<Color>[] c = { new List<Color>(), new List<Color>() };
    readonly List<Vector2>[] uv = { new List<Vector2>(), new List<Vector2>() };
    readonly List<int>[] t = { new List<int>(), new List<int>() };
    readonly World w;
    Vector3 camPos, camR, camU;
    public float flash, shake, tint = 1f;   // tint: ambient light level applied to smoke and dust

    public Fx(World world, Shader sh)
    {
        w = world;
        for (int i = 0; i < 2; i++)
        {
            mesh[i] = new Mesh { name = "Fx" + i }; mesh[i].MarkDynamic();
            mat[i] = new Material(sh);
            mat[i].SetFloat("_Dst", i == 0 ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat[i].renderQueue = 3010 + (1 - i);
        }
    }

    static float R(float a, float b) { return Random.Range(a, b); }

    public void Emit(Vector3 p, Vector3 vel, float life, float size, float grow, float grav, Color col, bool add)
    {
        if (ps.Count > 3000) return;
        ps.Add(new P { p = p, v = vel, life = life, max = life, size = size, grow = grow, grav = grav, c = col, add = add });
    }

    public void Dust(Vector3 p, int n = 10)
    {
        for (int i = 0; i < n; i++)
            Emit(p + new Vector3(R(-.4f, .4f), 0.05f, R(-.4f, .4f)), new Vector3(R(-.5f, .5f), R(.2f, .8f), R(-.5f, .5f)), R(.6f, 1.3f), R(.15f, .3f), 0.5f, 0f, new Color(0.75f, 0.68f, 0.55f, 0.5f), false);
    }
    public void Smoke(Vector3 p)
    {
        Emit(p, new Vector3(R(-.1f, .1f) + 0.15f, R(.35f, .6f), R(-.1f, .1f)), R(1.6f, 2.6f), 0.1f, 0.22f, 0f, new Color(0.8f, 0.8f, 0.82f, 0.35f), false);
    }
    public void Fire(Vector3 p, int n = 14)
    {
        for (int i = 0; i < n; i++)
        {
            Emit(p + new Vector3(R(-.3f, .3f), R(0, .3f), R(-.3f, .3f)), new Vector3(R(-.3f, .3f), R(.8f, 2f), R(-.3f, .3f)), R(.4f, 1f), R(.2f, .4f), -0.2f, 0f, new Color(4f, R(1f, 2f), 0.3f, 0.8f), true);
            if (i % 3 == 0) Emit(p + Vector3.up * 0.3f, new Vector3(R(-.3f, .3f), R(.8f, 1.5f), R(-.3f, .3f)), R(1.5f, 3f), 0.3f, 0.5f, 0f, new Color(0.12f, 0.11f, 0.1f, 0.6f), false);
        }
    }
    public void Lava(Vector3 p, int n = 24)
    {
        for (int i = 0; i < n; i++)
            Emit(p, new Vector3(R(-2.5f, 2.5f), R(4f, 9f), R(-2.5f, 2.5f)), R(1.2f, 2.4f), R(.12f, .28f), 0f, 9f, new Color(6f, R(1.2f, 2.6f), 0.2f, 1f), true);
        for (int i = 0; i < n / 3; i++)
            Emit(p, new Vector3(R(-1f, 1f), R(1.5f, 3.5f), R(-1f, 1f)), R(2.5f, 5f), 0.6f, 0.9f, 0f, new Color(0.1f, 0.09f, 0.09f, 0.7f), false);
    }
    public void Splash(Vector3 p)
    {
        p.y = World.WaterY + 0.05f;
        for (int i = 0; i < 8; i++)
            Emit(p, new Vector3(R(-.7f, .7f), R(1f, 2.2f), R(-.7f, .7f)), R(.4f, .7f), R(.06f, .12f), 0f, 6f, new Color(0.9f, 0.97f, 1f, 0.8f), false);
    }
    public void Sparks(Vector3 p, Color col, int n = 3)
    {
        for (int i = 0; i < n; i++)
            Emit(p + Vector3.up * 0.3f, new Vector3(R(-1f, 1f), R(.5f, 2f), R(-1f, 1f)), R(.2f, .45f), R(.05f, .09f), 0f, 5f, col, true);
    }
    public void Bubbles(Vector3 p)
    {
        for (int i = 0; i < 6; i++)
            Emit(p + new Vector3(R(-.2f, .2f), 0.05f, R(-.2f, .2f)), new Vector3(0, R(.2f, .5f), 0), R(.5f, 1f), R(.06f, .12f), 0f, 0f, new Color(0.35f, 0.5f, 0.15f, 0.8f), false);
    }

    public void Lightning(Vector3 ground)
    {
        Vector3 a = ground + new Vector3(R(-3f, 3f), 26f, R(-3f, 3f));
        int seg = 14; Vector3 prev = a;
        for (int i = 1; i <= seg; i++)
        {
            float f = i / (float)seg;
            Vector3 p = Vector3.Lerp(a, ground, f) + (i == seg ? Vector3.zero : new Vector3(R(-1f, 1f), 0, R(-1f, 1f)) * (1f - f * 0.5f));
            bolts.Add(new Bolt { a = prev, b = p, life = 0.28f, width = 0.16f });
            if (i % 4 == 0) bolts.Add(new Bolt { a = p, b = p + new Vector3(R(-3f, 3f), R(-3f, -1f), R(-3f, 3f)), life = 0.2f, width = 0.07f });
            prev = p;
        }
        Fire(ground, 10);
        Sparks(ground, new Color(3f, 4f, 8f, 1f), 16);
        flash = Mathf.Max(flash, 1f); shake = Mathf.Max(shake, 0.35f);
    }

    void Line(int m, Vector3 a, Vector3 b, float width, Color col)
    {
        Vector3 side = Vector3.Cross(b - a, camPos - a).normalized * width;
        int i = v[m].Count;
        v[m].Add(a - side); v[m].Add(a + side); v[m].Add(b + side); v[m].Add(b - side);
        var mid = new Vector2(0.5f, 0.5f);
        for (int k = 0; k < 4; k++) { c[m].Add(col); uv[m].Add(mid); }
        t[m].Add(i); t[m].Add(i + 1); t[m].Add(i + 2); t[m].Add(i); t[m].Add(i + 2); t[m].Add(i + 3);
    }

    /// Immediate-mode terrain-hugging ring (call every frame, before Render).
    public void Ring(Vector3 center, float radius, Color col, float thick = 0.06f)
    {
        int seg = Mathf.Clamp((int)(radius * 14f), 16, 72);
        Vector3 prevI = Vector3.zero, prevO = Vector3.zero;
        var mid = new Vector2(0.5f, 0.5f);
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg; Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            Vector3 pi = center + d * (radius - thick), po = center + d * (radius + thick);
            pi.y = Mathf.Max(w.Height(pi.x, pi.z), World.WaterY) + 0.06f; po.y = Mathf.Max(w.Height(po.x, po.z), World.WaterY) + 0.06f;
            if (i > 0)
            {
                int k = v[0].Count;
                v[0].Add(prevI); v[0].Add(prevO); v[0].Add(po); v[0].Add(pi);
                for (int j = 0; j < 4; j++) { c[0].Add(col); uv[0].Add(mid); }
                t[0].Add(k); t[0].Add(k + 1); t[0].Add(k + 2); t[0].Add(k); t[0].Add(k + 2); t[0].Add(k + 3);
            }
            prevI = pi; prevO = po;
        }
    }

    /// Immediate-mode vertical light beam.
    public void Beam(Vector3 b, float height, float width, Color col)
    {
        Vector3 side = Vector3.Cross(Vector3.up, camPos - b).normalized * width;
        int i = v[0].Count;
        Vector3 top = b + Vector3.up * height;
        v[0].Add(b - side); v[0].Add(b + side); v[0].Add(top + side); v[0].Add(top - side);
        uv[0].Add(new Vector2(0, 0.5f)); uv[0].Add(new Vector2(1, 0.5f)); uv[0].Add(new Vector2(1, 0.5f)); uv[0].Add(new Vector2(0, 0.5f));
        var fade = col; fade.a = 0f;
        c[0].Add(col); c[0].Add(col); c[0].Add(fade); c[0].Add(fade);
        t[0].Add(i); t[0].Add(i + 1); t[0].Add(i + 2); t[0].Add(i); t[0].Add(i + 2); t[0].Add(i + 3);
    }

    public void Begin(Camera cam)
    {
        camPos = cam.transform.position; camR = cam.transform.right; camU = cam.transform.up;
        for (int i = 0; i < 2; i++) { v[i].Clear(); c[i].Clear(); uv[i].Clear(); t[i].Clear(); }
    }

    public void Render(float dt)
    {
        flash = Mathf.Max(0f, flash - dt * 4f);
        shake = Mathf.Max(0f, shake - dt * 1.2f);
        for (int i = ps.Count - 1; i >= 0; i--)
        {
            P p = ps[i];
            p.life -= dt;
            if (p.life <= 0f) { ps[i] = ps[ps.Count - 1]; ps.RemoveAt(ps.Count - 1); continue; }
            p.v.y -= p.grav * dt; p.p += p.v * dt; p.size = Mathf.Max(0.01f, p.size + p.grow * dt);
            ps[i] = p;
            int m = p.add ? 0 : 1;
            float f = p.life / p.max; Color col = p.c; if (!p.add) { col.r *= tint; col.g *= tint; col.b *= tint; } col.a *= Mathf.Min(1f, f * 2.5f) * Mathf.Min(1f, (1f - f) * 8f + 0.2f);
            Vector3 r = camR * p.size, u = camU * p.size;
            int k = v[m].Count;
            v[m].Add(p.p - r - u); v[m].Add(p.p + r - u); v[m].Add(p.p + r + u); v[m].Add(p.p - r + u);
            uv[m].Add(new Vector2(0, 0)); uv[m].Add(new Vector2(1, 0)); uv[m].Add(new Vector2(1, 1)); uv[m].Add(new Vector2(0, 1));
            for (int j = 0; j < 4; j++) c[m].Add(col);
            t[m].Add(k); t[m].Add(k + 1); t[m].Add(k + 2); t[m].Add(k); t[m].Add(k + 2); t[m].Add(k + 3);
        }
        for (int i = bolts.Count - 1; i >= 0; i--)
        {
            Bolt b = bolts[i]; b.life -= dt;
            if (b.life <= 0f) { bolts.RemoveAt(i); continue; }
            bolts[i] = b;
            float a = Mathf.Clamp01(b.life / 0.2f) * (Random.value > 0.25f ? 1f : 0.3f);
            Line(0, b.a, b.b, b.width, new Color(5f, 6f, 12f, a));
            Line(0, b.a, b.b, b.width * 4f, new Color(1f, 1.5f, 4f, a * 0.25f));
        }
        for (int i = 0; i < 2; i++)
        {
            mesh[i].Clear();
            if (v[i].Count == 0) continue;
            mesh[i].SetVertices(v[i]); mesh[i].SetColors(c[i]); mesh[i].SetUVs(0, uv[i]); mesh[i].SetTriangles(t[i], 0);
            mesh[i].bounds = new Bounds(new Vector3(World.N * 0.5f, 5f, World.N * 0.5f), new Vector3(1000, 200, 1000));
            Graphics.DrawMesh(mesh[i], Matrix4x4.identity, mat[i], 0, null, 0, null, false, false);
        }
    }
}

public class CameraRig
{
    public Vector3 focus, tFocus;
    public float yaw = 35f, tYaw = 35f, dist = 30f, tDist = 30f;
    Vector3 lastMouse;

    public void Jump(float x, float z) { tFocus = new Vector3(x, 0, z); }

    public void Update(Camera cam, World w, float dt, bool input, bool scroll, float shake)
    {
        if (input)
        {
            Quaternion q = Quaternion.Euler(0, tYaw, 0);
            Vector3 mv = Vector3.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) mv += Vector3.forward;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) mv += Vector3.back;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) mv += Vector3.left;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) mv += Vector3.right;
            tFocus += q * mv.normalized * (tDist * 0.9f * dt);
            if (Input.GetKey(KeyCode.Q)) tYaw += 90f * dt;
            if (Input.GetKey(KeyCode.E)) tYaw -= 90f * dt;
            if (Input.GetMouseButton(2)) tYaw += (Input.mousePosition.x - lastMouse.x) * 0.25f;
            if (scroll) tDist *= 1f - Mathf.Clamp(Input.mouseScrollDelta.y, -3f, 3f) * 0.08f;
        }
        lastMouse = Input.mousePosition;
        tDist = Mathf.Clamp(tDist, 7f, 80f);
        tFocus.x = Mathf.Clamp(tFocus.x, 0, World.N); tFocus.z = Mathf.Clamp(tFocus.z, 0, World.N);
        float k = 1f - Mathf.Exp(-9f * dt);
        float gy = Mathf.Max(w.Height(tFocus.x, tFocus.z), World.WaterY);
        tFocus.y = gy;
        focus = Vector3.Lerp(focus, tFocus, k);
        yaw = Mathf.Lerp(yaw, tYaw, k); dist = Mathf.Lerp(dist, tDist, k);
        float pitch = Mathf.Lerp(33f, 58f, Mathf.InverseLerp(7f, 80f, dist));
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
        Vector3 pos = focus - rot * Vector3.forward * dist;
        if (shake > 0f) pos += new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * shake * 0.35f;
        cam.transform.SetPositionAndRotation(pos, rot);
    }
}
