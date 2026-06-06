# PerfectParse Technical Reference

## How Combat Events Are Tracked

This document explains how PerfectParse intercepts, categorizes, and logs combat events from Erenshor. It is intended for mod developers who want to understand the event pipeline, extend it, or build compatible tooling.

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Event Pipeline](#event-pipeline)
- [Damage Tracking](#damage-tracking)
  - [Physical / Melee Damage](#physical--melee-damage)
  - [Magic / Spell Damage](#magic--spell-damage)
  - [Bleed Damage](#bleed-damage)
  - [Self-Damage](#self-damage)
  - [Damage Reflection](#damage-reflection)
  - [Environmental Damage](#environmental-damage)
  - [Finale (Instant Kill)](#finale-instant-kill)
  - [Boss Mechanic Damage](#boss-mechanic-damage)
- [Heal Tracking](#heal-tracking)
  - [Direct Heals](#direct-heals)
  - [Healing Over Time (HoT)](#healing-over-time-hot)
  - [Simple Heals (Lifesteal / Regen)](#simple-heals-lifesteal--regen)
  - [Mana Restoration](#mana-restoration)
  - [Resonance Procs](#resonance-procs)
- [Attack Source Attribution](#attack-source-attribution)
  - [CombatContext (Frame-Scoped)](#combatcontext-frame-scoped)
  - [Bleed Skill Name Resolution](#bleed-skill-name-resolution)
- [Entity Identification](#entity-identification)
- [Event Relevance Filtering](#event-relevance-filtering)
- [DamageMe Return Value Reference](#damageme-return-value-reference)
- [Event Models](#event-models)
- [JSONL Output Format](#jsonl-output-format)

---

## Architecture Overview

```
Game Methods
    |
    v
Harmony Patches (Prefix / Postfix)
    |
    v
CombatEventBus (filters, assigns encounter IDs)
    |
    +---> JsonLineWriter (background thread -> JSONL file)
    +---> EncounterTracker (encounter boundaries)
    +---> CombatWindow (live IMGUI display)
```

All patches feed into `CombatEventBus`, which decides whether an event is relevant (involves the player or their group), assigns it to the current encounter, and dispatches it to subscribers.

Patch application strategy:
- **DamagePatches** and **HealPatches** are applied manually (not via `[HarmonyPatch]` attributes) for reliability.
- **ContextPatches** use attribute-based patching.
- **BossMechanicPatches** are applied manually with independent try/catch per patch so a missing game class does not prevent others from loading.

---

## Event Pipeline

1. A **prefix patch** fires before the game method executes. Used to capture pre-call state (HP snapshots, attack type context).
2. The game method runs.
3. A **postfix patch** fires after the game method. It reads the return value and pre-call state, builds a `CombatEvent` or `HealEvent`, and calls `CombatEventBus.EmitDamage()` or `CombatEventBus.EmitHeal()`.
4. The bus filters for relevance, assigns `EncounterId`, and invokes event subscribers.
5. `JsonLineWriter` serializes the event to JSONL on a background thread (never blocks the game thread).

---

## Damage Tracking

### Physical / Melee Damage

**Patched method:** `Character.DamageMe(int _incdmg, int _dmgType, Character _attacker, bool _criticalHit)`

**Postfix** reads the return value to determine outcome:

| Return Value | Event Type | Meaning |
|---|---|---|
| `> 0` | `Damage` | Hit landed, value is post-mitigation damage |
| `0` | `Miss` | Attack missed (dodge/parry) |
| `-2` | `ShieldAbsorb` | Spell shield absorbed the attack |
| `-1` | *(skipped)* | Target is invulnerable |
| `-3` | *(skipped)* | Friendly fire |
| `-5` | *(skipped)* | Mining node |
| `-6` | *(skipped)* | Treasure chest |

The attack source string (e.g. `"Melee"`, `"Skill:Backstab"`) is resolved from `CombatContext`. If no context is set, it defaults to `"Melee"`.

**Fields captured:** `_incdmg` as `RawAmount`, return value as `FinalAmount`, `_dmgType` mapped to a string (`Physical`, `Magic`, `Elemental`, `Void`, `Poison`), `_criticalHit` as `Critical`.

---

### Magic / Spell Damage

**Patched method:** `Character.MagicDamageMe(int _incdmg, int _dmgType, Character _attacker)`

| Return Value | Event Type | Meaning |
|---|---|---|
| `> 0` | `Damage` | Spell hit |
| `0` | `Resist` | Spell fully resisted |

Context defaults to `"Spell:Unknown"` if no `CombatContext` is set. Spells never crit in this game, so `Critical` is always `false`.

---

### Bleed Damage

**Patched method:** `Character.BleedDamageMe(Character _attacker, int _incdmg, int _dmgType, Spell _spell, bool _crit)`

This is the most complex damage path because the game passes `_attacker = null` for all bleed ticks (see `Stats.cs` line 1527). PerfectParse recovers the original caster using a queue-based approach.

#### How bleed owner recovery works

`Stats.TickEffects()` iterates status effect slots 0 through 29 and calls `BleedDamageMe` once per active bleed. PerfectParse exploits this deterministic order:

1. **First `BleedDamageMe` call for a given target in a frame:** The prefix scans all 30 status effect slots and enqueues every bleed owner (with spell name) in slot order.
2. **Each subsequent call:** The postfix dequeues the next entry from the queue.

Because the queue is built in the same slot order that `TickEffects` uses, the dequeue order matches the call order 1:1. This correctly attributes bleed damage even with multiple concurrent bleed sources (e.g. two Stormcallers bleeding the same raid boss).

The event source string is formatted as `"Bleed:SpellName"` (e.g. `"Bleed:Razor Tipped Arrow"`). See [Bleed Skill Name Resolution](#bleed-skill-name-resolution) for how the spell name is determined.

**Known caveat:** The first bleed tick in each frame gets slightly higher damage because it is calculated before the first tick reduces the target's HP.

#### Bleed skill map cleanup

`_bleedSkillMap` entries are pruned once per frame when the map exceeds 16 entries. Entries with destroyed Unity objects (checked via `ReferenceEquals(obj, null) || !obj`) are removed.

---

### Self-Damage

Two methods handle self-inflicted damage where source and target are the same entity:

| Patched Method | Source String | Example |
|---|---|---|
| `Character.SelfDamageMe()` | `"SelfDamage:Stance"` | Berserker stance recoil |
| `Character.SelfDamageMeFlat()` | `"SelfDamage:Flat"` | Ability HP costs |

Both emit `Damage` events.

---

### Damage Reflection

**Patched method:** `Character.DamageShieldTaken(int, Character _giver)` (prefix)

When a protective shield (e.g. Thorn Shield) reflects damage back to the attacker, this emits a `Reflect` event. The source is the shield owner (`_giver`) and the target is the attacker. Damage type is always `Magic`.

---

### Environmental Damage

**Patched method:** `Character.EnvironmentalDamageMe(int _incdmg)`

Lava, traps, fire pits, and other hazards. The `SourceId` is set to the literal string `"Environment"` (no attacker character exists). Return value `-1` (invulnerable) is skipped.

---

### Finale (Instant Kill)

**Patched method:** `WandBolt.DeliverDamage()` (prefix + postfix)

The Finale ascension mechanic sets `CurrentHP = 0` directly without calling `DamageMe`, so it would otherwise be invisible to the parser.

Detection logic (all conditions must be true):
- Target HP is 0 after delivery
- Target HP was > 0 before delivery
- Target HP was <= 15% of max HP before delivery

Emits a `Finale` event with `Source = "Wand (Finale)"` and `RawAmount` / `FinalAmount` set to the pre-delivery HP.

---

### Boss Mechanic Damage

Several boss scripts call `DamageMe` / `MagicDamageMe` directly without going through `SpellVessel.ResolveSpell`. Without dedicated patches, this damage would show as unnamed `"Melee"` attacks.

`BossMechanicPatches.cs` provides prefix patches that set `CombatContext` before the damage call. Each patch is wrapped in its own try/catch so a missing game class does not prevent others from loading.

| Game Class | Method | Context Set |
|---|---|---|
| `AEEvent` | `TriggerAE()` | `"Spell:{DamageReason}"` (or `"Spell:AoE"`) |
| `AEEvent2` | `Update()` | `"Spell:{DamageReason}"` (or `"Spell:AoE"`) |
| `NPCFightEvent` | `BreathAttack()` | `"Spell:Breath Attack"` |
| `SableheartEvent` | `Update()` | `"Spell:Sableheart's Curse"` |
| `AstraBreathScriot` | `Update()` | `"Spell:Dragon Breath"` (or `"Spell:{OverrideBreath}"`) |
| `FernHighPriest` | `Update()` | `"Spell:Shared Pain"` |
| `InfernoEnergy` | `Update()` | `"Spell:Twin Explosion"` |
| `DeathTouch` | `Update()` | `"Spell:Death Touch"` |

Private fields are accessed via Harmony's `Traverse.Create(__instance).Field(name).GetValue<T>()`.

---

## Heal Tracking

### Direct Heals

**Patched method:** `Stats.HealMe(Spell _spell, int _amt, bool _isCrit, bool _isMana, Character _source)` (postfix)

This handles full spell-based heals (e.g. Healing Touch, the heal component of Group Regrowth).

- Zero-amount heals without mana restore are skipped (these are HoT-only spell applications).
- `_amt` is captured as `RawAmount`, the method's return value (actual HP gained) as `ActualAmount`.
- If `_isMana` is true, the event type is `ManaRestore` instead of `Heal`.
- The `IsResonance` flag is read from `ResonanceContext` (see [Resonance Procs](#resonance-procs)).

---

### Healing Over Time (HoT)

**Patched method:** `Stats.TickEffects()` (prefix + postfix)

HoT tracking is complex because `TickEffects` processes all active HoTs in a single call and only the total HP delta is observable.

#### Prefix (before tick)

1. Captures pre-tick HP.
2. Scans all 30 status effect slots looking for active HoTs matching these criteria:
   - `TargetHealing > 0`
   - `Duration > 0`
   - `DamageType == Physical`
   - Not blocked by `CombatStance.StopRegen`
   - Not a `WornEffect` (equipment passive regen)
3. For each qualifying HoT, pre-calculates the expected tick amount:
   ```
   expected = effect.TargetHealing
            + (owner.WisScaleMod / 100) * owner.CurrentWis * 10
            + (if Druid: owner.CurrentWis)
   ```
4. Stores a list of `HoTInfo` structs: `(SpellName, Owner, ExpectedAmount)`.

#### Postfix (after tick)

1. Calculates actual delta: `CurrentHP - preTickHP`.
2. If delta > 0, distributes it proportionally across HoTs based on expected amounts:
   ```
   For each HoT:
     share = delta * (expected / totalExpected)
   Last HoT gets the remainder to prevent rounding drift.
   ```
3. Emits a `HoT` event for each contributing HoT.
4. Owner entity resolution is deferred until actual healing occurs (avoids registering destroyed entities that contributed 0).
5. Fallback: if no HoT slots were found but HP increased, a generic `"HoT:Unknown"` event is emitted.

**Example:** Two HoTs ticking with expected amounts 100 and 50, actual HP gained is 90:
- HoT 1: `90 * (100 / 150)` = 60
- HoT 2: `90 - 60` = 30 (remainder)

---

### Simple Heals (Lifesteal / Regen)

**Patched method:** `Stats.HealMe(int _amt)` (prefix + postfix)

This handles self-heals from lifesteal, lifetap, and regen effects.

- **Prefix** captures pre-heal HP.
- **Postfix** calculates actual healed = `CurrentHP - preHP`.
- Source and target are the same entity.
- Emits `HealSimple` event type.

---

### Mana Restoration

Mana restores flow through the same `Stats.HealMe(Spell, int, bool, bool, Character)` method as direct heals, with the `_isMana` parameter set to `true`. These are emitted as `ManaRestore` events with the same fields as `Heal` events.

---

### Resonance Procs

**Patched method:** `SpellVessel.ResolveSpell()` (prefix + postfix in HealPatches)

Resonance is a mechanic where spells can proc additional effects.

- **Prefix** reads the private `SpellVessel.resonating` field via reflection.
- If true, sets `ResonanceContext.IsResonance = true`.
- `HealMe` postfix reads this flag and includes it on the `HealEvent`.
- **Postfix** clears the flag.

This allows reports to distinguish resonance-triggered heals from normal casts.

---

## Attack Source Attribution

### CombatContext (Frame-Scoped)

`CombatContext` is the mechanism that connects "what kind of attack is happening" to "what damage was dealt." It is set by prefix patches (before the game method runs) and read by postfix patches (after damage is calculated).

**Design properties:**
- **Keyed by attacker Character instance** — multiple entities can act in the same frame without conflicts.
- **Auto-expires after 1 frame** — no manual cleanup needed.
- **Lazy cleanup** — stale entries (destroyed Unity objects, old frames) are removed once per frame when the dictionary exceeds 16 entries.

#### Context sources and what sets them

| Patched Method | Context Value |
|---|---|
| `PlayerCombat.HandleDamageResult()` | `"Melee"` |
| `NPC.PerformMeleeHit()` | `"Melee"` |
| `NPC.PerformMeleeHitPreCalc()` | `"Melee"` |
| `UseSkill.DoSkill()` | `"Skill:{SkillName}"` |
| `UseSkill.DoSkillNoChecks()` | `"Skill:{SkillName}"` |
| `SpellVessel.ResolveSpell()` | `"Spell:{SpellName}"` |
| `WandBolt.DeliverDamage()` | `"Bow"` (Physical) or `"Wand"` (Magic) |
| `NPC.DoBowAttack()` (all overloads) | `"Bow"` |
| Boss mechanic patches | Various (see [Boss Mechanic Damage](#boss-mechanic-damage)) |

#### Source string taxonomy

| Prefix | Meaning | Example |
|---|---|---|
| `Melee` | Melee auto-attack | `"Melee"` |
| `Bow` | Physical ranged (projectile) | `"Bow"` |
| `Wand` | Magical ranged (wand bolt) | `"Wand"` |
| `Skill:` | Named skill | `"Skill:Backstab"` |
| `Spell:` | Named spell | `"Spell:Fire Bolt"` |
| `Bleed:` | Bleed tick from named spell | `"Bleed:Razor Tipped Arrow"` |
| `SelfDamage:` | Self-inflicted | `"SelfDamage:Stance"` |
| `Wand (Finale)` | Finale instant kill | `"Wand (Finale)"` |

---

### Bleed Skill Name Resolution

The game separates skills from their effects at the data level. For example:

```
Skill: "Arterial Razor"
  -> CastOnTarget spell: "Razor Tipped Arrow"
    -> StatusEffect: "Bleed"
```

PerfectParse resolves the display name using a three-layer priority system:

| Priority | Source | When It Fires |
|---|---|---|
| **1 (highest)** | `UseSkill.DoSkill` / `DoSkillNoChecks` | Sets `_pendingBleedSkill[caster]` if the skill applies a bleed |
| **2** | `CastSpell.StartSpellFromProc` | Sets pending if not already set (proc-triggered bleeds) |
| **3 (lowest)** | `SpellVessel.ResolveSpell` | Sets pending if neither above set |

When `Character.AddStatusEffect` fires for a bleed spell, the `_pendingBleedSkill` value is persisted into `_bleedSkillMap` keyed by `(target, bleedSpell, owner)`. `BleedDamageMe` postfix looks up this map to get the display name.

**Result:** Bleed ticks display as `"Bleed:Razor Tipped Arrow"` (the spell-level name, which matches what the game uses in its own combat log), not the skill book name or generic "Bleed."

---

## Entity Identification

### Entity ID Format

| Entity Type | ID Format | Example |
|---|---|---|
| Player | `"Player"` | `"Player"` |
| Sim (grouped/raided NPC ally) | `"Sim:{Name}"` | `"Sim:Bowen"` |
| Pet / Summoned | `"Pet:{InstanceID}:{Name}"` | `"Pet:123456:Dragon"` |
| NPC (enemy) | `"NPC:{InstanceID}:{Name}"` | `"NPC:456789:Sableheart"` |

Instance IDs disambiguate multiple spawns of the same named entity.

### Entity Type Detection

```
if (!character.isNPC)         -> Player
if (stats.Charmed || npc.SummonedByPlayer) -> Pet
if (npc.SimPlayer)            -> SimPlayer
else                          -> NPC
```

### EntityRegistry Storage

Two dictionaries serve different purposes:

- **`_cache`** (keyed by Unity instance ID): Deduplication during gameplay. Cleared on zone change.
- **`_reportEntities`** (keyed by entity ID string): Persists across zone changes for HTML report generation. Never cleared.

### Class Name Remapping

Applied only to Player and SimPlayer entities:

| Code Name | Display Name |
|---|---|
| `Duelist` | `Windblade` |

---

## Event Relevance Filtering

`CombatEventBus` silently drops events that do not involve the player or their group. An entity is relevant if:

1. **Player** (`isNPC == false`): Always relevant.
2. **Pet/Summoned** (`Charmed` or `SummonedByPlayer`): Always relevant.
3. **Sim Player** (`SimPlayer == true`): Relevant if `InGroup` or `InRaid`.
4. **NPC**: Only relevant as a target if the source is relevant (or vice versa).

`InRaid` is checked via reflection for playtest/retail compatibility.

---

## DamageMe Return Value Reference

| Value | Meaning | PerfectParse Behavior |
|---|---|---|
| `> 0` | Damage dealt (post-mitigation) | Emit `Damage` event |
| `0` | Miss (DamageMe) / Resist (MagicDamageMe) | Emit `Miss` or `Resist` |
| `-1` | Invulnerable | Skip |
| `-2` | Spell shield absorbed | Emit `ShieldAbsorb` |
| `-3` | Friendly fire | Skip |
| `-5` | Mining node | Skip |
| `-6` | Treasure chest | Skip |

---

## Event Models

### CombatEvent

| Field | Type | Description |
|---|---|---|
| `Timestamp` | `long` | Unix milliseconds |
| `Type` | `string` | `Damage`, `Miss`, `Resist`, `ShieldAbsorb`, `Reflect`, `Finale` |
| `SourceId` | `string` | Attacker entity ID |
| `TargetId` | `string` | Defender entity ID |
| `DamageType` | `string` | `Physical`, `Magic`, `Elemental`, `Void`, `Poison` |
| `RawAmount` | `int` | Pre-mitigation damage |
| `FinalAmount` | `int` | Post-mitigation damage |
| `Critical` | `bool` | Critical hit flag |
| `Source` | `string` | Attack source (see [taxonomy](#source-string-taxonomy)) |
| `EncounterId` | `int` | Encounter this event belongs to |

### HealEvent

| Field | Type | Description |
|---|---|---|
| `Timestamp` | `long` | Unix milliseconds |
| `Type` | `string` | `Heal`, `HoT`, `HealSimple`, `ManaRestore` |
| `SourceId` | `string` | Healer entity ID |
| `TargetId` | `string` | Target entity ID |
| `SpellName` | `string` | Spell or ability name |
| `RawAmount` | `int` | Expected heal amount |
| `ActualAmount` | `int` | Actual HP/mana gained |
| `Critical` | `bool` | Critical hit flag |
| `IsMana` | `bool` | True if mana restore |
| `IsResonance` | `bool` | True if triggered by resonance proc |
| `EncounterId` | `int` | Encounter this event belongs to |

---

## JSONL Output Format

Each line is a self-contained JSON object. The `"ev"` field distinguishes event types.

### Combat event

```json
{
  "ev": "combat",
  "t": 1700000000000,
  "type": "Damage",
  "src": "Player",
  "tgt": "NPC:456789:Sableheart",
  "dmgType": "Physical",
  "raw": 250,
  "final": 200,
  "crit": true,
  "source": "Skill:Backstab",
  "enc": 1
}
```

### Heal event

```json
{
  "ev": "heal",
  "t": 1700000000000,
  "type": "Heal",
  "src": "Sim:Bowen",
  "tgt": "Player",
  "spell": "Healing Touch",
  "raw": 500,
  "actual": 480,
  "crit": true,
  "reso": true,
  "enc": 1
}
```

### Entity snapshot

```json
{
  "ev": "entity",
  "id": "Pet:123456:Dragon",
  "name": "Dragon",
  "class": "Magic Beast",
  "level": 20,
  "type": "Pet",
  "master": "Player"
}
```

---

## Known Edge Cases

**Mizuki dagger throw:** `MizukiEvent.SetNewAggro` is a coroutine that calls `DamageMe` after `yield return new WaitForFixedUpdate()`. The frame-scoped `CombatContext` expires before the damage call fires, so the dagger throw appears as `"Melee"`. Mizuki also has real melee attacks, making it indistinguishable without component-level checks. Left as-is.

**First bleed tick damage:** The first bleed tick in a frame is calculated on the target's current HP before any bleed tick reduces it, resulting in slightly higher damage than subsequent ticks in the same frame.

**HoT rounding:** When multiple HoTs tick simultaneously, proportional distribution can produce fractional values. The last HoT in the list receives the remainder to prevent drift.

**Destroyed entity references:** Unity overrides `==` for destroyed objects. All null checks for Unity objects use `ReferenceEquals(obj, null) || !obj` to correctly detect destruction.
