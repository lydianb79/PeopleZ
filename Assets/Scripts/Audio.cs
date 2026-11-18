using UnityEngine;

/// Stereo Schroeder reverb (4 damped combs + 2 allpasses per channel).
public class Reverb
{
    readonly float[][] comb = new float[8][]; readonly int[] ci = new int[8]; readonly float[] cs = new float[8];
    readonly float[][] ap = new float[4][]; readonly int[] ai = new int[4];
    public float feedback = 0.84f, damp = 0.3f;

    public Reverb(int sr)
    {
        int[] cl = { 1557, 1617, 1491, 1422 }, al = { 556, 441 };
        float k = sr / 44100f;
        for (int i = 0; i < 4; i++) { comb[i] = new float[(int)(cl[i] * k)]; comb[i + 4] = new float[(int)((cl[i] + 23) * k)]; }
        for (int i = 0; i < 2; i++) { ap[i] = new float[(int)(al[i] * k)]; ap[i + 2] = new float[(int)((al[i] + 23) * k)]; }
    }

    float Chan(float x, int c0, int a0)
    {
        float o = 0f;
        for (int k = 0; k < 4; k++)
        {
            int c = c0 + k; float[] b = comb[c]; float y = b[ci[c]];
            cs[c] = y * (1f - damp) + cs[c] * damp;
            b[ci[c]] = x + cs[c] * feedback + 1e-20f;
            if (++ci[c] >= b.Length) ci[c] = 0;
            o += y;
        }
        for (int k = 0; k < 2; k++)
        {
            int a = a0 + k; float[] b = ap[a]; float y = b[ai[a]];
            b[ai[a]] = o + y * 0.5f;
            if (++ai[a] >= b.Length) ai[a] = 0;
            o = y - o;
        }
        return o * 0.25f;
    }

    public void Process(float inL, float inR, out float l, out float r) { l = Chan(inL, 0, 0); r = Chan(inR, 4, 2); }
}

/// Offline synthesis helpers for sound effects.
static class Syn
{
    public const int SR = 44100;
    static readonly System.Random rng = new System.Random(4321);
    public static float N() { return (float)rng.NextDouble() * 2f - 1f; }
    public static float U(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

    public static float[] Gen(float len, System.Func<float, float> f)
    {
        int n = (int)(len * SR); var d = new float[n];
        for (int i = 0; i < n; i++) d[i] = f(i / (float)SR);
        return d;
    }

    /// One-shot stereo clip with baked reverb tail.
    public static AudioClip Clip(string name, float[] l, float[] r, float wet, float tail = 1.2f, float peak = 0.9f)
    {
        int n = l.Length + (wet > 0f ? (int)(tail * SR) : 0);
        var data = new float[n * 2];
        var rev = new Reverb(SR);
        float mx = 1e-5f;
        for (int i = 0; i < n; i++)
        {
            float a = i < l.Length ? l[i] : 0f, b = i < r.Length ? r[i] : 0f, wl = 0f, wr = 0f;
            if (wet > 0f) rev.Process(a, b, out wl, out wr);
            float fade = i > n - 2000 ? (n - i) / 2000f : 1f;
            a = (a + wl * wet) * fade; b = (b + wr * wet) * fade;
            data[i * 2] = a; data[i * 2 + 1] = b;
            mx = Mathf.Max(mx, Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)));
        }
        float g = peak / mx;
        for (int i = 0; i < data.Length; i++) data[i] *= g;
        var clip = AudioClip.Create(name, n, 2, SR, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// Seamless stereo loop (tail cross-faded into the head).
    public static AudioClip Loop(string name, float[] l, float[] r, float peak = 0.8f, bool crossfade = true)
    {
        int xf = crossfade ? SR : 0, n = l.Length - xf;
        var data = new float[n * 2];
        float mx = 1e-5f;
        for (int i = 0; i < n; i++)
        {
            float a = l[i], b = r[i];
            if (i < xf) { float f = i / (float)xf; a = a * f + l[n + i] * (1f - f); b = b * f + r[n + i] * (1f - f); }
            data[i * 2] = a; data[i * 2 + 1] = b;
            mx = Mathf.Max(mx, Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)));
        }
        float g = peak / mx;
        for (int i = 0; i < data.Length; i++) data[i] *= g;
        var clip = AudioClip.Create(name, n, 2, SR, false);
        clip.SetData(data, 0);
        return clip;
    }

    public static float[] Bell(float len, float[] freq, float[] at, float decay = 4f, float fm = 2.2f)
    {
        return Gen(len, t =>
        {
            float s = 0f;
            for (int i = 0; i < freq.Length; i++)
            {
                float u = t - at[i];
                if (u < 0f) continue;
                float w = 6.2832f * freq[i] * u;
                s += Mathf.Sin(w + fm * Mathf.Sin(w * 3.5f) * Mathf.Exp(-u * 7f)) * Mathf.Exp(-u * decay) * Mathf.Min(1f, u * 400f);
            }
            return s;
        });
    }
}

/// Sound effects, positional playback and layered ambience — all synthesised at start-up.
public class Sfx
{
    readonly AudioSource[] pool = new AudioSource[16]; int next;
    readonly AudioSource sea, wind, birds, crickets;
    readonly AudioClip up, down, build, levelUp, capture, thunder, rumble, erupt, lavaPop, flood, splash, bubbles, horn, click, deny, crumble;
    readonly AudioClip[] clash = new AudioClip[3];
    float clashCd, splashCd, popCd, seaV, windV, birdV, cricketV;
    public float volume = 1f;

