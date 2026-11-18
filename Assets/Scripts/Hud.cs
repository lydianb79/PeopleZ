using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MinimapClick : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    public void OnPointerDown(PointerEventData e) { Go(e); }
    public void OnDrag(PointerEventData e) { Go(e); }
    void Go(PointerEventData e)
    {
        var rt = (RectTransform)transform;
        Vector2 lp;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out lp)) return;
        Rect r = rt.rect;
        Game.I.rig.Jump((lp.x - r.xMin) / r.width * World.N, (lp.y - r.yMin) / r.height * World.N);
    }
}

public class Hud
{
    static readonly Color PanelCol = new Color(0.05f, 0.07f, 0.11f, 0.78f), Accent = new Color(0.45f, 0.80f, 1f), Dim = new Color(1, 1, 1, 0.55f),
        Blue = new Color(0.25f, 0.55f, 1f), Red = new Color(1f, 0.30f, 0.25f), Gold = new Color(1f, 0.82f, 0.35f);

    readonly Game g; readonly Sim sim; readonly World w;
    readonly Font font; readonly Sprite round;
    readonly Transform canvas;
    Text popA, popB, manaTxt, info, toast, speedTxt;
    RectTransform balance, manaFill;
    readonly Image[] powerBg = new Image[8]; readonly Text[] powerName = new Text[8], powerCost = new Text[8];
    readonly Image[] modeBg = new Image[3];
    Texture2D mini; Color32[] px = new Color32[World.N * World.N];
    GameObject title, end;
    float toastT, slowT;

