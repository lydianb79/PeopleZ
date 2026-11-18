using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public class Game : MonoBehaviour
{
    public static Game I;
    static bool seenTitle;

    public World world; public Sim sim; public TerrainView view; public Fx fx; public Sfx sfx; public Hud hud; public MusicEngine music;
    public CameraRig rig = new CameraRig(); public Camera cam;
    public int tool, speed = 1;
    public bool paused, started, ended, fastForward;

    ColorAdjustments grade; DepthOfField dof; Light sun;
    Mesh birdMesh; Material birdMat; readonly Matrix4x4[] birdM = new Matrix4x4[24];
    public float tod;                      // time of day 0..1 (0 = sunrise, 0.7 = sunset)
    const float DayLength = 540f;
    float nightExposure, nightLevel, battle, forceBattle; string wavPath;
    bool testRun; string iconPath; RenderTexture iconRT; int iconFrames; const int IconC = 64;

    void SetupIcon()
    {
        int c = IconC;
        world.MakeIsland(c, c, 4, 3);
        for (int z = c - 2; z <= c + 3; z++) for (int x = c - 2; x <= c + 3; x++) while (world.h[World.VI(x, z)] < 3) world.Raise(x, z);
        foreach (var t in new[] { new[] { -5, 1, 1 }, new[] { 5, 0, 2 }, new[] { -5, -3, 2 }, new[] { 5, -3, 3 }, new[] { 5, 3, 1 }, new[] { -5, 4, 2 }, new[] { -6, -1, 1 }, new[] { 6, -2, 1 }, new[] { 0, 5, 1 }, new[] { 2, 5, 2 }, new[] { -2, 5, 1 }, new[] { -4, -5, 4 }, new[] { 6, 1, 2 } })
            world.tree[World.CI(c + t[0], c + t[1])] = (byte)t[2];
        world.treesDirty = true;
        var castle = sim.PlaceTown(0, c, c, 100f);
        fastForward = true;
        for (int i = 0; i < 5; i++) sim.Update(0.4f);
        sim.walkers.Clear();
        var w1 = sim.Spawn(0, c - 0.6f, c - 1.6f, 20f); var w2 = sim.Spawn(0, c + 1.7f, c - 1.9f, 30f);
        w1.x = c - 0.6f; w1.z = c - 1.6f; w1.heading = 200f; w1.dead = false; w2.x = c + 1.7f; w2.z = c - 1.9f; w2.heading = 150f; w2.dead = false;
        fastForward = false;
        if (castle != null) castle.go.transform.rotation = Quaternion.identity;
        started = true; paused = true; hud.Hide(); music.muted = true; sfx.volume = 0f;
        Application.runInBackground = true;
        iconRT = new RenderTexture(2048, 2048, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = iconRT; cam.fieldOfView = 30f; cam.aspect = 1f;
    }

    void IconFrame()
    {
        float[] cp = { -4.5f, 0.4f, -9f, 1.2f, 26f };
        string ca = Arg("-pzcam");
        if (ca != null) { var parts = ca.Split(','); for (int i = 0; i < parts.Length && i < 5; i++) cp[i] = float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture); }
        Vector3 target = new Vector3(IconC + 0.5f, 3 * World.Step + cp[3], IconC + 0.55f);
        cam.transform.position = target + new Vector3(cp[0], cp[1], cp[2]);
        cam.transform.LookAt(target);
        cam.fieldOfView = cp[4];
        dof.active = false;
        if (++iconFrames < 40) return;
        RenderTexture.active = iconRT;
        var big = new Texture2D(2048, 2048, TextureFormat.RGBA32, false);
        big.ReadPixels(new Rect(0, 0, 2048, 2048), 0, 0); big.Apply();
        RenderTexture.active = null;
        const int S = 1024; const float half = 412f, rad = 186f;
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx0 = x + 0.5f - S * 0.5f, fy0 = y + 0.5f - S * 0.5f;
                float d = Sdf(fx0, fy0, half, rad), ds = Sdf(fx0, fy0 + 12f, half, rad);
                float a = Mathf.Clamp01(0.5f - d), sh = 0.38f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(26f, -6f, ds));
                Color col = Color.black;
                if (a > 0f)
                {
                    float u = (fx0 + half) / (half * 2f), v = (fy0 + half) / (half * 2f);
                    for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) col += big.GetPixelBilinear(u + i * 0.0004f, v + j * 0.0004f);
                    col /= 9f;
                    col += Color.white * 0.10f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-5f, 0f, d)) * Mathf.Clamp01(fy0 / half + 0.3f);   // rim light
                    col *= 1f - 0.18f * Mathf.Clamp01(-fy0 / half - 0.55f);
                }
                float oa = a + sh * (1f - a);
                px[y * S + x] = oa > 0f ? new Color(col.r * a / oa, col.g * a / oa, col.b * a / oa, oa) : Color.clear;
            }
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.SetPixels(px); tex.Apply();
        System.IO.File.WriteAllBytes(iconPath, tex.EncodeToPNG());
        iconPath = null; Application.Quit();
    }

    static float Sdf(float x, float y, float half, float r)
    {
        float qx = Mathf.Abs(x) - half + r, qy = Mathf.Abs(y) - half + r;
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    static Vector3 V(Color c) { return new Vector3(c.r, c.g, c.b); }

    void UpdateSky(float dt)
    {
        tod = (tod + dt / DayLength) % 1f;
        float a = tod < 0.7f ? Mathf.PI * tod / 0.7f : Mathf.PI * (1f + (tod - 0.7f) / 0.3f);
        float el = Mathf.Sin(a) * 58f;                          // sun elevation in degrees
        float az = -50f - Mathf.Cos(a) * 70f;
        bool day = el >= 0f;
        float lightEl = day ? Mathf.Max(el, 4f) : Mathf.Max(-el * 0.9f, 14f);
        sun.transform.rotation = Quaternion.Euler(lightEl, day ? az : az + 180f, 0f);
        float dayK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 26f, el)), nightK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, -13f, el));
        float duskK = Mathf.Clamp01(1f - dayK - nightK);
        Color zen = new Color(0.09f, 0.25f, 0.62f) * dayK + new Color(0.16f, 0.17f, 0.40f) * duskK + new Color(0.010f, 0.016f, 0.05f) * nightK;
        Color hor = new Color(0.60f, 0.75f, 0.90f) * dayK + new Color(1.0f, 0.50f, 0.26f) * duskK + new Color(0.035f, 0.06f, 0.13f) * nightK;
        Color gnd = new Color(0.20f, 0.24f, 0.28f) * dayK + new Color(0.20f, 0.15f, 0.16f) * duskK + new Color(0.012f, 0.016f, 0.03f) * nightK;
        Color sc = day ? Color.Lerp(new Color(1f, 0.50f, 0.22f), new Color(1f, 0.93f, 0.80f), dayK) : new Color(0.42f, 0.56f, 1f);
        float inten = day ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 9f, el)) * 1.95f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-2f, -12f, el)) * 0.42f;
        sun.color = sc; sun.intensity = inten;
        Shader.SetGlobalVector("_PZSunDir", -sun.transform.forward);
        Shader.SetGlobalVector("_PZSunColor", V(sc) * (day ? Mathf.Lerp(0.25f, 0.95f, inten / 1.95f) : 0.28f * inten / 0.42f + 0.02f));
        Shader.SetGlobalVector("_PZZenith", V(zen));
        Shader.SetGlobalVector("_PZHorizon", V(hor));
        Shader.SetGlobalVector("_PZGround", V(gnd));
        Shader.SetGlobalFloat("_PZNight", nightK);
        nightLevel = nightK;
        Shader.SetGlobalFloat("_PZGlow", Mathf.Lerp(1.3f, 7f, Mathf.Max(nightK, duskK * 0.6f)));
        nightExposure = nightK * 0.9f + duskK * 0.15f;
        if (fx != null) fx.tint = Mathf.Lerp(1f, 0.16f, nightK) * Mathf.Lerp(1f, 0.75f, duskK);
    }

    void DrawBirds()
    {
        float t = Time.time;
        for (int i = 0; i < birdM.Length; i++)
        {
            int flock = i / 8; float k = i % 8;
            float ang = t * (0.10f + flock * 0.03f) + flock * 2.1f + k * 0.05f, rad = 26f + flock * 14f + Mathf.Sin(k * 1.7f) * 3f;
            Vector3 c = new Vector3(World.N * (0.35f + flock * 0.15f), 9f + flock * 2f + Mathf.Sin(t * 0.3f + k) * 0.6f, World.N * (0.6f - flock * 0.12f));
            Vector3 p = c + new Vector3(Mathf.Cos(ang) * rad, Mathf.Sin(k * 2.3f) * 0.8f, Mathf.Sin(ang) * rad);
            float flap = Mathf.Sin(t * 9f + k * 1.3f) * 0.32f;
            birdM[i] = Matrix4x4.TRS(p, Quaternion.Euler(0, -ang * Mathf.Rad2Deg, 0), new Vector3(0.75f, flap, 0.75f));
        }
        Graphics.RenderMeshInstanced(new RenderParams(birdMat) { shadowCastingMode = ShadowCastingMode.On, worldBounds = new Bounds(new Vector3(World.N * 0.5f, 8f, World.N * 0.5f), new Vector3(300, 60, 300)) }, birdMesh, 0, birdM);
    }
    float repeatT, shotT = -1f; int levelH = -1; string shotPath;
    UniversalRenderPipelineAsset urp;

    static string Arg(string name)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return null;
    }

    void Awake()
    {
        I = this;
        QualitySettings.vSyncCount = 1;
        // Always render at the display's native pixels; a stale saved window size must never be stretched to fullscreen.
        testRun = Arg("-screen-width") != null || Arg("-pzicon") != null;
        if (!testRun) Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
        Application.targetFrameRate = 120;
        urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        // --- camera, sun, sky, post
        var cgo = new GameObject("Camera");
        cam = cgo.AddComponent<Camera>();
        cam.fieldOfView = 36f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 900f; cam.allowHDR = true; cam.allowMSAA = true;
        cam.clearFlags = CameraClearFlags.Skybox;
        cgo.AddComponent<AudioListener>();
        cgo.tag = "MainCamera";
        var cd = cam.GetUniversalAdditionalCameraData();
        cd.renderPostProcessing = true;

        cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cd.antialiasingQuality = AntialiasingQuality.High;

        var lgo = new GameObject("Sun");
        sun = lgo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.92f;

        Shader.SetGlobalFloat("_PZFogDensity", 0.0028f);
        Shader.SetGlobalFloat("_PZWaterY", World.WaterY);
        RenderSettings.skybox = new Material(Resources.Load<Shader>("Shaders/PZSky"));
        RenderSettings.fog = false;
        tod = float.TryParse(Arg("-pztime"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float tv) ? tv : 0.16f;
        UpdateSky(0f);

        var vgo = new GameObject("PostFX");
        var vol = vgo.AddComponent<Volume>(); vol.isGlobal = true;
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = prof.Add<Bloom>(true); bloom.threshold.value = 1.0f; bloom.intensity.value = 0.7f; bloom.scatter.value = 0.74f; bloom.highQualityFiltering.value = true;
        var tone = prof.Add<Tonemapping>(true); tone.mode.value = TonemappingMode.ACES;
        grade = prof.Add<ColorAdjustments>(true); grade.postExposure.value = 0.25f; grade.contrast.value = 10f; grade.saturation.value = 14f;
        var wb = prof.Add<WhiteBalance>(true); wb.temperature.value = 6f; wb.tint.value = 2f;
        var smh = prof.Add<ShadowsMidtonesHighlights>(true);
        smh.shadows.value = new Vector4(0.92f, 0.98f, 1.10f, 0f); smh.highlights.value = new Vector4(1.06f, 1.02f, 0.94f, 0f);
        var vig = prof.Add<Vignette>(true); vig.intensity.value = 0.26f; vig.smoothness.value = 0.5f;
        var ca = prof.Add<ChromaticAberration>(true); ca.intensity.value = 0.05f;
        var grain = prof.Add<FilmGrain>(true); grain.intensity.value = 0.12f; grain.response.value = 0.8f;
        dof = prof.Add<DepthOfField>(true); dof.mode.value = DepthOfFieldMode.Gaussian; dof.gaussianMaxRadius.value = 1.1f; dof.highQualitySampling.value = true;
        vol.profile = prof;

        birdMesh = Models.Bird();
        birdMat = new Material(Resources.Load<Shader>("Shaders/PZLit")) { enableInstancing = true };

        // --- world
        int seed = int.TryParse(Arg("-pzseed"), out int sd) ? sd : Random.Range(1, 99999);
        world = new World();
        world.Generate(seed);
        Shader lit = Resources.Load<Shader>("Shaders/PZLit");
        var treeMat = new Material(lit) { enableInstancing = true }; treeMat.SetFloat("_Sway", 1f);
        view = new TerrainView(world, transform, new Material(Resources.Load<Shader>("Shaders/PZTerrain")), new Material(Resources.Load<Shader>("Shaders/PZWater")), treeMat);
        fx = new Fx(world, Resources.Load<Shader>("Shaders/PZFx"));
        sfx = new Sfx(cgo);
        music = new GameObject("Music").AddComponent<MusicEngine>();
        sim = new Sim(world, fx, sfx, lit, transform);
        iconPath = Arg("-pzicon");
        if (iconPath == null) sim.Begin();
        hud = new Hud(this);

        rig.focus = rig.tFocus = new Vector3(World.N * 0.30f, 1.2f, World.N * 0.30f);
        rig.dist = 70f; rig.tDist = 24f;

        // --- test hooks
        shotPath = Arg("-pzshot");
        started = seenTitle || shotPath != null;
        hud.ShowTitle(!started);
        if (float.TryParse(Arg("-pzfast"), out float ff))
        {
            fastForward = true;
            for (float t = 0; t < ff; t += 0.05f) sim.Update(0.05f);
            fastForward = false;
        }
        if (float.TryParse(Arg("-pzzoom"), out float z)) rig.dist = rig.tDist = z;
        if (Arg("-pztest") != null)
        {
            sim.teams[0].mana = 2500f;
            Vector3 p = rig.focus;
            sim.Cast(0, 3, p + new Vector3(7, 0, -3)); sim.Cast(0, 2, p + new Vector3(-5, 0, 4)); sim.Cast(0, 5, p + new Vector3(4, 0, 9)); sim.Cast(0, 1, p + new Vector3(-3, 0, -3));
        }
        if (shotPath != null) { shotT = 2.5f; Application.runInBackground = true; }
        if (iconPath != null) SetupIcon();
        wavPath = Arg("-pzwav");
        if (wavPath != null)
        {
            started = true; hud.ShowTitle(false); Application.runInBackground = true;
            float.TryParse(Arg("-pzint"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out forceBattle);
            music.capture = new float[music.SampleRate * 2 * 45];
        }
    }

    public void Toast(string s) { if (hud != null && !fastForward) hud.Toast(s); }
    public void SelectTool(int i) { tool = i; sfx.Click(); }
    public void SetMode(int m)
    {
        if (sim.cataclysm) return;
        sfx.Click();
        if (m == 1 && !sim.teams[0].hasBeacon) { tool = 1; Toast("Place a beacon for your followers to gather at"); return; }
        sim.teams[0].mode = m;
        Toast(m == 0 ? "Followers: settle the land" : m == 1 ? "Followers: gather at the beacon" : "Followers: attack the rival!");
        if (m == 2) sfx.Horn();
    }
    public void Restart() { seenTitle = true; SceneManager.LoadScene(0); }

    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        string hint = "";

        if (!started)
        {
            rig.tYaw += dt * 4f;
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) { started = true; seenTitle = true; hud.ShowTitle(false); }
        }
        else if (!ended)
        {
            for (int i = 0; i < 8; i++) if (Input.GetKeyDown(KeyCode.Alpha1 + i)) SelectTool(i);
            if (Input.GetKeyDown(KeyCode.Escape)) tool = 0;
            if (Input.GetKeyDown(KeyCode.Z)) SetMode(0);
            if (Input.GetKeyDown(KeyCode.X)) SetMode(1);
            if (Input.GetKeyDown(KeyCode.C)) SetMode(2);
            if (Input.GetKeyDown(KeyCode.Space)) paused = !paused;
            if (Input.GetKeyDown(KeyCode.Tab)) speed = speed == 1 ? 3 : 1;

            if (!paused)
                for (int i = 0; i < speed; i++) sim.Update(dt);
            if (sim.winner >= 0) { ended = true; hud.ShowEnd(sim.winner == 0); music.stinger = sim.winner == 0 ? 1 : 2; if (sim.winner != 0) sfx.Rumble(); }
        }

        rig.Update(cam, world, dt, started && iconPath == null, !overUI, fx.shake);
        if (iconPath != null) IconFrame();
        if (!testRun)
        {
            if (Input.GetKeyDown(KeyCode.F))
            {
                if (Screen.fullScreen) Screen.SetResolution(1600, 1000, FullScreenMode.Windowed);
                else Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
            }
            else if (Screen.fullScreen && (Screen.width != Display.main.systemWidth || Screen.height != Display.main.systemHeight) && Time.frameCount % 30 == 0)
                Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
        }
        if (urp != null) urp.shadowDistance = Mathf.Clamp(rig.dist * 2.6f + 15f, 40f, 220f);
        UpdateSky(paused ? 0f : dt * speed);
        dof.gaussianStart.value = rig.dist * 1.7f + 6f; dof.gaussianEnd.value = rig.dist * 4.5f + 30f;
        DrawBirds();
        if (Input.GetKeyDown(KeyCode.M)) { music.muted = !music.muted; Toast(music.muted ? "Music off" : "Music on"); }
        float battleTarget = ended ? 0f : (sim.cataclysm ? 1f : Mathf.Clamp01(sim.fighting / 9f));
        battle = Mathf.Lerp(battle, battleTarget, 1f - Mathf.Exp(-dt * (battleTarget > battle ? 0.8f : 0.25f)));
        music.intensity = started ? battle : 0f; music.night = nightLevel;
        if (wavPath != null)
        {
            music.intensity = forceBattle;
            if (music.capturePos >= music.capture.Length - 1)
            {
                var bytes = new byte[music.capture.Length * 2];
                for (int i = 0; i < music.capture.Length; i++)
                {
                    short v16 = (short)(Mathf.Clamp(music.capture[i], -1f, 1f) * 32767f);
                    bytes[i * 2] = (byte)(v16 & 0xff); bytes[i * 2 + 1] = (byte)((v16 >> 8) & 0xff);
                }
                System.IO.File.WriteAllBytes(wavPath, bytes);
                System.IO.File.WriteAllText(wavPath + ".txt", music.SampleRate.ToString());
                wavPath = null; Application.Quit();
            }
        }
        sfx.Update(dt, world, rig, nightLevel);
        fx.Begin(cam);

        if (started && !ended && iconFrames == 0 && iconPath == null) hint = Tools(dt, overUI);

        view.Sync();
        view.Draw();
        sim.Draw(dt);
        fx.Render(dt);
        grade.postExposure.value = 0.25f + nightExposure + fx.flash * 1.6f;
        hud.Update(dt, hint);

        if (shotT > 0f)
        {
            shotT -= dt;
            if (shotT <= 0f) { ScreenCapture.CaptureScreenshot(shotPath); shotT = -100f; }
        }
        else if (shotT < -50f) { shotT += 1f; if (shotT > -90f) Application.Quit(); }
    }

    string Tools(float dt, bool overUI)
    {
        Team me = sim.teams[0];
        Vector3 hit;
        bool has = world.Raycast(cam.ScreenPointToRay(Input.mousePosition), out hit) && world.Inside(hit) && !overUI;
        if (!has) { levelH = -1; return overUI ? "" : "Sea beyond the edge of the world"; }
        string hint;
        if (tool == 0)
        {
            int vx = Mathf.Clamp(Mathf.RoundToInt(hit.x), 1, World.N - 1), vz = Mathf.Clamp(Mathf.RoundToInt(hit.z), 1, World.N - 1);
            int hv = world.h[World.VI(vx, vz)];
            Vector3 vp = new Vector3(vx, 0, vz);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool l = Input.GetMouseButton(0), r = Input.GetMouseButton(1);
            fx.Ring(vp, 0.38f, shift ? new Color(3f, 2.2f, 0.6f, 0.9f) : new Color(1.2f, 2.4f, 4f, 0.9f), 0.05f);
            if (Input.GetMouseButtonDown(0) && shift) levelH = hv;
            if (!l) levelH = -1;
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) repeatT = 0f;
            repeatT -= dt;
            if ((l || r) && repeatT <= 0f)
            {
                repeatT = 0.11f;
                int n = 0; bool up = l;
                if (shift && l && levelH >= 0) { if (hv == levelH) n = -2; else up = hv < levelH; }
                if (n != -2)
                {
                    n = sim.Sculpt(0, vx, vz, up);
                    if (n < 0) { Toast("Not enough mana"); sfx.Deny(); }
                    else if (n > 0)
                    {
                        if (up) sfx.Up(); else sfx.Down();
                        fx.Dust(new Vector3(vx, world.Height(vx, vz), vz), 3);
                    }
                }
            }
            Settlement s = sim.TownNear(hit);
            hint = s != null
                ? (s.team == 0 ? "Your " : "Rival ") + Sim.LevelName[s.level] + "  —  " + Mathf.FloorToInt(s.pop) + " / " + Sim.Cap[s.level] + " people"
                : (shift ? "Level land to height " + (levelH >= 0 ? levelH : hv) : "Height " + hv + (hv == 0 ? "  (sea)" : ""));
        }
        else
        {
            float rad = Sim.PowerRadius[tool];
            bool can = me.mana >= Sim.PowerCost[tool];
            Color col = can ? new Color(3f, 1.4f, 0.4f, 0.9f) : new Color(1f, 0.2f, 0.2f, 0.5f);
            if (rad > 0f) fx.Ring(hit, rad, col, 0.07f);
            hint = Sim.PowerName[tool] + (tool >= 6 ? "  —  click anywhere to unleash" : "  —  click to cast") + (can ? "" : "   (need " + Sim.PowerCost[tool].ToString("0") + " mana)");
            if (Input.GetMouseButtonDown(1)) tool = 0;
            else if (Input.GetMouseButtonDown(0))
            {
                if (sim.Cast(0, tool, hit))
                {
                    if (tool == 6) Toast("The seas rise!");
                    if (tool == 7) Toast("CATACLYSM — every soul marches to the final battle");
                    if (tool != 2) tool = 0;
                }
                else { Toast(can ? "Cannot cast there" : "Not enough mana"); sfx.Deny(); }
            }
        }
        return hint;
    }
}