    static AudioClip Stereo(string name, float wet, System.Func<float[]> make, float tail = 1.2f, float peak = 0.9f)
    {
        return Syn.Clip(name, make(), make(), wet, tail, peak);
    }

    static float[] MkEarth(bool rising)
    {
        float ph = 0f, lp = 0f;
        return Syn.Gen(0.42f, t =>
        {
            float k = Mathf.Clamp01(t / 0.22f), f = rising ? Mathf.Lerp(58f, 104f, k) : Mathf.Lerp(98f, 46f, k);
            ph += f / Syn.SR; lp += (Syn.N() - lp) * 0.11f;
            return Mathf.Sin(ph * 6.2832f) * Mathf.Exp(-t * 9f) * 0.9f + lp * Mathf.Exp(-t * 13f) * 1.7f * Mathf.Min(1f, t * 250f);
        });
    }

    static float[] MkThunder()
    {
        float a = 0f, b = 0f; float[] ct = { 0f, Syn.U(0.05f, 0.12f), Syn.U(0.16f, 0.3f), Syn.U(0.4f, 0.7f) };
        float p1 = Syn.U(0, 6f), p2 = Syn.U(0, 6f);
        return Syn.Gen(4.2f, t =>
        {
            float crack = 0f;
            for (int i = 0; i < ct.Length; i++) if (t > ct[i]) crack += Mathf.Exp(-(t - ct[i]) * (i == 3 ? 14f : 36f)) * (i == 3 ? 0.5f : 1f);
            float wn = Syn.N();
            a += (wn - a) * 0.3f; b += (wn - b) * 0.014f;
            float roll = 0.55f + 0.45f * Mathf.Sin(t * 6.5f + p1 + Mathf.Sin(t * 2.1f + p2) * 2.2f);
            return a * crack * 1.2f + b * 10f * Mathf.Exp(-t * 0.85f) * roll * Mathf.Min(1f, t * 5f + 0.25f);
        });
    }

    static float[] MkRumble()
    {
        float b = 0f, ph = 0f, p = Syn.U(0, 6f);
        return Syn.Gen(3.4f, t =>
        {
            b += (Syn.N() - b) * 0.011f; ph += 37f / Syn.SR;
            float env = Mathf.Min(1f, t * 2.5f) * Mathf.Exp(-t * 0.9f);
            return (b * 11f * (0.6f + 0.4f * Mathf.Sin(t * 9f + p)) + Mathf.Sin(ph * 6.2832f) * 0.5f * (0.5f + 0.5f * Mathf.Sin(t * 13f))) * env;
        });
    }

    static float[] MkErupt()
    {
        float ph = 0f, lp = 0f, cr = 0f;
        return Syn.Gen(3.2f, t =>
        {
            float f = 28f + 60f * Mathf.Exp(-t * 3f); ph += f / Syn.SR;
            float k = Mathf.Lerp(0.5f, 0.015f, Mathf.Clamp01(t / 1.6f));
            float wn = Syn.N(); lp += (wn - lp) * k;
            if (Syn.U(0, 1) < 0.0025f) cr = Syn.U(0.3f, 1f);
            cr *= 0.994f;
            return Mathf.Sin(ph * 6.2832f) * Mathf.Exp(-t * 1.5f) * 1.3f + lp * Mathf.Exp(-t * 1.5f) * 2.2f + wn * cr * 0.25f * Mathf.Exp(-t * 0.8f);
        });
    }

    static float[] MkCrumble()
    {
        float lp = 0f, ph = 0f; float[] bt = new float[7];
        for (int i = 0; i < bt.Length; i++) bt[i] = Syn.U(0f, 0.6f);
        return Syn.Gen(1.0f, t =>
        {
            float e = 0f;
            for (int i = 0; i < bt.Length; i++) if (t > bt[i]) e += Mathf.Exp(-(t - bt[i]) * 22f);
            lp += (Syn.N() - lp) * 0.22f; ph += (70f - t * 25f) / Syn.SR;
            return lp * e * 0.9f + Mathf.Sin(ph * 6.2832f) * Mathf.Exp(-t * 6f) * 0.8f;
        });
    }

    static float[] MkFlood()
    {
        float lp = 0f, ph = 0f, hp = 0f;
        return Syn.Gen(5f, t =>
        {
            float s = Mathf.Sin(Mathf.PI * t / 5f), wn = Syn.N();
            lp += (wn - lp) * (0.015f + 0.22f * s * s); hp += (wn - hp) * 0.5f; ph += 44f / Syn.SR;
            return lp * Mathf.Pow(s, 0.7f) * 2.4f + (wn - hp) * s * s * 0.25f + Mathf.Sin(ph * 6.2832f) * s * 0.5f;
        });
    }

    static float[] MkSplash()
    {
        float lp = 0f; float[] bt = { Syn.U(0.05f, 0.15f), Syn.U(0.15f, 0.3f), Syn.U(0.25f, 0.4f) }, bf = { Syn.U(700, 1100), Syn.U(900, 1500), Syn.U(1100, 1800) };
        return Syn.Gen(0.6f, t =>
        {
            float wn = Syn.N(); lp += (wn - lp) * 0.35f;
            float s = (wn - lp) * Mathf.Exp(-t * 10f) * 0.8f + lp * Mathf.Exp(-t * 7f) * 0.6f;
            for (int i = 0; i < 3; i++) { float u = t - bt[i]; if (u > 0f) s += Mathf.Sin(6.2832f * bf[i] * (1f + u * 3f) * u) * Mathf.Exp(-u * 45f) * 0.35f; }
            return s;
        });
    }

