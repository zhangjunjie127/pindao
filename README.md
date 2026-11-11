# Mossbound Duel

Unity 6000.6.0f1 single-arena melee combat slice. The original geometry-only
prototype has been replaced with animated characters and layered environment art.

## Run

- Open this folder in Unity 6000.6.0f1.
- Open `Assets/Scenes/Main.unity` and press Play.
- Or launch `outputs/MossboundDuel/MossboundDuel.exe`.
- The arena waits for the first gameplay input before starting the encounter.
- User-facing Chinese instructions, asset credits and verification records are in `outputs`.

## Controls

- `A/D`: move
- `Space`: jump
- `I` while airborne: aerial spinning special, separate from normal/heavy attacks
- `O`: six-cut rising cross-slash, 50 focus and 28 stamina
- `J` / left mouse: buffered three-hit combo
- `U`: heavy slash / stagger punish
- `Q`: spend full charge and stamina for the high-damage charged slash; effective boss damage builds charge
- `K` / right mouse: timed parry
- `J` during blade bind: timed pressure beats; dodge can disengage
- `Left Shift` / `L`: dash
- `E`: heal
- `R`: reset
- `Escape`: pause and settings
- `F8`: toggle demonstration (uses the same combat input path, no invulnerability)
- `F9`: toggle close-up dodge practice demonstration; toggling off resets manual combat
- `F11`: fullscreen
- `F12`: screenshot
- `M`: mute
- `Enter`: skip entrance / finishing presentation

## Structure

- `DuelCombat`: fixed-step encounter, directional hit boxes, resources and timing.
- `SoulBladePrototype`: preserved scene entry point, animation, environment, VFX and HUD.
- `DuelSound`: original synthesized cue layers and combat rhythm.
- `DuelPost`: camera post-processing.
- `DuelBossVisuals`: charge telegraphs and original falling-sword mesh, driven by combat hazard clocks.
- `DuelBuild.Test`: deterministic combat regression checks.
- `DuelBuild.Build`: run checks and build Windows player.

The flat arena uses one-dimensional grounded separation and a fixed-floor jump model.
It is not a general platforming controller; slopes and multi-level platforms need
a Rigidbody2D/controller integration before adding exploration levels.

Boss patterns include parry-stoppable blade rushes, unparryable shoulder charges
(two telegraphed passes in rage), and four sequential sky swords in phase two.
The final sword creates two ground waves. Each landing position locks at warning
spawn. A 0.34-second summon cut-in freezes both combat clocks while preserving
buffered input; the camera fully returns before hazards begin.

Reference-completion pass adds two/three consecutive leap slams, two/three
ground waves followed by a rage lunge, a focus-powered six-cut rising slash,
and sustained blade binding after consecutive close parries or clashes.
Blade binding has five readable rhythm beats, a stamina-costing escape,
finite failure damage, and a stagger punish on success. The dedicated bind
atlas keeps both feet planted; generated ember animation accompanies the
cross-slash, ground waves, and finisher. Entrance and victory have short,
skippable camera treatments. Sound sources are pooled so overlapping steel
hits do not change each other's pitch.
The entrance and rage transition stage short two-character exchanges with
cropped half-body portraits from the existing character art; Enter skips either.

`--record-start 0 --record-seconds 66 --record-fps 30 --record-dir <path>`
also exports the engine's cue samples, times, pan and pitch for reproducible
preview audio. The preview audio is an offline mix of those game cues, not
a microphone or desktop recording. Offscreen videos still exclude IMGUI.
`work/assemble_combat_preview.py` validates frames and encodes the full fight
and highlight reel. See `outputs/Verification/补全验收.md` for the precise
scope and verification limits.

QA: `--demo --qa --qa-metrics-only --quit-after 80 --capture-dir <absolute-path>`
checks a normal-time encounter without screenshots. For reproducible visual
captures use `--qa-offscreen` instead of `--qa-metrics-only`; these are real
camera renders without the IMGUI HUD, at a fixed simulation timestep, muted.
Their wall-clock FPS/audio numbers are intentionally omitted.
`--record-ultimate --record-seconds 18 --record-dir <absolute-path>` records
20 fps starting from the first summon (`--record-fps 60` also supported). This path does not change combat health,
damage, AI patterns or the demonstration's legal player inputs.

The player dodge uses a dedicated generated 12-frame atlas in each direction.
Movement has an authored startup/burst/braking curve, 0.38s full duration and
the original 0.21s invulnerability. At 0.27s recovery can cancel into attack/parry.
`--dodge-showcase --dodge-closeup` sets up a non-attacking practice opponent and
alternates legal left/right dodge inputs. `--demo --record-dodge` records a normal
encounter starting with the first shoulder telegraph; the same dodge implementation
is used in practice, the encounter demo and manual play.

The aerial special uses its own 12-frame, waist-anchored atlas in each direction.
Jump first, then press I. It costs 32 stamina, permits one use per airtime, and
grants no invulnerability. Two visible rotations have three separately gated
damage windows (9 + 9 + 16), then descent and 0.18s grounded recovery. Directional
input steers the airborne skill; active rotation cannot be canceled, but its
ending can be canceled into dodge/parry. Normal and heavy attacks are unchanged.
`--spin-showcase --spin-closeup` records the same legal jump/special input path
against a stationary practice opponent. `--demo` also uses it in recovery openings.

Art: Maaot Mossy Cavern and Engvee Animated Knight. Both combatants use different
scales and recolors of the same knight pack. This is not the reference video's
hand-drawn samurai identity or shrine scene. No source video artwork/audio is shipped.
Do not redistribute extracted third-party art as a standalone asset pack.
