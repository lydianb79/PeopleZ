using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class Team
{
    public int id, mode;                 // mode: 0 settle, 1 gather, 2 fight
    public float mana = 80f, pop;
    public bool hasBeacon; public int bx, bz;
    public int houses, walkers;
    public readonly int[] flow = new int[World.N * World.N];
}

public class Settlement
{
    public int cx, cz, team, level = -1, baseH;
    public float pop, spawnCd = 3f, anim, smoke;
    public bool dead;
    public GameObject go; public MeshFilter mf; public MeshRenderer mr;
}

public class Walker
{
    public float x, z, str, heading, phase, drown, age, ox, oz;
    public int team, tx, tz;
    public bool hasTarget, fighting, dead;
}

public class Sim
{
    const int N = World.N;
    public const float MaxMana = 2500f, SculptCost = 0.25f;
    public static readonly int[] Cap = { 4, 10, 22, 40, 70, 120 };
    static readonly float[] Grow = { 0.06f, 0.10f, 0.15f, 0.22f, 0.30f, 0.42f };
    public static readonly string[] LevelName = { "Camp", "Hut", "House", "Manor", "Keep", "Castle" };
    public static readonly string[] PowerName = { "Sculpt", "Beacon", "Lightning", "Swamp", "Quake", "Volcano", "Flood", "Cataclysm" };
    public static readonly float[] PowerCost = { 0, 0, 60, 120, 250, 500, 900, 2000 };
    public static readonly float[] PowerRadius = { 0.4f, 0.6f, 1.6f, 3f, 6f, 4f, 0f, 0f };
    public static readonly Color[] TeamColor = { new Color(0.10f, 0.36f, 0.95f), new Color(0.90f, 0.13f, 0.10f) };

    public readonly Team[] teams = { new Team { id = 0 }, new Team { id = 1 } };
    public readonly List<Settlement> towns = new List<Settlement>();
    public readonly List<Walker> walkers = new List<Walker>();
    public readonly Settlement[] at = new Settlement[N * N];
    public bool cataclysm;
    public int winner = -1, fighting;
    public float time;

    readonly World w; readonly Fx fx; readonly Sfx sfx;
    readonly bool[] blocked = new bool[N * N];
    readonly List<int> sites = new List<int>(), src = new List<int>();
    readonly Mesh[] houseMesh; readonly Mesh figure, beaconMesh;
    readonly Material[] teamMat = new Material[2], figMat = new Material[2]; readonly Material beaconMat;
    readonly Transform root;
    readonly Matrix4x4[][] wm = { new Matrix4x4[1023], new Matrix4x4[1023] };
    readonly int[] wc = new int[2];
    readonly RenderParams[] wrp = new RenderParams[2];
    float flowT, valT, aiT, aiFightT, aiNextFight = 90f, aiPowerCd = 25f;

    static float R(float a, float b) { return Random.Range(a, b); }

    public Sim(World world, Fx effects, Sfx sound, Shader lit, Transform parent)
    {
        w = world; fx = effects; sfx = sound;
        root = new GameObject("Settlements").transform; root.SetParent(parent, false);
        houseMesh = Models.Houses(); figure = Models.Figure(); beaconMesh = Models.Beacon();
        for (int i = 0; i < 2; i++)
        {
            teamMat[i] = new Material(lit) { enableInstancing = true };
            teamMat[i].SetColor("_Team", TeamColor[i]); teamMat[i].SetFloat("_Glow", 1f); teamMat[i].SetFloat("_Sway", 1f);
            figMat[i] = new Material(lit) { enableInstancing = true };
            figMat[i].SetColor("_Team", TeamColor[i]); figMat[i].SetFloat("_Walk", 1f);
            wrp[i] = new RenderParams(figMat[i])
            {
                shadowCastingMode = ShadowCastingMode.On, receiveShadows = true,
                worldBounds = new Bounds(new Vector3(N * 0.5f, 3f, N * 0.5f), new Vector3(N + 10, 30, N + 10))
            };
        }
        beaconMat = new Material(lit); beaconMat.SetFloat("_Glow", 1.5f);
    }