    static float[] MkBubbles()
    {
        float[] bt = new float[7], bf = new float[7];
        for (int i = 0; i < 7; i++) { bt[i] = Syn.U(0f, 0.7f); bf[i] = Syn.U(220f, 520f); }
        return Syn.Gen(1.0f, t =>
        {
            float s = 0f;
            for (int i = 0; i < 7; i++) { float u = t - bt[i]; if (u > 0f) s += Mathf.Sin(6.2832f * bf[i] * (1f + u * 5f) * u) * Mathf.Exp(-u * 28f) * Mathf.Min(1f, u * 300f); }
            return s * 0.5f;
        });
    }

    static float[] MkClash()
    {
        float[] f = { Syn.U(1500, 2100), Syn.U(2400, 3100), Syn.U(3600, 4400), Syn.U(5000, 6200), Syn.U(7000, 8200) };
        float d = Syn.U(18f, 30f);
        return Syn.Gen(0.4f, t =>
        {
            float s = Syn.N() * Mathf.Exp(-t * 220f) * 0.8f;
            for (int i = 0; i < f.Length; i++) s += Mathf.Sin(6.2832f * f[i] * t) * Mathf.Exp(-t * (d + i * 6f)) / (1f + i * 0.5f);
            return s;
        });
    }

    static float[] MkHorn()
    {
        float ph = 0f;
        return Syn.Gen(1.9f, t =>
        {
            float f = (t < 0.6f ? 146.83f : 220f) * (1f + 0.004f * Mathf.Sin(t * 34f) * Mathf.Clamp01(t - 0.8f));
            ph += f / Syn.SR;
            float seg = t < 0.6f ? t : t - 0.6f, env = Mathf.Min(1f, seg * 11f) * (t > 1.5f ? Mathf.Max(0f, 1f - (t - 1.5f) / 0.4f) : 1f);
            float bright = 2f + 8f * env * Mathf.Min(1f, seg * 3f + 0.3f), s = 0f;
            for (int h = 1; h <= 10; h++) s += Mathf.Sin(ph * 6.2832f * h) / h * Mathf.Clamp01(bright - h);
            return s * env;
        });
    }

    static float[] MkSea()
    {
        float lp = 0f, br = 0f, hp = 0f, p = Syn.U(0, 6.28f);
        return Syn.Gen(15f, t =>
        {
            float wn = Syn.N();
            lp += (wn - lp) * 0.07f; br += (wn - br) * 0.004f; hp += (wn - hp) * 0.45f;
            float sw = 0.5f + 0.5f * Mathf.Sin(6.2832f * t / 7f + p), sw2 = 0.5f + 0.5f * Mathf.Sin(6.2832f * t / 4.666f + p * 1.7f);
            float env = 0.3f + 0.7f * sw * sw;
            return lp * env * (0.7f + 0.3f * sw2) + (wn - hp) * env * env * 0.05f + br * 2.5f;
        });
    }

    static float[] MkWind()
    {
        float a = 0f, b = 0f, p = Syn.U(0, 6.28f);
        return Syn.Gen(13f, t =>
        {
            float wn = Syn.N();
            a += (wn - a) * (0.035f + 0.025f * Mathf.Sin(6.2832f * t / 12f + p)); b += (a - b) * 0.004f;
            return (a - b) * (0.6f + 0.4f * Mathf.Sin(6.2832f * t / 6f + p * 2f));
        });
    }

    static float[] MkBirds()
    {
        int n = (int)(18f * Syn.SR); var d = new float[n];
        for (int e = 0; e < 13; e++)
        {
            float start = Syn.U(1.2f, 16f), f0 = Syn.U(2300f, 5200f), amp = Syn.U(0.08f, 0.35f), trill = Syn.U(14f, 30f), sweep = Syn.U(-0.25f, 0.35f);
            int notes = (int)Syn.U(2, 6); float nd = Syn.U(0.05f, 0.11f), gap = Syn.U(0.02f, 0.07f);
            for (int k = 0; k < notes; k++)
            {
                int s0 = (int)((start + k * (nd + gap)) * Syn.SR), len = (int)(nd * Syn.SR);
                float ph = 0f, fk = f0 * (1f + Syn.U(-0.08f, 0.08f));
                for (int i = 0; i < len && s0 + i < n; i++)
                {
                    float u = i / (float)len;
                    ph += fk * (1f + sweep * u + 0.06f * Mathf.Sin(u * nd * trill * 6.2832f)) / Syn.SR;
                    d[s0 + i] += Mathf.Sin(ph * 6.2832f) * Mathf.Sin(u * Mathf.PI) * amp;
                }
            }
        }
        return d;
    }

    static float[] MkCrickets(float carrier, float offset)
    {
        return Syn.Gen(8f, t =>
        {
            float u = (t + offset) % 0.5f;
            float pulse = u < 0.16f ? Mathf.Pow(Mathf.Abs(Mathf.Sin(6.2832f * 28f * t)), 3f) : 0f;
            return Mathf.Sin(6.2832f * carrier * t) * pulse;
        });
    }

    AudioSource LoopSrc(GameObject host, AudioClip c)
    {
        var s = host.AddComponent<AudioSource>();
        s.clip = c; s.loop = true; s.spatialBlend = 0f; s.volume = 0f; s.playOnAwake = false; s.Play();
        return s;
    }

