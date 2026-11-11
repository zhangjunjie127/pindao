using System;
using System.Collections.Generic;
using UnityEngine;

// Fixed-step combat clock, independent of rendering, camera shake and hit-stop.
public sealed class DuelCombat
{
    public enum PlayerMode { Ready, Attack, Parry, Dash, Hurt, Heal, Dead, AirSpin, SpinLand, CrossSlash, BladeLock, Charging }
    public enum EnemyMode { Intro, Approach, Windup, Strike, Recover, Stagger, Phase, Dead, BladeLock }
    public struct Controls
    {
        public float move;
        public bool attack, heavy, parry, dash, jump, heal, special, burst, charge, skip;
    }
    public struct Cue
    {
        public string kind;
        public Vector2 at;
        public int strength;
        public Cue(string kind, Vector2 at, int strength = 1)
        { this.kind = kind; this.at = at; this.strength = strength; }
    }
    public sealed class Fighter
    {
        public float x, y, vy, hp, stamina = 100, posture, time, duration, invincible;
        public int facing = 1;
        public bool connected;
        public Vector2 Center => new Vector2(x, y + .85f);
    }
    public const float Left = -7.4f, Right = 7.4f, BossMaxHp = 520;
    public const float DodgeDuration = .38f, DodgeRecovery = .27f;
    public const float AirSpinStart = .08f, AirSpinEnd = .56f, AirSpinCost = 32;
    public const float AirSpinRadius = 1.5f, SpinLandingDuration = .18f;
    public const float CrossCost = 50, CrossEnd = 1.15f;
    public const float ChargeEnergyMax = 100, ChargeGainPerDamage = .2f;
    public const float ChargeDuration = 1.4f, ChargeDamage = 180, ChargeRange = 5.8f;
    public const int LockBeats = 5;
    public const float LockFirstBeat = .62f, LockInterval = .84f, LockWindow = .18f;
    public const float LockDuration = 4.45f;
    public const float IntroDuration = 7.2f, PhaseDuration = 7.4f;
    public const int BladeRush = 4, ShoulderRush = 5, SkySwords = 6;
    static readonly int[] FirstPatterns = { 0, 2, 3, BladeRush, 1, ShoulderRush };
    static readonly int[] RagePatterns = { 0, 2, ShoulderRush, SkySwords, 3, BladeRush, 1, SkySwords };
    public readonly Fighter player = new Fighter(), boss = new Fighter();
    public readonly List<Cue> cues = new List<Cue>();
    public readonly List<Wave> waves = new List<Wave>();
    public readonly List<FallingSword> swords = new List<FallingSword>();
    public PlayerMode playerMode;
    public EnemyMode enemyMode;
    public float clock, parryCooldown, staminaDelay, attackBuffer, heavyBuffer, comboGrace;
    public int combo, pattern, strike, potions, parries, bestChain, chain, hitsTaken, bossCycle;
    public bool secondPhase, won, lost;
    public bool airSkillUsed;
    public float focus, chargeEnergy, lockTime;
    public int lockBeat, lockSuccess, lockMisses, lockHitMask, locksWon, crossSlashes, leapSlams, waveCasts;
    public bool IsLocked => enemyMode == EnemyMode.BladeLock;
    public float NextLockBeat => LockFirstBeat + lockBeat * LockInterval;
    public float LandingTarget => jumpTarget;
    public bool RhythmWindowOpen => playerMode == PlayerMode.Attack &&
        player.time >= player.duration * .68f && player.time <= player.duration * .92f;
    public Vector2 LockContact => new Vector2((player.x + boss.x) * .5f + player.facing * .23f, 1.8f);
    public float lastHit, phaseTime;
    float dashDirection, jumpTarget, bossMoveStart, healAt;
    int summonedSwords;
    int spinHitMask, spinSweep;
    int crossHitMask, crossSweep, parryStreak;
    float lastParry, lockInputCooldown;
    float attackEndTime;
    bool introLanded;
    bool swingEmitted, bossSwingEmitted, jumpStarted, phaseTriggered, dashKicked, dashBraked;
    bool attackFollowupQueued, rhythmFollowupQueued;
    public sealed class Wave
    {
        public float x, direction, age;
        public bool spent;
    }
    public sealed class FallingSword
    {
        public float x, age;
        public bool great, impacted;
        public float Warning => great ? 1.05f : .8f;
        public float FallTime => great ? .28f : .22f;
        public float ImpactTime => Warning + FallTime;
        public float Radius => great ? 1.12f : .64f;
        public float TipHeight => age < Warning ? 7.6f :
            Mathf.Lerp(7.6f, -.18f, Mathf.Pow(Mathf.Clamp01((age - Warning) / FallTime), 2));
    }
    public DuelCombat() { Reset(); }
    public void Reset()
    {
        player.x = -4.4f; player.y = player.vy = 0;
        player.hp = player.stamina = 100; player.time = player.invincible = 0;
        player.facing = 1; player.connected = false;
        boss.x = 3.5f; boss.y = boss.vy = 0; boss.hp = BossMaxHp;
        boss.facing = -1; boss.time = boss.posture = boss.invincible = 0;
        boss.connected = false; boss.duration = IntroDuration;
        playerMode = PlayerMode.Ready; enemyMode = EnemyMode.Intro;
        clock = parryCooldown = staminaDelay = attackBuffer = heavyBuffer = comboGrace = 0;
        combo = pattern = strike = parries = chain = bestChain = hitsTaken = bossCycle = 0;
        potions = 3; secondPhase = won = lost = phaseTriggered = false;
        swingEmitted = bossSwingEmitted = jumpStarted = dashKicked = dashBraked = false;
        attackEndTime = 0; attackFollowupQueued = rhythmFollowupQueued = false;
        lastHit = 0; summonedSwords = 0;
        airSkillUsed = false; spinHitMask = spinSweep = 0;
        focus = 50; chargeEnergy = 0; crossHitMask = crossSweep = parryStreak = 0; lastParry = -10;
        lockTime = lockInputCooldown = 0; lockBeat = lockSuccess = lockMisses = lockHitMask = 0;
        locksWon = crossSlashes = leapSlams = waveCasts = 0; introLanded = false;
        cues.Clear(); waves.Clear(); swords.Clear();
    }
    public void Step(float dt, Controls input)
    {
        if (won || lost) { player.time += dt; boss.time += dt; return; }
        if (enemyMode == EnemyMode.Phase)
        {
            if (input.skip) { SetEnemy(EnemyMode.Approach, .45f); return; }
            boss.time += dt;
            EnemyStep(dt);
            return;
        }
        clock += dt;
        player.time += dt; boss.time += dt;
        if (IsLocked) { StepBladeLock(dt, input); return; }
        if (input.skip && enemyMode == EnemyMode.Intro)
        {
            boss.y = boss.vy = 0; introLanded = true; SetEnemy(EnemyMode.Approach, .7f);
        }
        parryCooldown = Mathf.Max(0, parryCooldown - dt);
        staminaDelay = Mathf.Max(0, staminaDelay - dt);
        attackBuffer = Mathf.Max(0, attackBuffer - dt);
        heavyBuffer = Mathf.Max(0, heavyBuffer - dt);
        comboGrace = Mathf.Max(0, comboGrace - dt);
        player.invincible = Mathf.Max(0, player.invincible - dt);
        if (playerMode != PlayerMode.Charging && input.attack)
        {
            if (playerMode != PlayerMode.Attack) attackBuffer = .18f;
            else if (!attackFollowupQueued)
            {
                float beatStart = player.duration * .68f;
                float beatEnd = player.duration * .92f;
                if (player.time <= beatEnd && player.time >= beatStart - .22f)
                {
                    attackBuffer = .22f;
                    attackFollowupQueued = true;
                    if (player.time >= beatStart) ConfirmRhythmFollowup();
                }
                else if (player.time > beatEnd)
                { attackBuffer = .18f; attackFollowupQueued = true; comboGrace = 0; }
            }
        }
        if (playerMode != PlayerMode.Charging && input.heavy) heavyBuffer = .18f;
        if (staminaDelay <= 0 && playerMode != PlayerMode.Attack &&
            playerMode != PlayerMode.AirSpin && playerMode != PlayerMode.CrossSlash &&
            playerMode != PlayerMode.Charging)
            player.stamina = Mathf.Min(100, player.stamina + dt * 28);
        if (enemyMode != EnemyMode.Stagger && clock - lastHit > 3.5f)
            boss.posture = Mathf.Max(0, boss.posture - dt * 6);

        Gravity(player, dt);
        Gravity(boss, dt);
        PlayerStep(dt, input);
        EnemyStep(dt);
        SeparateGroundedFighters();
        if (playerMode == PlayerMode.Attack) TryPlayerHit();
        if (playerMode == PlayerMode.AirSpin) TryAirSpinHit();
        if (playerMode == PlayerMode.CrossSlash) TryCrossHit();
        if (enemyMode == EnemyMode.Strike) TryEnemyHit();
        if (IsLocked) return;
        StepSwords(dt);
        for (int i = waves.Count - 1; i >= 0; --i)
        {
            var wave = waves[i];
            wave.x += wave.direction * dt * (secondPhase ? 7.8f : 6.1f);
            wave.age += dt;
            if (!wave.spent && Mathf.Abs(wave.x - player.x) < .6f && player.y < .62f)
            {
                wave.spent = true;
                if (player.invincible <= 0) HurtPlayer(16, (int)wave.direction);
                else Emit("Evade", player.Center);
            }
            if (wave.age > 3 || wave.spent) waves.RemoveAt(i);
        }
        player.x = Mathf.Clamp(player.x, Left, Right);
        boss.x = Mathf.Clamp(boss.x, Left + .4f, Right - .4f);
        if (boss.hp <= BossMaxHp * .5f && !phaseTriggered && enemyMode == EnemyMode.Approach)
        {
            phaseTriggered = secondPhase = true;
            SetEnemy(EnemyMode.Phase, PhaseDuration); Emit("Phase", boss.Center);
        }
    }
    void SeparateGroundedFighters()
    {
        if (player.y > .3f || boss.y > .3f || playerMode == PlayerMode.Dash || won || lost) return;
        if (enemyMode == EnemyMode.Strike && (pattern == BladeRush || pattern == ShoulderRush)) return;
        const float gap = 1.32f;
        if (Mathf.Abs(player.x - boss.x) >= gap) return;
        float side = player.x < boss.x ? -1 : player.x > boss.x ? 1 : -boss.facing;
        player.x = Mathf.Clamp(boss.x + side * gap, Left, Right);
        if (Mathf.Abs(player.x - boss.x) < gap)
            boss.x = Mathf.Clamp(player.x - side * gap, Left + .4f, Right - .4f);
    }
    void Gravity(Fighter f, float dt)
    {
        if (f.y <= 0 && f.vy <= 0) return;
        bool spinning = f == player && playerMode == PlayerMode.AirSpin;
        bool crossing = f == player && playerMode == PlayerMode.CrossSlash;
        bool floating = spinning && player.time < AirSpinEnd;
        if (crossing && player.time > .3f && player.time < CrossEnd) f.vy = -.15f;
        if (crossing && player.time >= CrossEnd) f.vy = Mathf.Min(f.vy, -8);
        if (spinning && !floating) f.vy = Mathf.Min(f.vy, -5.2f);
        f.vy -= (floating || crossing && player.time < CrossEnd ? 3.5f : 24) * dt; f.y += f.vy * dt;
        if (f.y <= 0)
        {
            f.y = f.vy = 0;
            Emit("Land", new Vector2(f.x, 0), f == boss ? 2 : 1);
            if (f == player)
            {
                airSkillUsed = false;
                if (spinning || crossing)
                {
                    SetPlayer(PlayerMode.SpinLand, SpinLandingDuration);
                    Emit("SpinLand", new Vector2(f.x, 0));
                }
            }
        }
    }
    void PlayerStep(float dt, Controls input)
    {
        if (playerMode == PlayerMode.Dead) return;
        if (playerMode == PlayerMode.Charging)
        {
            if (input.charge) CancelCharge(true);
            else if (player.time >= ChargeDuration) ReleaseCharge();
            return;
        }
        if (playerMode == PlayerMode.Attack &&
            player.time >= (attackEndTime > 0 ? attackEndTime : player.duration))
        {
            playerMode = PlayerMode.Ready;
            comboGrace = combo == 3 ? 0 : rhythmFollowupQueued ? .3f : .65f;
        }
        if (playerMode == PlayerMode.Attack && attackFollowupQueued && !rhythmFollowupQueued &&
            RhythmWindowOpen && attackBuffer > 0)
            ConfirmRhythmFollowup();
        if ((playerMode == PlayerMode.Parry || playerMode == PlayerMode.Dash ||
             playerMode == PlayerMode.Hurt || playerMode == PlayerMode.SpinLand) && player.time >= player.duration)
            playerMode = PlayerMode.Ready;
        if (playerMode == PlayerMode.Heal)
        {
            if (player.time >= healAt && !player.connected)
            {
                player.connected = true; player.hp = Mathf.Min(100, player.hp + 38);
                Emit("Heal", player.Center);
            }
            if (player.time >= player.duration) playerMode = PlayerMode.Ready;
        }
        bool free = playerMode == PlayerMode.Ready;
        if (free && Mathf.Abs(input.move) > .01f) player.facing = input.move > 0 ? 1 : -1;
        bool recoveringDash = playerMode == PlayerMode.Dash && player.time >= DodgeRecovery;
        bool cancel = free || recoveringDash || (playerMode == PlayerMode.Attack && player.time > player.duration * .66f) ||
            (playerMode == PlayerMode.AirSpin && player.time >= AirSpinEnd) ||
            (playerMode == PlayerMode.CrossSlash && player.time >= CrossEnd);
        if (input.dash && cancel && !recoveringDash && Spend(22))
        {
            SetPlayer(PlayerMode.Dash, DodgeDuration); player.invincible = .21f;
            dashKicked = dashBraked = false;
            dashDirection = Mathf.Abs(input.move) > .01f ? Mathf.Sign(input.move) : player.facing;
            player.facing = (int)dashDirection; Emit("Dash", player.Center);
        }
        else if (input.parry && cancel && parryCooldown <= 0 && Spend(7))
        {
            SetPlayer(PlayerMode.Parry, .34f); parryCooldown = .39f;
            Emit("Guard", player.Center);
        }
        else if (free && input.charge)
        {
            if (player.y <= .005f && chargeEnergy >= ChargeEnergyMax && player.stamina >= 99.99f)
            {
                chargeEnergy = 0;
                Spend(100);
                attackBuffer = heavyBuffer = comboGrace = 0;
                SetPlayer(PlayerMode.Charging, ChargeDuration);
                Emit("ChargeStart", player.Center);
            }
            else Emit("ChargeDenied", player.Center, player.stamina < 99.99f ? 2 : 1);
        }
        else if (free || recoveringDash)
        {
            if (free && Mathf.Abs(input.move) > .01f) player.facing = input.move > 0 ? 1 : -1;
            if (free) player.x += input.move * dt * 4.8f;
            if (free && input.jump && player.y <= .005f)
            { player.vy = 8.5f; Emit("Jump", player.Center); }
            if (free && input.burst && focus >= CrossCost && Spend(28))
            {
                focus -= CrossCost; crossSlashes++; crossHitMask = crossSweep = 0;
                attackBuffer = heavyBuffer = comboGrace = 0;
                SetPlayer(PlayerMode.CrossSlash, CrossEnd);
                player.vy = player.y < .3f ? 6.4f : 2;
                Emit("CrossStart", player.Center);
            }
            else if (free && input.special && player.y > .08f && !airSkillUsed && Spend(AirSpinCost))
            {
                airSkillUsed = true; spinHitMask = spinSweep = 0;
                attackBuffer = heavyBuffer = comboGrace = 0;
                SetPlayer(PlayerMode.AirSpin, AirSpinEnd);
                player.vy = 1.2f;
                Emit("AirSpin", player.Center);
            }
            else if (heavyBuffer > 0 && Spend(28)) StartAttack(3);
            else if (attackBuffer > 0 && Spend(11)) StartAttack(comboGrace > 0 ? (combo + 1) % 3 : 0);
            else if (input.heal && potions > 0 && player.hp < 100 && player.y <= .01f)
            {
                potions--; SetPlayer(PlayerMode.Heal, 1.0f); healAt = .7f;
            }
        }
        if (playerMode == PlayerMode.Dash)
        {
            player.x += dashDirection * (DodgeTravel(player.time) - DodgeTravel(Mathf.Max(0, player.time - dt)));
            if (!dashKicked && player.time >= .04f)
            { dashKicked = true; Emit("DashKick", player.Center); }
            if (!dashBraked && player.time >= .19f)
            { dashBraked = true; Emit("DashBrake", player.Center); }
        }
        if (playerMode == PlayerMode.Attack)
        {
            if (player.time > PlayerStartup && player.time < PlayerStartup + .12f)
                player.x += player.facing * dt * (combo >= 2 ? 4.5f : 2.8f);
            if (!swingEmitted && player.time >= PlayerStartup - .03f)
            { swingEmitted = true; Emit("PlayerSwing", player.Center, combo + 1); }
        }
        if (playerMode == PlayerMode.AirSpin)
        {
            if (player.time < AirSpinEnd)
                player.x += Mathf.Clamp(input.move, -1, 1) * dt * 2.8f;
            if (spinSweep < 3 && player.time >= AirSpinStart + spinSweep * .16f)
            {
                spinSweep++;
                Emit("AirSpinSweep", player.Center, spinSweep);
            }
        }
        if (playerMode == PlayerMode.CrossSlash)
        {
            if (player.time > .24f && player.time < CrossEnd)
                player.x += player.facing * dt * (Mathf.Abs(player.x - boss.x) > 1.1f ? 3.4f : .15f);
            if (crossSweep < 6 && player.time >= CrossHitTime(crossSweep) - .025f)
            { Emit("CrossSweep", player.Center, ++crossSweep); }
        }
    }
    public static float CrossHitTime(int hit) => hit == 0 ? .18f : .38f + (hit - 1) * .16f;
    void TryCrossHit()
    {
        if (enemyMode == EnemyMode.Phase || enemyMode == EnemyMode.Intro || enemyMode == EnemyMode.Dead) return;
        for (int hit = 0; hit < 6; hit++)
        {
            float t = player.time - CrossHitTime(hit);
            if (t < 0 || t > .09f || (crossHitMask & (1 << hit)) != 0) continue;
            Rect blade = FacingBox(player.x - player.facing * .3f, Mathf.Max(0, player.y - .8f),
                player.facing, 2.35f, 2.45f);
            if (!blade.Overlaps(HurtBox(boss, true))) continue;
            crossHitMask |= 1 << hit;
            DamageBoss(hit == 5 ? 20 : 7, hit == 5 ? 10 : 4, "CrossHit", hit + 1);
            if (won) break;
        }
    }
    public static int AirSpinFrame(float time)
    {
        if (time < .04f) return 0;
        if (time < AirSpinStart) return 1;
        if (time < AirSpinEnd) return 2 + Mathf.Min(7, (int)(Mathf.Repeat((time - AirSpinStart) / .24f, 1) * 8));
        return time < .63f ? 10 : 11;
    }
    void TryAirSpinHit()
    {
        float active = player.time - .1f;
        int segment = Mathf.FloorToInt(active / .16f);
        if (segment < 0 || segment > 2 || active - segment * .16f >= .1f ||
            (spinHitMask & (1 << segment)) != 0) return;
        if (enemyMode == EnemyMode.Phase || enemyMode == EnemyMode.Intro || enemyMode == EnemyMode.Dead) return;
        Rect box = HurtBox(boss, true);
        Vector2 center = player.Center;
        Vector2 nearest = new Vector2(Mathf.Clamp(center.x, box.xMin, box.xMax),
            Mathf.Clamp(center.y, box.yMin, box.yMax));
        if ((nearest - center).sqrMagnitude > AirSpinRadius * AirSpinRadius) return;
        spinHitMask |= 1 << segment;
        DamageBoss(segment == 2 ? 16 : 9, segment == 2 ? 12 : 6, "AirSpinHit", segment + 1);
    }
    public static float DodgeTravel(float time)
    {
        if (time <= 0) return 0;
        if (time < .04f) return .08f * Mathf.Pow(time / .04f, 2);
        if (time < .17f) return .08f + 2.2f * (time - .04f) / .13f;
        if (time < DodgeRecovery)
        {
            float t = (time - .17f) / .1f;
            return 2.28f + .6f * (1 - (1 - t) * (1 - t));
        }
        return 2.88f;
    }
    public static int DodgeFrame(float time)
    {
        if (time < .015f) return 0;
        if (time < .035f) return 1;
        if (time < .05f) return 2;
        if (time < .07f) return 3;
        if (time < .095f) return 4;
        if (time < .13f) return 5;
        if (time < .175f) return 6;
        if (time < .22f) return 7;
        if (time < .27f) return 8;
        if (time < .31f) return 9;
        if (time < .35f) return 10;
        return 11;
    }
    bool Spend(float value)
    {
        if (player.stamina < value) return false;
        player.stamina -= value; staminaDelay = .5f; return true;
    }
    void SetPlayer(PlayerMode mode, float duration)
    {
        playerMode = mode; player.time = 0; player.duration = duration; player.connected = false;
        attackEndTime = mode == PlayerMode.Attack ? duration : 0;
    }
    void StartAttack(int index)
    {
        combo = index; attackBuffer = heavyBuffer = 0;
        swingEmitted = false;
        attackFollowupQueued = rhythmFollowupQueued = false;
        float duration = index == 3 ? .72f : index == 2 ? .56f : .4f;
        SetPlayer(PlayerMode.Attack, duration);
        attackEndTime = duration;
    }
    void ConfirmRhythmFollowup()
    {
        rhythmFollowupQueued = true;
        attackBuffer = .18f;
        attackEndTime = Mathf.Min(attackEndTime, Mathf.Max(player.time + .025f, PlayerStartup + .17f));
        Emit("ComboRhythm", player.Center, combo + 1);
    }
    public float PlayerStartup => combo == 3 ? .32f : combo == 2 ? .23f : .13f;
    public bool ParryActive => playerMode == PlayerMode.Parry && player.time <= .145f;
    public bool EnemyActive => enemyMode == EnemyMode.Strike && pattern != 3 && pattern != SkySwords &&
        (pattern == 2 ? boss.y <= .08f && boss.time > .32f :
         pattern == BladeRush || pattern == ShoulderRush ? boss.time < boss.duration : boss.time <= .13f);
    public bool Danger => pattern == 3 || pattern == ShoulderRush || pattern == SkySwords;
    public static Rect FacingBox(float x, float y, int facing, float reach, float height)
    {
        return new Rect(facing > 0 ? x : x - reach, y + .12f, reach, height);
    }
    public static Rect HurtBox(Fighter f, bool large = false) =>
        new Rect(f.x - (large ? .4f : .27f), f.y + .12f, large ? .8f : .54f, large ? 1.95f : 1.4f);
    public Rect PlayerBlade => FacingBox(player.x, player.y, player.facing, combo == 3 ? 2.05f : 1.65f, 1.65f);
    public Rect EnemyBlade => pattern == 2
        ? new Rect(boss.x - 2.1f, .02f, 4.2f, .9f)
        : pattern == ShoulderRush ? FacingBox(boss.x - boss.facing * .4f, boss.y, boss.facing, 1.6f, .95f)
        : pattern == BladeRush ? FacingBox(boss.x, boss.y, boss.facing, 1.85f, 1.75f)
        : FacingBox(boss.x, boss.y, boss.facing, pattern == 1 ? 2.8f : 2.25f, 1.75f);
    void TryPlayerHit()
    {
        if (player.connected || player.time < PlayerStartup || player.time > PlayerStartup + .14f) return;
        if (!PlayerBlade.Overlaps(HurtBox(boss, true))) return;
        if (enemyMode == EnemyMode.Phase || enemyMode == EnemyMode.Intro || enemyMode == EnemyMode.Dead) return;
        player.connected = true;
        if (EnemyActive && !Danger && pattern != 2 && !boss.connected && EnemyBlade.Overlaps(HurtBox(player)))
        {
            boss.connected = true; boss.posture += 17; lastHit = clock; GainFocus(10);
            Emit("Clash", (boss.Center + player.Center) * .5f);
            parryStreak = clock - lastParry < 2 ? parryStreak + 1 : 1; lastParry = clock;
            if (CanLock) { BeginBladeLock(); return; }
            CheckBreak(); return;
        }
        bool riposte = enemyMode == EnemyMode.Stagger && combo == 3;
        float damage = riposte ? 48 : combo == 3 ? 24 : combo == 2 ? 15 : 9;
        DamageBoss(damage, combo >= 2 ? 10 : 5, riposte ? "Riposte" : "HitBoss", combo >= 2 ? 2 : 1, riposte);
    }
    void DamageBoss(float damage, float posture, string cue, int strength, bool riposte = false)
    {
        float dealt = Mathf.Min(boss.hp, damage);
        boss.hp = Mathf.Max(0, boss.hp - damage);
        boss.posture += posture; lastHit = clock;
        chain++; bestChain = Mathf.Max(bestChain, chain);
        if (cue != "CrossHit") GainFocus(riposte ? 18 : 4);
        float oldChargeEnergy = chargeEnergy;
        chargeEnergy = Mathf.Min(ChargeEnergyMax, chargeEnergy + dealt * ChargeGainPerDamage);
        if (oldChargeEnergy < ChargeEnergyMax && chargeEnergy >= ChargeEnergyMax)
            Emit("ChargeReady", player.Center);
        Emit(cue, boss.Center, strength);
        if (boss.hp <= 0)
        {
            won = true; SetEnemy(EnemyMode.Dead, 2); Emit("Win", boss.Center); waves.Clear(); swords.Clear();
        }
        else if (riposte) { boss.posture = 0; SetEnemy(EnemyMode.Recover, 1.1f); }
        else CheckBreak();
    }
    void CancelCharge(bool active)
    {
        SetPlayer(PlayerMode.Ready, 0);
        Emit(active ? "ChargeCanceled" : "ChargeInterrupted", player.Center);
    }
    void ReleaseCharge()
    {
        bool hit = enemyMode != EnemyMode.Intro && enemyMode != EnemyMode.Phase &&
            enemyMode != EnemyMode.Dead && (boss.x - player.x) * player.facing >= -.5f &&
            Mathf.Abs(boss.x - player.x) <= ChargeRange && Mathf.Abs(boss.y - player.y) < 2.3f;
        combo = 3;
        SetPlayer(PlayerMode.Attack, .82f);
        player.connected = true;
        if (hit)
        {
            DamageBoss(ChargeDamage, 55, "ChargeHit", 3);
            Emit("ChargeRelease", player.Center, 1);
        }
        else Emit("ChargeMiss", player.Center);
    }
    void GainFocus(float amount) { focus = Mathf.Min(100, focus + amount); }
    bool CanLock => player.y < .15f && boss.y < .15f &&
        Mathf.Abs(player.x - boss.x) < 3 && waves.Count == 0 && swords.Count == 0;
    void BeginBladeLock()
    {
        lockTime = lockInputCooldown = 0; lockBeat = lockSuccess = lockMisses = lockHitMask = 0;
        parryStreak = 0; player.vy = boss.vy = 0; player.y = boss.y = 0;
        boss.posture = Mathf.Min(90, boss.posture);
        player.facing = boss.x > player.x ? 1 : -1; boss.facing = -player.facing;
        float center = Mathf.Clamp((player.x + boss.x) * .5f, Left + 1.3f, Right - 1.3f);
        player.x = center - player.facing * .9f; boss.x = center + player.facing * .9f;
        SetPlayer(PlayerMode.BladeLock, LockDuration); SetEnemy(EnemyMode.BladeLock, LockDuration);
        attackBuffer = heavyBuffer = 0; Emit("LockStart", LockContact);
    }
    void StepBladeLock(float dt, Controls input)
    {
        lockTime += dt; lockInputCooldown = Mathf.Max(0, lockInputCooldown - dt);
        if (input.dash)
        {
            EndBladeLock(false, true);
            return;
        }
        if (lockBeat < LockBeats && (input.attack || input.parry) && lockInputCooldown <= 0)
        {
            lockInputCooldown = .16f;
            if (Mathf.Abs(lockTime - NextLockBeat) <= LockWindow)
            {
                lockHitMask |= 1 << lockBeat; lockSuccess++; lockBeat++; GainFocus(6);
                Emit("LockBeat", LockContact, lockSuccess);
            }
            else
            {
                lockMisses++; Emit("LockMiss", player.Center);
            }
        }
        if (lockBeat < LockBeats && lockTime > NextLockBeat + LockWindow)
        { lockBeat++; lockMisses++; Emit("LockMiss", player.Center); }
        if (lockMisses >= 3) EndBladeLock(false, false);
        else if (lockBeat >= LockBeats && lockTime > LockDuration - .05f)
            EndBladeLock(lockSuccess >= 4, false);
    }
    void EndBladeLock(bool success, bool retreat)
    {
        SetPlayer(PlayerMode.Ready, 0); SetEnemy(EnemyMode.Recover, .85f);
        if (success)
        {
            locksWon++; player.stamina = Mathf.Min(100, player.stamina + 24);
            DamageBoss(30, 0, "LockWin", 2);
            if (!won) { boss.posture = 100; SetEnemy(EnemyMode.Stagger, 2.5f); Emit("Break", boss.Center); }
        }
        else
        {
            boss.posture = Mathf.Min(boss.posture, 65);
            if (retreat && Spend(22))
            {
                dashDirection = -player.facing; player.facing = (int)dashDirection;
                dashKicked = dashBraked = false;
                SetPlayer(PlayerMode.Dash, DodgeDuration); player.invincible = .21f; Emit("Dash", player.Center);
            }
            else HurtPlayer(14, boss.facing);
            Emit("LockLost", player.Center);
        }
    }
    void CheckBreak()
    {
        if (boss.posture < 100 || enemyMode == EnemyMode.Stagger || won) return;
        boss.posture = 100; SetEnemy(EnemyMode.Stagger, 2.5f); Emit("Break", boss.Center);
    }
    void EnemyStep(float dt)
    {
        switch (enemyMode)
        {
            case EnemyMode.Intro:
                if (!introLanded && boss.time > .55f)
                { introLanded = true; boss.y = 5.2f; boss.vy = -2; Emit("Entrance", boss.Center); }
                if (boss.time >= boss.duration) SetEnemy(EnemyMode.Approach, .7f);
                break;
            case EnemyMode.Approach:
                boss.facing = player.x < boss.x ? -1 : 1;
                float distance = Mathf.Abs(player.x - boss.x);
                if (distance > 2)
                    boss.x = Mathf.MoveTowards(boss.x, player.x - boss.facing * 1.8f, dt * (secondPhase ? 2.8f : 2.1f));
                if (boss.time >= boss.duration && (distance < 3.2f || boss.time > 2.2f))
                {
                    var patterns = secondPhase ? RagePatterns : FirstPatterns;
                    pattern = patterns[bossCycle++ % patterns.Length];
                    if (distance > 4 && pattern == 0) pattern = BladeRush;
                    if (pattern == SkySwords && (waves.Count > 0 || swords.Count > 0)) pattern = BladeRush;
                    strike = 0; Windup();
                }
                break;
            case EnemyMode.Windup:
                if (pattern == 2 && !jumpStarted && boss.time > .18f)
                {
                    jumpStarted = true; boss.vy = 10.2f;
                    bossMoveStart = boss.x;
                    Emit("BossJump", boss.Center);
                }
                if (pattern == 2 && jumpStarted)
                    boss.x = Mathf.Lerp(bossMoveStart, jumpTarget, Mathf.Clamp01((boss.time - .18f) / .68f));
                if (boss.time >= boss.duration)
                {
                    SetEnemy(EnemyMode.Strike, pattern == SkySwords ? 3.65f :
                        pattern == BladeRush ? .55f : pattern == ShoulderRush ? .44f : pattern == 2 ? .42f : .24f);
                    if (pattern == 3)
                    {
                        waves.Add(new Wave { x = boss.x + boss.facing * .7f, direction = boss.facing });
                        waveCasts++;
                        Emit("Wave", boss.Center);
                    }
                    else if (pattern == BladeRush || pattern == ShoulderRush)
                        Emit(pattern == BladeRush ? "Rush" : "Shoulder", boss.Center, 2);
                    else if (pattern == SkySwords) summonedSwords = 0;
                    else if (pattern != 2)
                    { Emit("BossSwing", boss.Center, pattern == 1 ? 2 : strike + 1); bossSwingEmitted = true; }
                }
                break;
            case EnemyMode.Strike:
                if (pattern == BladeRush || pattern == ShoulderRush)
                    boss.x = Mathf.Clamp(boss.x + boss.facing * dt * (pattern == BladeRush ? 13.2f : 10.5f), Left + .4f, Right - .4f);
                if (pattern == SkySwords && summonedSwords < 4 &&
                    boss.time >= (summonedSwords == 3 ? 2f : summonedSwords * .55f))
                {
                    var sword = new FallingSword { x = Mathf.Clamp(player.x, Left + .3f, Right - .3f), great = summonedSwords == 3 };
                    swords.Add(sword); summonedSwords++;
                    Emit("SwordMark", new Vector2(sword.x, 0), sword.great ? 2 : 1);
                }
                if (pattern == 1 && boss.time < .15f) boss.x += boss.facing * dt * 10;
                if (pattern == 0 && boss.time < .12f) boss.x += boss.facing * dt * 3;
                if (pattern == 2 && boss.y <= .01f && !bossSwingEmitted)
                {
                    bossSwingEmitted = true; boss.time = .33f;
                    boss.duration = .52f; leapSlams++; Emit("Slam", new Vector2(boss.x, .1f), strike + 1);
                }
                if (boss.time >= boss.duration)
                {
                    if (pattern == 0 && strike < (secondPhase ? 3 : 2))
                    {
                        strike++; Windup();
                    }
                    else if (pattern == ShoulderRush && secondPhase && strike == 0)
                    { strike++; Windup(); }
                    else if (pattern == 2 && strike < (secondPhase ? 2 : 1))
                    { strike++; Windup(); }
                    else if (pattern == 3 && strike < (secondPhase ? 2 : 1))
                    { strike++; Windup(); }
                    else if (pattern == 3 && secondPhase)
                    { pattern = 1; strike = 0; Windup(); }
                    else SetEnemy(EnemyMode.Recover, pattern == SkySwords ? 1.55f :
                        pattern >= BladeRush ? 1.1f : pattern == 2 ? 1.15f : pattern == 0 ? .95f : .75f);
                }
                break;
            case EnemyMode.Recover:
                if (boss.time >= boss.duration) SetEnemy(EnemyMode.Approach, secondPhase ? .3f : .52f);
                break;
            case EnemyMode.Stagger:
                if (boss.time >= boss.duration)
                { boss.posture = 0; SetEnemy(EnemyMode.Recover, .6f); }
                break;
            case EnemyMode.Phase:
                if (boss.time >= boss.duration)
                { waves.Clear(); pattern = SkySwords; strike = 0; bossCycle = 0; Windup(); }
                break;
        }
    }
    void Windup()
    {
        // Facing and leap destination lock before the attack, never mid-swing.
        boss.facing = player.x < boss.x ? -1 : 1;
        if (pattern == 2) jumpTarget = Mathf.Clamp(player.x, Left + .5f, Right - .5f);
        float duration = pattern == SkySwords ? .75f : pattern == BladeRush ? .85f :
            pattern == ShoulderRush ? (strike == 0 ? .78f : .66f) :
            pattern == 0 ? (strike == 0 ? .58f : strike == 1 ? .3f : .57f)
            : pattern == 1 ? .8f : pattern == 2 ? .7f : strike == 0 ? .9f : .72f;
        if (secondPhase && pattern < BladeRush && pattern != 2) duration *= .86f;
        SetEnemy(EnemyMode.Windup, duration);
        bossSwingEmitted = jumpStarted = false;
        Emit(pattern == SkySwords ? "SwordSummon" : pattern == BladeRush ? "RushTell" :
            pattern == ShoulderRush ? "ShoulderTell" : pattern == 3 ? "Danger" : "Tell", boss.Center, strike + 1);
    }
    void SetEnemy(EnemyMode mode, float duration)
    {
        enemyMode = mode; boss.time = 0; boss.duration = duration; boss.connected = false;
    }
    void TryEnemyHit()
    {
        if (!EnemyActive || boss.connected || !EnemyBlade.Overlaps(HurtBox(player))) return;
        boss.connected = true;
        if (player.invincible > 0) { Emit("Evade", player.Center); return; }
        bool facing = (boss.x - player.x) * player.facing >= -.1f;
        if (ParryActive && facing && !Danger)
        {
            parries++; chain++; bestChain = Mathf.Max(bestChain, chain);
            boss.posture += pattern == 2 ? 43 : pattern == BladeRush ? 42 : 34;
            player.stamina = Mathf.Min(100, player.stamina + 14);
            parryCooldown = 0; playerMode = PlayerMode.Ready;
            player.invincible = .09f; lastHit = clock;
            GainFocus(14);
            parryStreak = clock - lastParry < 2 ? parryStreak + 1 : 1; lastParry = clock;
            Emit("Parry", (player.Center + boss.Center) * .5f, pattern == 2 ? 2 : 1);
            if (CanLock && parryStreak >= 3) { BeginBladeLock(); return; }
            if (pattern == BladeRush) { SetEnemy(EnemyMode.Recover, 1.1f); Emit("RushStopped", boss.Center); }
            CheckBreak(); return;
        }
        HurtPlayer(pattern == ShoulderRush ? 24 : pattern == BladeRush ? 21 : pattern == 2 ? 22 : pattern == 1 ? 18 : 13, boss.facing);
    }
    void StepSwords(float dt)
    {
        for (int i = swords.Count - 1; i >= 0; i--)
        {
            var sword = swords[i]; sword.age += dt;
            if (!sword.impacted && sword.age >= sword.ImpactTime)
            {
                sword.impacted = true;
                Emit("SwordImpact", new Vector2(sword.x, 0), sword.great ? 2 : 1);
                var column = new Rect(sword.x - sword.Radius, 0, sword.Radius * 2, 8);
                if (column.Overlaps(HurtBox(player)))
                {
                    if (player.invincible > 0) Emit("Evade", player.Center);
                    else HurtPlayer(sword.great ? 32 : 20, player.x >= sword.x ? 1 : -1);
                }
                if (sword.great && !lost && !won)
                {
                    waves.Add(new Wave { x = sword.x - 1.2f, direction = -1 });
                    waves.Add(new Wave { x = sword.x + 1.2f, direction = 1 });
                }
            }
            if (sword.age > sword.ImpactTime + .8f) swords.RemoveAt(i);
        }
    }
    void HurtPlayer(float damage, int direction)
    {
        if (lost || won) return;
        hitsTaken++; chain = 0;
        parryStreak = 0;
        if (playerMode == PlayerMode.Charging)
            Emit("ChargeInterrupted", player.Center);
        player.hp = Mathf.Max(0, player.hp - damage);
        player.invincible = .68f; player.x += direction * .45f;
        attackBuffer = heavyBuffer = 0;
        SetPlayer(PlayerMode.Hurt, .28f);
        Emit("HitPlayer", player.Center, 2);
        if (player.hp <= 0)
        { lost = true; playerMode = PlayerMode.Dead; player.time = 0; Emit("Lose", player.Center); }
    }
    void Emit(string name, Vector2 position, int strength = 1) => cues.Add(new Cue(name, position, strength));

