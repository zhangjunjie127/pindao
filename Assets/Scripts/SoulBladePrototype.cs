using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed class SoulBladePrototype : MonoBehaviour
{
    const float Ground = -2.1f, StepTime = 1f / 120;
    public DuelCombat Fight { get; private set; }
    Camera cam;
    DuelSound sound;
    DuelPost post;
    DuelBossVisuals bossVisuals;
    SpriteRenderer hero, enemy, heroShadow, enemyShadow;
    LineRenderer spinTrail, spinEdge;
    Material spriteMaterial, heroMaterial, enemyMaterial, lineMaterial;
    readonly Dictionary<string, Sprite[]> animation = new Dictionary<string, Sprite[]>();
    readonly Dictionary<string, Sprite> art = new Dictionary<string, Sprite>();
    readonly List<VisualEffect> effects = new List<VisualEffect>();
    readonly List<Scenery> scenery = new List<Scenery>();
    readonly List<GameObject> waveVisuals = new List<GameObject>();
    readonly List<Material> materials = new List<Material>();
    Texture2D white, soft;
    Sprite softSprite;
    Font font;
    float accumulator, freeze, slow, shake, flash, hurt, heroFlash, enemyFlash, pulse, trailTimer;
    float uiHealth = 1, uiBoss = 1, cueLife, cueMax, realClock;
    string cueText = "", cueSub = "";
    Color cueColor = Color.white;
    Vector3 cameraRest = new Vector3(0, .35f, -10);
    DuelCombat.Controls pending;
    bool paused, demo, reducedShake, qa, screenshotBusy;
    string captureDir, recordDir;
    int recordedFrames, recordFps = 20;
    float nextRecordedFrame;
    int captureIndex;
    float quitAfter;
    readonly float[] audioSamples = new float[512];
    float audioPeak, audioRms, frameTimeSum;
    int measuredFrames;
    bool engaged;
    const float CutInDuration = .34f;
    float cutIn, recordStart = 2, recordSeconds = 16;
    bool offscreenQa, recordUltimate, metricsOnly;
    int cutInFrames, cutInViolations, swordImpacts;
    bool dodgeShowcase, showcaseCloseup, recordDodge;
    int showcaseDashes, dodgeCount;
    bool spinShowcase, spinCloseup;
    int showcaseSpins, spinCount, spinHits;
    Sprite[] emberFrames;
    float finishTime, lockCamera, impactLines, chargeFxTimer;
    int crossHits, lockWins;
    bool crossShowcase, recordFinish;
    int showcaseCrosses;
    static readonly string[,] ControlHints = {
        { "AD / ← →", "移动" }, { "J / 左键", "轻斩" }, { "U", "重斩" },
        { "K / 右键", "弹反" }, { "左Shift / L", "闪避" }, { "空格", "跳跃" }, { "空中 I", "旋斩" }, { "O", "斜连斩" }, { "E", "喝药" },
        { "Esc", "暂停" }, { "R", "重试" }, { "M", "声音" }, { "F8", "观战" },
        { "F9", "演武" }, { "F11", "全屏" }, { "F12", "截图" }, { "Enter", "跳过" },
        { "Q", "蓄力斩" }
    };
    [Serializable] class ClipFile { public ClipInfo[] clips; }
    [Serializable] class ClipInfo { public string name; public int frames, cols, width, height; }
    sealed class Scenery { public Transform transform; public Vector3 origin; public float parallax, sway, phase; }
    sealed class VisualEffect
    {
        public GameObject go;
        public float life, maxLife;
        public Vector3 velocity, scale;
        public SpriteRenderer sprite;
        public LineRenderer line;
        public Color color;
        public bool expand, gravity;
        public Sprite[] frames;
    }
    void Awake()
    {
        Application.targetFrameRate = 120; QualitySettings.vSyncCount = 1;
        Fight = new DuelCombat(); white = Texture2D.whiteTexture;
        font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 22);
        spriteMaterial = Own(new Material(Resources.Load<Shader>("DuelSprite")));
        lineMaterial = Own(new Material(Shader.Find("Sprites/Default")));
        MakeSoftTexture(); LoadAnimation(); BuildArena(); BuildActors();
        bossVisuals = new DuelBossVisuals(spriteMaterial, lineMaterial, Ground);
        sound = gameObject.AddComponent<DuelSound>();
        ParseArguments();
        if (qa && !string.IsNullOrEmpty(recordDir)) sound.PrepareCapture(Path.Combine(recordDir, "..", "audio"));
        if (offscreenQa) sound.Toggle();
        ResetFight();
    }
    void ParseArguments()
    {
        var args = Environment.GetCommandLineArgs();
        demo = Array.IndexOf(args, "--demo") >= 0;
        qa = Array.IndexOf(args, "--qa") >= 0;
        offscreenQa = qa && Array.IndexOf(args, "--qa-offscreen") >= 0;
        metricsOnly = Array.IndexOf(args, "--qa-metrics-only") >= 0;
        recordUltimate = Array.IndexOf(args, "--record-ultimate") >= 0;
        dodgeShowcase = Array.IndexOf(args, "--dodge-showcase") >= 0;
        showcaseCloseup = Array.IndexOf(args, "--dodge-closeup") >= 0;
        recordDodge = Array.IndexOf(args, "--record-dodge") >= 0;
        spinShowcase = Array.IndexOf(args, "--spin-showcase") >= 0;
        spinCloseup = Array.IndexOf(args, "--spin-closeup") >= 0;
        crossShowcase = Array.IndexOf(args, "--cross-showcase") >= 0;
        recordFinish = Array.IndexOf(args, "--record-finish") >= 0;
        if (dodgeShowcase || spinShowcase || crossShowcase) demo = false;
        if (recordUltimate) recordStart = float.MaxValue;
        if (recordDodge) recordStart = float.MaxValue;
        if (dodgeShowcase) recordStart = .5f;
        if (spinShowcase) recordStart = .5f;
        if (crossShowcase) recordStart = .5f;
        if (recordFinish) recordStart = float.MaxValue;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--capture-dir") captureDir = args[i + 1];
            if (args[i] == "--record-dir") recordDir = args[i + 1];
            if (args[i] == "--quit-after") float.TryParse(args[i + 1], out quitAfter);
            if (args[i] == "--record-seconds") float.TryParse(args[i + 1], out recordSeconds);
            if (args[i] == "--record-start") float.TryParse(args[i + 1], out recordStart);
            if (args[i] == "--record-fps" && int.TryParse(args[i + 1], out var fps)) recordFps = Mathf.Clamp(fps, 15, 60);
        }
        if (!string.IsNullOrEmpty(captureDir)) Directory.CreateDirectory(captureDir);
        if (!string.IsNullOrEmpty(recordDir)) Directory.CreateDirectory(recordDir);
    }
    Material Own(Material material) { materials.Add(material); return material; }
    void LoadAnimation()
    {
        var ember = Resources.Load<Texture2D>("Art/EmberBlade");
        if (ember)
        {
            emberFrames = new Sprite[8];
            float cellWidth = ember.width / 4f, cellHeight = ember.height / 2f;
            for (int i = 0; i < 8; i++)
                emberFrames[i] = Sprite.Create(ember,
                    new Rect(i % 4 * cellWidth, ember.height - (i / 4 + 1) * cellHeight, cellWidth, cellHeight),
                    new Vector2(.5f, .5f), 100);
        }
        foreach (var clip in JsonUtility.FromJson<ClipFile>(Resources.Load<TextAsset>("Art/clips").text).clips)
        {
            var tex = Resources.Load<Texture2D>("Art/" + clip.name);
            var frames = new Sprite[clip.frames];
            for (int i = 0; i < frames.Length; i++)
            {
                var rect = new Rect(i % clip.cols * clip.width,
                    tex.height - (i / clip.cols + 1) * clip.height, clip.width, clip.height);
                var pivot = clip.name.EndsWith("_AirSpin") ? new Vector2(.5f, .5f) : new Vector2(.5f, 19f / 288);
                frames[i] = Sprite.Create(tex, rect, pivot, 100, 0, SpriteMeshType.FullRect);
            }
            animation.Add(clip.name, frames);
        }
    }
    Sprite Art(string name)
    {
        if (art.TryGetValue(name, out var result)) return result;
        var tex = Resources.Load<Texture2D>("Art/" + name);
        if (tex == null) throw new InvalidOperationException("Missing scenery: " + name);
        result = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f), 100);
        art.Add(name, result); return result;
    }
    void MakeSoftTexture()
    {
        // Light falloff for particles/contact shadows, not replacement character artwork.
        soft = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var pixels = new Color[4096];
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 31.5f;
            pixels[y * 64 + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Max(0, 1 - d), 2));
        }
        soft.SetPixels(pixels); soft.Apply();
        softSprite = Sprite.Create(soft, new Rect(0, 0, 64, 64), new Vector2(.5f, .5f), 64);
    }
    SpriteRenderer NewSprite(string name, Sprite sprite, int order, Material material = null)
    {
        var go = new GameObject(name); var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite; sr.sortingOrder = order; sr.sharedMaterial = material ?? spriteMaterial;
        return sr;
    }
    void Decor(string file, float x, float y, float width, float height, int order,
        Color tint, float fog = 0, float parallax = .1f, float sway = 0)
    {
        var mat = Own(new Material(spriteMaterial));
        mat.SetColor("_FogColor", new Color(.045f, .085f, .10f)); mat.SetFloat("_Fog", fog);
        var sr = NewSprite(file, Art(file), order, mat);
        sr.color = tint; sr.transform.position = new Vector3(x, y, 0);
        sr.transform.localScale = new Vector3(width / sr.sprite.bounds.size.x, height / sr.sprite.bounds.size.y, 1);
        scenery.Add(new Scenery { transform = sr.transform, origin = sr.transform.position,
            parallax = parallax, sway = sway, phase = UnityEngine.Random.value * 6 });
    }
    void BuildArena()
    {
        cam = Camera.main;
        if (!cam)
        {
            var go = new GameObject("Main Camera"); go.tag = "MainCamera";
            cam = go.AddComponent<Camera>(); go.AddComponent<AudioListener>();
        }
        cam.orthographic = true; cam.orthographicSize = 5;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.085f, .13f, .15f);
        cam.transform.position = cameraRest;
        post = cam.gameObject.AddComponent<DuelPost>();
        var pale = new Color(.51f, .69f, .6f);
        // Independent silhouettes, platforms and plants, not a baked arena image.
        for (int i = 0; i < 7; i++)
            Decor("column" + (i % 2), -12 + i * 4.1f, 2.7f + i % 3 * .3f, 2.4f, 12, -70, pale, .83f, .06f);
        Decor("column3", -6.7f, -1.3f, 13, 5.5f, -65, pale, .65f, .12f);
        Decor("column4", 8.8f, -.3f, 7, 8, -65, pale, .62f, .12f);
        Decor("column2", -.6f, -2.1f, 7.5f, 5.1f, -66, pale, .73f, .1f);
        float[] rootX = { -10, -7.1f, -3.8f, 3.6f, 7.4f, 10.3f };
        for (int i = 0; i < rootX.Length; i++)
            Decor("prop" + (9 + i % 4), rootX[i], 1.1f, .65f + i % 2 * .3f, 8.4f, -48,
                new Color(.48f, .72f, .57f), .28f, .22f, .4f);
        for (int i = 0; i < 11; i++)
            Decor("hanging" + (i % 5), -10.8f + i * 2.2f, 4.1f + (i % 3) * .24f,
                1.2f + (i % 2) * .6f, 3 + (i % 3) * .7f, -30,
                new Color(.42f, .64f, .46f), .06f, .38f, .7f);
        Decor("prop0", -6.9f, -1.38f, 3.8f, 1.7f, -15, new Color(.69f, .79f, .68f));
        Decor("prop1", 6.8f, -.6f, 3.8f, 3.7f, -15, new Color(.61f, .71f, .63f));
        Decor("hill5", -.5f, -1.95f, 10.5f, 1.1f, -20, new Color(.5f, .75f, .6f), .16f);
        for (int i = 0; i < 10; i++)
            Decor("prop" + (i % 2 == 0 ? 22 : 24), -9 + i * 2.1f, -1.65f, .18f, 1.15f,
                -12, new Color(.53f, .95f, .78f), 0, .1f, 2);
        var floor = new GameObject("Arena collision");
        floor.transform.position = new Vector3(0, Ground - .6f, 0);
        floor.AddComponent<BoxCollider2D>().size = new Vector2(18, 1.2f);
        for (int i = 0; i < 16; i++)
            Decor("floor", -11.25f + i * 1.5f, Ground - .18f, 1.505f, .54f,
                0, new Color(.59f, .68f, .51f), 0, 1);
        Decor("platform7", 0, Ground - .47f, 19, .8f, -1, new Color(.43f, .64f, .49f), 0, 1);
        Decor("hill6", -8.8f, -3.9f, 4.8f, 3, 60, new Color(.12f, .22f, .19f), 0, 1.1f);
        Decor("hill2", 8.7f, -3.9f, 4.6f, 3.2f, 60, new Color(.13f, .23f, .19f), 0, 1.1f);
        Decor("hanging6", -9.3f, 2, 3.2f, 7.5f, 65, new Color(.13f, .23f, .19f), 0, 1.1f, .6f);
        Decor("hanging7", 9.3f, 2.7f, 3.1f, 7.5f, 65, new Color(.13f, .23f, .19f), 0, 1.1f, .6f);
        var water = NewSprite("Water", Sprite.Create(Texture2D.whiteTexture,
            new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1), -8,
            Own(new Material(Resources.Load<Shader>("DuelWater"))));
        water.transform.position = new Vector3(0, -3.9f, 0);
        water.transform.localScale = new Vector3(24, 2.6f, 1);
        BuildAtmosphere();
    }
    void BuildAtmosphere()
    {
        var mat = Own(new Material(spriteMaterial));
        mat.mainTexture = soft;
        for (int layer = 0; layer < 2; layer++)
        {
            var go = new GameObject("Drifting spores " + layer);
            go.transform.position = new Vector3(0, .4f, 0);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop();
            var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(4, 9);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.03f, .11f);
            main.startSize = new ParticleSystem.MinMaxCurve(.025f, .07f);
            main.startColor = layer == 0 ? new Color(.48f, .9f, .75f, .5f) : new Color(1, .72f, .35f, .6f);
            main.maxParticles = 130; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission; emission.rateOverTime = layer == 0 ? 14 : 5;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(20, 8, .1f);
            var noise = ps.noise; noise.enabled = true; noise.strength = .12f; noise.frequency = .15f;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = mat; renderer.sortingOrder = layer == 0 ? -10 : 45; ps.Play();
        }
    }
    void BuildActors()
    {
        heroMaterial = Own(new Material(spriteMaterial)); enemyMaterial = Own(new Material(spriteMaterial));
        heroMaterial.SetColor("_Accent", new Color(.22f, .78f, .77f));
        enemyMaterial.SetColor("_Accent", new Color(.98f, .48f, .22f));
        heroMaterial.SetFloat("_Recolor", 1); enemyMaterial.SetFloat("_Recolor", 1);
        hero = NewSprite("Wanderer", animation["R_Idle"][0], 15, heroMaterial);
        enemy = NewSprite("Ash Warden", animation["L_Idle"][0], 14, enemyMaterial);
        heroShadow = NewSprite("Player shadow", softSprite, 2); enemyShadow = NewSprite("Warden shadow", softSprite, 2);
        heroShadow.color = enemyShadow.color = new Color(0, 0, 0, .8f);
        spinTrail = MakeLine("Aerial rotating blade", 20, new Color(.32f, .88f, .93f), .2f);
        spinEdge = MakeLine("Aerial blade edge", 21, Color.white, .04f);
        foreach (var line in new[] { spinTrail, spinEdge })
        {
            line.positionCount = 41;
            line.widthCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(.65f, .7f),
                new Keyframe(.9f, 1), new Keyframe(1, .12f));
            line.enabled = false;
        }
    }
    void ResetFight()
    {
        Fight.Reset(); pending = default;
        showcaseDashes = showcaseSpins = showcaseCrosses = 0;
        if (dodgeShowcase) { Fight.enemyMode = DuelCombat.EnemyMode.Recover; Fight.boss.duration = 3600; }
        if (spinShowcase)
        {
            Fight.enemyMode = DuelCombat.EnemyMode.Recover; Fight.boss.duration = 3600;
            Fight.player.x = -1.45f; Fight.boss.x = 0;
        }
        if (crossShowcase)
        {
            Fight.enemyMode = DuelCombat.EnemyMode.Recover; Fight.boss.duration = 3600;
            Fight.player.x = -1.45f; Fight.boss.x = 0; Fight.focus = 100;
        }
        foreach (var fx in effects) if (fx.go) Destroy(fx.go);
        effects.Clear();
        foreach (var go in waveVisuals) Destroy(go);
        waveVisuals.Clear();
        freeze = slow = shake = flash = hurt = heroFlash = enemyFlash = accumulator = pulse = cutIn = 0;
        finishTime = lockCamera = impactLines = chargeFxTimer = 0;
        cueLife = 0; uiHealth = uiBoss = 1; paused = false; engaged = demo || dodgeShowcase || spinShowcase || crossShowcase;
        Notify("苔 渊", "THE MOSSBOUND DUEL", new Color(.83f, .9f, .82f), 2.4f);
        UpdateActors(0);
        bossVisuals.Update(Fight); UpdateCamera(1);
    }
    void Update()
    {
        float realDt = offscreenQa ? 1f / 60 : Mathf.Min(Time.unscaledDeltaTime, .1f); realClock += realDt;
        if (Input.GetKeyDown(KeyCode.Escape)) paused = !paused;
        if (Input.GetKeyDown(KeyCode.R)) ResetFight();
        if (Input.GetKeyDown(KeyCode.F8)) { dodgeShowcase = spinShowcase = crossShowcase = false; demo = !demo; ResetFight(); }
        if (Input.GetKeyDown(KeyCode.F9))
        { dodgeShowcase = !dodgeShowcase; spinShowcase = crossShowcase = false; showcaseCloseup = true; demo = false; ResetFight(); }
        if (Input.GetKeyDown(KeyCode.M)) sound.Toggle();
        if (Input.GetKeyDown(KeyCode.F11)) Screen.fullScreen = !Screen.fullScreen;
        if (Input.GetKeyDown(KeyCode.F12)) Capture();
        sound.SetPhase(Fight.secondPhase, paused);
        if (paused) return;
        if (!demo && !dodgeShowcase && !spinShowcase && !crossShowcase) ReadInput();
        if (pending.move != 0 || pending.attack || pending.heavy || pending.parry || pending.dash || pending.jump ||
            pending.heal || pending.special || pending.burst || pending.charge || pending.skip)
            engaged = true;
        if (Fight.won) finishTime += realDt;
        if (Fight.won && pending.skip) finishTime = Mathf.Max(2.2f, finishTime);
        cueLife = Mathf.Max(0, cueLife - realDt);
        flash = Mathf.MoveTowards(flash, 0, realDt * 4); hurt = Mathf.MoveTowards(hurt, 0, realDt * 2);
        shake = Mathf.MoveTowards(shake, 0, realDt * 2.7f);
        heroFlash = Mathf.MoveTowards(heroFlash, 0, realDt * 7); enemyFlash = Mathf.MoveTowards(enemyFlash, 0, realDt * 7);
        pulse = Mathf.MoveTowards(pulse, 0, realDt * 2);
        impactLines = Mathf.MoveTowards(impactLines, 0, realDt * 3.5f);
        cutIn = Mathf.Max(0, cutIn - realDt);
        bool cutInFrozen = cutIn > 0;
        float clockBefore = Fight.clock, healthBefore = Fight.player.hp;
        float dt = engaged ? realDt : 0;
        if (cutIn > 0) dt = 0;
        else if (freeze > 0) { freeze -= realDt; dt = 0; }
        else if (slow > 0) { slow -= realDt; dt *= .4f; }
        accumulator += dt;
        while (accumulator >= StepTime)
        {
            var input = crossShowcase ? CrossShowcaseInput() : spinShowcase ? SpinShowcaseInput() :
                dodgeShowcase ? ShowcaseInput() : demo ? Fight.Demonstration() : pending;
            Fight.Step(StepTime, input);
            pending.attack = pending.heavy = pending.parry = pending.dash = pending.jump =
                pending.heal = pending.special = pending.burst = pending.charge = pending.skip = false;
            foreach (var cue in Fight.cues) Handle(cue);
            Fight.cues.Clear(); accumulator -= StepTime;
            if (freeze > 0 || cutIn > 0) { accumulator = 0; break; }
        }
        if (qa && cutInFrozen)
        {
            cutInFrames++;
            if (Fight.clock != clockBefore || Fight.player.hp != healthBefore) cutInViolations++;
        }
        UpdateActors(dt); UpdateEffects(realDt); UpdateScenery(); UpdateWaves(); bossVisuals.Update(Fight); UpdateCamera(realDt);
        chargeFxTimer -= realDt;
        if (Fight.playerMode == DuelCombat.PlayerMode.Charging && chargeFxTimer <= 0)
        {
            Vector3 at = new Vector3(Fight.player.x, Ground + Fight.player.y + .85f, 0);
            Ring(at, .65f + Mathf.PingPong(Fight.player.time * 1.8f, .3f),
                new Color(1, .59f, .22f, .8f), .24f);
            Spark(at, new Color(1, .75f, .36f), 3, .3f);
            pulse = Mathf.Max(pulse, .16f);
            chargeFxTimer = .2f;
        }
        uiHealth = Mathf.MoveTowards(uiHealth, Fight.player.hp / 100, realDt * .3f);
        uiBoss = Mathf.MoveTowards(uiBoss, Fight.boss.hp / DuelCombat.BossMaxHp, realDt * .13f);
        if (qa)
        {
            frameTimeSum += Time.unscaledDeltaTime; measuredFrames++;
            if (!offscreenQa && Time.frameCount % 20 == 0)
            {
                AudioListener.GetOutputData(audioSamples, 0);
                float squareSum = 0;
                foreach (var sample in audioSamples) { audioPeak = Mathf.Max(audioPeak, Mathf.Abs(sample)); squareSum += sample * sample; }
                audioRms = Mathf.Max(audioRms, Mathf.Sqrt(squareSum / audioSamples.Length));
            }
        }
        if (qa && !metricsOnly && captureIndex < 5 && realClock > 2 + captureIndex * 6) { Capture(); captureIndex++; }
        if (!string.IsNullOrEmpty(recordDir) && realClock >= recordStart &&
            realClock < recordStart + recordSeconds && realClock >= nextRecordedFrame)
        {
            SaveFrame(Path.Combine(recordDir, "frame-" + recordedFrames.ToString("D4") + ".png"));
            recordedFrames++; nextRecordedFrame = recordStart + recordedFrames / (float)recordFps;
        }
        if (quitAfter > 0 && realClock >= quitAfter)
        {
            if (!string.IsNullOrEmpty(captureDir))
                File.WriteAllText(Path.Combine(captureDir, "runtime-check.json"),
                    "{\"seconds\":" + Fight.clock.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"parries\":" + Fight.parries + ",\"hitsTaken\":" + Fight.hitsTaken +
                    ",\"bossHp\":" + Fight.boss.hp.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"audioPeak\":" + (offscreenQa ? "null" : audioPeak.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)) +
                    ",\"audioRmsMax\":" + (offscreenQa ? "null" : audioRms.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)) +
                    ",\"averageFps\":" + (offscreenQa ? "null" : (measuredFrames / Mathf.Max(.001f, frameTimeSum)).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)) +
                    ",\"cutInFrames\":" + cutInFrames + ",\"cutInViolations\":" + cutInViolations + ",\"swordImpacts\":" + swordImpacts +
                    ",\"dodges\":" + dodgeCount + ",\"dodgeShowcase\":" + dodgeShowcase.ToString().ToLowerInvariant() +
                    ",\"airSpins\":" + spinCount + ",\"airSpinHits\":" + spinHits +
                    ",\"crossSlashes\":" + Fight.crossSlashes + ",\"crossHits\":" + crossHits +
                    ",\"locksWon\":" + lockWins + ",\"leapSlams\":" + Fight.leapSlams + ",\"waveCasts\":" + Fight.waveCasts +
                    ",\"recordedFrames\":" + recordedFrames +
                    ",\"offscreen\":" + offscreenQa.ToString().ToLowerInvariant() +
                    ",\"won\":" + Fight.won.ToString().ToLowerInvariant() + "}");
            Application.Quit();
        }
    }
    DuelCombat.Controls ShowcaseInput()
    {
        if (Fight.clock < 1.1f + showcaseDashes * 1.6f) return default;
        var input = new DuelCombat.Controls { dash = true, move = showcaseDashes % 2 == 0 ? 1 : -1 };
        showcaseDashes++; return input;
    }
    DuelCombat.Controls SpinShowcaseInput()
    {
        if (Fight.clock < .9f + showcaseSpins * 2.5f || Fight.playerMode != DuelCombat.PlayerMode.Ready) return default;
        if (Fight.player.y <= .005f) return new DuelCombat.Controls { jump = true };
        if (Fight.player.y < .55f) return default;
        showcaseSpins++;
        return new DuelCombat.Controls { special = true };
    }
    DuelCombat.Controls CrossShowcaseInput()
    {
        if (Fight.clock < .8f + showcaseCrosses * 3.1f || Fight.playerMode != DuelCombat.PlayerMode.Ready ||
            showcaseCrosses >= 2) return default;
        showcaseCrosses++;
        return new DuelCombat.Controls { burst = true };
    }
    void ReadInput()
    {
        pending.move = Input.GetAxisRaw("Horizontal");
        pending.attack |= Input.GetKeyDown(KeyCode.J) || Input.GetMouseButtonDown(0);
        pending.heavy |= Input.GetKeyDown(KeyCode.U);
        pending.special |= Input.GetKeyDown(KeyCode.I);
        pending.burst |= Input.GetKeyDown(KeyCode.O);
        pending.charge |= Input.GetKeyDown(KeyCode.Q);
        pending.skip |= Input.GetKeyDown(KeyCode.Return);
        pending.parry |= Input.GetKeyDown(KeyCode.K) || Input.GetMouseButtonDown(1);
        pending.dash |= Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.L);
        pending.jump |= Input.GetKeyDown(KeyCode.Space); pending.heal |= Input.GetKeyDown(KeyCode.E);
    }
    void UpdateActors(float dt)
    {
        var p = Fight.player; var b = Fight.boss;
        enemy.enabled = Fight.enemyMode != DuelCombat.EnemyMode.Intro || b.time > .55f;
        enemyShadow.enabled = enemy.enabled && b.y < 3;
        hero.transform.position = new Vector3(p.x, Ground + p.y, 0);
        enemy.transform.position = new Vector3(b.x, Ground + b.y, 0);
        hero.transform.localScale = Vector3.one * 1.07f; enemy.transform.localScale = Vector3.one * 1.7f;
        string action = "Idle"; float progress = (engaged ? Fight.clock : realClock) * .8f % 1; bool loop = true;
        switch (Fight.playerMode)
        {
            case DuelCombat.PlayerMode.Attack:
                action = "Slash_" + (Fight.combo + 1);
                progress = p.time < Fight.PlayerStartup ? p.time / Fight.PlayerStartup * .38f
                    : p.time < Fight.PlayerStartup + .14f ? .38f + (p.time - Fight.PlayerStartup) / .14f * .37f
                    : .75f + (p.time - Fight.PlayerStartup - .14f) / (p.duration - Fight.PlayerStartup - .14f) * .25f;
                loop = false; break;
            case DuelCombat.PlayerMode.Parry: action = "Block"; progress = Mathf.Min(.52f, p.time / .15f * .52f); loop = false; break;
            case DuelCombat.PlayerMode.Hurt: action = "Impact_Body"; progress = p.time / p.duration; loop = false; break;
            case DuelCombat.PlayerMode.Dash:
                action = "Dodge"; progress = (DuelCombat.DodgeFrame(p.time) + .001f) / 11; loop = false; break;
            case DuelCombat.PlayerMode.AirSpin:
                action = "AirSpin"; progress = (DuelCombat.AirSpinFrame(p.time) + .001f) / 11; loop = false;
                hero.transform.position += Vector3.up * .85f;
                break;
            case DuelCombat.PlayerMode.SpinLand:
                action = "Dodge"; progress = (8 + Mathf.Clamp01(p.time / p.duration) * 3) / 11; loop = false; break;
            case DuelCombat.PlayerMode.CrossSlash:
                int segment = Mathf.Clamp(Mathf.FloorToInt((p.time - .38f) / .16f) + 1, 0, 5);
                action = "Slash_" + (segment % 3 + 1);
                progress = p.time < .18f ? p.time / .18f * .38f :
                    p.time < DuelCombat.CrossEnd ? .38f + Mathf.Repeat(p.time - .18f, .16f) / .16f * .42f : .88f;
                loop = false; break;
            case DuelCombat.PlayerMode.BladeLock:
                action = "BladeBind"; progress = Mathf.Repeat(Fight.lockTime * 1.5f, 1); loop = true;
                hero.transform.position += Vector3.right * p.facing * .015f * Mathf.Sin(realClock * 35);
                break;
            case DuelCombat.PlayerMode.Charging:
                action = "PowerUp"; progress = p.time / DuelCombat.ChargeDuration; loop = false; break;
            case DuelCombat.PlayerMode.Heal: action = "Cast_Powerful"; progress = p.time / p.duration; loop = false; break;
            case DuelCombat.PlayerMode.Dead: action = "Death_Backward"; progress = p.time / 1.3f; loop = false; break;
            default:
                if (p.y > .03f) { action = p.vy > 0 ? "Jump" : "Fall"; progress = Mathf.Clamp01(1 - p.y / 2); loop = false; }
                else if (Mathf.Abs(pending.move) > 0 || demo && Mathf.Abs(p.x - b.x) > 1.8f)
                { action = "Run"; progress = Fight.clock * 1.8f % 1; }
                break;
        }
        Pose(hero, p.facing, action, progress, loop);
        UpdateSpinTrail();
        action = "Idle"; progress = (engaged ? Fight.clock : realClock) * .6f % 1; loop = true;
        switch (Fight.enemyMode)
        {
            case DuelCombat.EnemyMode.Intro:
                action = b.y > .01f ? "Fall" : b.time > .55f && b.time < 1.5f ? "Slash_2" : "Idle";
                progress = b.y > .01f ? .5f : .8f; loop = false; break;
            case DuelCombat.EnemyMode.Approach:
                if (Mathf.Abs(p.x - b.x) > 2.1f) { action = "Run"; progress = Fight.clock % 1; } break;
            case DuelCombat.EnemyMode.Windup:
                action = Fight.pattern == DuelCombat.SkySwords || Fight.pattern == 3 ? "Cast_Powerful" :
                    Fight.pattern == DuelCombat.ShoulderRush ? "Block" : Fight.pattern == DuelCombat.BladeRush ? "Slash_3" :
                    Fight.pattern == 2 ? "Jump" : "Slash_" + (Fight.pattern == 1 ? 3 : Fight.strike % 3 + 1);
                progress = b.time / b.duration * (Fight.pattern >= 2 ? .65f : .36f); loop = false; break;
            case DuelCombat.EnemyMode.Strike:
                action = Fight.pattern == DuelCombat.SkySwords || Fight.pattern == 3 ? "Cast_Powerful" :
                    Fight.pattern == DuelCombat.ShoulderRush ? "Block" : Fight.pattern == DuelCombat.BladeRush ? "Slash_3" :
                    Fight.pattern == 2 ? "Slash_2" : "Slash_" + (Fight.pattern == 1 ? 3 : Fight.strike % 3 + 1);
                progress = .38f + b.time / b.duration * .44f; loop = false; break;
            case DuelCombat.EnemyMode.Recover: action = "Slash_1"; progress = .84f + b.time / b.duration * .16f; loop = false; break;
            case DuelCombat.EnemyMode.Stagger: action = "Impact_Body"; progress = .45f; loop = false; break;
            case DuelCombat.EnemyMode.Phase: action = "PowerUp"; progress = b.time / b.duration; loop = false; break;
            case DuelCombat.EnemyMode.Dead: action = "Death_Backward"; progress = b.time / 1.7f; loop = false; break;
            case DuelCombat.EnemyMode.BladeLock:
                action = "BladeBind"; progress = Mathf.Repeat(Fight.lockTime * 1.5f + .5f, 1); loop = true;
                break;
        }
        if (cutIn > 0 && !Fight.won)
        {
            action = "PowerUp"; progress = .38f + (1 - cutIn / CutInDuration) * .22f; loop = false;
        }
        Pose(enemy, b.facing, action, progress, loop);
        if (Fight.won && finishTime < 1.45f)
        {
            Pose(hero, p.facing, "Slash_4", finishTime < .45f ? .35f : .55f + (finishTime - .45f) * .3f, false);
            Pose(enemy, b.facing, finishTime < .8f ? "Impact_Body" : "Death_Backward",
                finishTime < .8f ? .5f : (finishTime - .8f) / 1.1f, false);
        }
        bool rushing = Fight.enemyMode == DuelCombat.EnemyMode.Strike &&
            (Fight.pattern == DuelCombat.BladeRush || Fight.pattern == DuelCombat.ShoulderRush);
        enemy.transform.localRotation = Quaternion.Euler(0, 0, rushing ? -b.facing * 12 : 0);
        heroMaterial.SetFloat("_Flash", heroFlash); enemyMaterial.SetFloat("_Flash", enemyFlash);
        hero.color = p.invincible > .25f && Fight.playerMode != DuelCombat.PlayerMode.Dash
            ? new Color(1, 1, 1, .6f + Mathf.Sin(realClock * 45) * .3f) : Color.white;
        enemy.color = Fight.secondPhase ? new Color(1, .83f, .78f) : new Color(.88f, .93f, 1);
        heroShadow.transform.position = new Vector3(p.x, Ground + .04f, 0);
        enemyShadow.transform.position = new Vector3(b.x, Ground + .04f, 0);
        heroShadow.transform.localScale = new Vector3(1.2f / (1 + p.y * .2f), .21f, 1);
        enemyShadow.transform.localScale = new Vector3(2.1f / (1 + b.y * .2f), .3f, 1);
        trailTimer -= dt;
        bool dashTrail = Fight.playerMode == DuelCombat.PlayerMode.Dash && p.time >= .04f && p.time < .21f;
        bool airTrail = Fight.playerMode == DuelCombat.PlayerMode.AirSpin && p.time >= DuelCombat.AirSpinStart && p.time < DuelCombat.AirSpinEnd;
        bool crossTrail = Fight.playerMode == DuelCombat.PlayerMode.CrossSlash && p.time < DuelCombat.CrossEnd;
        if (trailTimer <= 0 && (dashTrail || airTrail || crossTrail ||
            Fight.enemyMode == DuelCombat.EnemyMode.Strike && (Fight.pattern == 1 || rushing)))
        { Afterimage(dashTrail || airTrail || crossTrail ? hero : enemy); trailTimer = airTrail || crossTrail ? .075f : dashTrail ? .045f : .035f; }
    }
    void UpdateSpinTrail()
    {
        var p = Fight.player;
        float t = p.time - DuelCombat.AirSpinStart;
        bool active = Fight.playerMode == DuelCombat.PlayerMode.AirSpin && t >= 0 && p.time < DuelCombat.AirSpinEnd && !Fight.won && !Fight.lost;
        spinTrail.enabled = spinEdge.enabled = active;
        if (!active) return;
        float opacity = Mathf.Clamp01(t / .035f) * Mathf.Clamp01((DuelCombat.AirSpinEnd - p.time) / .045f);
        var cyan = new Color(.36f, .9f, .96f, .85f * opacity);
        spinTrail.startColor = new Color(cyan.r, cyan.g, cyan.b, 0); spinTrail.endColor = cyan;
        spinEdge.startColor = new Color(.85f, 1, 1, 0); spinEdge.endColor = new Color(.85f, 1, 1, opacity);
        spinTrail.widthMultiplier = .21f; spinEdge.widthMultiplier = .045f;
        spinTrail.transform.position = spinEdge.transform.position = new Vector3(p.x, Ground + p.y + .85f, 0);
        float tip = 55 - t / .24f * 360;
        for (int i = 0; i <= 40; i++)
        {
            float angle = (tip + (1 - i / 40f) * 245) * Mathf.Deg2Rad;
            Vector3 point = new Vector3(Mathf.Cos(angle) * p.facing, Mathf.Sin(angle), 0) * DuelCombat.AirSpinRadius;
            spinTrail.SetPosition(i, point);
            spinEdge.SetPosition(i, point * 1.035f);
        }
    }
    void Pose(SpriteRenderer sr, int facing, string action, float progress, bool loop)
    {
        var frames = animation[(facing > 0 ? "R_" : "L_") + action];
        int index = loop ? (int)(Mathf.Repeat(progress, 1) * frames.Length) : Mathf.FloorToInt(Mathf.Clamp01(progress) * (frames.Length - 1));
        sr.sprite = frames[Mathf.Clamp(index, 0, frames.Length - 1)];
    }
    void Handle(DuelCombat.Cue cue)
    {
        var at = new Vector3(cue.at.x, Ground + cue.at.y, 0);
        var gold = new Color(1, .71f, .27f); var cyan = new Color(.6f, .98f, 1);
        sound.Cue(cue.kind, cue.strength, cue.at.x / 12, realClock);
        if (qa && !string.IsNullOrEmpty(captureDir))
            File.AppendAllText(Path.Combine(captureDir, "events.txt"),
                realClock.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " " + cue.kind + " " + cue.strength + "\n");
        switch (cue.kind)
        {
            case "Entrance":
                Notify("灰 烬 守 卫", "此处，不容无誓之刃。", gold, 1.9f); break;
            case "ChargeStart":
                Ring(at, .7f, gold, .5f); Spark(at, gold, 20, .6f);
                Notify("烈 魂 蓄 势", "蓄力中不可防御 · 再按 Q 中断", gold, DuelCombat.ChargeDuration); break;
            case "ChargeReady":
                Ring(at, 1.15f, gold, .7f); Spark(at, gold, 34, .8f);
                Impact(at, Color.white, 30, .08f, .45f); pulse = .45f; impactLines = .35f;
                Notify("烈 魂 已 满", Fight.player.stamina >= 99.99f
                    ? "按 Q 蓄势释放重斩" : "体力全满后按 Q", gold, 1.35f); break;
            case "ChargeDenied":
                Notify(cue.strength == 2 ? "体 力 未 满" : "烈 魂 未 满", "命中积攒烈魂，体力全满后按 Q", gold, .8f); break;
            case "ChargeCanceled":
            case "ChargeInterrupted":
                Spark(at, new Color(1, .35f, .2f), 16, .35f);
                Notify("蓄 势 中 断", "能量与体力不返还", new Color(1, .47f, .29f), 1.1f); break;
            case "ChargeMiss":
                Arc(at, Fight.player.facing, 4.2f, gold, .3f, 120);
                Impact(at, gold, 34, .07f, .5f);
                Notify("烈 魂 斩 空", "调整距离与朝向", gold, .85f); break;
            case "ChargeHit":
                Impact(at, Color.white, 82, .2f, 1.25f);
                Arc(at, Fight.player.facing, 5.2f, gold, .42f, 185);
                Ring(at, 1.8f, new Color(1, .82f, .43f), .38f);
                pulse = 1; impactLines = 1;
                Notify("烈 魂 · 破 军", "180 伤害", gold, 1.1f); break;
            case "ComboRhythm":
                Ring(at, .55f, cyan, .22f);
                Notify("节 奏 接 招", "", cyan, .42f); break;
            case "CrossStart":
                Notify("烬 · 连 斩", "", gold, .75f);
                Spark(at, gold, 12, .35f); break;
            case "CrossSweep":
                EmberSlash(at + new Vector3(Fight.player.facing * .7f, -.2f, 0),
                    Fight.player.facing, cue.strength % 2 == 0 ? -18 : 22, cue.strength == 6 ? 3.7f : 3.1f);
                break;
            case "CrossHit":
                crossHits++; enemyFlash = .8f;
                Impact(at, cue.strength == 6 ? Color.white : gold, cue.strength == 6 ? 35 : 12,
                    cue.strength == 6 ? .095f : .035f, cue.strength == 6 ? .6f : .18f);
                if (cue.strength == 6) { impactLines = .55f; pulse = .6f; }
                break;
            case "LockStart":
                Impact(at, Color.white, 30, .075f, .45f); impactLines = .8f;
                Notify("拼 刀", "看准亮起的节拍，按 J / 左键", cyan, 1.8f); break;
            case "LockBeat":
                Impact(at, cue.strength >= 4 ? gold : cyan, 10 + cue.strength * 3, .035f, .16f + cue.strength * .04f);
                Ring(at, .4f + cue.strength * .1f, Color.white, .15f); impactLines = .2f;
                Notify("拼 刀  " + cue.strength + " / " + DuelCombat.LockBeats, "继续跟拍 · J / 左键", cyan, .72f);
                break;
            case "LockMiss":
                Spark(at, new Color(1, .25f, .15f), 8, .2f);
                Notify("失 拍", "看准亮起的节拍再按 J", new Color(1, .42f, .3f), .7f); break;
            case "LockWin":
                at = new Vector3(Fight.LockContact.x, Ground + Fight.LockContact.y, 0);
                lockWins++; Impact(at, Color.white, 45, .14f, .8f); impactLines = 1;
                EmberSlash(at, Fight.player.facing, -35, 4.5f);
                Notify("压 刀 · 破 势", "", gold, 1.2f); break;
            case "LockLost":
                Notify("退 势", "", new Color(1, .4f, .3f), .7f); break;
            case "Wave":
                EmberSlash(at + Vector3.up * .1f, Fight.boss.facing, 0, 2.8f);
                break;
            case "RushTell":
                Notify("奔 袭", "", gold, .8f); Spark(at + Vector3.up * 1.3f, gold, 12, .55f); break;
            case "ShoulderTell":
                Notify("危 · 冲撞", "", new Color(1, .3f, .19f), .75f);
                if (recordDodge && recordStart == float.MaxValue) recordStart = realClock;
                Ring(at, .8f, new Color(1, .2f, .13f), .45f); break;
            case "Rush":
                Arc(at, Fight.boss.facing, 2.3f, gold, .32f, 60); Spark(at, gold, 16, .55f); break;
            case "Shoulder":
                Spark(at - Vector3.up * .7f, new Color(.7f, .63f, .45f), 22, .6f); shake = .25f; break;
            case "RushStopped":
                Notify("截 势", "RUSH BROKEN", cyan, .65f); break;
            case "SwordSummon":
                cutIn = CutInDuration;
                Ring(at + Vector3.up * 1.2f, 1.4f, gold, .48f);
                Spark(at + Vector3.up * 1.2f, gold, 24, .6f);
                Notify("敕 · 天 坠", "SWORDS OF JUDGMENT", gold, 1.05f);
                if (recordUltimate && recordStart == float.MaxValue) recordStart = realClock;
                break;
            case "SwordMark":
                Spark(new Vector3(at.x, Ground + .05f, 0), gold, 4, .3f); break;
            case "SwordImpact":
                swordImpacts++;
                if (cutIn > 0) cutInViolations++;
                bool great = cue.strength > 1;
                Impact(at + Vector3.up * .18f, great ? new Color(1, .46f, .15f) : gold,
                    great ? 65 : 30, great ? .13f : .055f, great ? .95f : .45f);
                Ring(at + Vector3.up * .1f, great ? 1.7f : .9f, gold, great ? .65f : .35f);
                break;
            case "PlayerSwing": Arc(at, Fight.player.facing, Fight.combo >= 2 ? 1.8f : 1.45f, cyan, .16f); break;
            case "AirSpin": spinCount++; break;
            case "AirSpinHit":
                spinHits++; enemyFlash = .8f;
                bool finisher = cue.strength == 3;
                Impact(at, finisher ? Color.white : cyan, finisher ? 28 : 12, finisher ? .065f : .022f, finisher ? .4f : .16f);
                if (finisher) Ring(at, .65f, cyan, .18f);
                break;
            case "SpinLand":
                DodgeDust(Fight.player.x, Fight.player.facing, true);
                Spark(at + Vector3.up * .06f, new Color(.6f, .74f, .63f, .5f), 8, .22f);
                break;
            case "BossSwing": Arc(at + Vector3.up * .35f, Fight.boss.facing, Fight.pattern == 1 ? 2.4f : 2.1f, gold, .19f); break;
            case "Tell": Spark(at + Vector3.up * 1.65f, gold, 5, .45f); break;
            case "Danger": Notify("危", "", new Color(1, .3f, .19f), .85f); Ring(at, 1.1f, new Color(1, .2f, .13f), .48f); break;
            case "Guard": Arc(at, Fight.player.facing, .8f, new Color(.4f, .88f, 1, .6f), .12f, 65); break;
            case "Parry": case "Clash":
                Impact(at, cyan, cue.kind == "Parry" ? 36 : 20, .1f, .75f);
                Ring(at, .7f, Color.white, .22f); Ring(at, 1.7f, cyan, .3f); pulse = .7f; slow = .13f;
                impactLines = .55f;
                Notify(cue.kind == "Parry" ? "铿 · 弹反" : "拼 刀", "STEEL MEETS STEEL", cyan, .65f); break;
            case "HitBoss":
                enemyFlash = .8f;
                Impact(at, gold, cue.strength == 2 ? 24 : 15, cue.strength == 2 ? .08f : .045f, cue.strength == 2 ? .4f : .22f); break;
            case "HitPlayer":
                heroFlash = .9f; hurt = .6f; Impact(at, new Color(1, .25f, .18f), 22, .075f, .6f); break;
            case "Break":
                Impact(at, gold, 42, .14f, .85f); Notify("破 势", "WARDEN EXPOSED", gold, 1.4f); break;
            case "Riposte":
                Impact(at, Color.white, 55, .17f, 1); Arc(at, Fight.player.facing, 3.4f, gold, .25f);
                slow = .35f; pulse = 1; Notify("绝 斩", "CRITICAL STRIKE", gold, 1.1f); break;
            case "Slam":
                Spark(at, gold, 30 + cue.strength * 6, .65f); Ring(at, 2.1f, gold, .35f); shake = .6f;
                Impact(at, gold, 12, .055f, .6f);
                EmberSlash(at + Vector3.up * .7f, Fight.boss.facing, -50, 3.4f);
                break;
            case "Dash":
                dodgeCount++;
                break;
            case "DashKick":
                if (Fight.player.y < .1f) DodgeDust(Fight.player.x, Fight.player.facing, false);
                break;
            case "DashBrake":
                if (Fight.player.y < .1f) DodgeDust(Fight.player.x, Fight.player.facing, true);
                break;
            case "Land": case "Jump":
                Spark(new Vector3(at.x, Ground + .08f, 0), new Color(.58f, .75f, .63f, .5f), 7, .35f); break;
            case "Phase":
                Ring(at, 2.3f, new Color(1, .3f, .17f), 1.1f); slow = .25f; shake = .65f;
                Notify("余 烬 再 燃", "灰烬守卫进入暴走", gold, 5.8f); break;
            case "Heal": Ring(at, .8f, new Color(.5f, 1, .64f), .6f); Spark(at, new Color(.56f, 1, .77f), 20, .7f); break;
            case "Win":
                Impact(at, Color.white, 65, .18f, .8f); slow = .65f; cueLife = 0;
                finishTime = 0; impactLines = 1;
                EmberSlash(at, Fight.player.facing, -18, 6.5f);
                if (recordFinish && recordStart == float.MaxValue) recordStart = realClock;
                break;
        }
        if (cue.kind == "Win" || cue.kind == "Lose") cutIn = 0;
        if (qa && !metricsOnly && (cue.kind == "Parry" || cue.kind == "Break" || cue.kind == "Slam" ||
            cue.kind == "Rush" || cue.kind == "Shoulder" || cue.kind == "SwordSummon" ||
            cue.kind == "SwordMark" || cue.kind == "SwordImpact" || cue.kind == "DashKick" || cue.kind == "AirSpinSweep" ||
            cue.kind == "CrossSweep" || cue.kind == "LockBeat" || cue.kind == "LockWin" || cue.kind == "Win")) Capture(cue.kind);
    }
    void EmberSlash(Vector3 at, int facing, float angle, float size)
    {
        if (emberFrames == null) return;
        var sr = NewSprite("Painted ember slash", emberFrames[0], 32);
        sr.transform.position = at;
        sr.transform.localScale = new Vector3(size / sr.sprite.bounds.size.x * facing, size / sr.sprite.bounds.size.y, 1);
        sr.transform.rotation = Quaternion.Euler(0, 0, angle * facing);
        effects.Add(new VisualEffect { go = sr.gameObject, sprite = sr, frames = emberFrames,
            color = Color.white, life = .23f, maxLife = .23f });
    }
    void DodgeDust(float x, int direction, bool braking)
    {
        Color dust = new Color(.49f, .59f, .51f, .32f);
        for (int i = 0; i < (braking ? 6 : 8); i++)
        {
            var sr = NewSprite(braking ? "Boot dust" : "Push-off dust", softSprite, 16);
            sr.transform.position = new Vector3(x + direction * (braking ? .42f : -.35f), Ground + .04f, 0);
            sr.transform.localScale = new Vector3(UnityEngine.Random.Range(.16f, .35f), .09f, 1);
            sr.color = dust;
            effects.Add(new VisualEffect { go = sr.gameObject, sprite = sr, color = dust,
                scale = sr.transform.localScale, expand = true, life = .24f, maxLife = .24f,
                velocity = new Vector3(-direction * UnityEngine.Random.Range(.7f, 2.4f), UnityEngine.Random.Range(.08f, .4f), 0) });
        }
        if (!braking) return;
        var scrape = MakeLine("Boot skid", 17, new Color(.68f, .77f, .62f, .5f), .025f);
        scrape.transform.position = new Vector3(x, Ground + .035f, 0); scrape.positionCount = 2;
        scrape.SetPosition(0, new Vector3(direction * .55f, 0, 0));
        scrape.SetPosition(1, new Vector3(-direction * .3f, 0, 0));
        effects.Add(new VisualEffect { go = scrape.gameObject, line = scrape, life = .18f, maxLife = .18f,
            color = new Color(.68f, .77f, .62f, .5f) });
    }
    void Impact(Vector3 at, Color color, int count, float stop, float power)
    {
        Spark(at, color, count, .65f);
        freeze = Mathf.Max(freeze, stop); shake = Mathf.Max(shake, power); flash = Mathf.Max(flash, power * .18f);
        var sr = NewSprite("Impact bloom", softSprite, 40);
        sr.transform.position = at; sr.transform.localScale = Vector3.one * 1.8f;
        sr.color = new Color(color.r, color.g, color.b, .8f);
        effects.Add(new VisualEffect { go = sr.gameObject, sprite = sr, color = sr.color,
            scale = sr.transform.localScale, life = .18f, maxLife = .18f });
    }
    void Spark(Vector3 at, Color color, int count, float duration)
    {
        for (int i = 0; i < count; i++)
        {
            float a = UnityEngine.Random.value * Mathf.PI * 2;
            Vector3 v = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * UnityEngine.Random.Range(2.5f, 8);
            var line = MakeLine("Steel shard", 42, color, UnityEngine.Random.Range(.012f, .032f));
            line.positionCount = 2; line.SetPosition(0, Vector3.zero); line.SetPosition(1, -v.normalized * UnityEngine.Random.Range(.08f, .3f));
            line.transform.position = at; float life = duration * UnityEngine.Random.Range(.4f, 1);
            effects.Add(new VisualEffect { go = line.gameObject, line = line, velocity = v,
                life = life, maxLife = life, color = color, gravity = true });
        }
    }
    LineRenderer MakeLine(string name, int order, Color color, float width)
    {
        var go = new GameObject(name); var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = lineMaterial; line.useWorldSpace = false;
        line.startWidth = width; line.endWidth = width * .15f;
        line.startColor = color; line.endColor = new Color(color.r, color.g, color.b, 0);
        line.sortingOrder = order; line.numCapVertices = 2; return line;
    }
    void Arc(Vector3 at, int direction, float radius, Color color, float duration, float degrees = 150)
    {
        var line = MakeLine("Blade trail", 35, color, .12f);
        line.transform.position = at; line.positionCount = 27;
        line.widthCurve = new AnimationCurve(new Keyframe(0, .02f), new Keyframe(.35f, 1), new Keyframe(.7f, .7f), new Keyframe(1, 0));
        line.widthMultiplier = .18f;
        for (int i = 0; i < 27; i++)
        {
            float a = Mathf.Lerp(-degrees * .5f, degrees * .5f, i / 26f) * Mathf.Deg2Rad;
            line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius * direction, Mathf.Sin(a) * radius * .7f, 0));
        }
        effects.Add(new VisualEffect { go = line.gameObject, line = line, life = duration, maxLife = duration, color = color });
    }
    void Ring(Vector3 at, float radius, Color color, float duration)
    {
        var line = MakeLine("Impact ring", 37, color, .035f);
        line.transform.position = at; line.positionCount = 65;
        line.startWidth = line.endWidth = .035f; line.startColor = line.endColor = color;
        for (int i = 0; i <= 64; i++)
        {
            float angle = i / 64f * Mathf.PI * 2;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius);
        }
        effects.Add(new VisualEffect { go = line.gameObject, line = line, life = duration, maxLife = duration,
            color = color, expand = true, scale = Vector3.one });
    }
    void Afterimage(SpriteRenderer source)
    {
        var sr = NewSprite("Motion echo", source.sprite, 9);
        sr.color = Fight.playerMode == DuelCombat.PlayerMode.CrossSlash && source == hero
            ? new Color(1, .58f, .2f, .38f) : new Color(.27f, .78f, .77f, .3f);
        sr.transform.position = source.transform.position; sr.transform.localScale = source.transform.localScale;
        sr.transform.rotation = source.transform.rotation;
        effects.Add(new VisualEffect { go = sr.gameObject, sprite = sr, life = .22f, maxLife = .22f,
            color = sr.color, scale = sr.transform.localScale });
    }
    void UpdateEffects(float dt)
    {
        for (int i = effects.Count - 1; i >= 0; --i)
        {
            var fx = effects[i]; fx.life -= dt;
            if (fx.life <= 0) { Destroy(fx.go); effects.RemoveAt(i); continue; }
            float t = fx.life / fx.maxLife;
            if (fx.gravity) fx.velocity += Vector3.down * dt * 9;
            fx.go.transform.position += fx.velocity * dt;
            if (fx.expand) fx.go.transform.localScale = fx.scale * (1.4f - t * .65f);
            var c = fx.color; c.a *= t;
            if (fx.sprite) fx.sprite.color = c;
            if (fx.sprite && fx.frames != null)
                fx.sprite.sprite = fx.frames[Mathf.Min(fx.frames.Length - 1, (int)((1 - t) * fx.frames.Length))];
            if (fx.line) { fx.line.startColor = c; fx.line.endColor = fx.expand ? c : new Color(c.r, c.g, c.b, 0); }
        }
    }
    void UpdateScenery()
    {
        foreach (var item in scenery)
        {
            item.transform.position = item.origin + new Vector3(cam.transform.position.x * (1 - item.parallax), 0, 0);
            if (item.sway > 0) item.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(realClock * .7f + item.phase) * item.sway);
        }
    }
    void UpdateWaves()
    {
        while (waveVisuals.Count < Fight.waves.Count)
        {
            var line = MakeLine("Ground blade wave", 26, new Color(1, .19f, .07f), .16f);
            line.positionCount = 20; line.startWidth = .06f; line.endWidth = .18f; waveVisuals.Add(line.gameObject);
            if (emberFrames != null)
            {
                var blade = NewSprite("Painted wave edge", emberFrames[3], 25);
                blade.color = new Color(1, .22f, .12f, .9f);
                blade.transform.SetParent(line.transform, false);
                blade.transform.localPosition = new Vector3(0, .75f, 0);
            }
        }
        for (int i = 0; i < waveVisuals.Count; i++)
        {
            bool active = i < Fight.waves.Count; waveVisuals[i].SetActive(active); if (!active) continue;
            var w = Fight.waves[i]; var line = waveVisuals[i].GetComponent<LineRenderer>();
            line.transform.position = new Vector3(w.x, Ground, 0);
            var painted = line.GetComponentInChildren<SpriteRenderer>();
            if (painted)
            {
                painted.sprite = emberFrames[2 + (int)(w.age * 18) % 4];
                painted.transform.localScale = new Vector3(.44f * w.direction, .44f, 1);
                painted.transform.localRotation = Quaternion.Euler(0, 0, w.direction * 24);
            }
            for (int p = 0; p < 20; p++)
            {
                float t = p / 19f;
                line.SetPosition(p, new Vector3(Mathf.Sin(t * Mathf.PI) * .55f * w.direction, t * 1.25f, 0));
            }
        }
    }
    void UpdateCamera(float dt)
    {
        cameraRest.x = Mathf.Lerp(cameraRest.x, Mathf.Clamp((Fight.player.x + Fight.boss.x) * .115f, -.8f, .8f), dt * 3);
        float size = Mathf.Max(4.65f, 8.6f / cam.aspect);
        float cut = CutInWeight(cutIn);
        cam.orthographicSize = Mathf.Lerp(size - pulse * .2f, 2.65f, cut);
        var noise = new Vector3(Mathf.Sin(realClock * 127), Mathf.Sin(realClock * 101), 0);
        var focus = new Vector3(Fight.boss.x, Ground + Fight.boss.y + 1.6f, -10);
        cam.transform.position = Vector3.Lerp(cameraRest, focus, cut) + noise * shake * (reducedShake ? .015f : .065f);
        lockCamera = Mathf.MoveTowards(lockCamera, Fight.IsLocked ? 1 : 0, dt * 2);
        if (lockCamera > 0)
        {
            var duelFocus = new Vector3((Fight.player.x + Fight.boss.x) * .5f, Ground + 1.35f, -10);
            cam.transform.position = Vector3.Lerp(cam.transform.position, duelFocus, lockCamera * .85f);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, Mathf.Max(3.25f, 4.4f / cam.aspect), lockCamera);
        }
        if (engaged && Fight.enemyMode == DuelCombat.EnemyMode.Intro)
        {
            float t = Fight.boss.time;
            Vector3 wide = cameraRest;
            Vector3 close = new Vector3(Fight.boss.x - .35f, Ground + Fight.boss.y + 1.35f, -10);
            float push = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - .65f) / 1.85f));
            float pull = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - 4.15f) / 3.05f));
            cam.transform.position = Vector3.Lerp(Vector3.Lerp(wide, close, push), wide, pull) +
                noise * shake * (reducedShake ? .015f : .065f);
            cam.orthographicSize = Mathf.Lerp(Mathf.Lerp(size, 3.25f, push), size, pull);
        }
        else if (engaged && Fight.enemyMode == DuelCombat.EnemyMode.Phase)
        {
            float t = Fight.boss.time;
            Vector3 wide = new Vector3((Fight.player.x + Fight.boss.x) * .5f, Ground + 1.35f, -10);
            Vector3 close = new Vector3(Fight.boss.x - .3f, Ground + Fight.boss.y + 1.45f, -10);
            float push = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / 2.2f));
            float pull = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - 4.5f) / 2.9f));
            cam.transform.position = Vector3.Lerp(Vector3.Lerp(wide, close, push), wide, pull) +
                noise * shake * (reducedShake ? .015f : .065f);
            cam.orthographicSize = Mathf.Lerp(Mathf.Lerp(size, 3.1f, push), size, pull);
        }
        float finish = Fight.won ? Mathf.Clamp01(finishTime / .12f) * Mathf.Clamp01((1.8f - finishTime) / .6f) : 0;
        if (finish > 0)
        {
            var duelFocus = new Vector3((Fight.player.x + Fight.boss.x) * .5f, Ground + 1.15f, -10);
            cam.transform.position = Vector3.Lerp(cam.transform.position, duelFocus, finish);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, 3.2f, finish);
        }
        if (dodgeShowcase && showcaseCloseup)
        {
            cam.orthographicSize = Mathf.Max(1.85f, 3.5f / cam.aspect);
            cam.transform.position = new Vector3(-2.96f, Ground + .82f, -10);
        }
        if (spinShowcase && spinCloseup)
        {
            cam.orthographicSize = Mathf.Max(2.8f, 4.3f / cam.aspect);
            cam.transform.position = new Vector3(-.6f, Ground + 1.65f, -10);
        }
        post.intensity = pulse; post.damage = hurt; post.flash = reducedShake ? flash * .25f : flash;
        post.lines = reducedShake ? 0 : impactLines;
        post.ink = Fight.won ? finish * .8f : 0;
        post.bars = Mathf.Max(cut, lockCamera * .55f,
            Fight.enemyMode == DuelCombat.EnemyMode.Phase ? .62f : 0, finish);
        post.focus = cam.WorldToViewportPoint(new Vector3((Fight.player.x + Fight.boss.x) * .5f, Ground + 1.1f, 0));
    }
    public static float CutInWeight(float remaining)
    {
        if (remaining <= 0) return 0;
        float elapsed = CutInDuration - remaining;
        return Mathf.SmoothStep(0, 1, Mathf.Min(elapsed / .065f, remaining / .13f));
    }
    void Notify(string text, string sub, Color color, float duration)
    { cueText = text; cueSub = sub; cueColor = color; cueLife = cueMax = duration; }
    void Capture(string name = "arena")
    {
        if (screenshotBusy) return;
        string directory = captureDir ?? Path.Combine(Application.dataPath, "..", "Screenshots");
        Directory.CreateDirectory(directory); screenshotBusy = true;
        StartCoroutine(CaptureFrame(Path.Combine(directory, name + "-" + Time.frameCount + ".png")));
    }
    IEnumerator CaptureFrame(string path)
    {
        if (offscreenQa)
        {
            for (int i = 0; i < 5; i++) yield return null;
            SaveFrame(path);
        }
        else
        {
            yield return new WaitForEndOfFrame(); SaveVisibleFrame(path);
        }
        yield return null; screenshotBusy = false;
    }
    void SaveVisibleFrame(string path)
    {
        var oldActive = RenderTexture.active;
        var image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = null;
            image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = oldActive;
            Destroy(image);
        }
    }
    void SaveFrame(string path)
    {
        if (!offscreenQa) { ScreenCapture.CaptureScreenshot(path); return; }
        // Hidden-window QA renders the real scene; IMGUI HUD is not part of these images.
        var target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24);
        var oldTarget = cam.targetTexture; var oldActive = RenderTexture.active;
        Texture2D image = null;
        try
        {
            cam.targetTexture = target; cam.Render(); RenderTexture.active = target;
            image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            cam.targetTexture = oldTarget; RenderTexture.active = oldActive;
            RenderTexture.ReleaseTemporary(target); if (image) Destroy(image);
        }
    }
    GUIStyle Text(int size, Color color, TextAnchor align = TextAnchor.UpperLeft)
    {
        var s = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, alignment = align };
        s.normal.textColor = color; return s;
    }
    void Box(Rect r, Color color) { GUI.color = color; GUI.DrawTexture(r, white); GUI.color = Color.white; }
    void Bar(Rect r, float value, float delayed, Color color)
    {
        Box(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(.06f, .07f, .07f, .9f));
        Box(r, new Color(.18f, .19f, .17f));
        Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(delayed), r.height), new Color(.76f, .61f, .38f, .8f));
        Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), color);
        Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), 1), color * 1.35f);
    }
    void OnGUI()
    {
        if (Fight == null) return;
        float w = Screen.width, h = Screen.height;
        Color ivory = new Color(.86f, .87f, .78f), gold = new Color(.85f, .7f, .4f);
        GUI.Label(new Rect(30, 20, 55, 55), "刃", Text(33, ivory, TextAnchor.MiddleCenter));
        GUI.Label(new Rect(95, 23, 240, 26), "无 名 行 者", Text(15, ivory));
        Bar(new Rect(97, 53, 230, 10), Fight.player.hp / 100, uiHealth, new Color(.69f, .17f, .16f));
        Bar(new Rect(97, 72, 190, 4), Fight.player.stamina / 100, Fight.player.stamina / 100, new Color(.45f, .73f, .49f));
        Bar(new Rect(97, 84, 190, 4), Fight.focus / 100, Fight.focus / 100, new Color(.96f, .57f, .18f));
        for (int i = 0; i < 2; i++)
            Box(new Rect(97 + 95 * i, 82, 1, 8), new Color(.04f, .05f, .04f));
        float chargeFill = Fight.playerMode == DuelCombat.PlayerMode.Charging
            ? Fight.player.time / DuelCombat.ChargeDuration : Fight.chargeEnergy / DuelCombat.ChargeEnergyMax;
        bool chargeFull = Fight.chargeEnergy >= DuelCombat.ChargeEnergyMax;
        if (chargeFull)
            Box(new Rect(94, 95, 136, 13), new Color(1, .65f, .19f, .12f + .13f * (Mathf.Sin(realClock * 7) * .5f + .5f)));
        Color chargeColor = chargeFull
            ? Color.Lerp(new Color(1, .62f, .18f), Color.white, (Mathf.Sin(realClock * 7) * .5f + .5f) * .32f)
            : new Color(.78f, .4f, .19f);
        Bar(new Rect(97, 98, 120, 7), chargeFill, chargeFill, chargeColor);
        string chargeLabel = Fight.playerMode == DuelCombat.PlayerMode.Charging
            ? "Q 烈魂蓄势 " + Mathf.FloorToInt(chargeFill * 100) + "%"
            : Fight.chargeEnergy < DuelCombat.ChargeEnergyMax ? "Q 烈魂 " + Mathf.FloorToInt(Fight.chargeEnergy) + "%"
            : Fight.player.stamina < 99.99f ? "Q 烈魂已满 · 体力未满" : "Q 蓄力斩就绪";
        GUI.Label(new Rect(97, 107, 230, 20), chargeLabel, Text(12, gold));
        GUI.Label(new Rect(97, 125, 230, 22), "灵药  " + Fight.potions +
            (Fight.focus >= DuelCombat.CrossCost ? "     烬刃 · 就绪" : "     烬刃 " + Mathf.FloorToInt(Fight.focus)), Text(12, gold));
        if (w >= 900)
        {
            GUI.Label(new Rect(w * .5f - 90, 25, 180, 28), dodgeShowcase ? "演 武" : demo ? "观 战" : "苔 渊", Text(18, ivory, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(w * .5f - 90, 54, 180, 26), Fight.secondPhase ? "II  ·  余烬" : "I  ·  守誓", Text(12, gold, TextAnchor.MiddleCenter));
        }
        if (Fight.RhythmWindowOpen)
        {
            float alpha = .72f + .28f * (Mathf.Sin(realClock * 12) * .5f + .5f);
            Box(new Rect(w * .5f - 74, 82, 148, 23), new Color(.025f, .07f, .07f, .8f));
            GUI.Label(new Rect(w * .5f - 72, 82, 144, 23), "J  ·  连 斩",
                Text(13, new Color(.62f, .96f, 1, alpha), TextAnchor.MiddleCenter));
        }
        if (Fight.chain > 1) GUI.Label(new Rect(97, 151, 190, 36), Fight.chain.ToString("00") + " 连", Text(24, gold));
        float barWidth = Mathf.Min(720, w - 140), bx = (w - barWidth) * .5f;
        GUI.Label(new Rect(bx, h - 104, barWidth, 29), "灰 烬 守 卫", Text(20, ivory, TextAnchor.MiddleCenter));
        Bar(new Rect(bx, h - 64, barWidth, 8), Fight.boss.hp / DuelCombat.BossMaxHp, uiBoss, new Color(.58f, .22f, .16f));
        Bar(new Rect(w * .5f - barWidth * .26f, h - 43, barWidth * .52f, 3), Fight.boss.posture / 100, Fight.boss.posture / 100, gold);
        if (cueLife > 0 && !Fight.won && !Fight.lost)
        {
            float alpha = Mathf.Min(1, cueLife * 4) * Mathf.Min(1, (cueMax - cueLife) * 12);
            Color c = cueColor; c.a = alpha;
            GUI.Label(new Rect(0, h * .22f, w, 56), cueText, Text(cueText == "危" ? 44 : 29, c, TextAnchor.MiddleCenter));
            c.a *= .65f; GUI.Label(new Rect(0, h * .22f + 53, w, 26), cueSub, Text(11, c, TextAnchor.MiddleCenter));
        }
        if (Fight.IsLocked)
        {
            float y = h - 202;
            GUI.Label(new Rect(w * .5f - 220, y - 39, 440, 30), "拼 刀  ·  看准亮拍按 J / 左键", Text(21, ivory, TextAnchor.MiddleCenter));
            for (int i = 0; i < DuelCombat.LockBeats; i++)
            {
                float x = w * .5f + (i - 2) * 52;
                Color c = i < Fight.lockBeat ? (Fight.lockHitMask & (1 << i)) != 0 ? gold :
                    new Color(.8f, .25f, .17f) : new Color(.3f, .35f, .34f);
                if (i == Fight.lockBeat)
                {
                    float delta = Mathf.Abs(Fight.NextLockBeat - Fight.lockTime);
                    c = delta <= DuelCombat.LockWindow ? Color.white : cyanColor;
                    float span = 30 + Mathf.Clamp01(delta / DuelCombat.LockInterval) * 16;
                    Box(new Rect(x - span * .5f, y - 4, span, 3), c);
                }
                GUI.Label(new Rect(x - 22, y - 3, 44, 36), "◆", Text(26, c, TextAnchor.MiddleCenter));
            }
        }
        if (Fight.won && finishTime < 2.1f)
            GUI.Label(new Rect(0, h * .23f, w, 64), finishTime > .55f ? "终 · 斩" : "",
                Text(42, ivory, TextAnchor.MiddleCenter));
        if (Fight.won && finishTime >= 2.1f || Fight.lost || paused)
        {
            Box(new Rect(0, 0, w, h), new Color(.015f, .025f, .025f, paused ? .83f : .63f));
            GUI.Label(new Rect(0, h * .26f, w, 74), paused ? "暂 停" : Fight.won ? "守 誓 已 破" : "身 陨",
                Text(38, !paused && Fight.lost ? new Color(.77f, .2f, .17f) : gold, TextAnchor.MiddleCenter));
            if (!paused) GUI.Label(new Rect(0, h * .26f + 83, w, 32), "弹反  " + Fight.parries +
                "     拼刀胜  " + Fight.locksWon + "     受击  " + Fight.hitsTaken, Text(15, ivory, TextAnchor.MiddleCenter));
            float y = h * .5f;
            if (paused)
            {
                if (MenuButton("继续", y)) paused = false;
                if (MenuButton(sound.Muted ? "声音：关" : "声音：开", y + 47)) sound.Toggle();
                if (MenuButton(reducedShake ? "镜头震动：低" : "镜头震动：标准", y + 94)) reducedShake = !reducedShake;
                if (MenuButton("重新挑战", y + 141)) ResetFight();
            }
            else if (MenuButton("再次挑战", y)) ResetFight();
        }
        DrawControlHints(w);
    }
    static readonly Color cyanColor = new Color(.5f, .88f, .94f);
    public static Rect ControlHintsBounds(float width)
    {
        int rows = width >= 900 ? (ControlHints.GetLength(0) + 1) / 2 : ControlHints.GetLength(0);
        return new Rect(width - (width >= 900 ? 360 : 230), 18, width >= 900 ? 340 : 210,
            32 + rows * (width >= 900 ? 20 : 18));
    }
    void DrawControlHints(float width)
    {
        Rect panel = ControlHintsBounds(width);
        int rows = width >= 900 ? (ControlHints.GetLength(0) + 1) / 2 : ControlHints.GetLength(0);
        float columnWidth = (panel.width - 24) / (width >= 900 ? 2 : 1);
        Box(panel, new Color(.025f, .04f, .04f, .72f));
        Box(new Rect(panel.x, panel.y, 2, panel.height), new Color(.65f, .57f, .35f, .65f));
        GUI.Label(new Rect(panel.x + 12, panel.y + 5, panel.width - 24, 22), "操作", Text(13, new Color(.84f, .73f, .48f)));
        var keyStyle = Text(12, new Color(.9f, .92f, .88f), TextAnchor.MiddleRight);
        var actionStyle = Text(13, new Color(.73f, .79f, .75f), TextAnchor.MiddleLeft);
        for (int i = 0; i < ControlHints.GetLength(0); i++)
        {
            float x = panel.x + 12 + i / rows * columnWidth;
            float rowHeight = width >= 900 ? 20 : 18;
            float y = panel.y + 26 + i % rows * rowHeight;
            string key = ControlHints[i, 0], action = ControlHints[i, 1];
            if (Fight.IsLocked && i == 1) action = "拼刀按拍";
            GUI.Label(new Rect(x, y, 88, rowHeight), key, keyStyle);
            GUI.Label(new Rect(x + 98, y, columnWidth - 98, rowHeight), action, actionStyle);
        }
    }
    bool MenuButton(string text, float y)
    {
        var rect = new Rect(Screen.width * .5f - 110, y, 220, 37);
        bool hover = rect.Contains(Event.current.mousePosition);
        if (hover) Box(rect, new Color(.67f, .58f, .35f, .12f));
        Box(new Rect(rect.x, rect.yMax - 1, rect.width, 1), new Color(.67f, .58f, .35f, hover ? .8f : .25f));
        var style = Text(17, new Color(.87f, .85f, .75f), TextAnchor.MiddleCenter);
        return GUI.Button(rect, text, style);
    }
    void OnDestroy()
    {
        bossVisuals?.Dispose();
        foreach (var mat in materials) if (mat) Destroy(mat);
        foreach (var frames in animation.Values) foreach (var sprite in frames) Destroy(sprite);
        foreach (var sprite in art.Values) Destroy(sprite);
        if (soft) Destroy(soft); if (softSprite) Destroy(softSprite); if (font) Destroy(font);
        if (emberFrames != null) foreach (var sprite in emberFrames) Destroy(sprite);
    }
}