    public Hud(Game game)
    {
        g = game; sim = game.sim; w = game.world;
        font = Font.CreateDynamicFontFromOSFont(new[] { "Avenir Next", "Helvetica Neue", "Arial" }, 32);
        round = MakeRound();

        var cgo = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var cv = cgo.GetComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.6f;
        canvas = cgo.transform;
        if (EventSystem.current == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        Build();
    }

    static Sprite MakeRound()
    {
        const int S = 64; const float R = 18f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var p = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = Mathf.Max(0, Mathf.Max(R - x - 0.5f, x + 0.5f - (S - R))), dy = Mathf.Max(0, Mathf.Max(R - y - 0.5f, y + 0.5f - (S - R)));
                float a = Mathf.Clamp01(R - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                p[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(p); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R + 2, R + 2, R + 2, R + 2));
    }

    static RectTransform RT(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    Image Img(RectTransform rt, Color c, bool raycast = true)
    {
        var im = rt.gameObject.AddComponent<Image>();
        im.sprite = round; im.type = Image.Type.Sliced; im.color = c; im.raycastTarget = raycast;
        return im;
    }

    Text Txt(Transform parent, string s, int size, Color c, TextAnchor a = TextAnchor.MiddleCenter, FontStyle fs = FontStyle.Normal)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(14, 6); rt.offsetMax = new Vector2(-14, -6);
        var t = go.AddComponent<Text>();
        t.font = font; t.text = s; t.fontSize = size; t.color = c; t.alignment = a; t.fontStyle = fs; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    Text TxtAt(Transform parent, string s, int size, Color c, TextAnchor a, Vector2 anchorMin, Vector2 anchorMax, FontStyle fs = FontStyle.Normal)
    {
        var t = Txt(parent, s, size, c, a, fs);
        t.rectTransform.anchorMin = anchorMin; t.rectTransform.anchorMax = anchorMax;
        return t;
    }

    void Build()
    {
        // --- top left: balance of power
        var tl = RT("Status", canvas, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24), new Vector2(420, 150));
        Img(tl, PanelCol);
        TxtAt(tl, "PEOPLEZ", 22, Accent, TextAnchor.UpperLeft, new Vector2(0, 0.6f), Vector2.one, FontStyle.Bold);
        speedTxt = TxtAt(tl, "", 16, Dim, TextAnchor.UpperRight, new Vector2(0, 0.6f), Vector2.one);
        popA = TxtAt(tl, "", 18, Blue, TextAnchor.MiddleLeft, new Vector2(0, 0.46f), new Vector2(1, 0.74f), FontStyle.Bold);
        popB = TxtAt(tl, "", 18, Red, TextAnchor.MiddleLeft, new Vector2(0, 0.24f), new Vector2(1, 0.52f), FontStyle.Bold);
        var bar = RT("Bar", tl, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(390, 16));
        Img(bar, Red, false);
        balance = RT("Fill", bar, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        balance.anchorMin = Vector2.zero; balance.anchorMax = new Vector2(0.5f, 1); balance.offsetMin = balance.offsetMax = Vector2.zero;
        Img(balance, Blue, false);

        // --- top centre: mana
        var tc = RT("Mana", canvas, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(560, 58));
        Img(tc, PanelCol);
        var mb = RT("Bar", tc, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(530, 30));
        Img(mb, new Color(1, 1, 1, 0.08f), false);
        manaFill = RT("Fill", mb, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        manaFill.anchorMin = Vector2.zero; manaFill.anchorMax = new Vector2(0.1f, 1); manaFill.offsetMin = manaFill.offsetMax = Vector2.zero;
        Img(manaFill, new Color(0.55f, 0.45f, 1f, 0.95f), false);
        manaTxt = Txt(tc, "", 18, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        var trt = RT("Toast", canvas, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -96), new Vector2(900, 44));
        toast = Txt(trt, "", 24, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
        toast.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.8f);

        // --- top right: help
        var tr = RT("Help", canvas, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -24), new Vector2(330, 172));
        Img(tr, PanelCol);
        Txt(tr, "<b>Left click</b>  raise land\n<b>Right click</b>  lower land\n<b>Shift + drag</b>  level land\n<b>WASD</b> move  ·  <b>Q/E</b> rotate  ·  <b>Wheel</b> zoom\n<b>Space</b> pause  ·  <b>Tab</b> speed\n<b>M</b> music  ·  <b>F</b> fullscreen", 15, Dim, TextAnchor.MiddleLeft).supportRichText = true;

        // --- bottom centre: powers
        float bw = 118, gap = 8, total = 8 * bw + 7 * gap;
        var pb = RT("Powers", canvas, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(total + 24, 108));
        Img(pb, PanelCol);
        for (int i = 0; i < 8; i++)
        {
            int idx = i;
            var b = RT("P" + i, pb, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-total / 2 + bw / 2 + i * (bw + gap), 0), new Vector2(bw, 84));
            powerBg[i] = Img(b, new Color(1, 1, 1, 0.07f));
            var btn = b.gameObject.AddComponent<Button>(); btn.targetGraphic = powerBg[i];
            btn.onClick.AddListener(() => g.SelectTool(idx));
            TxtAt(b, (i + 1).ToString(), 13, Dim, TextAnchor.UpperLeft, new Vector2(0, 0.6f), Vector2.one).rectTransform.offsetMin = new Vector2(10, 0);
            powerName[i] = TxtAt(b, Sim.PowerName[i], 17, Color.white, TextAnchor.MiddleCenter, new Vector2(0, 0.3f), new Vector2(1, 0.85f), FontStyle.Bold);
            powerName[i].rectTransform.offsetMin = powerName[i].rectTransform.offsetMax = Vector2.zero;
            powerCost[i] = TxtAt(b, Sim.PowerCost[i] > 0 ? Sim.PowerCost[i].ToString("0") + " mana" : (i == 0 ? "¼ per step" : "free"), 13, Gold, TextAnchor.LowerCenter, Vector2.zero, new Vector2(1, 0.4f));
            powerCost[i].rectTransform.offsetMin = new Vector2(0, 8); powerCost[i].rectTransform.offsetMax = Vector2.zero;
        }
        var irt = RT("Info", canvas, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 140), new Vector2(1000, 36));
        info = Txt(irt, "", 18, Color.white, TextAnchor.MiddleCenter);
        info.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.9f);