    // Exercises the same control/state path as live input; never changes damage or health.
    public Controls Demonstration()
    {
        var input = new Controls();
        if (IsLocked)
        {
            input.attack = lockBeat < LockBeats && lockTime >= NextLockBeat - .025f;
            return input;
        }
        if (playerMode == PlayerMode.Charging) return input;
        float distance = Mathf.Abs(player.x - boss.x);
        int toward = boss.x > player.x ? 1 : -1;
        input.move = distance > 1.65f ? toward : 0;
        if (enemyMode == EnemyMode.Recover && player.hp < 65 && potions > 0)
        { input.move = 0; input.heal = true; return input; }
        if (enemyMode == EnemyMode.Stagger)
        { input.heavy = distance < 1.9f; return input; }
        if (enemyMode == EnemyMode.Windup)
        {
            if (pattern == ShoulderRush)
            {
                input.move = 0;
                if (boss.duration - boss.time < .055f && distance < 2.4f)
                { input.move = toward; input.dash = true; }
            }
            else if (pattern == BladeRush)
            { input.move = 0; }
            else if (pattern == SkySwords) input.move = 0;
            else if (pattern == 3)
            {
                input.move = 0;
                input.jump = boss.duration - boss.time < .12f && distance < 3.5f;
            }
            else if (pattern != 2 && boss.duration - boss.time < .065f)
            { input.parry = true; input.move = toward; }
        }
        if (enemyMode == EnemyMode.Strike && pattern == BladeRush)
        {
            input.move = toward;
            input.parry = distance < 2.35f && !boss.connected;
        }
        if (enemyMode == EnemyMode.Strike && pattern == ShoulderRush)
        {
            input.move = 0;
            if (distance < 2.1f && !boss.connected && playerMode == PlayerMode.Ready)
            { input.move = toward; input.dash = true; }
        }
        if (pattern == SkySwords && enemyMode == EnemyMode.Strike)
        {
            input.move = 0;
            foreach (var sword in swords)
            {
                if (sword.impacted || sword.ImpactTime - sword.age > .7f ||
                    Mathf.Abs(player.x - sword.x) > sword.Radius + .65f) continue;
                int away = player.x > sword.x ? 1 : -1;
                if (player.x < Left + 2) away = 1;
                if (player.x > Right - 2) away = -1;
                input.move = away;
                break;
            }
        }
        if (pattern == 2 && enemyMode == EnemyMode.Strike && boss.y < .55f && boss.vy < 0)
        { input.parry = true; input.move = toward; }
        foreach (var wave in waves)
            if (Mathf.Abs(wave.x - player.x) < 2.2f && (player.x - wave.x) * wave.direction > 0) input.jump = true;
        if (enemyMode == EnemyMode.Recover && distance < 1.8f)
        {
            int nextPattern = secondPhase
                ? RagePatterns[bossCycle % RagePatterns.Length]
                : FirstPatterns[bossCycle % FirstPatterns.Length];
            if (chargeEnergy >= ChargeEnergyMax && player.stamina >= 99.99f &&
                nextPattern != BladeRush &&
                playerMode == PlayerMode.Ready && player.y <= .005f)
            { input.charge = true; return input; }
            if (focus >= CrossCost && player.stamina >= 28 && playerMode == PlayerMode.Ready &&
                boss.duration - boss.time > .65f && crossSlashes <= locksWon)
            { input.burst = true; return input; }
            bool spinOpening = boss.duration >= .9f;
            if (spinOpening && player.y > .08f && !airSkillUsed)
                input.special = player.y > .45f;
            else if (spinOpening && playerMode == PlayerMode.Ready && player.y <= .005f &&
                boss.duration - boss.time > .55f && player.stamina >= AirSpinCost)
                input.jump = true;
            else if (player.y <= .005f &&
                (playerMode == PlayerMode.Ready || RhythmWindowOpen)) input.attack = true;
        }
        if (player.hp < 65 && enemyMode == EnemyMode.Approach && distance > 2.3f) input.heal = true;
        return input;
    }
}