    public void Begin()
    {
        int[] sx = { (int)(N * 0.30f), (int)(N * 0.70f) }, sz = { (int)(N * 0.30f), (int)(N * 0.70f) };
        for (int t = 0; t < 2; t++)
        {
            w.FlattenArea(sx[t], sz[t], 4, 2);
            for (int i = 0; i < 5; i++) Spawn(t, sx[t] + R(-2f, 2f), sz[t] + R(-2f, 2f), 5f);
        }
        RebuildFlows();
    }

    public Walker Spawn(int team, float x, float z, float str)
    {
        var k = new Walker { team = team, x = x, z = z, str = Mathf.Min(str, 250f), heading = R(0, 360), phase = R(0, 6f), ox = R(-0.28f, 0.28f), oz = R(-0.28f, 0.28f) };
        walkers.Add(k);
        return k;
    }

    int Mode(Team t) { return cataclysm ? 2 : t.mode; }

    // ------------------------------------------------------------------ update
    public void Update(float dt)
    {
        time += dt;
        for (int i = 0; i < 2; i++)
            teams[i].mana = Mathf.Min(MaxMana, teams[i].mana + (0.6f + Mathf.Sqrt(teams[i].pop) * 0.3f) * dt * (i == 1 ? 0.7f : 1f));

        valT -= dt; if (valT <= 0f) { valT = 0.5f; Validate(); }
        flowT -= dt; if (flowT <= 0f) { flowT = 0.6f; RebuildFlows(); }

        for (int i = 0; i < towns.Count; i++) UpdateTown(towns[i], dt);
        for (int i = 0; i < walkers.Count; i++) if (!walkers[i].dead) StepWalker(walkers[i], dt);
        Combat(dt);
        walkers.RemoveAll(k => k.dead);
        towns.RemoveAll(s => s.dead);

        for (int i = 0; i < 2; i++) { teams[i].pop = 0; teams[i].houses = 0; teams[i].walkers = 0; }
        foreach (var s in towns) { teams[s.team].pop += s.pop; teams[s.team].houses++; }
        foreach (var k in walkers) { teams[k.team].pop += k.str; teams[k.team].walkers++; }

        AI(dt);

        if (winner < 0 && time > 5f)
        {
            if (teams[1].houses + teams[1].walkers == 0) winner = 0;
            else if (teams[0].houses + teams[0].walkers == 0) winner = 1;
        }
    }

    void UpdateTown(Settlement s, float dt)
    {
        if (s.dead) return;
        int cap = Cap[s.level];
        if (!cataclysm && s.pop < cap) s.pop = Mathf.Min(cap, s.pop + Grow[s.level] * dt);
        s.spawnCd -= dt;
        if (s.pop >= cap && s.spawnCd <= 0f && teams[s.team].walkers < 140)
        {
            float o = Mathf.Max(1f, Mathf.Floor(s.pop * 0.5f));
            s.pop -= o;
            Spawn(s.team, s.cx + 0.5f, s.cz + 0.5f, o);
            s.spawnCd = 12f;
        }
        if (s.anim < 1f)
        {
            s.anim = Mathf.Min(1f, s.anim + dt * 2.5f);
            float e = 1f - Mathf.Pow(1f - s.anim, 3f);
            s.go.transform.localScale = new Vector3(1f, 0.3f + 0.7f * e, 1f) * (0.8f + 0.2f * e);
        }
        if (s.level >= 1)
        {
            s.smoke -= dt;
            if (s.smoke <= 0f) { s.smoke = R(0.3f, 0.6f); fx.Smoke(s.go.transform.position + new Vector3(R(-.1f, .1f), 0.6f + s.level * 0.14f, R(-.1f, .1f))); }
        }
    }

    bool Blocked(int cx, int cz)
    {
        for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
                if (World.InC(cx + dx, cz + dz) && at[World.CI(cx + dx, cz + dz)] != null) return true;
        return false;
    }