    public Sfx(GameObject host)
    {
        for (int i = 0; i < pool.Length; i++) { pool[i] = host.AddComponent<AudioSource>(); pool[i].spatialBlend = 0f; pool[i].playOnAwake = false; }

        up = Stereo("up", 0.12f, () => MkEarth(true), 0.5f);
        down = Stereo("down", 0.12f, () => MkEarth(false), 0.5f);
        build = Stereo("build", 0.5f, () => Syn.Bell(0.9f, new[] { 880f, 1318.5f }, new[] { 0f, 0.09f }), 1.6f, 0.7f);
        levelUp = Stereo("levelUp", 0.5f, () => Syn.Bell(1.1f, new[] { 587.33f, 880f, 1174.66f }, new[] { 0f, 0.08f, 0.16f }), 1.8f, 0.7f);
        capture = Stereo("capture", 0.5f, () => Syn.Bell(1.4f, new[] { 440f, 587.33f, 880f, 1174.66f, 1479.98f }, new[] { 0f, 0.07f, 0.14f, 0.21f, 0.3f }, 3f), 2f, 0.8f);
        thunder = Stereo("thunder", 0.4f, MkThunder, 2f);
        rumble = Stereo("rumble", 0.25f, MkRumble, 1.5f);
        erupt = Stereo("erupt", 0.35f, MkErupt, 2f);
        crumble = Stereo("crumble", 0.25f, MkCrumble, 1f);
        flood = Stereo("flood", 0.3f, MkFlood, 2f);
        splash = Stereo("splash", 0.15f, MkSplash, 0.6f);
        bubbles = Stereo("bubbles", 0.2f, MkBubbles, 0.8f);
        horn = Stereo("horn", 0.45f, MkHorn, 2f, 0.8f);
        for (int i = 0; i < 3; i++) clash[i] = Stereo("clash" + i, 0.2f, MkClash, 0.7f, 0.7f);
        lavaPop = Stereo("lavaPop", 0.2f, () =>
        {
            float ph = 0f, lp = 0f;
            return Syn.Gen(0.3f, t => { ph += (170f - t * 300f) / Syn.SR; lp += (Syn.N() - lp) * 0.2f; return Mathf.Sin(ph * 6.2832f) * Mathf.Exp(-t * 16f) + lp * Mathf.Exp(-t * 20f); });
        }, 0.6f, 0.7f);
        click = Stereo("click", 0f, () => Syn.Gen(0.06f, t => Mathf.Sin(6.2832f * 1500f * t) * Mathf.Exp(-t * 110f) + Mathf.Sin(6.2832f * 2250f * t) * Mathf.Exp(-t * 160f) * 0.5f), 0f, 0.45f);
        deny = Stereo("deny", 0.1f, () => Syn.Gen(0.26f, t =>
        {
            float f = t < 0.12f ? 233f : 185f, u = t < 0.12f ? t : t - 0.12f;
            return (Mathf.Sin(6.2832f * f * t) + 0.3f * Mathf.Sin(6.2832f * f * 3f * t)) * Mathf.Exp(-u * 22f) * Mathf.Min(1f, u * 300f);
        }), 0.4f, 0.5f);

        sea = LoopSrc(host, Syn.Loop("sea", MkSea(), MkSea()));
        wind = LoopSrc(host, Syn.Loop("wind", MkWind(), MkWind()));
        birds = LoopSrc(host, Syn.Loop("birds", MkBirds(), MkBirds(), 0.7f, false));
        crickets = LoopSrc(host, Syn.Loop("crickets", MkCrickets(4300f, 0f), MkCrickets(4725f, 0.23f), 0.6f, false));
    }

    static bool Silent { get { return Game.I == null || Game.I.fastForward; } }

    void Play(AudioClip c, float vol, float pitchVar = 0f, float pan = 0f)
    {
        if (Silent || vol * volume < 0.01f) return;
        var s = pool[next]; next = (next + 1) % pool.Length;
        s.Stop(); s.clip = c; s.volume = Mathf.Clamp01(vol * volume); s.panStereo = pan;
        s.pitch = 1f + Random.Range(-pitchVar, pitchVar);
        s.Play();
    }

    /// Play with stereo pan and distance fall-off relative to what the camera is looking at.
    void PlayAt(AudioClip c, float vol, Vector3 pos, float pitchVar = 0f)
    {
        if (Silent) return;
        var g = Game.I; Vector3 d = pos - g.rig.focus; d.y = 0f;
        float range = g.rig.dist * 1.3f + 6f, dist = d.magnitude;
        float att = 1f / (1f + (dist / range) * (dist / range) * 2.5f);
        float pan = Mathf.Clamp(Vector3.Dot(d, g.cam.transform.right) / range, -1f, 1f) * 0.85f;
        Play(c, vol * att, pitchVar, pan);
    }

    public void Up() { Play(up, 0.6f, 0.12f); }
    public void Down() { Play(down, 0.6f, 0.12f); }
    public void Click() { Play(click, 0.5f, 0.03f); }
    public void Deny() { Play(deny, 0.5f); }
    public void Chime(float vol = 0.5f) { Play(levelUp, vol, 0.03f); }
    public void Build(Vector3 p, bool mine) { PlayAt(build, mine ? 0.5f : 0.25f, p, 0.06f); }
    public void LevelUp(Vector3 p) { PlayAt(levelUp, 0.4f, p, 0.04f); }
    public void Capture(bool mine) { Play(mine ? capture : crumble, 0.7f); }
    public void Thunder() { Play(thunder, 1f, 0.1f); }
    public void Thunder(Vector3 p) { PlayAt(thunder, 1.4f, p, 0.12f); }
    public void Rumble() { Play(rumble, 1f, 0.08f); }
    public void Erupt(Vector3 p) { PlayAt(erupt, 1.5f, p, 0.08f); }
    public void Flood() { Play(flood, 1f); }
    public void Horn() { Play(horn, 0.7f); }
    public void Bubbles(Vector3 p) { PlayAt(bubbles, 0.7f, p, 0.15f); }
    public void Crumble(Vector3 p) { PlayAt(crumble, 0.8f, p, 0.15f); }
    public void LavaPop(Vector3 p) { if (popCd > 0f) return; popCd = 0.12f; PlayAt(lavaPop, 0.6f, p, 0.3f); }
    public void Clash(Vector3 p) { if (clashCd > 0f) return; clashCd = 0.09f; PlayAt(clash[Random.Range(0, 3)], 0.45f, p, 0.2f); }
    public void Splash(Vector3 p) { if (splashCd > 0f) return; splashCd = 0.15f; PlayAt(splash, 0.6f, p, 0.2f); }