        // --- bottom left: follower orders
        var ml = RT("Modes", canvas, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 24), new Vector2(190, 196));
        Img(ml, PanelCol);
        TxtAt(ml, "FOLLOWERS", 13, Dim, TextAnchor.UpperCenter, new Vector2(0, 0.8f), Vector2.one, FontStyle.Bold);
        string[] mn = { "Settle", "Gather", "Fight" }, mk = { "Z", "X", "C" };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var b = RT("M" + i, ml, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -38 - i * 50), new Vector2(162, 44));
            modeBg[i] = Img(b, new Color(1, 1, 1, 0.07f));
            var btn = b.gameObject.AddComponent<Button>(); btn.targetGraphic = modeBg[i];
            btn.onClick.AddListener(() => g.SetMode(idx));
            Txt(b, mn[i], 18, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Txt(b, mk[i], 13, Dim, TextAnchor.MiddleLeft);
        }

        // --- bottom right: minimap
        var mm = RT("Minimap", canvas, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 24), new Vector2(264, 264));
        Img(mm, PanelCol);
        var mi = RT("Map", mm, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240, 240));
        mini = new Texture2D(World.N, World.N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var raw = mi.gameObject.AddComponent<RawImage>(); raw.texture = mini;
        mi.gameObject.AddComponent<MinimapClick>();

        // --- title
        title = RT("Title", canvas, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 400)).gameObject;
        Img((RectTransform)title.transform, new Color(0.04f, 0.06f, 0.10f, 0.86f));
        TxtAt(title.transform, "PEOPLEZ", 84, Color.white, TextAnchor.MiddleCenter, new Vector2(0, 0.62f), Vector2.one, FontStyle.Bold);
        TxtAt(title.transform, "Shape the land. Guide your people. Outlast the rival god.", 22, Accent, TextAnchor.MiddleCenter, new Vector2(0, 0.48f), new Vector2(1, 0.66f));
        TxtAt(title.transform, "Your followers (blue) settle on flat ground — the flatter the land around a home, the grander it grows\nand the more mana it yields. Spend mana on divine powers to break the red tribe.", 17, Dim, TextAnchor.MiddleCenter, new Vector2(0, 0.2f), new Vector2(1, 0.5f));
        TxtAt(title.transform, "Click to begin", 24, Gold, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(1, 0.22f), FontStyle.Bold);

        // --- end screen
        end = RT("End", canvas, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 300)).gameObject;
        Img((RectTransform)end.transform, new Color(0.04f, 0.06f, 0.10f, 0.9f));
        TxtAt(end.transform, "", 72, Color.white, TextAnchor.MiddleCenter, new Vector2(0, 0.5f), Vector2.one, FontStyle.Bold).name = "Head";
        TxtAt(end.transform, "", 20, Dim, TextAnchor.MiddleCenter, new Vector2(0, 0.34f), new Vector2(1, 0.54f)).name = "Sub";
        var rb = RT("Again", end.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 28), new Vector2(240, 56));
        var rim = Img(rb, new Color(0.25f, 0.55f, 1f, 0.9f));
        var rbtn = rb.gameObject.AddComponent<Button>(); rbtn.targetGraphic = rim;
        rbtn.onClick.AddListener(() => g.Restart());
        Txt(rb, "Play again", 22, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        end.SetActive(false);
    }

    public void Hide() { canvas.gameObject.SetActive(false); }
    public void ShowTitle(bool on) { title.SetActive(on); }
    public void Toast(string s) { toast.text = s; toastT = 4f; }

    public void ShowEnd(bool won)
    {
        end.SetActive(true);
        var head = end.transform.Find("Head").GetComponent<Text>();
        head.text = won ? "VICTORY" : "DEFEAT"; head.color = won ? Gold : Red;
        end.transform.Find("Sub").GetComponent<Text>().text = won ? "The rival tribe is no more. The world is yours." : "Your last follower has fallen.";
    }

    public void Update(float dt, string hint)
    {
        Team a = sim.teams[0], b = sim.teams[1];
        toastT -= dt;
        var tc = toast.color; tc.a = Mathf.Clamp01(toastT); toast.color = tc;
        info.text = hint;
        for (int i = 0; i < 8; i++)
        {
            bool can = a.mana >= Sim.PowerCost[i], sel = g.tool == i;
            powerBg[i].color = sel ? new Color(0.45f, 0.80f, 1f, 0.45f) : new Color(1, 1, 1, can ? 0.09f : 0.03f);
            powerName[i].color = new Color(1, 1, 1, can ? 1f : 0.35f);
            powerCost[i].color = new Color(Gold.r, Gold.g, Gold.b, can ? 1f : 0.4f);
        }
        for (int i = 0; i < 3; i++)
            modeBg[i].color = (sim.cataclysm ? 2 : a.mode) == i ? new Color(0.45f, 0.80f, 1f, 0.45f) : new Color(1, 1, 1, 0.08f);
        manaFill.anchorMax = new Vector2(Mathf.Clamp01(a.mana / Sim.MaxMana), 1);

        slowT -= dt;
        if (slowT > 0f) return;
        slowT = 0.2f;
        popA.text = "You     " + Mathf.RoundToInt(a.pop) + " people  ·  " + a.houses + " homes";
        popB.text = "Rival  " + Mathf.RoundToInt(b.pop) + " people  ·  " + b.houses + " homes";
        float tot = a.pop + b.pop;
        balance.anchorMax = new Vector2(tot > 0 ? Mathf.Clamp(a.pop / tot, 0.02f, 0.98f) : 0.5f, 1);
        manaTxt.text = "MANA  " + Mathf.FloorToInt(a.mana);
        speedTxt.text = g.paused ? "PAUSED" : (g.speed > 1 ? "▶▶ ×" + g.speed : "");
        DrawMinimap();
    }

    void DrawMinimap()
    {
        const int N = World.N;
        for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                int c = z * N + x; int h = w.h[World.VI(x, z)];
                Color32 col;
                if (h == 0) { float d = Mathf.Clamp01(-w.vy[World.VI(x, z)] / 3.2f); col = Color32.Lerp(new Color32(60, 150, 180, 255), new Color32(14, 45, 90, 255), d); }
                else if (w.swamp[c]) col = new Color32(40, 50, 20, 255);
                else if (h >= 8) col = new Color32(235, 238, 245, 255);
                else col = Color32.Lerp(new Color32(70, 130, 50, 255), new Color32(150, 140, 110, 255), (h - 1) / 7f);
                px[c] = col;
            }
        foreach (var s in sim.towns)
        {
            Color32 tcol = s.team == 0 ? new Color32(80, 160, 255, 255) : new Color32(255, 70, 60, 255);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (World.InC(s.cx + dx, s.cz + dz)) px[(s.cz + dz) * N + s.cx + dx] = tcol;
        }
        foreach (var k in sim.walkers)
        {
            int x = (int)k.x, z = (int)k.z;
            if (World.InC(x, z)) px[z * N + x] = k.team == 0 ? new Color32(190, 225, 255, 255) : new Color32(255, 180, 170, 255);
        }
        int fx = Mathf.Clamp((int)g.rig.focus.x, 3, N - 4), fz = Mathf.Clamp((int)g.rig.focus.z, 3, N - 4);
        for (int i = -3; i <= 3; i++)
        {
            if (i == 0) continue;
            px[fz * N + fx + i] = new Color32(255, 255, 255, 255); px[(fz + i) * N + fx] = new Color32(255, 255, 255, 255);
        }
        mini.SetPixels32(px); mini.Apply(false);
    }
}
