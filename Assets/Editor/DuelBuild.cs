using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public sealed class DuelTextureImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/Art/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 8192;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
    }
}

public static class DuelBuild
{
    const float Dt = 1f / 120;
    static readonly List<string> results = new List<string>();
    static void Check(bool condition, string description)
    {
        if (!condition)
        {
            Directory.CreateDirectory("work");
            File.WriteAllLines("work/combat-test-progress.txt", results);
            throw new Exception("Combat check FAILED: " + description);
        }
        results.Add("PASS: " + description);
    }
    static DuelCombat Ready(float height = 0)
    {
        var c = new DuelCombat();
        c.enemyMode = DuelCombat.EnemyMode.Recover; c.boss.duration = 100;
        c.player.x = 0; c.player.y = height; c.boss.x = 1.4f; return c;
    }
    static void Advance(DuelCombat c, float seconds, DuelCombat.Controls input = default)
    {
        for (int i = 0; i < Mathf.CeilToInt(seconds / Dt); i++)
        {
            c.Step(Dt, input);
            input.attack = input.heavy = input.parry = input.dash = input.jump =
                input.heal = input.special = input.burst = input.skip = false;
        }
    }
    static DuelCombat Locked()
    {
        var c = Ready();
        for (int i = 0; i < 3; i++)
        {
            c.enemyMode = DuelCombat.EnemyMode.Strike; c.pattern = 0;
            c.boss.time = 0; c.boss.duration = .24f; c.boss.connected = false; c.boss.facing = -1;
            c.Step(Dt, new DuelCombat.Controls { parry = true });
            if (!c.IsLocked) Advance(c, .11f);
        }
        return c;
    }
    static void ReferenceChecks()
    {
        var c = Ready();
        c.Step(Dt, new DuelCombat.Controls { burst = true });
        Check(c.playerMode == DuelCombat.PlayerMode.CrossSlash && c.focus == 0 && c.player.stamina == 72,
            "ground O launches an independent cross-slash and charges focus/stamina once");
        Check(c.player.invincible == 0, "cross-slash grants no hidden invulnerability");
        Advance(c, .17f);
        Check(c.boss.hp == DuelCombat.BossMaxHp, "cross-slash has harmless anticipation");
        Advance(c, 1.6f);
        Check(c.boss.hp == DuelCombat.BossMaxHp - 55 && c.cues.FindAll(q => q.kind == "CrossHit").Count == 6,
            "six cross-slash windows deal 7+7+7+7+7+20 exactly once each");
        Check(c.player.y == 0 && c.playerMode == DuelCombat.PlayerMode.Ready,
            "cross-slash descends and finishes grounded recovery");
        c = Ready(); c.focus = 49; c.staminaDelay = 1;
        c.Step(Dt, new DuelCombat.Controls { burst = true });
        Check(c.playerMode == DuelCombat.PlayerMode.Ready && c.focus == 49 && c.player.stamina == 100,
            "insufficient focus rejects cross-slash without spending stamina");
        c = Ready(); c.player.stamina = 27; c.staminaDelay = 1;
        c.Step(Dt, new DuelCombat.Controls { burst = true });
        Check(c.playerMode == DuelCombat.PlayerMode.Ready && c.focus == 50,
            "insufficient stamina rejects cross-slash without spending focus");
        c = Ready(.9f); c.focus = 100;
        for (int i = 0; i < 80; i++) c.Step(Dt, new DuelCombat.Controls { burst = true });
        Check(c.crossSlashes == 1 && c.focus == 50, "held burst input cannot restart a committed skill");
        c = Ready(); c.player.facing = -1; c.boss.x = -1.4f;
        Advance(c, 1.8f, new DuelCombat.Controls { burst = true });
        Check(c.boss.hp == DuelCombat.BossMaxHp - 55, "left-facing cross-slash matches right-facing damage");
        c = Ready(); c.boss.x = -1.4f;
        Advance(c, 1.8f, new DuelCombat.Controls { burst = true });
        Check(c.boss.hp == DuelCombat.BossMaxHp, "cross-slash cannot hit behind its locked facing");
        c = Ready(); c.boss.x = 7;
        Advance(c, 1.8f, new DuelCombat.Controls { burst = true });
        Check(c.boss.hp == DuelCombat.BossMaxHp, "cross-slash respects distance rather than auto-hitting");
        c = Ready(); c.Step(Dt, new DuelCombat.Controls { burst = true });
        c.player.y = .001f; c.player.vy = -20; c.Step(Dt, default);
        Advance(c, 1.2f);
        Check(c.cues.FindAll(q => q.kind == "CrossHit").Count == 0,
            "forced early landing removes all remaining cross-slash hitboxes");
        c = Ready(); c.Step(Dt, new DuelCombat.Controls { burst = true });
        Advance(c, .24f);
        var sword = new DuelCombat.FallingSword { x = c.player.x };
        sword.age = sword.ImpactTime - Dt * .5f; c.swords.Add(sword);
        c.Step(Dt, default); Advance(c, .8f);
        Check(c.hitsTaken == 1 && c.cues.FindAll(q => q.kind == "CrossHit").Count == 1,
            "incoming sky sword interrupts burst and cancels later cuts");
        c = Ready(); c.focus = 0; Advance(c, .3f, new DuelCombat.Controls { attack = true });
        Check(c.focus == 4, "ordinary contact replenishes focus");
        c = Ready(); c.playerMode = DuelCombat.PlayerMode.Attack; c.player.time = .13f; c.player.duration = .4f;
        c.enemyMode = DuelCombat.EnemyMode.Strike; c.pattern = 0; c.boss.time = 0;
        c.boss.duration = 1; c.boss.facing = -1;
        c.Step(Dt, default);
        Check(c.IsLocked && c.cues.Exists(q => q.kind == "Clash"),
            "one close ordinary sword clash immediately enters blade lock");
        c = Locked();
        Check(c.IsLocked && c.playerMode == DuelCombat.PlayerMode.BladeLock && c.parries == 3,
            "three close sword parries enter sustained blade lock through normal controls");
        float hp = c.player.hp;
        for (int i = 0; i < 600 && c.IsLocked; i++) c.Step(Dt, c.Demonstration());
        Check(c.locksWon == 1 && c.lockSuccess == 5 && c.player.hp == hp &&
            c.enemyMode == DuelCombat.EnemyMode.Stagger && c.boss.hp == DuelCombat.BossMaxHp - 30,
            "five timed beats win lock, damage once and open stagger punish");
        Advance(c, .6f, new DuelCombat.Controls { heavy = true });
        Check(c.boss.hp == DuelCombat.BossMaxHp - 78, "blade-lock victory chains into the existing critical punish");
        c = Locked(); Advance(c, 3f);
        Check(!c.IsLocked && c.player.hp == 86, "ignored lock times out with finite damage and releases controls");
        c = Locked();
        for (int i = 0; i < 90 && c.IsLocked; i++) c.Step(Dt, new DuelCombat.Controls { attack = true });
        Check(!c.IsLocked && c.locksWon == 0 && c.lockMisses >= 3, "button mashing cannot bypass the lock rhythm");
        c = Locked(); c.Step(Dt, new DuelCombat.Controls { dash = true });
        Check(!c.IsLocked && c.playerMode == DuelCombat.PlayerMode.Dash && c.player.hp == 100,
            "player can retreat from blade lock with a real stamina-costing dodge");
        c = Locked(); c.player.stamina = 0; c.Step(Dt, new DuelCombat.Controls { dash = true });
        Check(!c.IsLocked && c.playerMode == DuelCombat.PlayerMode.Hurt && c.player.hp == 86,
            "empty stamina cannot grant a free invulnerable escape from lock");
        c = Locked(); c.Reset();
        Check(!c.IsLocked && c.lockBeat == 0 && c.locksWon == 0 && c.focus == 50,
            "retry clears rhythm and skill resources");
        foreach (bool rage in new[] { false, true })
        {
            c = Ready(); c.secondPhase = rage; c.pattern = 2; c.strike = 0;
            c.enemyMode = DuelCombat.EnemyMode.Windup; c.boss.time = 0; c.boss.duration = .7f;
            for (int i = 0; i < 600 && c.enemyMode != DuelCombat.EnemyMode.Recover; i++)
                c.Step(Dt, default);
            Check(c.leapSlams == (rage ? 3 : 2) && c.enemyMode == DuelCombat.EnemyMode.Recover,
                (rage ? "rage" : "first phase") + " leap chain completes expected landings then exposes recovery");
            c = Ready(); c.secondPhase = rage; c.pattern = 3; c.strike = 0; c.boss.facing = -1;
            c.enemyMode = DuelCombat.EnemyMode.Windup; c.boss.duration = .9f;
            for (int i = 0; i < 600 && c.pattern == 3 && c.enemyMode != DuelCombat.EnemyMode.Recover; i++)
                c.Step(Dt, default);
            Check(c.waveCasts == (rage ? 3 : 2), (rage ? "rage" : "first phase") + " emits every telegraphed ground wave");
            if (rage) Check(c.pattern == 1 && c.enemyMode == DuelCombat.EnemyMode.Windup,
                "rage waves lead to a separately telegraphed melee lunge");
        }
        c = new DuelCombat(); c.Step(Dt, new DuelCombat.Controls { skip = true });
        Check(c.enemyMode == DuelCombat.EnemyMode.Approach && c.boss.y == 0 && c.player.hp == 100,
            "skip entrance lands boss safely and leaves approach grace");
        c = new DuelCombat();
        Check(c.boss.duration == DuelCombat.IntroDuration && c.boss.duration >= 7,
            "boss entrance leaves enough time for a visible push, hold and pull camera shot");
        Advance(c, DuelCombat.IntroDuration + .1f);
        Check(c.enemyMode == DuelCombat.EnemyMode.Approach,
            "boss does not begin attacking until the entrance camera has returned to the arena");
        c = Ready(); c.enemyMode = DuelCombat.EnemyMode.Approach;
        c.boss.hp = DuelCombat.BossMaxHp * .5f;
        c.Step(Dt, default);
        Check(c.enemyMode == DuelCombat.EnemyMode.Phase, "half-health transition starts a dedicated rage cinematic");
        float phaseClock = c.clock, phaseHp = c.player.hp, phaseBossHp = c.boss.hp;
        Advance(c, 5.8f, new DuelCombat.Controls { attack = true, heavy = true, parry = true, dash = true });
        Check(c.enemyMode == DuelCombat.EnemyMode.Phase && c.clock == phaseClock &&
            c.player.hp == phaseHp && c.boss.hp == phaseBossHp && c.playerMode == DuelCombat.PlayerMode.Ready,
            "rage cinematic freezes combat time, damage and player actions through the close-up hold");
        Advance(c, DuelCombat.PhaseDuration - 5.8f + .02f);
        Check(c.enemyMode == DuelCombat.EnemyMode.Windup,
            "rage cinematic returns to the arena before the next attack begins");
        Check(File.Exists("Assets/Resources/Art/EmberBlade.png"), "painted eight-frame ember slash atlas is present");
        foreach (var side in new[] { "L", "R" })
            Check(File.Exists("Assets/Resources/Art/" + side + "_BladeBind.png"),
                side + " dedicated planted blade-bind animation exists");
        for (int seed = 0; seed < 24; seed++)
        {
            var random = new System.Random(seed); c = new DuelCombat();
            for (int tick = 0; tick < 120 * 90 && !c.won && !c.lost; tick++)
            {
                bool press = tick % 11 == 0;
                c.Step(Dt, new DuelCombat.Controls {
                    move = random.Next(3) - 1, attack = press && random.Next(3) == 0,
                    heavy = press && random.Next(7) == 0, jump = press && random.Next(5) == 0,
                    dash = press && random.Next(5) == 0, parry = press && random.Next(3) == 0,
                    special = press && random.Next(5) == 0, burst = press && random.Next(7) == 0,
                    heal = press && random.Next(9) == 0
                });
                if (float.IsNaN(c.player.x) || float.IsNaN(c.boss.y) || c.player.stamina < 0 ||
                    c.focus < 0 || c.focus > 100 || c.player.hp < 0 || c.boss.hp < 0 ||
                    c.player.x < DuelCombat.Left || c.player.x > DuelCombat.Right ||
                    c.IsLocked != (c.playerMode == DuelCombat.PlayerMode.BladeLock))
                    throw new Exception("Random input invariant failed: seed " + seed + ", tick " + tick);
                c.cues.Clear();
            }
        }
        Check(true, "24 deterministic random-input runs preserve state, arena, health and resource invariants");
    }
    static void ChargeChecks()
    {
        var c = Ready();
        Advance(c, .3f, new DuelCombat.Controls { attack = true });
        float dealt = DuelCombat.BossMaxHp - c.boss.hp;
        Check(dealt > 0 && Mathf.Approximately(c.chargeEnergy, dealt * DuelCombat.ChargeGainPerDamage),
            "only actual boss HP damage grants proportional charge energy");

        c = Ready(); c.boss.x = 8;
        Advance(c, .3f, new DuelCombat.Controls { attack = true });
        Check(c.boss.hp == DuelCombat.BossMaxHp && c.chargeEnergy == 0,
            "whiffed attacks grant no charge energy");

        c = Ready(); c.playerMode = DuelCombat.PlayerMode.Attack; c.player.time = .13f;
        c.player.duration = .4f; c.enemyMode = DuelCombat.EnemyMode.Strike; c.pattern = 0;
        c.boss.time = 0; c.boss.duration = 1; c.boss.facing = -1;
        c.Step(Dt, default);
        Check(c.IsLocked && c.chargeEnergy == 0, "blade clashes grant no charge energy");
        c = Locked();
        Check(c.parries == 3 && c.chargeEnergy == 0, "successful parries grant no charge energy");

        c = Ready(); c.boss.hp = 4;
        Advance(c, .3f, new DuelCombat.Controls { attack = true });
        Check(c.won && c.boss.hp == 0 && Mathf.Approximately(c.chargeEnergy, .8f),
            "lethal hits grant charge only for the boss HP actually remaining");

        c = Ready(); c.chargeEnergy = DuelCombat.ChargeEnergyMax - 1;
        Advance(c, .3f, new DuelCombat.Controls { attack = true });
        int readyCues = c.cues.FindAll(q => q.kind == "ChargeReady").Count;
        Advance(c, .5f, new DuelCombat.Controls { attack = true });
        Check(c.chargeEnergy == DuelCombat.ChargeEnergyMax && readyCues == 1 &&
            c.cues.FindAll(q => q.kind == "ChargeReady").Count == 1,
            "full charge emits one ready cue on threshold crossing, not on every later hit");

        c = Ready(); c.chargeEnergy = DuelCombat.ChargeEnergyMax - .1f;
        c.Step(Dt, new DuelCombat.Controls { attack = true, charge = true });
        Check(c.chargeEnergy == DuelCombat.ChargeEnergyMax - .1f &&
            c.playerMode == DuelCombat.PlayerMode.Ready,
            "less than a full charge meter rejects activation without spending resources");

        c = Ready(); c.chargeEnergy = DuelCombat.ChargeEnergyMax; c.player.stamina = 99;
        c.staminaDelay = 1;
        c.Step(Dt, new DuelCombat.Controls { charge = true });
        Check(c.chargeEnergy == DuelCombat.ChargeEnergyMax && c.player.stamina == 99 &&
            c.playerMode == DuelCombat.PlayerMode.Ready,
            "less than full stamina rejects activation without spending charge energy");

        c = Ready(); c.chargeEnergy = DuelCombat.ChargeEnergyMax;
        c.Step(Dt, new DuelCombat.Controls { charge = true });
        Check(c.playerMode == DuelCombat.PlayerMode.Charging && c.chargeEnergy == 0 &&
            c.player.stamina == 0, "charge activation immediately consumes both full resource bars");
        c.Step(Dt, new DuelCombat.Controls { parry = true, attack = true, heavy = true });
        Check(c.playerMode == DuelCombat.PlayerMode.Charging && c.player.stamina == 0,
            "charging blocks defense, attacks and stamina recovery");
        c.Step(Dt, new DuelCombat.Controls { charge = true });
        Check(c.playerMode == DuelCombat.PlayerMode.Ready && c.chargeEnergy == 0 &&
            c.player.stamina == 0 && c.cues.Exists(q => q.kind == "ChargeCanceled"),
            "manual interruption refunds neither energy nor stamina");

        c = Ready(); c.chargeEnergy = DuelCombat.ChargeEnergyMax;
        c.Step(Dt, new DuelCombat.Controls { charge = true });
        var sword = new DuelCombat.FallingSword { x = c.player.x };
        sword.age = sword.ImpactTime - Dt * .5f; c.swords.Add(sword);
        c.Step(Dt, default);
        Check(c.playerMode == DuelCombat.PlayerMode.Hurt && c.chargeEnergy == 0 &&
            c.player.stamina == 0 && c.cues.Exists(q => q.kind == "ChargeInterrupted"),
            "taking a hit interrupts charging and spends both resources permanently");

        c = Ready(); c.chargeEnergy = DuelCombat.ChargeEnergyMax;
        c.Step(Dt, new DuelCombat.Controls { charge = true });
        Advance(c, DuelCombat.ChargeDuration + .02f);
        Check(c.playerMode == DuelCombat.PlayerMode.Attack &&
            Mathf.Approximately(c.chargeEnergy, DuelCombat.ChargeDamage * DuelCombat.ChargeGainPerDamage) &&
            c.player.stamina < 100,
            "completed charge spends its resources, then its actual damage earns charge energy normally");
        Check(c.boss.hp == DuelCombat.BossMaxHp - DuelCombat.ChargeDamage,
            "completed forward charge slash deals exactly 180 damage");

        c = Ready(); c.boss.x = 7; c.chargeEnergy = DuelCombat.ChargeEnergyMax;
        c.Step(Dt, new DuelCombat.Controls { charge = true });
        Advance(c, DuelCombat.ChargeDuration + .02f);
        Check(c.playerMode == DuelCombat.PlayerMode.Attack &&
            c.boss.hp == DuelCombat.BossMaxHp && c.chargeEnergy == 0 && c.player.stamina < 100 &&
            c.cues.Exists(q => q.kind == "ChargeMiss"),
            "completed charge can miss outside its forward range without refunding resources");
    }
    static void AttackRhythmChecks()
    {
        var c = Ready();
        c.Step(Dt, new DuelCombat.Controls { attack = true });
        Check(c.playerMode == DuelCombat.PlayerMode.Attack && c.combo == 0 &&
            Mathf.Approximately(c.player.duration, .4f),
            "first light attack keeps its original startup and duration");
        Advance(c, .01f);
        c.Step(Dt, new DuelCombat.Controls { attack = true });
        Advance(c, .08f);
        Check(c.combo == 0 && c.playerMode == DuelCombat.PlayerMode.Attack,
            "too-early follow-up does not buffer or accelerate the current attack");

        c = Ready();
        c.Step(Dt, new DuelCombat.Controls { attack = true });
        Advance(c, .15f);
        c.Step(Dt, new DuelCombat.Controls { attack = true });
        Check(c.playerMode == DuelCombat.PlayerMode.Attack && !c.RhythmWindowOpen &&
            c.cues.FindAll(q => q.kind == "ComboRhythm").Count == 0,
            "slightly early follow-up is buffered until the visible rhythm window");
        Advance(c, .18f);
        Check(c.combo == 1 && c.cues.Exists(q => q.kind == "ComboRhythm"),
            "early buffered follow-up confirms as soon as the rhythm window opens");
        Advance(c, .11f);
        c.Step(Dt, new DuelCombat.Controls { attack = true });
        Advance(c, .3f);
        Check(c.combo == 2 && c.playerMode == DuelCombat.PlayerMode.Attack,
            "same forgiving input window chains the second hit into the third");

        c = Ready();
        c.Step(Dt, new DuelCombat.Controls { attack = true });
        Advance(c, .25f);
        c.Step(Dt, new DuelCombat.Controls { attack = true });
        Advance(c, .08f);
        Check(c.playerMode == DuelCombat.PlayerMode.Attack && c.combo == 1 &&
            c.cues.Exists(q => q.kind == "ComboRhythm"),
            "on-beat follow-up shortens recovery and cleanly enters the next combo");

        c = Ready();
        c.Step(Dt, new DuelCombat.Controls { attack = true });
        Advance(c, .37f);
        c.Step(Dt, new DuelCombat.Controls { attack = true });
        Advance(c, .02f);
        Check(c.playerMode == DuelCombat.PlayerMode.Attack && c.combo == 1,
            "late follow-up continues the light combo without cutting off the current swing");

        c = Ready(); c.secondPhase = true; c.bossCycle = 5;
        c.chargeEnergy = DuelCombat.ChargeEnergyMax;
        Check(!c.Demonstration().charge,
            "showcase AI leaves the telegraphed boss rush visible before committing its finisher");

        c = Ready();
        c.Step(Dt, new DuelCombat.Controls { attack = true });
        Advance(c, 1.1f);
        c.Step(Dt, new DuelCombat.Controls { attack = true });
        Check(c.playerMode == DuelCombat.PlayerMode.Attack && c.combo == 0,
            "waiting beyond combo grace resets the next light attack to its opener");

        c = Ready();
        var rapidSwingSegments = new HashSet<int>();
        for (int i = 0; i < 240; i++)
        {
            c.Step(Dt, new DuelCombat.Controls { attack = i % 12 == 0 });
            foreach (var cue in c.cues)
                if (cue.kind == "PlayerSwing") rapidSwingSegments.Add(cue.strength);
            c.cues.Clear();
        }
        Check(rapidSwingSegments.Contains(1) && rapidSwingSegments.Contains(2) && rapidSwingSegments.Contains(3),
            "continuous 10 Hz light taps produce all three distinct swing segments");

        c = Ready();
        var steadySwingSegments = new HashSet<int>();
        float nextTap = 0;
        for (int i = 0; i < 480; i++)
        {
            float time = i * Dt;
            bool tap = time + .0001f >= nextTap;
            if (tap) nextTap += .5f;
            c.Step(Dt, new DuelCombat.Controls { attack = tap });
            foreach (var cue in c.cues)
                if (cue.kind == "PlayerSwing") steadySwingSegments.Add(cue.strength);
            c.cues.Clear();
        }
        Check(steadySwingSegments.Contains(1) && steadySwingSegments.Contains(2) && steadySwingSegments.Contains(3),
            "continuous 2 Hz light taps progress through all three swing segments");
    }
    public static void Test()
    {
        results.Clear();
        var opening = new DuelCombat();
        opening.Step(Dt, new DuelCombat.Controls { jump = true });
        Check(opening.player.vy > 0, "first jump is accepted during boss entrance");
        opening.Reset();
        opening.Step(Dt, new DuelCombat.Controls { attack = true });
        Check(opening.playerMode == DuelCombat.PlayerMode.Attack, "first attack is accepted during boss entrance");
        Check(!SoulBladePrototype.TryGetStoryDialogue(.9f, false, out _, out _, out _),
            "story dialogue stays hidden before the entrance close-up");
        Check(SoulBladePrototype.TryGetStoryDialogue(1.5f, false, out var bossSpeaker, out var openingLine, out _) &&
            bossSpeaker && openingLine == "此处，不容无誓之刃。",
            "entrance dialogue opens with the Warden's portrait and line");
        Check(SoulBladePrototype.TryGetStoryDialogue(4.2f, false, out bossSpeaker, out var reply, out _) &&
            !bossSpeaker && reply == "那便以剑，回答你。",
            "entrance dialogue hands off to the player's portrait and reply");
        Check(SoulBladePrototype.TryGetStoryDialogue(1.5f, true, out bossSpeaker, out var phaseLine, out _) &&
            bossSpeaker && phaseLine == "余烬未尽，便再燃一次。",
            "rage transition uses its own Warden dialogue");
        var phaseSkip = Ready(); phaseSkip.secondPhase = true; phaseSkip.enemyMode = DuelCombat.EnemyMode.Phase;
        phaseSkip.Step(Dt, new DuelCombat.Controls { skip = true });
        Check(phaseSkip.enemyMode == DuelCombat.EnemyMode.Approach,
            "Enter skips the rage dialogue and returns control to combat");
        foreach (float width in new[] { 640f, 1280f })
        {
            Rect heroPortrait = SoulBladePrototype.StoryPortraitBounds(width, 720, false);
            Rect bossPortrait = SoulBladePrototype.StoryPortraitBounds(width, 720, true);
            Check(heroPortrait.xMin >= 0 && heroPortrait.xMax <= width &&
                heroPortrait.yMin >= 0 && heroPortrait.yMax <= 720 &&
                bossPortrait.xMin >= 0 && bossPortrait.xMax <= width &&
                bossPortrait.yMin >= 0 && bossPortrait.yMax <= 720,
                "dialogue portraits fit on-screen at width " + width);
            Check(Mathf.Approximately(heroPortrait.xMin, 0) &&
                Mathf.Approximately(bossPortrait.xMax, width),
                "dialogue portraits align flush with their speaker's screen edge at width " + width);
        }
        var c = Ready();
        Advance(c, .35f, new DuelCombat.Controls { attack = true });
        Check(c.boss.hp == DuelCombat.BossMaxHp - 9, "one light swing damages once");
        c = Ready(); c.boss.x = -1.1f;
        Advance(c, .35f, new DuelCombat.Controls { attack = true });
        Check(c.boss.hp == DuelCombat.BossMaxHp, "forward swing cannot hit behind player");
        c = Ready(); c.boss.x = 5;
        Advance(c, .35f, new DuelCombat.Controls { attack = true });
        Check(c.boss.hp == DuelCombat.BossMaxHp, "out of range cannot hit");
        c = Ready(); c.boss.y = 3; c.boss.vy = 1;
        Advance(c, .18f, new DuelCombat.Controls { attack = true });
        Check(c.boss.hp == DuelCombat.BossMaxHp, "vertical hurt boxes reject air mismatch");
        c = Ready(); c.player.stamina = 0; c.staminaDelay = 1;
        c.Step(Dt, new DuelCombat.Controls { attack = true, dash = true });
        Check(c.playerMode == DuelCombat.PlayerMode.Ready, "no stamina prevents attacks and dash");
        c = Ready(); Advance(c, .01f, new DuelCombat.Controls { parry = true });
        Check(c.ParryActive, "parry opens on input");
        Advance(c, .17f);
        Check(!c.ParryActive, "parry window expires");
        float oldTime = c.player.time;
        c.Step(Dt, new DuelCombat.Controls { parry = true });
        Check(c.player.time > oldTime && c.parryCooldown > 0, "failed parry cannot restart by spam");
        c = Ready(); c.enemyMode = DuelCombat.EnemyMode.Strike;
        c.boss.duration = .24f; c.pattern = 0; c.boss.facing = -1;
        c.Step(Dt, new DuelCombat.Controls { parry = true });
        Check(c.parries == 1 && c.player.hp == 100 && c.boss.posture == 34, "timed front parry protects and adds posture");
        Advance(c, .1f);
        Check(c.parries == 1, "enemy strike cannot parry twice");
        c = Ready(); c.player.facing = -1; c.enemyMode = DuelCombat.EnemyMode.Strike;
        c.boss.duration = .24f; c.pattern = 0; c.boss.facing = -1;
        c.Step(Dt, new DuelCombat.Controls { parry = true });
        Check(c.player.hp < 100 && c.parries == 0, "back-facing guard fails");
        c = Ready(); c.enemyMode = DuelCombat.EnemyMode.Strike;
        c.boss.duration = .24f; c.pattern = 0; c.boss.facing = -1;
        c.Step(Dt, new DuelCombat.Controls { dash = true });
        Check(c.player.hp == 100 && c.player.invincible > 0, "dash has invulnerability");
        c = Ready(); c.boss.x = -6;
        c.Step(Dt, new DuelCombat.Controls { dash = true, move = 1 });
        float staminaAfterDash = c.player.stamina;
        Advance(c, .4f);
        Check(Mathf.Abs(c.player.x - 2.88f) < .02f && c.playerMode == DuelCombat.PlayerMode.Ready,
            "complete dodge moves 2.88 units and returns to ready");
        Check(staminaAfterDash == 78 && c.player.invincible == 0, "dodge costs once and recovery is not invulnerable");
        Check(DuelCombat.DodgeTravel(.04f) < .1f && DuelCombat.DodgeTravel(.17f) > 2.2f &&
            DuelCombat.DodgeTravel(.27f) == DuelCombat.DodgeTravel(.38f), "dodge has preparation, burst, brake and planted recovery");
        c = Ready(); c.boss.x = 6; c.player.facing = -1;
        Advance(c, .4f, new DuelCombat.Controls { dash = true });
        Check(Mathf.Abs(c.player.x + 2.88f) < .02f && c.player.facing == -1, "no-direction dodge follows facing to the left");
        c = Ready(); c.boss.x = -6;
        c.Step(Dt, new DuelCombat.Controls { dash = true, move = 1 });
        Advance(c, .15f, new DuelCombat.Controls { move = -1 });
        Check(c.player.x > 1.8f && c.player.facing == 1, "dodge direction cannot flip midway");
        Advance(c, .14f);
        c.Step(Dt, new DuelCombat.Controls { attack = true });
        Check(c.playerMode == DuelCombat.PlayerMode.Attack, "late dodge recovery can transition into attack");
        c = Ready(); c.boss.x = -6;
        c.Step(Dt, new DuelCombat.Controls { dash = true, move = 1 });
        Advance(c, .1f); c.Step(Dt, new DuelCombat.Controls { attack = true }); Advance(c, .18f);
        Check(c.playerMode == DuelCombat.PlayerMode.Attack, "attack buffered during dodge executes after braking");
        c = Ready(); c.boss.x = -6; c.player.stamina = 21;
        c.Step(Dt, new DuelCombat.Controls { dash = true });
        Check(c.playerMode != DuelCombat.PlayerMode.Dash, "insufficient stamina cannot start dodge animation");
        c = Ready(); c.boss.x = -6; c.player.y = 1; c.player.vy = 0;
        Advance(c, .3f, new DuelCombat.Controls { dash = true });
        Check(c.player.y == 0, "air dodge does not suspend gravity");
        Check(DuelCombat.DodgeFrame(0) == 0 && DuelCombat.DodgeFrame(.12f) == 5 &&
            DuelCombat.DodgeFrame(.36f) == 11, "dodge uses dedicated anticipation, low travel and recovery frames");
        c = Ready(); c.Step(Dt, new DuelCombat.Controls { special = true });
        Check(c.playerMode == DuelCombat.PlayerMode.Ready && c.player.stamina == 100,
            "special on the ground neither attacks nor consumes stamina");
        c = Ready(); c.Step(Dt, new DuelCombat.Controls { jump = true, special = true });
        Check(c.player.vy > 0 && c.playerMode == DuelCombat.PlayerMode.Ready,
            "jump and special on the ground do not auto-launch the special");
        Advance(c, .1f); c.Step(Dt, new DuelCombat.Controls { special = true });
        Check(c.playerMode == DuelCombat.PlayerMode.AirSpin, "jump followed by special starts the aerial skill");
        c = Ready(.8f); c.Step(Dt, new DuelCombat.Controls { attack = true });
        Check(c.playerMode == DuelCombat.PlayerMode.Attack && !c.airSkillUsed && c.combo == 0,
            "airborne normal attack remains a normal slash");
        c = Ready(.8f); c.Step(Dt, new DuelCombat.Controls { heavy = true });
        Check(c.playerMode == DuelCombat.PlayerMode.Attack && c.combo == 3 && !c.airSkillUsed,
            "airborne heavy attack remains separate from the special");
        c = Ready(.8f); c.Step(Dt, new DuelCombat.Controls { special = true });
        Check(c.player.stamina == 68 && c.airSkillUsed && c.player.invincible == 0,
            "air spin costs 32 once and grants no invulnerability");
        Advance(c, .09f);
        Check(c.boss.hp == DuelCombat.BossMaxHp, "air spin anticipation does not deal early damage");
        Advance(c, .025f);
        Check(c.boss.hp == DuelCombat.BossMaxHp - 9, "first spin segment hits once");
        Advance(c, .16f);
        Check(c.boss.hp == DuelCombat.BossMaxHp - 18, "second spin segment is a separate hit");
        Advance(c, .16f);
        Check(c.boss.hp == DuelCombat.BossMaxHp - 34 && c.boss.posture == 24,
            "third spin segment is stronger, total damage is 34 and posture damage 24");
        Advance(c, .8f);
        Check(c.boss.hp == DuelCombat.BossMaxHp - 34 && c.chain == 3,
            "one spin cannot damage more than three times");
        Check(c.player.y == 0 && c.playerMode == DuelCombat.PlayerMode.Ready && !c.airSkillUsed,
            "spin lands, recovers and restores the next jump's skill");
        c.cues.Clear(); Advance(c, .1f, new DuelCombat.Controls { jump = true });
        c.Step(Dt, new DuelCombat.Controls { special = true });
        Check(c.playerMode == DuelCombat.PlayerMode.AirSpin, "a fresh jump can use the special again");
        c = Ready(.8f); c.player.stamina = 31; c.staminaDelay = 1;
        c.Step(Dt, new DuelCombat.Controls { special = true });
        Check(c.playerMode == DuelCombat.PlayerMode.Ready && !c.airSkillUsed && c.player.stamina == 31,
            "insufficient stamina rejects the skill without consuming its air use");
        c = Ready(.8f);
        for (int i = 0; i < 60; i++) c.Step(Dt, new DuelCombat.Controls { special = true });
        Check(c.cues.FindAll(q => q.kind == "AirSpin").Count == 1 && c.player.stamina == 68,
            "special spam cannot restart the spin or spend twice");
        c = Ready(.8f); c.boss.x = 4;
        Advance(c, .6f, new DuelCombat.Controls { special = true });
        Check(c.boss.hp == DuelCombat.BossMaxHp, "spin cannot hit outside the visible blade radius");
        c = Ready(.8f); c.boss.x = -1.4f;
        Advance(c, .6f, new DuelCombat.Controls { special = true });
        Check(c.boss.hp == DuelCombat.BossMaxHp - 34, "rotating blade can hit behind the player");
        c = Ready(5);
        Advance(c, .6f, new DuelCombat.Controls { special = true });
        Check(c.boss.hp == DuelCombat.BossMaxHp, "air spin respects vertical separation");
        c = Ready(.8f); c.player.facing = -1; c.boss.x = -1.4f;
        Advance(c, .6f, new DuelCombat.Controls { special = true });
        Check(c.boss.hp == DuelCombat.BossMaxHp - 34 && c.player.facing == -1,
            "left-facing special has the same damage and keeps its facing");
        c = Ready(.8f); c.enemyMode = DuelCombat.EnemyMode.Phase; c.boss.duration = 2;
        Advance(c, .6f, new DuelCombat.Controls { special = true });
        Check(c.boss.hp == DuelCombat.BossMaxHp, "spin cannot damage phase-protected enemy");
        c = Ready(.8f); c.playerMode = DuelCombat.PlayerMode.Hurt; c.player.duration = .4f;
        c.Step(Dt, new DuelCombat.Controls { special = true });
        Check(c.playerMode == DuelCombat.PlayerMode.Hurt && !c.airSkillUsed,
            "special cannot cancel player hurt state");
        c = Ready(.8f);
        Advance(c, .2f, new DuelCombat.Controls { special = true });
        c.Step(Dt, new DuelCombat.Controls { dash = true, parry = true });
        Check(c.playerMode == DuelCombat.PlayerMode.AirSpin, "active spin commits before defensive cancel");
        Advance(c, .36f); c.Step(Dt, new DuelCombat.Controls { dash = true, move = -1 });
        Check(c.playerMode == DuelCombat.PlayerMode.Dash && c.airSkillUsed,
            "spin recovery can cancel to dodge without restoring its air use");
        Advance(c, .29f); c.Step(Dt, new DuelCombat.Controls { special = true });
        Check(c.playerMode != DuelCombat.PlayerMode.AirSpin, "canceling spin does not enable an immediate second spin");
        c = Ready(.8f); c.Step(Dt, new DuelCombat.Controls { special = true });
        var interrupt = new DuelCombat.FallingSword { x = 0 };
        interrupt.age = interrupt.ImpactTime - .15f; c.swords.Add(interrupt);
        Advance(c, .18f);
        Check(c.playerMode == DuelCombat.PlayerMode.Hurt && c.player.hp == 80,
            "sky sword interrupts the aerial special");
        Advance(c, .3f);
        Check(c.cues.FindAll(q => q.kind == "AirSpinHit").Count == 1,
            "interrupted spin stops all remaining damage segments");
        c = Ready(.8f); c.Step(Dt, new DuelCombat.Controls { special = true });
        c.player.y = .001f; c.player.vy = -4; c.Step(Dt, default);
        Check(c.playerMode == DuelCombat.PlayerMode.SpinLand && c.player.y == 0,
            "early ground contact immediately stops the aerial phase");
        c.Step(Dt, new DuelCombat.Controls { attack = true, special = true });
        Check(c.playerMode == DuelCombat.PlayerMode.SpinLand, "landing recovery cannot instantly restart an attack");
        Advance(c, .2f);
        Check(c.cues.FindAll(q => q.kind == "AirSpinHit").Count == 0,
            "early landing does not leave a hidden spinning hitbox");
        c = Ready(.8f); c.boss.hp = 8; c.swords.Add(new DuelCombat.FallingSword { x = 6 });
        Advance(c, .2f, new DuelCombat.Controls { special = true });
        Check(c.won && c.swords.Count == 0, "spin kill ends encounter and clears hazards");
        c.Reset();
        Check(!c.airSkillUsed && c.playerMode == DuelCombat.PlayerMode.Ready && c.player.vy == 0,
            "retry clears aerial skill state");
        Check(DuelCombat.AirSpinFrame(0) == 0 && DuelCombat.AirSpinFrame(.05f) == 1 &&
            DuelCombat.AirSpinFrame(.20f) == 6 && DuelCombat.AirSpinFrame(.44f) == 6 &&
            DuelCombat.AirSpinFrame(.7f) == 11, "aerial animation has preparation, two rotations and landing-ready finish");
        c = Ready(); c.pattern = 2; c.enemyMode = DuelCombat.EnemyMode.Strike;
        c.boss.y = 1; c.boss.vy = 5; c.boss.duration = 1;
        c.Step(Dt, default);
        Check(c.player.hp == 100, "leap does not hit during ascent");
        c = Ready(); c.enemyMode = DuelCombat.EnemyMode.Stagger;
        c.boss.y = 1; c.boss.vy = -1; c.boss.duration = 2;
        Advance(c, .6f);
        Check(c.boss.y == 0 && c.boss.vy == 0, "stunned airborne enemy lands");
        c = Ready(); c.boss.posture = 80; c.enemyMode = DuelCombat.EnemyMode.Strike;
        c.boss.duration = .24f; c.boss.facing = -1;
        c.Step(Dt, new DuelCombat.Controls { parry = true });
        Check(c.enemyMode == DuelCombat.EnemyMode.Stagger, "full posture triggers stagger");
        c = Ready(); c.enemyMode = DuelCombat.EnemyMode.Stagger; c.boss.duration = 2.5f;
        Advance(c, .5f, new DuelCombat.Controls { heavy = true });
        Check(c.boss.hp == DuelCombat.BossMaxHp - 48, "heavy punish becomes critical during stagger");
        c = Ready(); c.waves.Add(new DuelCombat.Wave { x = -.5f, direction = 1 });
        Advance(c, .05f);
        Check(c.player.hp < 100, "visible wave has moving ground collision");
        c = Ready(); c.player.y = 1.2f; c.waves.Add(new DuelCombat.Wave { x = -.5f, direction = 1 });
        Advance(c, .05f);
        Check(c.player.hp == 100, "jump clears ground wave");
        c = Ready(); c.player.hp = 20;
        Advance(c, 1.1f, new DuelCombat.Controls { heal = true });
        Check(c.player.hp == 58 && c.potions == 2, "healing consumes one flask after commitment");
        c = Ready(); c.player.x = DuelCombat.Right;
        Advance(c, 2, new DuelCombat.Controls { move = 1 });
        Check(c.player.x <= DuelCombat.Right, "arena bounds contain movement");
        c = Ready(); c.player.x = 1.3f;
        c.Step(Dt, default);
        Check(Mathf.Abs(c.player.x - c.boss.x) >= 1.319f, "grounded fighters cannot overlap");
        c = Ready(); c.boss.hp = 100; c.enemyMode = DuelCombat.EnemyMode.Approach;
        c.boss.duration = 5;
        c.Step(Dt, default);
        Check(c.secondPhase && c.enemyMode == DuelCombat.EnemyMode.Phase, "half health starts phase transition");
        c = Ready(); c.boss.hp = 1;
        Advance(c, .3f, new DuelCombat.Controls { attack = true });
        Check(c.won && c.enemyMode == DuelCombat.EnemyMode.Dead, "zero boss health wins");
        c = Ready(); c.player.hp = 1; c.enemyMode = DuelCombat.EnemyMode.Strike;
        c.boss.duration = .24f; c.boss.facing = -1;
        c.Step(Dt, default);
        Check(c.lost && c.playerMode == DuelCombat.PlayerMode.Dead, "zero player health enters defeat");
        c.Reset();
        Check(!c.won && !c.lost && c.waves.Count == 0 && c.cues.Count == 0 && c.player.hp == 100 &&
            c.boss.hp == DuelCombat.BossMaxHp && c.parryCooldown == 0, "retry clears transient combat state");
        c = Ready(); c.boss.x = -5; c.boss.facing = 1; c.pattern = DuelCombat.BladeRush;
        c.enemyMode = DuelCombat.EnemyMode.Strike; c.boss.duration = .55f;
        Advance(c, .5f);
        Check(c.hitsTaken == 1 && c.player.hp == 79, "rush hits once even on late contact after its first 130ms");
        c = Ready(); c.boss.x = -2.05f; c.boss.facing = 1; c.player.facing = -1;
        c.pattern = DuelCombat.BladeRush; c.enemyMode = DuelCombat.EnemyMode.Strike; c.boss.duration = .55f;
        c.Step(Dt, new DuelCombat.Controls { parry = true });
        Check(c.parries == 1 && c.enemyMode == DuelCombat.EnemyMode.Recover && c.boss.posture == 42,
            "front parry stops the entire blade rush and opens recovery");
        c = Ready(); c.pattern = DuelCombat.ShoulderRush; c.enemyMode = DuelCombat.EnemyMode.Strike;
        c.boss.duration = .44f; c.boss.facing = -1;
        c.Step(Dt, new DuelCombat.Controls { parry = true });
        Check(c.player.hp == 76 && c.parries == 0, "shoulder collision cannot be parried");
        c = Ready(); c.pattern = DuelCombat.ShoulderRush; c.enemyMode = DuelCombat.EnemyMode.Strike;
        c.boss.duration = .44f; c.boss.facing = -1; c.player.y = 1.3f; c.player.vy = 2;
        c.Step(Dt, default);
        Check(c.player.hp == 100, "jump clears shoulder hitbox");
        c = Ready(); c.pattern = DuelCombat.ShoulderRush; c.enemyMode = DuelCombat.EnemyMode.Strike;
        c.boss.duration = .44f; c.boss.facing = -1;
        c.Step(Dt, new DuelCombat.Controls { dash = true });
        Check(c.player.hp == 100, "dash can evade an unparryable shoulder");
        c = Ready(); c.pattern = DuelCombat.BladeRush; c.enemyMode = DuelCombat.EnemyMode.Strike;
        c.boss.duration = .55f; c.boss.facing = 1; c.player.x = -3;
        Advance(c, .5f);
        Check(c.boss.facing == 1 && c.boss.x <= DuelCombat.Right - .4f && c.player.hp == 100,
            "rush locks direction and respects arena bounds");
        c = Ready(); c.pattern = DuelCombat.ShoulderRush; c.enemyMode = DuelCombat.EnemyMode.Strike;
        c.boss.duration = .44f; c.boss.time = .44f; c.secondPhase = true; c.player.x = -5;
        c.Step(Dt, default);
        Check(c.enemyMode == DuelCombat.EnemyMode.Windup && c.strike == 1 && c.boss.duration >= .65f,
            "rage shoulder follow-up has its own readable windup");
        c = Ready(); c.pattern = DuelCombat.SkySwords; c.enemyMode = DuelCombat.EnemyMode.Windup;
        c.boss.duration = .75f;
        Advance(c, .7f);
        Check(c.swords.Count == 0 && c.player.hp == 100, "summon start cannot create an immediate hidden hit");
        c = Ready(); var sword = new DuelCombat.FallingSword { x = 0 }; c.swords.Add(sword);
        Advance(c, .79f);
        Check(c.player.hp == 100 && !sword.impacted, "sky sword has a harmless visible warning");
        Advance(c, .24f);
        Check(c.player.hp == 80 && c.hitsTaken == 1 && sword.impacted, "sky sword damages at impact");
        Advance(c, 1);
        Check(c.hitsTaken == 1 && c.swords.Count == 0, "embedded sword cannot repeatedly damage and expires");
        c = Ready(); c.boss.x = -5; sword = new DuelCombat.FallingSword { x = 0 }; c.swords.Add(sword);
        Advance(c, 1.1f, new DuelCombat.Controls { move = 1 });
        Check(sword.x == 0 && c.player.hp == 100, "marked sword never tracks a moving player after its warning");
        c = Ready(); sword = new DuelCombat.FallingSword { x = 0 }; sword.age = sword.ImpactTime - Dt * .5f;
        c.swords.Add(sword); c.Step(Dt, new DuelCombat.Controls { parry = true });
        Check(c.player.hp == 80 && c.parries == 0, "sky sword cannot be parried");
        c = Ready(); sword = new DuelCombat.FallingSword { x = 0 }; sword.age = sword.ImpactTime - Dt * .5f;
        c.swords.Add(sword); c.Step(Dt, new DuelCombat.Controls { dash = true });
        Check(c.player.hp == 100, "dash invulnerability works against falling sword");
        c = Ready(); sword = new DuelCombat.FallingSword { x = 3, great = true };
        sword.age = sword.ImpactTime - Dt * .5f; c.swords.Add(sword); c.Step(Dt, default);
        Check(c.waves.Count == 2 && c.waves[0].direction != c.waves[1].direction, "great sword launches opposing ground waves");
        c = Ready(); c.player.y = 1.3f; c.player.vy = 1;
        sword = new DuelCombat.FallingSword { x = 1.5f, great = true };
        sword.age = sword.ImpactTime - Dt * .5f; c.swords.Add(sword);
        Advance(c, .1f);
        Check(c.player.hp == 100, "jump avoids great sword shockwave outside the landing column");
        c.Reset();
        Check(c.swords.Count == 0 && c.waves.Count == 0, "reset clears falling swords and their waves");
        c = Ready(); c.boss.hp = 1; c.swords.Add(new DuelCombat.FallingSword { x = 0 });
        Advance(c, .3f, new DuelCombat.Controls { attack = true });
        Check(c.won && c.swords.Count == 0, "victory cancels pending sky swords");
        Check(SoulBladePrototype.CutInWeight(0) == 0 && SoulBladePrototype.CutInWeight(.34f) == 0 &&
            SoulBladePrototype.CutInWeight(.2f) > .99f, "brief cut-in returns exactly to normal camera");
        ReferenceChecks();
        ChargeChecks();
        AttackRhythmChecks();
        c = new DuelCombat();
        var seen = new HashSet<string>();
        int demoJumps = 0, demoSpecials = 0;
        for (int i = 0; i < 120 * 180 && !c.won && !c.lost; i++)
        {
            var demoInput = c.Demonstration();
            if (demoInput.jump) demoJumps++;
            if (demoInput.special) demoSpecials++;
            c.Step(Dt, demoInput);
            foreach (var cue in c.cues)
            {
                seen.Add(cue.kind);
                if (cue.kind == "SwordSummon" || cue.kind == "Rush" || cue.kind == "Shoulder" || cue.kind == "AirSpin")
                    results.Add("TIMING: " + c.clock.ToString("F2") + " " + cue.kind);
            }
            c.cues.Clear();
        }
        results.Add("DEMO: won=" + c.won + ", lost=" + c.lost + ", parries=" + c.parries +
            ", bossHP=" + c.boss.hp + ", playerHP=" + c.player.hp + ", events=" + string.Join(",", seen));
        Debug.Log(results[results.Count - 1] + ", jumpInputs=" + demoJumps + ", specialInputs=" + demoSpecials);
        Check(c.parries > 3 && seen.Contains("Break"), "live input path produces repeated parries and posture break");
        Check(c.won, "complete boss fight can be won through normal controls, without cheats");
        Check(seen.Contains("Shoulder") && seen.Contains("SwordSummon") && seen.Contains("SwordImpact"),
            "normal encounter demonstrates shoulder rush and rage sky swords");
        Check(seen.Contains("DashKick") && seen.Contains("DashBrake"), "normal combat demonstrates the complete dodge");
        Check(seen.Contains("AirSpin") && seen.Contains("AirSpinHit"), "normal combat demonstrates the aerial special");
        Check(seen.Contains("CrossHit") && seen.Contains("LockWin"), "normal combat demonstrates cross-slash and a won blade lock");
        var clipData = JsonUtility.FromJson<ClipManifest>(File.ReadAllText("Assets/Resources/Art/clips.json"));
        foreach (var side in new[] { "L", "R" })
        {
            var clip = Array.Find(clipData.clips, item => item.name == side + "_Dodge");
            Check(clip != null && clip.frames == 12 && File.Exists("Assets/Resources/Art/" + side + "_Dodge.png"),
                side + " dedicated dodge atlas contains twelve frames");
            clip = Array.Find(clipData.clips, item => item.name == side + "_AirSpin");
            Check(clip != null && clip.frames == 12 && File.Exists("Assets/Resources/Art/" + side + "_AirSpin.png"),
                side + " dedicated air spin atlas contains twelve frames");
        }
        var chibi = JsonUtility.FromJson<ChibiAtlasManifest>(
            File.ReadAllText("Assets/Resources/Art/ChibiAtlas.json"));
        Check(chibi != null && chibi.pages != null && chibi.pages.Length == 3 &&
            chibi.clips != null && chibi.clips.Length == 17,
            "Q-character atlas manifest contains three texture pages and seventeen action clips");
        int chibiFrameWidth = 0, chibiFrameHeight = 0;
        foreach (var required in new[] {
            "hero_idle:4", "hero_run:4", "hero_combo:8", "hero_jump:4", "hero_airspin:4",
            "hero_dodge:4", "hero_parry:4", "hero_charge:4", "hero_hurt:4",
            "boss_idle:4", "boss_run:4", "boss_rush:4", "boss_attack:4", "boss_leap:4",
            "boss_cast:4", "boss_rage:4", "boss_hurt:4"
        })
        {
            var parts = required.Split(':');
            var clip = Array.Find(chibi.clips, item => item.name == parts[0]);
            int expected = int.Parse(parts[1]);
            Check(clip != null && clip.frames != null && clip.frames.Length == expected,
                parts[0] + " has its dedicated " + expected + "-frame animation");
            Check(clip.size != null && clip.size.Length == 2 &&
                clip.pivot != null && clip.pivot.Length == 2,
                parts[0] + " declares a shared canvas size and pivot");
            foreach (var frame in clip.frames)
            {
                Check(frame.page >= 0 && frame.page < chibi.pages.Length &&
                    frame.x >= 0 && frame.y >= 0 && frame.width > 0 && frame.height > 0 &&
                    frame.durationMs > 0,
                    parts[0] + " atlas frames have valid pages, rectangles and timing");
                if (chibiFrameWidth == 0)
                {
                    chibiFrameWidth = frame.width;
                    chibiFrameHeight = frame.height;
                }
                Check(frame.width == chibiFrameWidth && frame.height == chibiFrameHeight &&
                    Mathf.Approximately(clip.size[0], chibiFrameWidth) &&
                    Mathf.Approximately(clip.size[1], chibiFrameHeight),
                    parts[0] + " keeps the shared character canvas size");
                Check(Mathf.Approximately(clip.pivot[0], .5f) &&
                    Mathf.Approximately(clip.pivot[1], 0.05078125f),
                    parts[0] + " keeps the shared foot anchor");
            }
        }
        foreach (float width in new[] { 640f, 960f, 1280f })
        {
            Rect panel = SoulBladePrototype.ControlHintsBounds(width);
            Check(panel.xMin > 327 && panel.xMax <= width - 20 && panel.yMin >= 0 && panel.yMax < 390,
                "top-right controls fit without covering health HUD at width " + width);
        }
        Directory.CreateDirectory("outputs/Verification");
        File.WriteAllLines("outputs/Verification/combat-tests.txt", results);
        Debug.Log(string.Join("\n", results));
    }
    [Serializable] sealed class ClipManifest { public ClipEntry[] clips; }
    [Serializable] sealed class ClipEntry { public string name; public int frames; }
    [Serializable] sealed class ChibiAtlasManifest { public ChibiAtlasPage[] pages; public ChibiAtlasClip[] clips; }
    [Serializable] sealed class ChibiAtlasPage { public string name; }
    [Serializable] sealed class ChibiAtlasClip
    {
        public string name;
        public float[] size, pivot;
        public ChibiAtlasFrame[] frames;
    }
    [Serializable] sealed class ChibiAtlasFrame
    {
        public int page, x, y, width, height;
        public float durationMs;
    }
    public static void Build()
    {
        Test();
        PlayerSettings.companyName = "Duel Workshop";
        PlayerSettings.productName = "Mossbound Duel";
        PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true; PlayerSettings.runInBackground = true;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        Directory.CreateDirectory("outputs/MossboundDuel");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/Main.unity" },
            locationPathName = "outputs/MossboundDuel/MossboundDuel.exe",
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Build failed: " + report.summary.result);
        Debug.Log("DUEL_BUILD_OK " + report.summary.totalSize);
    }
}