    /// Ambience mix: sea near water, wind up high, birds by day, crickets by night.
    public void Update(float dt, World w, CameraRig rig, float night)
    {
        clashCd -= dt; splashCd -= dt; popCd -= dt;
        int water = 0, total = 0;
        float r = rig.dist * 0.45f + 3f;
        for (int i = 0; i < 12; i++)
        {
            float a = i * 0.5236f, x = rig.focus.x + Mathf.Cos(a) * r * (i % 2 == 0 ? 1f : 0.5f), z = rig.focus.z + Mathf.Sin(a) * r * (i % 2 == 0 ? 1f : 0.5f);
            total++;
            if (x < 0 || z < 0 || x >= World.N || z >= World.N || w.h[World.VI((int)x, (int)z)] == 0) water++;
        }
        float wf = water / (float)total, high = Mathf.InverseLerp(10f, 80f, rig.dist), k = 1f - Mathf.Exp(-dt * 1.5f);
        seaV = Mathf.Lerp(seaV, Mathf.Lerp(0.10f, 0.55f, wf) * Mathf.Lerp(1f, 0.7f, high), k);
        windV = Mathf.Lerp(windV, Mathf.Lerp(0.06f, 0.4f, high), k);
        birdV = Mathf.Lerp(birdV, (1f - night) * (1f - wf * 0.6f) * Mathf.Lerp(0.32f, 0.12f, high), k);
        cricketV = Mathf.Lerp(cricketV, night * (1f - wf * 0.8f) * Mathf.Lerp(0.2f, 0.06f, high), k);
        sea.volume = seaV * volume; wind.volume = windV * volume; birds.volume = birdV * volume; crickets.volume = cricketV * volume;
    }
}

/// Real-time generative score: strings, choir, harp, flute / bells, bass and war drums,
/// synthesised on the audio thread and steered by time of day and battle intensity.
public class MusicEngine : MonoBehaviour
{
    public volatile float intensity, night;
    public volatile int stinger;          // 1 victory, 2 defeat
    public volatile bool muted;
    public float volume = 0.62f;
    public float[] capture; public volatile int capturePos;   // test hook: records the stereo output
    public int SampleRate { get { return sr; } }

    const int WT = 2048, KS = 4096, NV = 40, NP = 12;
    int sr; bool ready;
    float[] tStr, tFlute, tChoir, tSine;

    struct Voice
    {
        public bool on; public float[] tab;
        public float ph, inc, ph2, ratio2, mix2, env, amp, atk, rel, panL, panR, send, vib, vibPh, lp, cut;
        public int hold; public bool attacking;
    }
    readonly Voice[] v = new Voice[NV];

    readonly float[][] kb = new float[NP][];
    readonly int[] kw = new int[NP], klife = new int[NP];
    readonly float[] kd = new float[NP], kprev = new float[NP], kamp = new float[NP], kpl = new float[NP], kpr = new float[NP], kdamp = new float[NP];

    float dPh, dFreq = 50f, dEnv, dClick, shEnv, shPrev;
    Reverb rev;
    readonly System.Random rng = new System.Random(99);
    int stepLen, stepPos, stepCount = -1;
    float gain, inten, nightS;
    bool minor; int section, chordRoot; bool chordMinor;
    int stingMode, stingIdx;

    // melody
    readonly int[] melStart = new int[16], melDur = new int[16], melIdx = new int[16], prevIdx = new int[16];
    int melCount, prevCount, melPos = 4; bool melOn;

    static readonly int[][] ProgA = { new[] { 0, 0 }, new[] { -3, 1 }, new[] { 5, 0 }, new[] { 7, 0 }, new[] { 0, 0 }, new[] { 5, 0 }, new[] { 2, 1 }, new[] { 7, 0 } };
    static readonly int[][] ProgB = { new[] { -3, 1 }, new[] { 5, 0 }, new[] { 0, 0 }, new[] { 7, 0 }, new[] { -3, 1 }, new[] { 5, 0 }, new[] { 2, 1 }, new[] { 7, 0 } };
    static readonly int[][] ProgM = { new[] { 0, 1 }, new[] { -4, 0 }, new[] { 3, 0 }, new[] { -2, 0 }, new[] { 0, 1 }, new[] { 5, 1 }, new[] { -4, 0 }, new[] { 7, 0 } };
    static readonly int[] PentMaj = { 0, 2, 4, 7, 9 }, PentMin = { 0, 3, 5, 7, 10 };
    static readonly int[][] Motifs = { new[] { 2, 2, 4 }, new[] { 1, 1, 2, 4 }, new[] { 3, 1, 4 }, new[] { 4, 4 }, new[] { 2, 1, 1, 4 }, new[] { 2, 2, 2, 2 }, new[] { 6, 2 } };
    static readonly int[] ArpPat = { 0, 2, 3, 4, 3, 2, 1, 3 };
    static readonly int[] Victory = { 62, 66, 69, 74, 78, 81, 86, 90, 93, 98 }, Defeat = { 81, 77, 74, 69, 65, 62, 57, 50 };

