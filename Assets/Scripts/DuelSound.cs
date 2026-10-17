using System;
using System.IO;
using System.Text;
using UnityEngine;

// Original synthesized cues: metallic transients, low body impacts and a phase-aware drum bed.
public sealed class DuelSound : MonoBehaviour
{
    AudioSource voice, music;
    readonly AudioSource[] voices = new AudioSource[12];
    int voiceIndex;
    string captureDir;
    [Serializable] sealed class RecordedCue
    {
        public float time, pitch, volume, pan;
        public string clip, kind;
    }
    AudioClip slash, clang, hit, thud, warning, drink, phase, drumLoop, summon, falling, dashAir, bootScrape, spinAir;
    public bool Muted { get; private set; }
    void Awake()
    {
        for (int i = 0; i < voices.Length; i++)
        {
            voices[i] = gameObject.AddComponent<AudioSource>();
            voices[i].playOnAwake = false; voices[i].volume = .42f;
        }
        voice = voices[0];
        music = gameObject.AddComponent<AudioSource>();
        slash = Make("Blade air", .22f, 0);
        clang = Make("Steel resonance", .6f, 1);
        hit = Make("Armor impact", .27f, 2);
        thud = Make("Heavy impact", .55f, 3);
        warning = Make("Attack cue", .15f, 4);
        drink = Make("Restoration", .6f, 5);
        phase = Make("Phase resonance", 1.8f, 6);
        drumLoop = Make("Combat pulse", 16, 7);
        summon = Make("Judgment invocation", .65f, 8);
        falling = Make("Descending steel", .8f, 9);
        dashAir = Make("Low dodge air", .2f, 10);
        bootScrape = Make("Boot grip", .16f, 11);
        spinAir = Make("Aerial blade rotation", .17f, 12);
        music.clip = drumLoop; music.loop = true; music.volume = .13f; music.Play();
    }
    public void SetPhase(bool fierce, bool pause)
    {
        music.pitch = fierce ? 1.13f : 1;
        music.volume = Muted ? 0 : pause ? .045f : fierce ? .18f : .13f;
    }
    public void Toggle() { Muted = !Muted; foreach (var source in voices) source.mute = Muted; }
    public void Cue(string name, int weight, float pan = 0, float time = 0)
    {
        AudioClip clip = null; float volume = 1;
        switch (name)
        {
            case "PlayerSwing": case "BossSwing": clip = slash; volume = .65f; break;
            case "DashKick": clip = dashAir; volume = .7f; break;
            case "DashBrake": clip = bootScrape; volume = .38f; break;
            case "AirSpin": clip = dashAir; volume = .32f; break;
            case "AirSpinSweep": clip = spinAir; volume = .62f; break;
            case "ComboRhythm": clip = clang; volume = .32f; break;
            case "ChargeStart": clip = summon; volume = .75f; break;
            case "ChargeReady": clip = clang; volume = .8f; break;
            case "ChargeRelease": clip = falling; volume = .9f; break;
            case "ChargeHit": clip = thud; volume = 1; break;
            case "ChargeMiss": clip = slash; volume = .95f; break;
            case "ChargeCanceled": case "ChargeInterrupted": clip = bootScrape; volume = .8f; break;
            case "ChargeDenied": clip = warning; volume = .6f; break;
            case "CrossStart": clip = summon; volume = .48f; break;
            case "CrossSweep": clip = spinAir; volume = .75f; break;
            case "CrossHit": clip = weight == 6 ? thud : hit; volume = weight == 6 ? .85f : .6f; break;
            case "LockStart": case "LockWin": clip = clang; volume = .9f; break;
            case "LockBeat": clip = clang; volume = .5f + weight * .06f; break;
            case "LockMiss": case "LockLost": clip = bootScrape; volume = .75f; break;
            case "Entrance": clip = phase; volume = .55f; break;
            case "Wave": clip = slash; volume = .9f; break;
            case "AirSpinHit": clip = weight == 3 ? thud : hit; volume = weight == 3 ? .8f : .65f; break;
            case "SpinLand": clip = bootScrape; volume = .55f; break;
            case "Parry": case "Clash": clip = clang; break;
            case "HitBoss": case "HitPlayer": clip = hit; break;
            case "Slam": case "Break": case "Riposte": clip = thud; break;
            case "Tell": case "Guard": clip = warning; volume = .24f; break;
            case "Danger": case "Phase": case "Win": clip = phase; volume = .55f; break;
            case "Heal": clip = drink; break;
            case "Land": if (weight > 1) clip = thud; volume = .25f; break;
            case "RushTell": case "ShoulderTell": clip = warning; volume = .55f; break;
            case "Rush": case "Shoulder": clip = slash; volume = 1; break;
            case "RushStopped": clip = clang; volume = .5f; break;
            case "SwordSummon": clip = summon; volume = .85f; break;
            case "SwordMark": clip = falling; volume = weight > 1 ? .48f : .3f; break;
            case "SwordImpact": clip = thud; volume = weight > 1 ? 1 : .65f; break;
        }
        if (clip == null) return;
        voice = voices[voiceIndex++ % voices.Length];
        voice.pitch = name == "LockBeat" ? .88f + weight * .065f :
            name == "CrossSweep" ? .9f + weight * .04f : weight > 1 ? .88f : UnityEngine.Random.Range(.95f, 1.04f);
        voice.panStereo = Mathf.Clamp(pan, -.65f, .65f);
        voice.clip = clip; voice.volume = volume * .42f; voice.Play();
        if (captureDir != null)
            File.AppendAllText(Path.Combine(captureDir, "cues.jsonl"), JsonUtility.ToJson(new RecordedCue {
                time = time, pitch = voice.pitch, volume = voice.volume, pan = voice.panStereo,
                clip = clip.name, kind = name }) + "\n");
    }
    public void PrepareCapture(string directory)
    {
        captureDir = directory; Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "cues.jsonl"), "");
        foreach (var clip in Clips()) WriteWave(Path.Combine(directory, clip.name + ".wav"), clip);
    }
    AudioClip[] Clips() => new[] { slash, clang, hit, thud, warning, drink, phase, drumLoop,
        summon, falling, dashAir, bootScrape, spinAir };
    static void WriteWave(string path, AudioClip clip)
    {
        var data = new float[clip.samples]; clip.GetData(data, 0);
        using (var stream = new BinaryWriter(File.Create(path)))
        {
            stream.Write(Encoding.ASCII.GetBytes("RIFF")); stream.Write(36 + data.Length * 2);
            stream.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); stream.Write(16);
            stream.Write((short)1); stream.Write((short)1); stream.Write(clip.frequency);
            stream.Write(clip.frequency * 2); stream.Write((short)2); stream.Write((short)16);
            stream.Write(Encoding.ASCII.GetBytes("data")); stream.Write(data.Length * 2);
            foreach (float value in data) stream.Write((short)(Mathf.Clamp(value, -1, 1) * 32767));
        }
    }
    static readonly float[] BassNotes = { 73.42f, 58.27f, 65.41f, 55 };
    static AudioClip Make(string name, float duration, int kind)
    {
        const int rate = 44100;
        var samples = new float[Mathf.CeilToInt(rate * duration)];
        var rng = new System.Random(42 + kind);
        float low = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)rate, noise = (float)rng.NextDouble() * 2 - 1;
            low += (noise - low) * .12f;
            float value = 0;
            switch (kind)
            {
                case 0: value = low * Mathf.Sin(Mathf.PI * t / duration) * 1.5f + noise * .05f; break;
                case 1:
                    value = (.34f * S(1780, t) + .24f * S(2631, t) + .16f * S(4207, t)) *
                        Mathf.Exp(-t * 9) + noise * Mathf.Exp(-t * 70) * .5f; break;
                case 2: value = low * Mathf.Exp(-t * 25) * 2 + S(160 - t * 240, t) * Mathf.Exp(-t * 20) * .45f; break;
                case 3: value = S(72 - t * 40, t) * Mathf.Exp(-t * 8) * .8f + low * Mathf.Exp(-t * 18); break;
                case 4: value = (S(920, t) + S(1325,t)*.4f) * Mathf.Exp(-t*35)*.3f; break;
                case 5: value = (S(510+t*280,t)+S(764,t)*.3f)*Mathf.Sin(Mathf.PI*t/duration)*.18f; break;
                case 6: value = (S(69,t)+S(103.5f,t)*.4f+S(139,t)*.25f+low)*Mathf.Sin(Mathf.PI*t/duration)*.4f; break;
                case 7:
                    float beat = t % .5f, half = t % .25f;
                    float root = BassNotes[(int)(t / 4) % BassNotes.Length];
                    value = S(62-beat*44,beat)*Mathf.Exp(-beat*17)*.7f +
                        low*Mathf.Exp(-half*55)*.3f +
                        (S(root,t)+S(root*1.5f,t)*.4f+S(root*2.4f,t)*.2f)*.09f;
                    if ((int)(t*2)%4==3) value+=noise*Mathf.Exp(-beat*18)*.15f;
                    float phrase = t % 2;
                    value += (S(root * 4, phrase) + S(root * 6, phrase) * .2f) * Mathf.Exp(-phrase * 3) * .065f;
                    break;
                case 8:
                    value = (S(92, t) * .35f + S(184, t) * .18f + S(740 - t * 360, t) * .18f) *
                        Mathf.Sin(Mathf.PI * t / duration) + noise * Mathf.Exp(-t * 65) * .3f;
                    break;
                case 9:
                    float swell = t / duration;
                    value = (low * .9f + S(580 - t * 390, t) * .12f) * swell * swell;
                    break;
                case 10:
                    value = (low * 1.7f + noise * .07f + S(125 - t * 260, t) * .12f) *
                        Mathf.Sin(Mathf.PI * t / duration) * Mathf.Exp(-t * 4);
                    break;
                case 11:
                    value = (low * .8f + noise * .16f) * Mathf.Exp(-t * 24) +
                        S(110, t) * Mathf.Exp(-t * 45) * .16f;
                    break;
                case 12:
                    float air = Mathf.Pow(Mathf.Sin(Mathf.PI * t / duration), 2);
                    value = (low * 1.8f + noise * .08f + S(480 - t * 1600, t) * .13f) * air;
                    break;
            }
            float fade = Mathf.Min(1, t * 700) * Mathf.Min(1, (duration - t) * 80);
            samples[i] = Mathf.Clamp(value * fade, -.9f, .9f);
        }
        var clip = AudioClip.Create(name, samples.Length, 1, rate, false);
        clip.SetData(samples, 0); return clip;
    }
    static float S(float f, float t) => Mathf.Sin(2 * Mathf.PI * f * t);
    void OnDestroy() { foreach (var clip in Clips()) if (clip) Destroy(clip); }
}
