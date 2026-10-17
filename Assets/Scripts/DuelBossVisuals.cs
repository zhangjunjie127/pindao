using System.Collections.Generic;
using UnityEngine;

// The ground marks and falling blades read the same clocks and radii as damage.
public sealed class DuelBossVisuals
{
    sealed class SwordView
    {
        public DuelCombat.FallingSword model;
        public GameObject root;
        public Transform blade;
        public Material material;
        public LineRenderer mark, shaft, trail;
    }
    readonly List<SwordView> views = new List<SwordView>();
    readonly Material spriteMaterial, lineMaterial;
    readonly Mesh swordMesh;
    readonly LineRenderer rush, landing;
    readonly float ground;
    public DuelBossVisuals(Material spriteMaterial, Material lineMaterial, float ground)
    {
        this.spriteMaterial = spriteMaterial; this.lineMaterial = lineMaterial; this.ground = ground;
        swordMesh = MakeSword();
        rush = Line("Charge direction", 8);
        landing = Line("Leap landing zone", 7);
        landing.positionCount = 33;
    }
    LineRenderer Line(string name, int order, Transform parent = null)
    {
        var go = new GameObject(name); if (parent) go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = lineMaterial;
        line.useWorldSpace = false; line.sortingOrder = order;
        line.startWidth = line.endWidth = .035f; line.numCapVertices = 2;
        return line;
    }
    SwordView Create(DuelCombat.FallingSword sword)
    {
        var view = new SwordView { model = sword, root = new GameObject("Sky sword hazard") };
        var blade = new GameObject("Consecrated greatsword"); blade.transform.SetParent(view.root.transform, false);
        blade.AddComponent<MeshFilter>().sharedMesh = swordMesh;
        view.material = new Material(spriteMaterial);
        var renderer = blade.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = view.material; renderer.sortingOrder = 24;
        view.blade = blade.transform;
        view.mark = Line("Locked landing mark", 19, view.root.transform);
        view.shaft = Line("Falling corridor", 10, view.root.transform);
        view.trail = Line("Descent trail", 23, view.root.transform);
        view.mark.positionCount = 33; view.shaft.positionCount = view.trail.positionCount = 2;
        views.Add(view); return view;
    }
    public void Update(DuelCombat combat)
    {
        landing.enabled = combat.pattern == 2 && (combat.enemyMode == DuelCombat.EnemyMode.Windup ||
            combat.enemyMode == DuelCombat.EnemyMode.Strike && combat.boss.y > .02f);
        if (landing.enabled)
        {
            landing.transform.position = new Vector3(combat.LandingTarget, ground + .05f, 0);
            for (int i = 0; i <= 32; i++)
            {
                float a = i / 32f * Mathf.PI * 2;
                landing.SetPosition(i, new Vector3(Mathf.Cos(a) * 2.1f, Mathf.Sin(a) * .09f, 0));
            }
            landing.startColor = landing.endColor = new Color(1, .58f, .22f, .55f);
            landing.startWidth = landing.endWidth = .04f;
        }
        for (int i = views.Count - 1; i >= 0; i--)
            if (!combat.swords.Contains(views[i].model))
            {
                Object.Destroy(views[i].material); Object.Destroy(views[i].root); views.RemoveAt(i);
            }
        foreach (var sword in combat.swords)
        {
            SwordView view = views.Find(v => v.model == sword) ?? Create(sword);
            float fade = sword.impacted ? Mathf.Clamp01(1 - (sword.age - sword.ImpactTime) / .8f) : 1;
            float readiness = Mathf.Clamp01(sword.age / sword.Warning);
            Color color = sword.great ? new Color(1, .29f, .12f) : new Color(1, .71f, .26f);
            view.root.transform.position = new Vector3(sword.x, ground, 0);
            float scale = sword.great ? 1.3f : .93f;
            view.blade.localScale = new Vector3(scale, scale, 1);
            view.blade.localPosition = new Vector3(0, sword.TipHeight, 0);
            view.material.SetColor("_Color", new Color(1, 1, 1, fade));
            for (int i = 0; i <= 32; i++)
            {
                float a = i / 32f * Mathf.PI * 2;
                view.mark.SetPosition(i, new Vector3(Mathf.Cos(a) * sword.Radius, .07f + Mathf.Sin(a) * .1f, 0));
            }
            view.mark.startWidth = view.mark.endWidth = .035f + readiness * .035f;
            view.mark.startColor = view.mark.endColor = new Color(color.r, color.g, color.b, fade * (.5f + readiness * .5f));
            view.shaft.SetPosition(0, new Vector3(0, .12f, 0));
            view.shaft.SetPosition(1, new Vector3(0, 7.4f, 0));
            view.shaft.startWidth = sword.Radius * 1.5f; view.shaft.endWidth = .03f;
            view.shaft.startColor = new Color(color.r, color.g, color.b, sword.impacted ? 0 : .07f + readiness * .09f);
            view.shaft.endColor = Color.clear;
            view.trail.enabled = sword.age >= sword.Warning && !sword.impacted;
            view.trail.SetPosition(0, new Vector3(0, sword.TipHeight + 1, 0));
            view.trail.SetPosition(1, new Vector3(0, sword.TipHeight + 8, 0));
            view.trail.startWidth = .45f * scale; view.trail.endWidth = .02f;
            view.trail.startColor = new Color(1, .9f, .64f, .8f); view.trail.endColor = Color.clear;
        }
        bool charging = combat.enemyMode == DuelCombat.EnemyMode.Windup &&
            (combat.pattern == DuelCombat.BladeRush || combat.pattern == DuelCombat.ShoulderRush);
        rush.enabled = charging;
        if (charging)
        {
            float direction = combat.boss.facing, x = combat.boss.x;
            float end = Mathf.Clamp(x + direction * (combat.pattern == DuelCombat.BladeRush ? 7.3f : 4.6f), DuelCombat.Left, DuelCombat.Right);
            rush.positionCount = 5;
            rush.SetPosition(0, new Vector3(x, ground + .16f, 0));
            rush.SetPosition(1, new Vector3(end, ground + .16f, 0));
            rush.SetPosition(2, new Vector3(end - direction * .45f, ground + .43f, 0));
            rush.SetPosition(3, new Vector3(end, ground + .16f, 0));
            rush.SetPosition(4, new Vector3(end - direction * .45f, ground + .04f, 0));
            var c = combat.pattern == DuelCombat.BladeRush ? new Color(1, .79f, .37f, .8f) : new Color(1, .22f, .14f, .9f);
            rush.startColor = rush.endColor = c;
            rush.startWidth = rush.endWidth = .025f + combat.boss.time / combat.boss.duration * .035f;
        }
    }
    static Mesh MakeSword()
    {
        var vertices = new List<Vector3>(); var colors = new List<Color>(); var triangles = new List<int>();
        void Face(Color color, params Vector2[] points)
        {
            int start = vertices.Count;
            foreach (var point in points) { vertices.Add(new Vector3(point.x, point.y, 0)); colors.Add(color); }
            for (int i = 1; i < points.Length - 1; i++) { triangles.Add(start); triangles.Add(start + i); triangles.Add(start + i + 1); }
        }
        Color edge = new Color(.93f, .79f, .45f), steel = new Color(.31f, .46f, .5f);
        Face(edge, new Vector2(0, 0), new Vector2(-.4f, .75f), new Vector2(-.31f, 3.2f),
            new Vector2(.31f, 3.2f), new Vector2(.4f, .75f));
        Face(steel, new Vector2(0, .2f), new Vector2(-.28f, .85f), new Vector2(-.23f, 3.14f), new Vector2(0, 3.14f));
        Face(new Color(.76f, .88f, .87f), new Vector2(0, .2f), new Vector2(0, 3.14f), new Vector2(.23f, 3.14f), new Vector2(.28f, .85f));
        Face(new Color(.06f, .13f, .16f), new Vector2(-.055f, .85f), new Vector2(-.055f, 2.98f), new Vector2(.055f, 2.98f), new Vector2(.055f, .85f));
        Face(edge, new Vector2(-.82f, 3.02f), new Vector2(-.9f, 3.42f), new Vector2(-.28f, 3.28f),
            new Vector2(.28f, 3.28f), new Vector2(.9f, 3.42f), new Vector2(.82f, 3.02f));
        Face(new Color(.19f, .1f, .09f), new Vector2(-.115f, 3.25f), new Vector2(-.115f, 4.05f),
            new Vector2(.115f, 4.05f), new Vector2(.115f, 3.25f));
        for (int i = 0; i < 4; i++)
        {
            float y = 3.4f + i * .16f;
            Face(edge, new Vector2(-.14f, y), new Vector2(-.14f, y + .045f), new Vector2(.14f, y + .045f), new Vector2(.14f, y));
        }
        Face(edge, new Vector2(0, 4.35f), new Vector2(-.25f, 4.08f), new Vector2(0, 3.85f), new Vector2(.25f, 4.08f));
        Face(new Color(1, .22f, .12f), new Vector2(0, 4.22f), new Vector2(-.12f, 4.08f), new Vector2(0, 3.96f), new Vector2(.12f, 4.08f));
        for (int i = 0; i < 4; i++)
        {
            float y = 1.1f + i * .48f;
            Face(new Color(.6f, 1, .95f), new Vector2(0, y - .1f), new Vector2(-.075f, y),
                new Vector2(0, y + .15f), new Vector2(.075f, y));
        }
        var mesh = new Mesh { name = "Original sky greatsword" };
        mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
        return mesh;
    }
    public void Dispose()
    {
        foreach (var view in views) { Object.Destroy(view.material); Object.Destroy(view.root); }
        views.Clear(); if (rush) Object.Destroy(rush.gameObject);
        if (landing) Object.Destroy(landing.gameObject); Object.Destroy(swordMesh);
    }
}