    static float[] Table(float[] harm)
    {
        var t = new float[WT + 1]; float mx = 0f;
        for (int i = 0; i <= WT; i++)
        {
            float x = i * 6.2831853f / WT, s = 0f;
            for (int h = 0; h < harm.Length; h++) s += Mathf.Sin(x * (h + 1)) * harm[h];
            t[i] = s; mx = Mathf.Max(mx, Mathf.Abs(s));
        }
        for (int i = 0; i <= WT; i++) t[i] /= mx;
        return t;
    }

    void Awake()
    {
        sr = AudioSettings.outputSampleRate;
        var hs = new float[14];
        for (int h = 0; h < 14; h++) hs[h] = 1f / (h + 1) / (1f + (h / 6f) * (h / 6f));
        tStr = Table(hs);
        tFlute = Table(new[] { 1f, 0.32f, 0.11f, 0.04f });
        tChoir = Table(new[] { 1f, 0.55f, 0.42f, 0.5f, 0.24f, 0.12f, 0.07f });
        tSine = Table(new[] { 1f });
        for (int i = 0; i < NP; i++) kb[i] = new float[KS];
        rev = new Reverb(sr) { feedback = 0.87f, damp = 0.22f };
        stepLen = (int)(sr * 60f / 66f / 4f);

        var src = gameObject.AddComponent<AudioSource>();
        src.clip = AudioClip.Create("silence", sr, 2, sr, false);
        src.loop = true; src.spatialBlend = 0f; src.volume = 1f;
        src.Play();
        ready = true;
    }

    static float Hz(float midi) { return 440f * Mathf.Pow(2f, (midi - 69f) / 12f); }
    float Rnd() { return (float)rng.NextDouble(); }

    void NoteOn(float[] tab, float midi, float amp, float atk, float hold, float rel, float pan, float send, float ratio2 = 1f, float mix2 = 0f, float cut = 1f, float vib = 0f)
    {
        int slot = -1; float low = 1e9f;
        for (int i = 0; i < NV; i++)
        {
            if (!v[i].on) { slot = i; break; }
            float e = v[i].env * v[i].amp;
            if (!v[i].attacking && e < low) { low = e; slot = i; }
        }
        if (slot < 0) return;
        v[slot] = new Voice
        {
            on = true, tab = tab, inc = Hz(midi) / sr, ph = Rnd(), ph2 = Rnd(), ratio2 = ratio2, mix2 = mix2, amp = amp, env = 0f, attacking = true,
            atk = 1f / Mathf.Max(1f, atk * sr), hold = (int)(hold * sr), rel = Mathf.Pow(0.001f, 1f / Mathf.Max(1f, rel * sr)),
            panL = Mathf.Sqrt(0.5f - pan * 0.5f), panR = Mathf.Sqrt(0.5f + pan * 0.5f), send = send, cut = cut, vib = vib
        };
    }

    void Pluck(float midi, float amp, float pan, float bright)
    {
        int slot = 0, oldest = int.MaxValue;
        for (int i = 0; i < NP; i++) if (klife[i] < oldest) { oldest = klife[i]; slot = i; }
        float d = sr / Hz(midi) - 0.5f;
        if (d < 4f || d > KS - 8) return;
        float[] b = kb[slot]; float lp = 0f;
        int n = (int)d + 2;
        for (int j = 0; j < KS; j++) b[j] = 0f;
        for (int j = 0; j < n; j++) { lp += (Rnd() * 2f - 1f - lp) * bright; b[(kw[slot] - 1 - j) & (KS - 1)] = lp; }
        kd[slot] = d; kprev[slot] = 0f; kamp[slot] = amp; kdamp[slot] = 0.9965f; klife[slot] = sr * 4;
        kpl[slot] = Mathf.Sqrt(0.5f - pan * 0.5f); kpr[slot] = Mathf.Sqrt(0.5f + pan * 0.5f);
    }

    int Third { get { return chordMinor ? 3 : 4; } }

    int MelPitch(int idx) { int[] p = minor ? PentMin : PentMaj; return 74 + 12 * (idx / 5) + p[idx % 5]; }

    void NewPhrase(int bar)
    {
        bool repeat = prevCount > 0 && (bar % 4) == 2 && Rnd() < 0.7f;
        melOn = Rnd() < (inten > 0.5f ? 0.9f : 0.72f);
        if (!melOn) return;
        if (repeat)
        {
            melCount = prevCount;
            for (int i = 0; i < melCount; i++) melIdx[i] = prevIdx[i];
            melIdx[melCount - 1] = Mathf.Clamp(melIdx[melCount - 1] + (Rnd() < 0.5f ? -1 : 1), 0, 9);
        }
        else
        {
            melCount = 0; int pos = 0;
            for (int m = 0; m < 2; m++)
            {
                int[] mot = Motifs[rng.Next(Motifs.Length)];
                for (int i = 0; i < mot.Length; i++)
                {
                    melStart[melCount] = pos; melDur[melCount] = mot[i]; pos += mot[i];
                    int step = new[] { -2, -1, -1, 1, 1, 2 }[rng.Next(6)];
                    melPos = Mathf.Clamp(melPos + step, 0, 9);
                    if (melPos == 0 || melPos == 9) melPos = 4 + rng.Next(3) - 1;
                    melIdx[melCount++] = melPos;
                }
            }
            // land the phrase on a chord tone
            int[] ch = (minor ? ProgM : (section == 0 ? ProgA : ProgB))[(bar + 1) % 8];
            for (int k = 0; k < 4; k++)
            {
                bool done = false;
                foreach (int cand in new[] { melIdx[melCount - 1] - k, melIdx[melCount - 1] + k })
                {
                    if (cand < 0 || cand > 9) continue;
                    int pc = ((MelPitch(cand) - 62 - ch[0]) % 12 + 12) % 12;
                    if (pc == 0 || pc == 7 || pc == (ch[1] == 1 ? 3 : 4)) { melIdx[melCount - 1] = cand; done = true; break; }
                }
                if (done) break;
            }
            prevCount = melCount;
            for (int i = 0; i < melCount; i++) prevIdx[i] = melIdx[i];
        }
    }

