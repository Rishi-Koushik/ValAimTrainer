using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AimTrainer
{
    public class Target
    {
        public GameObject go, body;
        public Material mat;
        public Color baseColor;
        public float yaw, pitch, r, dist = 10f, born, life;
        public float vy, vp;          // angular velocity, deg/s
        public Vector4 bounds;        // yawMin, yawMax, pitchMin, pitchMax
        public Vector2 strafe;        // min/max strafe speed; zero means no strafing
        public float switchAt;
        public bool center, bob;
        public float bobPhase;
    }

    public class RunResult { public string id; public int score, best; public float acc; }

    public class Game : MonoBehaviour
    {
        // Valorant turns 0.07° per mouse count × in-game sens.
        const float ValorantYaw = 0.07f;
        // Unity's default Input Manager "Mouse X/Y" sensitivity is 0.1, so axis value = counts × 0.1.
        const float AxisPerCount = 0.1f;
        const float ValorantHFov = 103f;
        const float Floor = -1.7f, Ceil = 16f, RoomW = 80f, ZBack = -20f, ZFront = 42f;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        enum St { Menu, Ready, Countdown, Run, Paused, Results, Summary }
        St st = St.Menu;

        // --- shared with scenarios ---
        [NonSerialized] public float time, yaw, pitch;
        public readonly List<Target> targets = new List<Target>();
        public Vector2 AimPoint => new Vector2(Mathf.DeltaAngle(0, yaw), pitch);

        class Timer { public float at; public Action fn; }
        readonly List<Timer> timers = new List<Timer>();

        Camera cam;
        AudioSource sfx;
        AudioClip hitClip, missClip;
        Material bodyMat;
        Shader litShader;

        List<Scenario> all;
        Scenario S;
        string[] routineIds;
        int routineIdx = -1;
        readonly List<RunResult> routineResults = new List<RunResult>();
        RunResult lastResult;
        bool lastWasBest;
        int lastPrevBest;

        int shots, hits, misses, expired;
        float pts, held, onT, lastHit, cd, hitmarkUntil;
        bool mouseDown;
        readonly List<float> ttks = new List<float>();

        // --- settings ---
        float sens, vol;
        int dpi, xhColor, tgColor, xhLen, xhGap, xhThick;
        bool xhDot;
        string sensText, dpiText;

        static readonly (string n, string hex)[] XhColors =
            { ("Green", "#00ff6a"), ("Cyan", "#00e5ff"), ("Yellow", "#f5f542"), ("Red", "#ff4655"), ("Pink", "#ff5cf0"), ("White", "#ffffff") };
        static readonly (string n, string hex)[] TgColors =
            { ("Cyan", "#3ad7ff"), ("Red", "#ff4655"), ("Yellow", "#ffd23a"), ("Green", "#3ddc97"), ("Pink", "#ff6ad5"), ("White", "#f0f0f0") };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate()
        {
            if (FindAnyObjectByType<Game>() == null) new GameObject("AimTrainer").AddComponent<Game>();
        }

        // ================= setup =================
        void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            QualitySettings.antiAliasing = 4;
            LoadSettings();
            all = Scenario.All();
            // Everything is built in code, so make sure the shader exists in builds (missing shader = pink).
            litShader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");

            cam = Camera.main;
            if (cam == null)
            {
                var c = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = c.AddComponent<Camera>();
            }
            if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            cam.transform.position = Vector3.zero;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex("#0e1116");
            cam.nearClipPlane = .05f;
            cam.farClipPlane = 300f;

            sfx = cam.gameObject.AddComponent<AudioSource>();
            hitClip = Tone("hit", 1500, 600, .07f, true);
            missClip = Tone("miss", 240, 140, .09f, false);

            BuildRoom();
            InitTextures();
        }

        void BuildRoom()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.42f, .45f, .5f);
            var lightGo = new GameObject("Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = .8f;
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);

            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            Color bg = Hex("#1b1f26"), edge = Hex("#2c323c"), mid = Hex("#22272f");
            var px = new Color[256 * 256];
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                    px[y * 256 + x] = (x < 2 || y < 2 || x > 253 || y > 253) ? edge : (x == 128 || y == 128) ? mid : bg;
            tex.SetPixels(px);
            tex.Apply(true);

            float h = Ceil - Floor, cy = (Ceil + Floor) / 2, dz = ZFront - ZBack, cz = (ZFront + ZBack) / 2;
            Slab(new Vector3(0, Floor - .05f, cz), new Vector3(RoomW, .1f, dz), new Vector2(RoomW / 4, dz / 4), tex, .62f);
            Slab(new Vector3(0, Ceil + .05f, cz), new Vector3(RoomW, .1f, dz), new Vector2(RoomW / 4, dz / 4), tex, .45f);
            Slab(new Vector3(0, cy, ZFront + .05f), new Vector3(RoomW, h, .1f), new Vector2(RoomW / 4, h / 4), tex, 1f);
            Slab(new Vector3(0, cy, ZBack - .05f), new Vector3(RoomW, h, .1f), new Vector2(RoomW / 4, h / 4), tex, 1f);
            Slab(new Vector3(-RoomW / 2 - .05f, cy, cz), new Vector3(.1f, h, dz), new Vector2(dz / 4, h / 4), tex, 1f);
            Slab(new Vector3(RoomW / 2 + .05f, cy, cz), new Vector3(.1f, h, dz), new Vector2(dz / 4, h / 4), tex, 1f);

            bodyMat = new Material(litShader) { color = Hex("#3a3f49") };
        }

        void Slab(Vector3 pos, Vector3 scale, Vector2 tiling, Texture2D tex, float tint)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            w.name = "Wall";
            w.transform.position = pos;
            w.transform.localScale = scale;
            var m = new Material(litShader);
            w.GetComponent<Renderer>().sharedMaterial = m;
            m.mainTexture = tex;
            m.mainTextureScale = tiling;
            m.color = new Color(tint, tint, tint);
            m.SetFloat("_Glossiness", .05f);
        }

        AudioClip Tone(string clipName, float f0, float f1, float len, bool tri)
        {
            const int sr = 44100;
            int n = (int)(sr * len);
            var d = new float[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float f = f0 * Mathf.Pow(f1 / f0, t / len);
                ph += 2 * Math.PI * f / sr;
                float s = tri ? (float)(2 / Math.PI * Math.Asin(Math.Sin(ph))) : (float)Math.Sin(ph);
                d[i] = s * Mathf.Exp(-t * 45f);
            }
            var clip = AudioClip.Create(clipName, n, 1, sr, false);
            clip.SetData(d, 0);
            return clip;
        }

        void Play(AudioClip c, float v) { if (vol > 0) sfx.PlayOneShot(c, vol * v); }

        // ================= targets (API for scenarios) =================
        public Target Spawn(float y, float p, float r, float dist)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Target";
            go.transform.localScale = Vector3.one * (r * 2);
            var rend = go.GetComponent<Renderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var col = Hex(TgColors[tgColor].hex);
            var mat = new Material(litShader);
            rend.sharedMaterial = mat;
            mat.color = col;
            mat.SetFloat("_Glossiness", .65f);
            var t = new Target { go = go, mat = mat, baseColor = col, yaw = y, pitch = p, r = r, dist = dist, born = time };
            Place(t);
            targets.Add(t);
            return t;
        }

        // Decorative torso under a head target; shooting it counts as a miss.
        public void AddBody(Target t)
        {
            var head = t.go.transform.position;
            float top = head.y - t.r * 1.3f, hgt = top - Floor;
            var b = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            b.name = "Body";
            b.transform.localScale = new Vector3(.65f, hgt / 2, .65f);
            b.transform.position = new Vector3(head.x, Floor + hgt / 2, head.z);
            b.GetComponent<Renderer>().sharedMaterial = bodyMat;
            t.body = b;
        }

        public void Later(float sec, Action fn) => timers.Add(new Timer { at = time + sec, fn = fn });

        public List<Vector2> TargetPoints() => targets.Select(t => new Vector2(t.yaw, t.pitch)).ToList();

        public Vector2 RandPos(Vector4 R, List<Vector2> avoid, float minSep)
        {
            for (int i = 0; i < 50; i++)
            {
                var p = new Vector2(Random.Range(R.x, R.y), Random.Range(R.z, R.w));
                if (avoid == null || avoid.All(a => Vector2.Distance(a, p) >= minSep)) return p;
            }
            return new Vector2(Random.Range(R.x, R.y), Random.Range(R.z, R.w));
        }

        void Place(Target t) =>
            t.go.transform.position = Quaternion.Euler(-t.pitch, t.yaw, 0) * Vector3.forward * t.dist;

        void Kill(Target t)
        {
            t.go.SetActive(false); // drop the collider right away, Destroy is deferred
            Destroy(t.go);
            Destroy(t.mat);
            if (t.body) Destroy(t.body);
            targets.Remove(t);
        }

        void ClearTargets()
        {
            foreach (var t in targets.ToArray()) Kill(t);
            timers.Clear();
        }

        // ================= run lifecycle =================
        void Prepare(Scenario s)
        {
            S = s;
            ClearTargets();
            time = yaw = pitch = 0;
            shots = hits = misses = expired = 0;
            pts = held = onT = lastHit = 0;
            ttks.Clear();
            st = St.Ready;
        }

        void Begin()
        {
            S.Begin(this);
            cd = 3f;
            st = St.Countdown;
        }

        void Pause()
        {
            if (st != St.Run && st != St.Countdown) return;
            st = St.Paused;
            mouseDown = false;
        }

        void Finish()
        {
            var r = Stats();
            int prev = PlayerPrefs.GetInt("vat.best." + S.id, 0);
            lastWasBest = r.score > prev;
            lastPrevBest = prev;
            if (lastWasBest) { PlayerPrefs.SetInt("vat.best." + S.id, r.score); PlayerPrefs.Save(); }
            lastResult = new RunResult { id = S.id, score = r.score, acc = r.acc, best = Mathf.Max(prev, r.score) };
            if (routineIds != null) routineResults.Add(lastResult);
            ClearTargets();
            st = St.Results;
        }

        void Next()
        {
            if (routineIds == null) { Prepare(S); return; }
            if (routineIdx >= routineIds.Length - 1) { st = St.Summary; return; }
            routineIdx++;
            Prepare(Find(routineIds[routineIdx]));
        }

        void Retry()
        {
            if (routineIds != null && st == St.Results && routineResults.Count > 0) routineResults.RemoveAt(routineResults.Count - 1);
            Prepare(S);
        }

        void StartRoutine()
        {
            routineIds = Scenario.Routine;
            routineIdx = 0;
            routineResults.Clear();
            Prepare(Find(routineIds[0]));
        }

        void StartFree(Scenario s) { routineIds = null; Prepare(s); }

        void ToMenu()
        {
            ClearTargets();
            routineIds = null;
            S = null;
            st = St.Menu;
        }

        Scenario Find(string id) => all.First(s => s.id == id);

        (int score, float acc, float ttk) Stats()
        {
            if (S.track) { float a = onT / S.dur; return (Mathf.RoundToInt(a * 10000), a, 0); }
            int att = shots + expired;
            float acc = att > 0 ? hits / (float)att : 0;
            float ttk = ttks.Count > 0 ? ttks.Average() : 0;
            return (Mathf.RoundToInt(pts * acc), acc, ttk);
        }

        // ================= frame =================
        void Update()
        {
            cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(ValorantHFov, cam.aspect);

            bool playing = st == St.Countdown || st == St.Run;
            if (playing)
            {
                float k = sens * ValorantYaw / AxisPerCount;
                yaw += Input.GetAxisRaw("Mouse X") * k;
                pitch = Mathf.Clamp(pitch + Input.GetAxisRaw("Mouse Y") * k, -89f, 89f);
                if (Input.GetKeyDown(KeyCode.Escape)) Pause();
            }
            cam.transform.rotation = Quaternion.Euler(-pitch, yaw, 0);

            switch (st)
            {
                case St.Ready:
                    if (Input.GetMouseButtonDown(0)) Begin();
                    else if (Input.GetKeyDown(KeyCode.Escape)) ToMenu();
                    break;
                case St.Countdown:
                    cd -= Time.deltaTime;
                    if (cd <= 0) st = St.Run;
                    break;
                case St.Run:
                    mouseDown = Input.GetMouseButton(0);
                    if (Input.GetMouseButtonDown(0) && !S.track) Shoot();
                    time += Time.deltaTime;
                    Tick(Time.deltaTime);
                    if (time >= S.dur) Finish();
                    break;
                case St.Paused:
                    if (Input.GetKeyDown(KeyCode.R)) Retry();
                    break;
                case St.Results:
                    if (Input.GetKeyDown(KeyCode.Space)) Next();
                    else if (Input.GetKeyDown(KeyCode.R)) Retry();
                    break;
            }

            bool lockIt = st == St.Countdown || st == St.Run;
            Cursor.lockState = lockIt ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockIt;
        }

        void OnApplicationFocus(bool focus) { if (!focus && !autoShot) Pause(); }

        // Self-check: "VAL Aim Trainer.exe -autoshot <path prefix>" saves a menu and an in-drill screenshot, then quits.
        bool autoShot;
        void Start()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-autoshot");
            if (i >= 0 && i + 1 < args.Length) StartCoroutine(AutoShot(args[i + 1]));
        }

        System.Collections.IEnumerator AutoShot(string prefix)
        {
            autoShot = true;
            Application.runInBackground = true;
            yield return new WaitForSeconds(1.5f);
            ScreenCapture.CaptureScreenshot(prefix + "-menu.png");
            yield return new WaitForSeconds(.5f);
            StartFree(Find("headline"));
            Begin();
            st = St.Run;
            if (targets.Count > 0) { yaw = targets[0].yaw - 6; pitch = targets[0].pitch; }
            yield return new WaitForSeconds(.4f);
            ScreenCapture.CaptureScreenshot(prefix + "-game.png");
            yield return new WaitForSeconds(.5f);
            Application.Quit();
        }

        void Shoot()
        {
            shots++;
            Physics.SyncTransforms();
            Target t = null;
            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 500f))
                t = targets.Find(x => x.go == hit.collider.gameObject);
            if (t == null) { misses++; Play(missClip, .35f); return; }

            float ttk = time - Mathf.Max(t.born, lastHit);
            ttks.Add(ttk);
            lastHit = time;
            hits++;
            pts += S.speed ? 100 + Mathf.Clamp(150 * (1 - ttk), 0, 150) : 100;
            Kill(t);
            Play(hitClip, .6f);
            hitmarkUntil = Time.unscaledTime + .07f;
            S.OnHit(this, t);
        }

        void Tick(float dt)
        {
            foreach (var tm in timers.Where(x => time >= x.at).ToList()) { timers.Remove(tm); tm.fn(); }

            foreach (var t in targets.ToArray())
            {
                if (t.life > 0 && time - t.born > t.life) { expired++; Kill(t); S.OnExpire(this, t); continue; }
                if (t.strafe.y > 0 && time >= t.switchAt)
                {
                    t.vy = -Mathf.Sign(t.vy) * Random.Range(t.strafe.x, t.strafe.y);
                    t.switchAt = time + Random.Range(.25f, .9f);
                }
                if (t.vy == 0 && t.vp == 0) continue;
                t.yaw += t.vy * dt;
                t.pitch += t.vp * dt;
                if (t.bob) t.pitch = 3 + Mathf.Sin(time * 1.3f + t.bobPhase) * 2.2f;
                var B = t.bounds;
                if (t.yaw < B.x || t.yaw > B.y) { t.yaw = Mathf.Clamp(t.yaw, B.x, B.y); t.vy = -t.vy; }
                if (t.pitch < B.z || t.pitch > B.w) { t.pitch = Mathf.Clamp(t.pitch, B.z, B.w); t.vp = -t.vp; }
                Place(t);
            }

            S.Tick(this, dt);

            if (S.track && targets.Count > 0)
            {
                var t = targets[0];
                bool on = false;
                if (mouseDown)
                {
                    held += dt;
                    Physics.SyncTransforms();
                    on = Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 500f) && hit.collider.gameObject == t.go;
                    if (on) onT += dt;
                }
                t.mat.color = on ? Color.Lerp(t.baseColor, Color.white, .55f) : t.baseColor;
            }
        }

        // ================= settings =================
        void LoadSettings()
        {
            sens = PlayerPrefs.GetFloat("vat.sens", .35f);
            dpi = PlayerPrefs.GetInt("vat.dpi", 800);
            xhColor = PlayerPrefs.GetInt("vat.xhColor", 0);
            tgColor = PlayerPrefs.GetInt("vat.tgColor", 0);
            xhLen = PlayerPrefs.GetInt("vat.xhLen", 4);
            xhGap = PlayerPrefs.GetInt("vat.xhGap", 2);
            xhThick = PlayerPrefs.GetInt("vat.xhThick", 2);
            xhDot = PlayerPrefs.GetInt("vat.xhDot", 1) == 1;
            vol = PlayerPrefs.GetFloat("vat.vol", .5f);
            sensText = sens.ToString("0.000", Inv);
            dpiText = dpi.ToString(Inv);
        }

        void SaveSettings()
        {
            PlayerPrefs.SetFloat("vat.sens", sens);
            PlayerPrefs.SetInt("vat.dpi", dpi);
            PlayerPrefs.SetInt("vat.xhColor", xhColor);
            PlayerPrefs.SetInt("vat.tgColor", tgColor);
            PlayerPrefs.SetInt("vat.xhLen", xhLen);
            PlayerPrefs.SetInt("vat.xhGap", xhGap);
            PlayerPrefs.SetInt("vat.xhThick", xhThick);
            PlayerPrefs.SetInt("vat.xhDot", xhDot ? 1 : 0);
            PlayerPrefs.SetFloat("vat.vol", vol);
            PlayerPrefs.Save();
        }

        // ================= GUI =================
        static readonly Color cText = new Color32(232, 236, 241, 255), cMuted = new Color32(138, 148, 163, 255), cAccent = new Color32(255, 70, 85, 255);
        Texture2D texWhite, texBg, texPanel, texPanel2, texBtn, texBtnHover, texAccent, texAccentHover, texField;
        GUIStyle sText, sMuted, sLabel, sTitle, sH, sBig, sNum, sNumK, sPanel, sCell, sBtn, sBtnP, sCard, sField, sCenter, sCd;
        float styleK = -1;
        Vector2 menuScroll;

        float K => Screen.height / 1080f;
        int F(float s) => Mathf.Max(8, Mathf.RoundToInt(s * K));
        float Px(float v) => v * K;

        void InitTextures()
        {
            texWhite = Solid(Color.white);
            texBg = Solid(Hex("#0b0d10"));
            texPanel = Solid(Hex("#14171c"));
            texPanel2 = Solid(Hex("#0f1216"));
            texBtn = Solid(Hex("#1f242c"));
            texBtnHover = Solid(Hex("#2a313b"));
            texAccent = Solid(cAccent);
            texAccentHover = Solid(Hex("#ff6370"));
            texField = Solid(Hex("#0f1216"));
        }

        void EnsureStyles()
        {
            if (sText != null && Mathf.Approximately(styleK, K)) return;
            styleK = K;
            int pad = Mathf.RoundToInt(Px(22));

            sText = new GUIStyle(GUI.skin.label) { fontSize = F(18), wordWrap = true, richText = true };
            sText.normal.textColor = cText;
            sMuted = new GUIStyle(sText) { fontSize = F(15) };
            sMuted.normal.textColor = cMuted;
            sLabel = new GUIStyle(sMuted) { fontSize = F(13), fontStyle = FontStyle.Bold, wordWrap = false };
            sTitle = new GUIStyle(sText) { fontSize = F(46), fontStyle = FontStyle.Bold, wordWrap = false };
            sH = new GUIStyle(sMuted) { fontSize = F(17), fontStyle = FontStyle.Bold };
            sBig = new GUIStyle(sText) { fontSize = F(76), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            sCd = new GUIStyle(sText) { fontSize = F(140), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            sCenter = new GUIStyle(sText) { alignment = TextAnchor.MiddleCenter };
            sNum = new GUIStyle(sText) { fontSize = F(24), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            sNumK = new GUIStyle(sMuted) { fontSize = F(12), alignment = TextAnchor.MiddleCenter, wordWrap = false };

            sPanel = new GUIStyle { padding = new RectOffset(pad, pad, pad, pad) };
            sPanel.normal.background = texPanel;
            sCell = new GUIStyle { padding = new RectOffset(8, 8, Mathf.RoundToInt(Px(10)), Mathf.RoundToInt(Px(10))), margin = new RectOffset(4, 4, 4, 4) };
            sCell.normal.background = texPanel2;

            sBtn = new GUIStyle(GUI.skin.button)
            {
                fontSize = F(17), alignment = TextAnchor.MiddleCenter, border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(Mathf.RoundToInt(Px(18)), Mathf.RoundToInt(Px(18)), Mathf.RoundToInt(Px(11)), Mathf.RoundToInt(Px(11)))
            };
            SetBg(sBtn, texBtn, texBtnHover, cText);
            sBtnP = new GUIStyle(sBtn) { fontStyle = FontStyle.Bold };
            SetBg(sBtnP, texAccent, texAccentHover, Color.white);
            sCard = new GUIStyle(sBtn)
            {
                alignment = TextAnchor.UpperLeft, wordWrap = true, richText = true, fontSize = F(15),
                padding = new RectOffset(Mathf.RoundToInt(Px(14)), Mathf.RoundToInt(Px(14)), Mathf.RoundToInt(Px(12)), Mathf.RoundToInt(Px(12)))
            };
            SetBg(sCard, texPanel, texBtnHover, cText);

            sField = new GUIStyle(GUI.skin.textField) { fontSize = F(19), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            SetBg(sField, texField, texField, cText);
            sField.focused.background = texField;
            sField.focused.textColor = cText;
        }

        static void SetBg(GUIStyle s, Texture2D n, Texture2D h, Color text)
        {
            s.normal.background = n; s.hover.background = h; s.active.background = h; s.onNormal.background = n;
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.onNormal.textColor = text;
        }

        void OnGUI()
        {
            EnsureStyles();
            switch (st)
            {
                case St.Menu: DrawMenu(); break;
                case St.Ready: DrawReady(); break;
                case St.Countdown:
                case St.Run:
                    DrawHud();
                    DrawCrosshair();
                    if (st == St.Countdown) GUI.Label(new Rect(0, 0, Screen.width, Screen.height), Mathf.CeilToInt(cd).ToString(), sCd);
                    break;
                case St.Paused: DrawPause(); break;
                case St.Results: DrawResults(); break;
                case St.Summary: DrawSummary(); break;
            }
        }

        void Dim(float a)
        {
            GUI.color = new Color(0, 0, 0, a);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), texWhite);
            GUI.color = Color.white;
        }

        Rect CenterRect(float w, float h)
        {
            w = Mathf.Min(Px(w), Screen.width - 32);
            h = Mathf.Min(Px(h), Screen.height - 32);
            return new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
        }

        string Tag(string t) => $"<size={F(12)}><color=#ff8a94>  {t.ToUpperInvariant()}</color></size>";
        string StepText => routineIds != null ? $"Routine · drill {routineIdx + 1} of {routineIds.Length}" : "Free play";
        int Best(string id) => PlayerPrefs.GetInt("vat.best." + id, 0);

        void DrawMenu()
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), texBg);
            float pad = Px(40);
            var area = new Rect(pad, pad * .7f, Screen.width - pad * 2, Screen.height - pad * 1.4f);
            GUILayout.BeginArea(area);
            menuScroll = GUILayout.BeginScrollView(menuScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);

            GUILayout.Label("VAL <color=#ff4655>AIM</color> TRAINER", sTitle);
            GUILayout.Label("Small-target click drills tuned for Valorant: 103° FOV, Valorant sensitivity, and tap-fire aim.", sMuted);
            GUILayout.Space(Px(18));

            float colW = area.width - Px(30);
            float leftW = colW * .57f, rightW = colW - leftW - Px(16);
            GUILayout.BeginHorizontal();

            // routine
            GUILayout.BeginVertical(sPanel, GUILayout.Width(leftW));
            int total = Scenario.Routine.Sum(id => Mathf.RoundToInt(Find(id).dur));
            GUILayout.Label($"DAILY VALORANT ROUTINE  ·  {Mathf.RoundToInt(total / 60f)} MIN OF PLAY", sH);
            GUILayout.Space(Px(6));
            for (int i = 0; i < Scenario.Routine.Length; i++)
            {
                var s = Find(Scenario.Routine[i]);
                int best = Best(s.id);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color=#8a94a3>{i + 1}.</color>  <b>{s.name}</b>{Tag(s.tag)}\n<size={F(14)}><color=#8a94a3>{s.desc}</color></size>",
                    sText, GUILayout.Width(leftW - Px(150)));
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{s.dur:0}s" + (best > 0 ? $"\n<color=#ffc857>{best:N0}</color>" : ""), sMuted, GUILayout.Width(Px(80)));
                GUILayout.EndHorizontal();
                GUILayout.Space(Px(4));
            }
            GUILayout.Space(Px(10));
            if (GUILayout.Button("Start routine", sBtnP, GUILayout.Width(Px(220)))) StartRoutine();
            GUILayout.Space(Px(8));
            GUILayout.Label("Keep accuracy at about 85% or higher before you push speed. Run the routine once before you play.", sMuted);
            GUILayout.EndVertical();

            GUILayout.Space(Px(16));
            DrawSettings(rightW);
            GUILayout.EndHorizontal();

            // free play
            GUILayout.Space(Px(26));
            GUILayout.Label("FREE PLAY", sH);
            const int cols = 4;
            float cardW = (colW - Px(12) * (cols - 1)) / cols;
            for (int i = 0; i < all.Count; i += cols)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + cols, all.Count); j++)
                {
                    var s = all[j];
                    int best = Best(s.id);
                    string txt = $"<b><size={F(17)}>{s.name}</size></b>{Tag(s.tag)}\n<color=#8a94a3>{s.desc}</color>\n<color=#ffc857>{(best > 0 ? "Best " + best.ToString("N0") : "No score yet")} · {s.dur:0}s</color>";
                    if (GUILayout.Button(txt, sCard, GUILayout.Width(cardW), GUILayout.Height(Px(150)))) StartFree(s);
                    if (j < i + cols - 1) GUILayout.Space(Px(12) - 8);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(Px(8));
            }

            // notes
            GUILayout.Space(Px(18));
            GUILayout.BeginVertical(sPanel);
            GUILayout.Label("WHY THESE DRILLS", sH);
            GUILayout.Label(
                "• <b>Crosshair placement first.</b> Most Valorant kills are first-bullet headshots, so the drills use small heads and tap targets instead of spray.\n" +
                "• <b>Spidershot + Microshot</b> are the core pair most routines recommend: wide flicks back to center, then tiny micro-adjustments.\n" +
                "• <b>Sixshot / Gridshot</b> train fast target switching. Accuracy matters more than raw speed.\n" +
                "• <b>Frenzy / Frenzy Flick</b>: lots of very small balls. Frenzy works on fast clicks close together, Frenzy Flick on long flicks to tiny targets.\n" +
                "• <b>Reflexshot & Headshot Line</b> copy peeks: a head appears at crosshair height for a short moment, at Valorant distances.\n" +
                "• <b>Motionshot, Strafeshot, Strafetrack</b> cover enemies who ADAD-strafe or run. Tracking gets a short slot at the end.\n\n" +
                "<color=#8a94a3>Controls: Click to shoot · Esc pause (R restarts while paused) · Space next drill · R retry on results.</color>", sText);
            GUILayout.EndVertical();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawSettings(float w)
        {
            GUILayout.BeginVertical(sPanel, GUILayout.Width(w));
            GUILayout.Label("SETTINGS", sH);
            GUI.changed = false;

            // Sens: slider + text box with 3 decimals, like Valorant
            GUILayout.Space(Px(6));
            GUILayout.Label("VALORANT SENS", sLabel);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Space(Px(14));
            float ns = GUILayout.HorizontalSlider(sens, .05f, 2f);
            GUILayout.EndVertical();
            if (!Mathf.Approximately(ns, sens)) { sens = (float)Math.Round(ns, 3); sensText = sens.ToString("0.000", Inv); }
            GUI.SetNextControlName("sens");
            string t = GUILayout.TextField(sensText, 6, sField, GUILayout.Width(Px(110)), GUILayout.Height(Px(40)));
            if (t != sensText)
            {
                sensText = t;
                if (float.TryParse(t.Replace(',', '.'), NumberStyles.Float, Inv, out var v) && v > 0 && v <= 10) sens = (float)Math.Round(v, 3);
            }
            if (GUI.GetNameOfFocusedControl() != "sens") sensText = sens.ToString("0.000", Inv);
            GUILayout.EndHorizontal();

            GUILayout.Label("Uses your mouse's current DPI automatically, just like Valorant. Only the sens changes your aim.", sMuted);

            // DPI never affects aim (raw counts × sens); it only feeds the eDPI and cm/360 readout.
            GUILayout.Space(Px(6));
            GUILayout.BeginVertical(sCell);
            GUILayout.BeginHorizontal();
            GUILayout.Label("DPI for the readout (optional)", sMuted);
            GUI.SetNextControlName("dpi");
            string dt = GUILayout.TextField(dpiText, 6, sField, GUILayout.Width(Px(90)), GUILayout.Height(Px(32)));
            GUILayout.EndHorizontal();
            if (dt != dpiText) { dpiText = dt; if (int.TryParse(dt, NumberStyles.Integer, Inv, out var d) && d >= 100) dpi = d; }
            if (GUI.GetNameOfFocusedControl() != "dpi") dpiText = dpi.ToString(Inv);
            float cm = sens > 0 && dpi > 0 ? 360f / (sens * ValorantYaw * dpi) * 2.54f : 0;
            GUILayout.Label($"eDPI <b>{Mathf.RoundToInt(sens * dpi)}</b>  ·  <b>{cm:0.0} cm</b>/360°  ·  FOV <b>103°</b>", sMuted);
            GUILayout.EndVertical();

            GUILayout.Space(Px(6));
            xhColor = Cycler("CROSSHAIR COLOR", XhColors, xhColor);
            tgColor = Cycler("TARGET COLOR", TgColors, tgColor);
            xhLen = IntSlider("LINE LENGTH", xhLen, 0, 12);
            xhGap = IntSlider("GAP", xhGap, 0, 10);
            xhThick = IntSlider("THICKNESS", xhThick, 1, 4);
            GUILayout.Space(Px(4));
            if (GUILayout.Button("Center dot: " + (xhDot ? "On" : "Off"), sBtn)) xhDot = !xhDot;
            GUILayout.Label($"VOLUME {Mathf.RoundToInt(vol * 100)}%", sLabel);
            vol = GUILayout.HorizontalSlider(vol, 0, 1);

            // crosshair preview
            GUILayout.Space(Px(10));
            var pr = GUILayoutUtility.GetRect(w - Px(44), Px(70));
            GUI.DrawTexture(pr, texPanel2);
            DrawCrosshairAt(pr.center);

            GUILayout.Space(Px(8));
            GUILayout.Label("Turn off Windows \"Enhance pointer precision\". Leave Unity's Input Manager Mouse X/Y sensitivity at the default 0.1.", sMuted);
            if (GUI.changed) SaveSettings();
            GUILayout.EndVertical();
        }

        int Cycler(string label, (string n, string hex)[] opts, int idx)
        {
            GUILayout.Label(label, sLabel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", sBtn, GUILayout.Width(Px(44)))) idx = (idx + opts.Length - 1) % opts.Length;
            var r = GUILayoutUtility.GetRect(Px(40), Px(42), GUILayout.ExpandWidth(true));
            GUI.color = Hex(opts[idx].hex);
            GUI.DrawTexture(new Rect(r.x + 6, r.y + 6, Px(28), r.height - 12), texWhite);
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x + Px(28) + 16, r.y, r.width, r.height), opts[idx].n, new GUIStyle(sText) { alignment = TextAnchor.MiddleLeft });
            if (GUILayout.Button(">", sBtn, GUILayout.Width(Px(44)))) idx = (idx + 1) % opts.Length;
            GUILayout.EndHorizontal();
            return idx;
        }

        int IntSlider(string label, int v, int min, int max)
        {
            GUILayout.Label($"{label} {v}", sLabel);
            return Mathf.RoundToInt(GUILayout.HorizontalSlider(v, min, max));
        }

        void DrawCrosshair() => DrawCrosshairAt(new Vector2(Screen.width / 2f, Screen.height / 2f));

        void DrawCrosshairAt(Vector2 c)
        {
            float T = xhThick, L = xhLen, G = xhGap;
            float cx = Mathf.Floor(c.x), cy = Mathf.Floor(c.y), h = T / 2f;
            var rects = new List<Rect>();
            if (xhDot) rects.Add(new Rect(cx - h, cy - h, T, T));
            if (L > 0)
            {
                rects.Add(new Rect(cx - h, cy - h - G - L, T, L));
                rects.Add(new Rect(cx - h, cy + h + G, T, L));
                rects.Add(new Rect(cx - h - G - L, cy - h, L, T));
                rects.Add(new Rect(cx + h + G, cy - h, L, T));
            }
            GUI.color = new Color(0, 0, 0, .6f);
            foreach (var r in rects) GUI.DrawTexture(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), texWhite);
            GUI.color = Hex(XhColors[xhColor].hex);
            foreach (var r in rects) GUI.DrawTexture(r, texWhite);
            GUI.color = Color.white;

            if (st == St.Run && Time.unscaledTime < hitmarkUntil)
            {
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(45, c);
                GUI.DrawTexture(new Rect(cx - 1, cy - 11, 2, 22), texWhite);
                GUI.DrawTexture(new Rect(cx - 11, cy - 1, 22, 2), texWhite);
                GUI.matrix = m;
            }
        }

        void DrawHud()
        {
            var r = Stats();
            float bw = Px(130), bh = Px(64), gap = Px(24), y = Px(16);
            float x0 = Screen.width / 2f - bw * 1.5f - gap;
            string acc;
            if (S.track) acc = held > 0 ? (onT / held * 100).ToString("0") + "%" : "—";
            else acc = shots + expired > 0 ? (r.acc * 100).ToString("0") + "%" : "—";
            HudBox(new Rect(x0, y, bw, bh), "SCORE", r.score.ToString("N0"), cText);
            HudBox(new Rect(x0 + bw + gap, y, bw, bh), "TIME", Mathf.Max(0, Mathf.CeilToInt(S.dur - time)).ToString(), cAccent);
            HudBox(new Rect(x0 + (bw + gap) * 2, y, bw, bh), S.track ? "ON TARGET" : "ACCURACY", acc, cText);
            GUI.Label(new Rect(Px(16), Screen.height - Px(40), Px(600), Px(30)), S.name, sMuted);
        }

        void HudBox(Rect r, string k, string v, Color vc)
        {
            GUI.color = new Color(1, 1, 1, .85f);
            GUI.DrawTexture(r, texPanel2);
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x, r.y + Px(6), r.width, Px(16)), k, sNumK);
            var st2 = new GUIStyle(sNum) { fontSize = F(26) };
            st2.normal.textColor = vc;
            GUI.Label(new Rect(r.x, r.y + Px(22), r.width, r.height - Px(24)), v, st2);
        }

        void DrawReady()
        {
            Dim(.55f);
            GUILayout.BeginArea(CenterRect(620, 330), sPanel);
            GUILayout.Label(StepText, sMuted);
            GUILayout.Label($"<size={F(32)}><b>{S.name}</b></size>", sText);
            GUILayout.Space(Px(6));
            GUILayout.Label(S.desc, sMuted);
            GUILayout.FlexibleSpace();
            GUILayout.Label("<b>Click anywhere to start</b>", sCenter);
            GUILayout.Label("Esc: back to menu", new GUIStyle(sMuted) { alignment = TextAnchor.MiddleCenter });
            GUILayout.EndArea();
        }

        void DrawPause()
        {
            Dim(.7f);
            GUILayout.BeginArea(CenterRect(560, 220), sPanel);
            GUILayout.Label($"<size={F(30)}><b>Paused</b></size>", sCenter);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Resume", sBtnP)) { cd = 1f; st = St.Countdown; }
            if (GUILayout.Button("Restart drill", sBtn)) Retry();
            if (GUILayout.Button("Quit to menu", sBtn)) ToMenu();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawResults()
        {
            Dim(.75f);
            var r = lastResult;
            GUILayout.BeginArea(CenterRect(640, 560), sPanel);
            GUILayout.Label(StepText, sMuted);
            GUILayout.Label($"<size={F(30)}><b>{S.name}</b></size>", sText);
            GUILayout.Label(r.score.ToString("N0"), sBig);
            GUILayout.Label(lastWasBest ? "<color=#3ddc97><b>NEW PERSONAL BEST</b></color>" : $"Best: {lastPrevBest:N0}", sMuted);
            GUILayout.Space(Px(10));

            if (S.track)
            {
                GUILayout.BeginHorizontal();
                Cell("ON TARGET", (r.acc * 100).ToString("0.0") + "%");
                Cell("TIME ON", onT.ToString("0.0") + "s");
                Cell("HELD", held.ToString("0.0") + "s");
                GUILayout.EndHorizontal();
            }
            else
            {
                float ttk = ttks.Count > 0 ? ttks.Average() : 0;
                GUILayout.BeginHorizontal();
                Cell("ACCURACY", (r.acc * 100).ToString("0.0") + "%");
                Cell("KILLS", hits.ToString());
                Cell("KILLS / SEC", (hits / S.dur).ToString("0.00"));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                Cell("AVG KILL TIME", ttk > 0 ? Mathf.RoundToInt(ttk * 1000) + "ms" : "—");
                Cell("MISSES", misses.ToString());
                Cell("TIMED OUT", expired.ToString());
                GUILayout.EndHorizontal();
            }

            if (routineIds != null)
            {
                GUILayout.Space(Px(10));
                var bar = GUILayoutUtility.GetRect(10, Px(5), GUILayout.ExpandWidth(true));
                GUI.DrawTexture(bar, texBtn);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * (routineIdx + 1) / routineIds.Length, bar.height), texAccent);
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            string next = routineIds == null ? "Play again"
                : routineIdx >= routineIds.Length - 1 ? "Finish routine"
                : "Next: " + Find(routineIds[routineIdx + 1]).name;
            if (GUILayout.Button(next + "  (Space)", sBtnP)) Next();
            if (GUILayout.Button("Retry (R)", sBtn)) Retry();
            if (GUILayout.Button("Menu", sBtn)) ToMenu();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void Cell(string k, string v)
        {
            GUILayout.BeginVertical(sCell);
            GUILayout.Label(k, sNumK);
            GUILayout.Label(v, sNum);
            GUILayout.EndVertical();
        }

        void DrawSummary()
        {
            Dim(.8f);
            GUILayout.BeginArea(CenterRect(760, 120 + 44 * (routineResults.Count + 1) + 120), sPanel);
            GUILayout.Label($"<size={F(30)}><b>Routine complete</b></size>", sText);
            GUILayout.Label("Write down your weakest drill and play an extra round of it tomorrow.", sMuted);
            GUILayout.Space(Px(10));
            Row("<color=#8a94a3>DRILL</color>", "<color=#8a94a3>SCORE</color>", "<color=#8a94a3>ACC</color>", "<color=#8a94a3>BEST</color>");
            foreach (var r in routineResults)
                Row(Find(r.id).name, r.score.ToString("N0"), (r.acc * 100).ToString("0.0") + "%", r.best.ToString("N0"));
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Back to menu", sBtnP)) ToMenu();
            if (GUILayout.Button("Run again", sBtn)) StartRoutine();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void Row(string a, string b, string c, string d)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(a, sText, GUILayout.Width(Px(280)));
            GUILayout.Label(b, sText, GUILayout.Width(Px(140)));
            GUILayout.Label(c, sText, GUILayout.Width(Px(120)));
            GUILayout.Label(d, sText);
            GUILayout.EndHorizontal();
        }

        // ================= helpers =================
        static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta;

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