    void RebuildFlows()
    {
        System.Array.Clear(blocked, 0, blocked.Length);
        foreach (var s in towns)
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                    if (World.InC(s.cx + dx, s.cz + dz)) blocked[World.CI(s.cx + dx, s.cz + dz)] = true;
        sites.Clear();
        if (!cataclysm)
            for (int z = 2; z < N - 2; z++)
                for (int x = 2; x < N - 2; x++)
                {
                    int c = World.CI(x, z);
                    if (!blocked[c] && w.flat[c] >= 1 && !w.swamp[c]) sites.Add(c);
                }
        for (int i = 0; i < 2; i++)
        {
            Team t = teams[i]; int m = Mode(t);
            if (m == 1 && !t.hasBeacon) m = 0;
            if (m == 0) { w.Flow(sites, t.flow); continue; }
            src.Clear();
            if (m == 1) src.Add(World.CI(t.bx, t.bz));
            else
            {
                foreach (var s in towns) if (s.team != i) src.Add(World.CI(s.cx, s.cz));
                foreach (var k in walkers)
                    if (k.team != i && World.InC((int)k.x, (int)k.z)) src.Add(World.CI((int)k.x, (int)k.z));
            }
            w.Flow(src, t.flow);
        }
    }

    void Validate()
    {
        for (int i = 0; i < w.scorch.Length; i++) if (w.scorch[i] > 0f) w.scorch[i] = Mathf.Max(0f, w.scorch[i] - 0.006f);   // burnt ground slowly heals
        System.Array.Clear(w.fieldA, 0, w.fieldA.Length);
        System.Array.Clear(w.fieldB, 0, w.fieldB.Length);
        for (int i = 0; i < towns.Count; i++)
        {
            Settlement s = towns[i];
            if (s.dead) continue;
            int c = World.CI(s.cx, s.cz);
            if (w.flat[c] < 1 || w.swamp[c]) { DestroyTown(s, w.walk[c] && !w.swamp[c], false); continue; }
            s.baseH = w.flat[c];
            float[] field = s.team == 0 ? w.fieldA : w.fieldB;
            int count = 0;
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = s.cx + dx, z = s.cz + dz;
                    if (!World.InC(x, z)) continue;
                    int k = World.CI(x, z);
                    if (w.flat[k] != s.baseH || w.swamp[k]) continue;
                    count++;
                    if (dx == 0 && dz == 0) continue;
                    int vi = World.VI(x, z);
                    field[vi] = field[vi + 1] = field[vi + World.V] = field[vi + World.V + 1] = 1f;
                }
            int lvl = count >= 25 ? 5 : count >= 20 ? 4 : count >= 14 ? 3 : count >= 9 ? 2 : count >= 5 ? 1 : 0;
            if (lvl != s.level) SetLevel(s, lvl);
            s.go.transform.position = new Vector3(s.cx + 0.5f, s.baseH * World.Step, s.cz + 0.5f);
        }
        w.colorsDirty = true;
    }

    void SetLevel(Settlement s, int lvl)
    {
        bool up = lvl > s.level;
        s.level = lvl;
        s.mf.sharedMesh = houseMesh[lvl * 2 + ((s.cx * 7 + s.cz * 13) & 1)];
        w.treesDirty = true;
        s.anim = 0.35f;
        if (up && s.team == 0 && lvl >= 2 && s.go != null) sfx.LevelUp(s.go.transform.position);
        if (s.pop > Cap[lvl])
        {
            Spawn(s.team, s.cx + 0.5f, s.cz + 0.5f, s.pop - Cap[lvl]);
            s.pop = Cap[lvl];
        }
    }

    /// Directly place a settlement (icon render / tests).
    public Settlement PlaceTown(int team, int cx, int cz, float pop)
    {
        Found(Spawn(team, cx + 0.5f, cz + 0.5f, pop), cx, cz);
        return at[World.CI(cx, cz)];
    }

    void Found(Walker k, int cx, int cz)
    {
        var s = new Settlement { cx = cx, cz = cz, team = k.team, pop = k.str, baseH = w.flat[World.CI(cx, cz)] };
        s.go = new GameObject("Town");
        s.go.transform.SetParent(root, false);
        s.go.transform.SetPositionAndRotation(new Vector3(cx + 0.5f, s.baseH * World.Step, cz + 0.5f), Quaternion.Euler(0, Random.Range(0, 4) * 90f, 0));
        s.mf = s.go.AddComponent<MeshFilter>();
        s.mr = s.go.AddComponent<MeshRenderer>();
        s.mr.sharedMaterial = teamMat[k.team];
        towns.Add(s);
        at[World.CI(cx, cz)] = s;
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                if (World.InC(cx + dx, cz + dz) && w.tree[World.CI(cx + dx, cz + dz)] != 0) { w.tree[World.CI(cx + dx, cz + dz)] = 0; w.treesDirty = true; }
        SetLevel(s, 0);
        s.anim = 0f;
        k.dead = true;
        fx.Dust(s.go.transform.position, 8);
        sfx.Build(s.go.transform.position, k.team == 0);
        flowT = Mathf.Min(flowT, 0.1f);
    }

    void DestroyTown(Settlement s, bool survivors, bool burn)
    {
        if (s.dead) return;
        s.dead = true;
        at[World.CI(s.cx, s.cz)] = null;
        w.treesDirty = true;
        Vector3 p = s.go.transform.position;
        Object.Destroy(s.go);
        if (burn) { fx.Fire(p, 16); Scorch(p, 1.2f, 0.8f); } else fx.Dust(p, 12);
        sfx.Crumble(p);
        if (survivors && s.pop >= 1f) Spawn(s.team, s.cx + 0.5f, s.cz + 0.5f, s.pop * 0.6f);
        flowT = Mathf.Min(flowT, 0.1f);
    }

    static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 }, DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

    void StepWalker(Walker k, float dt)
    {
        int cx = (int)k.x, cz = (int)k.z;
        if (!World.InC(cx, cz) || k.x < 0 || k.z < 0) { k.dead = true; return; }
        int c = World.CI(cx, cz);
        Vector3 pos = new Vector3(k.x, w.Height(k.x, k.z), k.z);
        if (pos.y < World.WaterY - 0.1f)
        {
            k.drown += dt;
            if (k.drown > 1.0f) { k.dead = true; fx.Splash(pos); sfx.Splash(pos); return; }
        }
        else k.drown = 0f;
        if (w.swamp[c]) { k.dead = true; fx.Bubbles(pos); sfx.Bubbles(pos); return; }
        k.age += dt; k.str -= 0.02f * dt;
        if (k.str <= 0f) { k.dead = true; return; }
        if (k.fighting) { k.heading += dt * 500f; return; }

        Team t = teams[k.team];
        int mode = Mode(t);
        if (!k.hasTarget)
        {
            int d = t.flow[c];
            // arrival behaviour
            Settlement here = at[c];
            if (here != null && here.team == k.team && mode == 0 && k.age > 4f && here.pop < Cap[here.level])
            { here.pop += k.str; k.dead = true; return; }
            if (d == 0 && mode == 0 && w.flat[c] >= 1 && !w.swamp[c] && !Blocked(cx, cz) && cx >= 2 && cz >= 2 && cx < N - 2 && cz < N - 2)
            { Found(k, cx, cz); return; }

            int best = -1, bestD = d, start = Random.Range(0, 8);
            bool lost = d >= World.INF || d == 0;
            for (int j = 0; j < 8; j++)
            {
                int q = (start + j) & 7; int nx = cx + DX[q], nz = cz + DZ[q];
                if (!World.InC(nx, nz)) continue;
                int n = World.CI(nx, nz);
                if (!w.walk[n]) continue;
                if (q >= 4 && (!w.walk[World.CI(nx, cz)] || !w.walk[World.CI(cx, nz)])) continue;
                if (lost) { if (best < 0 || Random.value < 0.3f) best = q; }
                else if (t.flow[n] < bestD) { bestD = t.flow[n]; best = q; }
            }
            if (best < 0) return;
            k.tx = cx + DX[best]; k.tz = cz + DZ[best]; k.hasTarget = true;
        }
        float gx = k.tx + 0.5f + k.ox, gz = k.tz + 0.5f + k.oz;
        float ddx = gx - k.x, ddz = gz - k.z, dist = Mathf.Sqrt(ddx * ddx + ddz * ddz);
        float speed = (mode == 0 ? 1.5f : 1.9f) * dt;
        if (dist <= speed) { k.x = gx; k.z = gz; k.hasTarget = false; }
        else
        {
            k.x += ddx / dist * speed; k.z += ddz / dist * speed;
            k.heading = Mathf.LerpAngle(k.heading, Mathf.Atan2(ddx, ddz) * Mathf.Rad2Deg, 1f - Mathf.Exp(-12f * dt));
        }
        k.phase += dt * 11f;
    }

    void Combat(float dt)
    {
        int n = walkers.Count;
        for (int i = 0; i < n; i++) walkers[i].fighting = false;
        for (int i = 0; i < n; i++)
        {
            Walker a = walkers[i];
            if (a.dead) continue;
            for (int j = i + 1; j < n; j++)
            {
                Walker b = walkers[j];
                if (b.dead) continue;
                float dx = a.x - b.x, dz = a.z - b.z;
                if (dx > 0.7f || dx < -0.7f || dz > 0.7f || dz < -0.7f) continue;
                float d2 = dx * dx + dz * dz;
                if (a.team == b.team)
                {
                    if (d2 < 0.16f && Mode(teams[a.team]) == 1 && !cataclysm && a.str + b.str <= 250f)
                    { a.str += b.str; b.dead = true; }
                    continue;
                }
                if (d2 > 0.45f) continue;
                a.fighting = b.fighting = true;
                float da = dt * (2f + b.str * 0.25f), db = dt * (2f + a.str * 0.25f);
                a.str -= da; b.str -= db;
                if (Random.value < dt * 6f) sfx.Clash(new Vector3(a.x, 0, a.z));
                if (Random.value < dt * 6f) fx.Sparks(new Vector3((a.x + b.x) * 0.5f, w.Height(a.x, a.z), (a.z + b.z) * 0.5f), new Color(4f, 3f, 1.2f, 1f), 2);
                if (b.str <= 0f) b.dead = true;
                if (a.str <= 0f) { a.dead = true; break; }
            }
        }
        int fc = 0;
        for (int i = 0; i < n; i++) if (walkers[i].fighting && !walkers[i].dead) fc++;
        fighting = fc;
        // sieges
        for (int i = 0; i < n; i++)
        {
            Walker k = walkers[i];
            if (k.dead) continue;
            int cx = (int)k.x, cz = (int)k.z;
            if (!World.InC(cx, cz)) continue;
            Settlement s = at[World.CI(cx, cz)];
            if (s == null || s.dead || s.team == k.team) continue;
            k.fighting = true;
            float r = dt * (3f + k.str * 0.2f);
            s.pop -= r; k.str -= r * (0.6f + 0.12f * s.level);
            if (Random.value < dt * 5f) sfx.Clash(s.go.transform.position);
            if (Random.value < dt * 5f) fx.Sparks(s.go.transform.position, new Color(4f, 2f, 0.8f, 1f), 2);
            if (s.pop <= 0f)
            {
                if (k.str > 0f)
                {
                    s.team = k.team; s.pop = Mathf.Max(1f, k.str); s.mr.sharedMaterial = teamMat[k.team]; s.anim = 0.2f;
                    k.dead = true;
                    Game.I.Toast(k.team == 0 ? "Settlement captured!" : "A settlement has fallen to the rival");
                    sfx.Capture(k.team == 0);
                }
                else DestroyTown(s, false, true);
            }
            if (k.str <= 0f) k.dead = true;
        }
    }

    // ------------------------------------------------------------------ powers
    public int Sculpt(int team, int vx, int vz, bool up)
    {
        Team t = teams[team];
        if (t.mana < SculptCost) return -1;
        int n = up ? w.Raise(vx, vz) : w.Lower(vx, vz);
        t.mana = Mathf.Max(0f, t.mana - n * SculptCost);
        return n;
    }

    public bool Cast(int team, int p, Vector3 pos)
    {
        Team t = teams[team];
        if (t.mana < PowerCost[p]) return false;
        int cx = Mathf.FloorToInt(pos.x), cz = Mathf.FloorToInt(pos.z);
        int vx = Mathf.Clamp(Mathf.RoundToInt(pos.x), 2, N - 2), vz = Mathf.Clamp(Mathf.RoundToInt(pos.z), 2, N - 2);
        switch (p)
        {
            case 1:
                if (!World.InC(cx, cz) || !w.walk[World.CI(cx, cz)]) return false;
                t.hasBeacon = true; t.bx = cx; t.bz = cz; t.mode = 1; flowT = 0f;
                if (team == 0) sfx.Horn();
                break;
            case 2: Lightning(pos); break;
            case 3:
                for (int dz = -3; dz <= 3; dz++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (!World.InC(x, z) || dx * dx + dz * dz > 10) continue;
                        int c = World.CI(x, z);
                        if (w.flat[c] >= 1 && Random.value < 0.7f) { w.swamp[c] = true; w.tree[c] = 0; }
                    }
                w.colorsDirty = w.treesDirty = true; valT = 0f;
                fx.Bubbles(pos); sfx.Bubbles(pos);
                break;
            case 4:
                for (int i = 0; i < 46; i++)
                {
                    Vector2 o = Random.insideUnitCircle * 6f;
                    int x = Mathf.Clamp(vx + Mathf.RoundToInt(o.x), 2, N - 2), z = Mathf.Clamp(vz + Mathf.RoundToInt(o.y), 2, N - 2);
                    if (Random.value < 0.6f) w.Lower(x, z); else w.Raise(x, z);
                    if (i % 4 == 0) fx.Dust(new Vector3(x, w.Height(x, z), z), 5);
                }
                fx.shake = 1.2f; sfx.Rumble(); valT = 0f;
                break;
            case 5:
                if (Game.I != null && Game.I.isActiveAndEnabled && !Game.I.fastForward) Game.I.StartCoroutine(Volcano(vx, vz));
                else { for (int i = 0; i < 6; i++) w.Raise(vx, vz); Scorch(new Vector3(vx, 0, vz), 4f, 1f); }
                break;
            case 6:
                w.Flood(); fx.shake = 0.8f; sfx.Flood(); valT = 0f;
                break;
            case 7:
                cataclysm = true;
                foreach (var s in towns.ToArray())
                {
                    Spawn(s.team, s.cx + 0.5f, s.cz + 0.5f, Mathf.Max(1f, s.pop));
                    s.pop = 0; DestroyTown(s, false, true);
                }
                fx.shake = 1.5f; fx.flash = 1f; sfx.Thunder(); sfx.Rumble(); flowT = 0f;
                break;
        }
        t.mana -= PowerCost[p];
        return true;
    }

    void Scorch(Vector3 pos, float r, float amount)
    {
        int x0 = Mathf.RoundToInt(pos.x), z0 = Mathf.RoundToInt(pos.z), ir = Mathf.CeilToInt(r);
        for (int dz = -ir; dz <= ir; dz++)
            for (int dx = -ir; dx <= ir; dx++)
            {
                int x = x0 + dx, z = z0 + dz;
                if (!World.InV(x, z)) continue;
                float f = 1f - Mathf.Sqrt(dx * dx + dz * dz) / (r + 0.5f);
                if (f <= 0f) continue;
                int vi = World.VI(x, z);
                w.scorch[vi] = Mathf.Max(w.scorch[vi], Mathf.Clamp01(f * 1.6f) * amount);
                if (World.InC(x, z) && f > 0.3f && w.tree[World.CI(x, z)] != 0) { w.tree[World.CI(x, z)] = 0; w.treesDirty = true; }
            }
        w.colorsDirty = true;
    }

    void Damage(Vector3 pos, float r, float walkerDmg, float townDmg)
    {
        foreach (var k in walkers)
        {
            float dx = k.x - pos.x, dz = k.z - pos.z;
            if (dx * dx + dz * dz > r * r) continue;
            k.str -= walkerDmg; if (k.str <= 0f) k.dead = true;
        }
        foreach (var s in towns.ToArray())
        {
            float dx = s.cx + 0.5f - pos.x, dz = s.cz + 0.5f - pos.z;
            if (dx * dx + dz * dz > r * r) continue;
            s.pop = s.pop * 0.5f - townDmg;
            if (s.pop <= 0f) DestroyTown(s, false, true);
        }
    }

    void Lightning(Vector3 pos)
    {
        pos.y = Mathf.Max(w.Height(pos.x, pos.z), World.WaterY);
        fx.Lightning(pos); sfx.Thunder(pos);
        Scorch(pos, 1.6f, 1f);
        Damage(pos, 1.6f, 40f, 20f);
    }

    IEnumerator Volcano(int vx, int vz)
    {
        sfx.Rumble();
        for (int i = 0; i < 7; i++)
        {
            w.Raise(vx, vz);
            Vector3 p = new Vector3(vx, w.Height(vx, vz), vz);
            fx.Lava(p, 30); fx.shake = Mathf.Max(fx.shake, 0.8f); fx.flash = Mathf.Max(fx.flash, 0.3f);
            Scorch(p, 2f + i * 0.5f, 1f);
            Damage(p, 2f + i * 0.4f, 30f, 25f);
            valT = 0f;
            if (i % 2 == 0) sfx.Erupt(p);
            for (float t = 0; t < 0.38f; t += Time.deltaTime)
            {
                if (Random.value < 0.5f) { fx.Lava(new Vector3(vx, w.Height(vx, vz), vz), 3); sfx.LavaPop(new Vector3(vx, 0, vz)); }
                yield return null;
            }
        }
        for (float t = 0; t < 4f; t += Time.deltaTime)
        {
            if (Random.value < 0.35f) { fx.Lava(new Vector3(vx, w.Height(vx, vz), vz), 2); sfx.LavaPop(new Vector3(vx, 0, vz)); }
            yield return null;
        }
    }

    // ------------------------------------------------------------------ rival deity
    void AI(float dt)
    {
        Team t = teams[1], foe = teams[0];
        aiT -= dt;
        if (aiT > 0f || cataclysm) return;
        aiT = 1.0f;

        // shape land around own settlements / walkers
        for (int e = 0; e < 2 && t.mana > 3f; e++)
        {
            int bx, bz, H;
            Settlement s = null;
            for (int tries = 0; tries < 6 && towns.Count > 0; tries++)
            {
                var c = towns[Random.Range(0, towns.Count)];
                if (c.team == 1 && !c.dead && c.level < 5) { s = c; break; }
            }
            if (s != null) { bx = s.cx; bz = s.cz; H = s.baseH; }
            else
            {
                Walker k = null;
                for (int tries = 0; tries < 6 && walkers.Count > 0; tries++)
                {
                    var c = walkers[Random.Range(0, walkers.Count)];
                    if (c.team == 1) { k = c; break; }
                }
                if (k == null) break;
                bx = (int)k.x; bz = (int)k.z;
                if (!World.InC(bx, bz)) break;
                H = Mathf.Max(1, w.h[World.VI(bx, bz)]);
            }
            int best = -1, bestD = 999;
            for (int dz = -2; dz <= 3; dz++)
                for (int dx = -2; dx <= 3; dx++)
                {
                    int x = bx + dx, z = bz + dz;
                    if (x < 2 || z < 2 || x > N - 2 || z > N - 2) continue;
                    if (w.h[World.VI(x, z)] == H) continue;
                    int d = Mathf.Max(Mathf.Abs(dx * 2 - 1), Mathf.Abs(dz * 2 - 1)) * 4 + Random.Range(0, 3);
                    if (d < bestD) { bestD = d; best = World.VI(x, z); }
                }
            if (best < 0) continue;
            Sculpt(1, best % World.V, best / World.V, w.h[best] < H);
        }

        // posture
        aiNextFight -= 0.7f;
        if (aiFightT > 0f) { aiFightT -= 0.7f; if (aiFightT <= 0f) { t.mode = 0; aiNextFight = R(70f, 120f); } }
        else if (aiNextFight <= 0f && t.walkers >= 6 && t.pop > foe.pop * 0.7f) { t.mode = 2; aiFightT = 30f; Game.I.Toast("The rival's followers are on the warpath!"); }

        // powers
        aiPowerCd -= 0.7f;
        if (aiPowerCd > 0f || time < 60f) return;
        Walker strong = null; Settlement big = null;
        foreach (var k in walkers) if (k.team == 0 && (strong == null || k.str > strong.str)) strong = k;
        foreach (var s in towns) if (s.team == 0 && (big == null || s.level > big.level || (s.level == big.level && Random.value < 0.3f))) big = s;
        if (t.mana > 2200f && t.pop > foe.pop * 2.5f) { Cast(1, 7, Vector3.zero); Game.I.Toast("The rival unleashes CATACLYSM!"); return; }
        if (big != null && t.mana > 900f && Random.value < 0.5f)
        { Cast(1, 5, new Vector3(big.cx + 0.5f, 0, big.cz + 0.5f)); aiPowerCd = 60f; Game.I.Toast("The rival raises a volcano in your lands!"); return; }
        if (big != null && t.mana > 500f && Random.value < 0.4f)
        { Cast(1, 4, new Vector3(big.cx + 0.5f, 0, big.cz + 0.5f)); aiPowerCd = 45f; Game.I.Toast("The rival shakes the earth!"); return; }
        if (big != null && t.mana > 300f && Random.value < 0.4f)
        { Cast(1, 3, new Vector3(big.cx + R(-3f, 3f), 0, big.cz + R(-3f, 3f))); aiPowerCd = 35f; Game.I.Toast("A swamp festers near your people"); return; }
        if (t.mana > 150f)
        {
            if (strong != null && strong.str >= 8f) { Cast(1, 2, new Vector3(strong.x, 0, strong.z)); aiPowerCd = 14f; }
            else if (big != null && big.level >= 2) { Cast(1, 2, new Vector3(big.cx + 0.5f, 0, big.cz + 0.5f)); aiPowerCd = 20f; }
        }
    }

    // ------------------------------------------------------------------ rendering
    public void Draw(float dt)
    {
        wc[0] = wc[1] = 0;
        foreach (var k in walkers)
        {
            if (wc[k.team] >= 1023) continue;
            float y = Mathf.Max(w.Height(k.x, k.z), World.WaterY - 0.25f) + Mathf.Abs(Mathf.Sin(k.phase)) * 0.05f;
            float s = 0.5f + 0.4f * Mathf.Clamp01(k.str / 60f);
            wm[k.team][wc[k.team]++] = Matrix4x4.TRS(new Vector3(k.x, y, k.z), Quaternion.Euler(0, k.heading, 0), new Vector3(s, s, s));
        }
        for (int i = 0; i < 2; i++)
            if (wc[i] > 0) Graphics.RenderMeshInstanced(wrp[i], figure, 0, wm[i], wc[i]);

        Team t = teams[0];
        if (t.hasBeacon)
        {
            Vector3 p = new Vector3(t.bx + 0.5f, w.Height(t.bx + 0.5f, t.bz + 0.5f), t.bz + 0.5f);
            Graphics.DrawMesh(beaconMesh, Matrix4x4.TRS(p, Quaternion.Euler(0, Time.time * 40f, 0), Vector3.one), beaconMat, 0);
            float a = t.mode == 1 ? 0.5f : 0.15f;
            fx.Beam(p + Vector3.up * 0.3f, 14f, 0.35f, new Color(0.5f, 1.2f, 3f, a));
            fx.Ring(p, 0.7f + 0.15f * Mathf.Sin(Time.time * 3f), new Color(0.6f, 1.4f, 3f, a), 0.04f);
        }
    }

    public Settlement TownNear(Vector3 p)
    {
        int cx = Mathf.FloorToInt(p.x), cz = Mathf.FloorToInt(p.z);
        return World.InC(cx, cz) ? at[World.CI(cx, cz)] : null;
    }
}