    void Step()
    {
        stepCount++;
        int s16 = stepCount % 16, bar = stepCount / 16;
        if (stepCount == 0) { inten = intensity; nightS = night; }
        inten += (intensity - inten) * 0.12f; nightS += (night - nightS) * 0.1f;
        float stepSec = stepLen / (float)sr;

        int st = stinger;
        if (st != 0) { stinger = 0; stingMode = st; stingIdx = 0; minor = st == 2; NoteOnChord(st == 2); }
        if (stingMode != 0)
        {
            int[] seq = stingMode == 1 ? Victory : Defeat;
            if (stingMode == 1 || s16 % 2 == 0)
            {
                if (stingIdx < seq.Length) NoteOn(tSine, seq[stingIdx], 0.10f, 0.002f, 0f, 2.2f, (stingIdx % 3 - 1) * 0.4f, 0.8f, 4.01f, 0.28f);
                if (++stingIdx > seq.Length + 24) stingMode = 0;
            }
            if (stingIdx <= seq.Length) return;
        }

        if (s16 == 0)
        {
            if (bar % 8 == 0)
            {
                minor = nightS > 0.5f || inten > 0.5f;
                section = (section + (Rnd() < 0.6f ? 1 : 0)) % 2;
            }
            int[] ch = (minor ? ProgM : (section == 0 ? ProgA : ProgB))[bar % 8];
            chordRoot = ch[0]; chordMinor = ch[1] == 1;
            float barSec = stepSec * 16f, cut = Mathf.Lerp(0.24f, 0.45f, inten) * Mathf.Lerp(1f, 0.7f, nightS), pa = Mathf.Lerp(0.045f, 0.06f, inten);
            int r = 50 + chordRoot;
            NoteOn(tStr, r, pa * 0.75f, 1.1f, barSec, 2.6f, -0.3f, 0.55f, 1.004f, 1f, cut);
            NoteOn(tStr, r + 7, pa, 1.3f, barSec, 2.6f, 0.35f, 0.55f, 0.996f, 1f, cut);
            NoteOn(tStr, r + 24 + 7, pa * 0.45f, 1.8f, barSec, 3f, -0.4f, 0.7f, 1.003f, 1f, cut);
            NoteOn(tStr, r + 12 + Third, pa * 0.95f, 1.5f, barSec, 2.8f, -0.15f, 0.6f, 1.003f, 1f, cut);
            NoteOn(tStr, r + 12, pa * 0.7f, 1.5f, barSec, 2.8f, 0.2f, 0.6f, 1.005f, 1f, cut);
            if (inten < 0.6f)
            {
                NoteOn(tChoir, r + 12 + 7, 0.03f + nightS * 0.012f, 2f, barSec, 3f, 0.35f, 0.8f, 1.002f, 1f, 0.35f);
                NoteOn(tChoir, r + 24, 0.024f + nightS * 0.012f, 2.2f, barSec, 3f, -0.35f, 0.8f, 0.998f, 1f, 0.35f);
            }
            NoteOn(tSine, 38 + chordRoot, 0.07f, 0.04f, barSec * 0.9f, 0.9f, 0f, 0.1f, 2f, 0.2f);
            if (bar % 2 == 0) NewPhrase(bar);
        }

        // harp
        int harpEvery = nightS > 0.5f && inten < 0.4f ? 4 : 2;
        if (s16 % harpEvery == 0 && Rnd() < 0.9f)
        {
            int[] tones = { 0, 7, 12, 12 + Third, 19, 24 };
            int tone = tones[ArpPat[(stepCount / harpEvery) % 8]];
            Pluck(50 + chordRoot + tone + (nightS > 0.5f ? 12 : 0), 0.28f * (1f - inten * 0.4f), (Rnd() - 0.5f) * 1.2f, Mathf.Lerp(0.8f, 0.5f, nightS));
        }

        // melody
        if (melOn && s16 % 2 == 0)
        {
            int e8 = ((bar % 2) * 16 + s16) / 2;
            for (int i = 0; i < melCount; i++)
            {
                if (melStart[i] != e8) continue;
                float dur = melDur[i] * stepSec * 2f; int pitch = MelPitch(melIdx[i]);
                if (inten > 0.55f) NoteOn(tStr, pitch - 12, 0.10f, 0.05f, dur * 0.9f, 0.35f, 0.1f, 0.5f, 1.006f, 1f, 0.6f);
                else if (nightS > 0.5f) NoteOn(tSine, pitch, 0.11f, 0.002f, 0f, 1.9f, (Rnd() - 0.5f) * 0.6f, 0.75f, 4.01f, 0.25f);
                else NoteOn(tFlute, pitch, 0.12f, 0.07f, dur - 0.06f, 0.4f, 0.1f, 0.65f, 1f, 0f, 1f, 0.006f);
            }
        }

        // battle: string ostinato and war drums
        if (inten > 0.45f && s16 % 2 == 0)
            NoteOn(tStr, 38 + chordRoot + (s16 % 8 == 4 ? 7 : (s16 % 4 == 2 ? 12 : 0)), 0.085f * inten, 0.01f, stepSec * 0.9f, 0.12f, (s16 % 4 == 0 ? -0.3f : 0.3f), 0.3f, 1.005f, 1f, 0.65f);
        if (inten > 0.28f)
        {
            bool hit = s16 == 0 || s16 == 6 || s16 == 8 || s16 == 14 || (inten > 0.7f && (s16 == 3 || s16 == 11 || s16 == 13));
            if (hit) { dEnv = Mathf.Lerp(0.3f, 0.6f, inten) * (s16 == 0 ? 1f : 0.75f); dPh = 0f; dFreq = 150f; dClick = 0.3f * inten; }
            if (inten > 0.6f && s16 % 2 == 1) shEnv = 0.06f;
        }
    }

    void NoteOnChord(bool min)
    {
        int[] n = min ? new[] { 38, 45, 50, 53, 57 } : new[] { 38, 50, 57, 62, 66, 69 };
        for (int i = 0; i < n.Length; i++) NoteOn(tStr, n[i], 0.06f, min ? 1.2f : 0.25f, 5f, 4f, (i % 2 == 0 ? -0.5f : 0.5f), 0.7f, 1.004f, 1f, min ? 0.12f : 0.3f);
        NoteOn(tSine, 26, 0.16f, 0.05f, 4f, 2f, 0f, 0.1f);
        if (!min) { dEnv = 0.8f; dPh = 0f; dFreq = 125f; }
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!ready) return;
        int frames = data.Length / channels;
        float target = muted ? 0f : volume;
        for (int f = 0; f < frames; f++)
        {
            if (stepPos == 0) Step();
            if (++stepPos >= stepLen) stepPos = 0;
            gain += (target - gain) * 0.00002f;

            float l = 0f, r = 0f, sl = 0f, srr = 0f;
            for (int i = 0; i < NV; i++)
            {
                if (!v[i].on) continue;
                float mod = 1f;
                if (v[i].vib > 0f)
                {
                    v[i].vibPh += 5.3f / sr; if (v[i].vibPh >= 1f) v[i].vibPh -= 1f;
                    mod = 1f + v[i].vib * Mathf.Sin(v[i].vibPh * 6.2831853f);
                }
                float[] t = v[i].tab;
                float ph = v[i].ph + v[i].inc * mod; if (ph >= 1f) ph -= 1f; v[i].ph = ph;
                float x = ph * WT; int ix = (int)x; float s = t[ix] + (t[ix + 1] - t[ix]) * (x - ix);
                if (v[i].mix2 > 0f)
                {
                    float p2 = v[i].ph2 + v[i].inc * v[i].ratio2 * mod; p2 -= (int)p2; v[i].ph2 = p2;
                    x = p2 * WT; ix = (int)x; s += (t[ix] + (t[ix + 1] - t[ix]) * (x - ix)) * v[i].mix2;
                }
                if (v[i].attacking) { v[i].env += v[i].atk; if (v[i].env >= 1f) { v[i].env = 1f; v[i].attacking = false; } }
                if (v[i].hold > 0) v[i].hold--;
                else if (!v[i].attacking) { v[i].env *= v[i].rel; if (v[i].env < 0.0006f) v[i].on = false; }
                if (v[i].cut < 1f) { v[i].lp += (s - v[i].lp) * v[i].cut; s = v[i].lp; }
                s *= v[i].env * v[i].amp;
                float a = s * v[i].panL, b = s * v[i].panR;
                l += a; r += b; sl += a * v[i].send; srr += b * v[i].send;
            }
            for (int i = 0; i < NP; i++)
            {
                if (klife[i] <= 0) continue;
                klife[i]--;
                float[] b = kb[i];
                float rd = kw[i] - kd[i]; if (rd < 0f) rd += KS;
                int i0 = (int)rd; float fr = rd - i0;
                float y = b[i0 & (KS - 1)] * (1f - fr) + b[(i0 + 1) & (KS - 1)] * fr;
                b[kw[i]] = (y + kprev[i]) * 0.5f * kdamp[i]; kprev[i] = y;
                kw[i] = (kw[i] + 1) & (KS - 1);
                float o = y * kamp[i];
                l += o * kpl[i]; r += o * kpr[i]; sl += o * kpl[i] * 0.5f; srr += o * kpr[i] * 0.5f;
            }
            if (dEnv > 0.0005f)
            {
                dFreq = 64f + (dFreq - 64f) * 0.9994f; dPh += dFreq / sr; if (dPh >= 1f) dPh -= 1f;
                float wn = (float)rng.NextDouble() * 2f - 1f;
                float d = Mathf.Sin(dPh * 6.2831853f) * dEnv + wn * dClick;
                dEnv *= 0.99986f; dClick *= 0.996f;
                l += d * 0.7f; r += d * 0.7f; sl += d * 0.25f; srr += d * 0.25f;
            }
            if (shEnv > 0.0005f)
            {
                float wn = (float)rng.NextDouble() * 2f - 1f, hpn = wn - shPrev; shPrev = wn;
                l += hpn * shEnv * 0.6f; r += hpn * shEnv; shEnv *= 0.9992f;
            }

            float wl, wr;
            rev.Process(sl, srr, out wl, out wr);
            l = (l + wl * 1.1f) * gain * 1.6f; r = (r + wr * 1.1f) * gain * 1.6f;
            l = l * (27f + l * l) / (27f + 9f * l * l); r = r * (27f + r * r) / (27f + 9f * r * r);
            if (capture != null && capturePos < capture.Length - 1) { capture[capturePos] = l; capture[capturePos + 1] = r; capturePos += 2; }
            if (channels >= 2) { data[f * channels] = l; data[f * channels + 1] = r; }
            else data[f] = (l + r) * 0.5f;
        }
    }
}
