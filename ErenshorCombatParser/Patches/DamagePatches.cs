using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ErenshorCombatParser.Core;
using ErenshorCombatParser.Models;

namespace ErenshorCombatParser.Patches
{
    /// <summary>
    /// Harmony patches for all damage entry points in Character.
    /// Uses manual patching for reliability.
    /// </summary>
    public static class DamagePatches
    {
        // Bleed info queue: built once per target per frame tick, consumed in order
        private struct BleedInfo
        {
            public Character Owner;
            public string SpellName;
        }
        private static readonly Queue<BleedInfo> _bleedQueue = new Queue<BleedInfo>();
        private static int _bleedQueueFrame = -1;
        private static Character _bleedQueueTarget;

        // Persistent map: (target, bleed Spell asset, owner) → originating skill name.
        // Populated by AddStatusEffect postfix using _pendingBleedSkill context.
        // Consumed by BleedDamageMe to attribute bleed ticks back to the originating
        // skill (e.g. "Arterial Razor").
        private struct BleedKey : IEquatable<BleedKey>
        {
            public Character Target;
            public Spell Effect;
            public Character Owner;
            public bool Equals(BleedKey other) =>
                ReferenceEquals(Target, other.Target) &&
                ReferenceEquals(Effect, other.Effect) &&
                ReferenceEquals(Owner, other.Owner);
            public override bool Equals(object obj) => obj is BleedKey k && Equals(k);
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Target != null ? Target.GetHashCode() : 0;
                    h = h * 397 ^ (Effect != null ? Effect.GetHashCode() : 0);
                    h = h * 397 ^ (Owner != null ? Owner.GetHashCode() : 0);
                    return h;
                }
            }
        }
        private static readonly Dictionary<BleedKey, string> _bleedSkillMap = new Dictionary<BleedKey, string>();

        // Per-caster pending bleed skill name. Set in DoSkill prefix when the skill
        // has a bleed EffectToApply. Consumed by AddStatusEffect postfix.
        // This survives the CombatContext overwrite from ResolveSpell.
        private static readonly Dictionary<Character, string> _pendingBleedSkill = new Dictionary<Character, string>();

        // Cleanup tracking for _bleedSkillMap
        private static int _bleedMapCleanupFrame = -1;
        private static readonly List<BleedKey> _staleBleedKeys = new List<BleedKey>();

        // DOT tick attribution: when inside Stats.TickEffects(), DamageMe calls
        // for status effect ticks get their source from the effect's SpellName
        // instead of CombatContext (which may hold stale "Melee" from a concurrent
        // auto-attack). Queues are keyed by (attacker, dmgType) to handle resisted
        // ticks that skip the DamageMe call without desynchronizing.
        private static bool _inTickEffects;
        private static Character _tickEffectsTarget;
        private struct DotKey : IEquatable<DotKey>
        {
            public Character Attacker;
            public GameData.DamageType DmgType;
            public bool Equals(DotKey other) =>
                ReferenceEquals(Attacker, other.Attacker) && DmgType == other.DmgType;
            public override bool Equals(object obj) => obj is DotKey k && Equals(k);
            public override int GetHashCode()
            {
                unchecked { return (Attacker != null ? Attacker.GetHashCode() : 0) * 397 ^ (int)DmgType; }
            }
        }
        private static readonly Dictionary<DotKey, Queue<string>> _dotQueues = new Dictionary<DotKey, Queue<string>>();
        // Reusable queue pool to avoid allocations each tick
        private static readonly List<Queue<string>> _dotQueuePool = new List<Queue<string>>();

        // Last DamageMe result tracking — used by FinalePatches to compute
        // correct Finale amount by subtracting the wand hit that preceded it.
        private static Character _lastDmgTarget;
        private static int _lastDmgAmount;
        private static int _lastDmgFrame = -1;

        /// <summary>
        /// Returns and clears the last DamageMe amount for the given target
        /// if it occurred in the current frame. Returns 0 otherwise.
        /// </summary>
        public static int ConsumeLastDamage(Character target)
        {
            if (target != null && ReferenceEquals(target, _lastDmgTarget)
                && _lastDmgFrame == UnityEngine.Time.frameCount)
            {
                int amount = _lastDmgAmount;
                _lastDmgTarget = null;
                _lastDmgAmount = 0;
                return amount;
            }
            return 0;
        }

        /// <summary>
        /// Clears cached state that could leak between sessions (e.g. scene changes).
        /// </summary>
        public static void ClearState()
        {
            _bleedQueue.Clear();
            _bleedQueueFrame = -1;
            _bleedQueueTarget = null;
            _bleedSkillMap.Clear();
            _pendingBleedSkill.Clear();
            _bleedMapCleanupFrame = -1;
            _staleBleedKeys.Clear();
            _lastDmgTarget = null;
            _lastDmgAmount = 0;
            _lastDmgFrame = -1;
            _inTickEffects = false;
            _tickEffectsTarget = null;
            foreach (var kvp in _dotQueues)
            {
                kvp.Value.Clear();
                _dotQueuePool.Add(kvp.Value);
            }
            _dotQueues.Clear();
        }

        public static void Apply(Harmony harmony)
        {
            var self = typeof(DamagePatches);

            // DamageMe
            var dmgMe = typeof(Character).GetMethod("DamageMe",
                BindingFlags.Public | BindingFlags.Instance);
            if (dmgMe != null)
            {
                harmony.Patch(dmgMe,
                    postfix: new HarmonyMethod(self.GetMethod("DamageMe_Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
            }
            else Log.Error("Could not find Character.DamageMe");

            // MagicDamageMe
            var magicDmg = typeof(Character).GetMethod("MagicDamageMe",
                BindingFlags.Public | BindingFlags.Instance);
            if (magicDmg != null)
            {
                harmony.Patch(magicDmg,
                    postfix: new HarmonyMethod(self.GetMethod("MagicDamageMe_Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
            }
            else Log.Error("Could not find Character.MagicDamageMe");

            // BleedDamageMe
            var bleedDmg = typeof(Character).GetMethod("BleedDamageMe",
                BindingFlags.Public | BindingFlags.Instance);
            if (bleedDmg != null)
            {
                harmony.Patch(bleedDmg,
                    prefix: new HarmonyMethod(self.GetMethod("BleedDamageMe_Prefix",
                        BindingFlags.Static | BindingFlags.NonPublic)),
                    postfix: new HarmonyMethod(self.GetMethod("BleedDamageMe_Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
            }
            else Log.Error("Could not find Character.BleedDamageMe");

            // SelfDamageMe
            var selfDmg = typeof(Character).GetMethod("SelfDamageMe",
                BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(float) }, null);
            if (selfDmg != null)
            {
                harmony.Patch(selfDmg,
                    postfix: new HarmonyMethod(self.GetMethod("SelfDamageMe_Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
            }
            else Log.Error("Could not find Character.SelfDamageMe");

            // SelfDamageMeFlat
            var selfDmgFlat = typeof(Character).GetMethod("SelfDamageMeFlat",
                BindingFlags.Public | BindingFlags.Instance);
            if (selfDmgFlat != null)
            {
                harmony.Patch(selfDmgFlat,
                    postfix: new HarmonyMethod(self.GetMethod("SelfDamageMeFlat_Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
            }
            else Log.Error("Could not find Character.SelfDamageMeFlat");

            // DamageShieldTaken
            var dmgShield = typeof(Character).GetMethod("DamageShieldTaken",
                BindingFlags.Public | BindingFlags.Instance);
            if (dmgShield != null)
            {
                harmony.Patch(dmgShield,
                    prefix: new HarmonyMethod(self.GetMethod("DamageShieldTaken_Prefix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
            }
            else Log.Error("Could not find Character.DamageShieldTaken");

            // EnvironmentalDamageMe
            var envDmg = typeof(Character).GetMethod("EnvironmentalDamageMe",
                BindingFlags.Public | BindingFlags.Instance);
            if (envDmg != null)
            {
                harmony.Patch(envDmg,
                    postfix: new HarmonyMethod(self.GetMethod("EnvironmentalDamageMe_Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
            }
            else Log.Error("Could not find Character.EnvironmentalDamageMe");

            // TickEffects — DOT tick attribution
            var tickFx = typeof(Stats).GetMethod("TickEffects",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (tickFx != null)
            {
                harmony.Patch(tickFx,
                    prefix: new HarmonyMethod(self.GetMethod("TickEffects_Prefix",
                        BindingFlags.Static | BindingFlags.NonPublic)),
                    postfix: new HarmonyMethod(self.GetMethod("TickEffects_Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
            }
            else Log.Error("Could not find Stats.TickEffects");

            // AddStatusEffect — capture originating skill name for bleed effects.
            // 4-param overload (called by UseSkill.DoSkill with _specificCaster)
            var addSE4 = typeof(Stats).GetMethod("AddStatusEffect",
                BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(Spell), typeof(bool), typeof(int), typeof(Character) }, null);
            if (addSE4 != null)
            {
                harmony.Patch(addSE4,
                    postfix: new HarmonyMethod(self.GetMethod("AddStatusEffect4_Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
            }

            // 5-param overload (with explicit duration)
            var addSE5 = typeof(Stats).GetMethod("AddStatusEffect",
                BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(Spell), typeof(bool), typeof(int), typeof(Character), typeof(float) }, null);
            if (addSE5 != null)
            {
                harmony.Patch(addSE5,
                    postfix: new HarmonyMethod(self.GetMethod("AddStatusEffect5_Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic)));
            }
        }

        // ============================================================
        // Postfix handlers
        // ============================================================

        static void DamageMe_Postfix(
            int __result,
            Character __instance,
            int _incdmg,
            GameData.DamageType _dmgType,
            Character _attacker,
            bool _criticalHit)
        {
            try
            {
                string eventType;
                int finalAmount;

                if (__result > 0) { eventType = "Damage"; finalAmount = __result; }
                else if (__result == 0) { eventType = "Miss"; finalAmount = 0; }
                else if (__result == -2) { eventType = "ShieldAbsorb"; finalAmount = 0; }
                else return; // -1 invuln, -3 friendly, -5 mining, -6 chest — skip

                // The game's DoSkill/DoSkillNoChecks call isCriticalAttack() but
                // store the result in a local variable, never passing it through
                // to DamageMe's _criticalHit parameter. ConsumeCrit() checks if
                // isCriticalAttack() returned true for this attacker this frame.
                // Always consume (even when _criticalHit is already true) to
                // prevent stale overrides leaking into later DamageMe calls
                // in the same frame (e.g. melee auto-attack crit followed by
                // a backstab that shouldn't inherit it).
                bool critOverride = CombatContext.ConsumeCrit(_attacker);
                bool isCrit = _criticalHit || critOverride;

                // If we're inside TickEffects, attribute this damage to the DOT
                // spell name instead of whatever CombatContext holds (which may
                // be "Melee" from a concurrent auto-attack in the same frame).
                string source = null;
                if (_inTickEffects && ReferenceEquals(__instance, _tickEffectsTarget)
                    && _attacker != null)
                {
                    var dotKey = new DotKey { Attacker = _attacker, DmgType = _dmgType };
                    Queue<string> dotQueue;
                    if (_dotQueues.TryGetValue(dotKey, out dotQueue) && dotQueue.Count > 0)
                        source = "Spell:" + dotQueue.Dequeue();
                }
                if (source == null)
                    source = CombatContext.Get(_attacker) ?? "Melee";

                // Record last damage for FinalePatches to subtract from PreHP
                if (__result > 0)
                {
                    _lastDmgTarget = __instance;
                    _lastDmgAmount = __result;
                    _lastDmgFrame = UnityEngine.Time.frameCount;
                }

                CombatEventBus.EmitDamage(new CombatEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = eventType,
                    SourceId = EntityRegistry.ResolveId(_attacker),
                    TargetId = EntityRegistry.ResolveId(__instance),
                    DamageType = _dmgType.ToString(),
                    RawAmount = _incdmg,
                    FinalAmount = finalAmount,
                    Critical = isCrit,
                    Source = source
                }, _attacker, __instance);
            }
            catch (Exception ex) { Log.Error("DamageMe: " + ex); }
        }

        static void MagicDamageMe_Postfix(
            int __result,
            Character __instance,
            int _dmg,
            GameData.DamageType _dmgType,
            Character _attacker)
        {
            try
            {
                string eventType;
                int finalAmount;

                if (__result > 0) { eventType = "Damage"; finalAmount = __result; }
                else if (__result == 0) { eventType = "Resist"; finalAmount = 0; }
                else return;

                string source = CombatContext.Get(_attacker) ?? "Spell:Unknown";

                CombatEventBus.EmitDamage(new CombatEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = eventType,
                    SourceId = EntityRegistry.ResolveId(_attacker),
                    TargetId = EntityRegistry.ResolveId(__instance),
                    DamageType = _dmgType.ToString(),
                    RawAmount = _dmg,
                    FinalAmount = finalAmount,
                    Critical = false,
                    Source = source
                }, _attacker, __instance);
            }
            catch (Exception ex) { Log.Error("MagicDamageMe: " + ex); }
        }

        // ============================================================
        // AddStatusEffect postfixes — capture originating skill for bleeds
        // ============================================================

        /// <summary>
        /// Called from ContextPatches when a skill that applies a bleed is used.
        /// Stashes the skill name so AddStatusEffect postfix can pick it up.
        /// </summary>
        public static void SetPendingBleedSkill(Character caster, string skillName)
        {
            if (caster != null && skillName != null)
            {
                _pendingBleedSkill[caster] = skillName;
            }
        }

        public static bool HasPendingBleedSkill(Character caster)
        {
            return caster != null && _pendingBleedSkill.ContainsKey(caster);
        }

        static void RecordBleedSkill(Stats __instance, Spell spell, Character caster)
        {
            try
            {
                if (spell == null) return;
                if (caster == null) return;

                string pendingSkill;
                bool hasPending = _pendingBleedSkill.TryGetValue(caster, out pendingSkill);

                if (spell.BleedDamagePercent <= 0) return;
                if (!hasPending) return;
                _pendingBleedSkill.Remove(caster);

                var target = __instance.Myself;
                if (target == null) return;

                // Strip CombatContext prefix (e.g. "Skill:Arterial Razor" → "Arterial Razor")
                string displayName = pendingSkill;
                int colonIdx = displayName.IndexOf(':');
                if (colonIdx >= 0)
                    displayName = displayName.Substring(colonIdx + 1);

                var key = new BleedKey { Target = target, Effect = spell, Owner = caster };
                _bleedSkillMap[key] = displayName;
            }
            catch (Exception ex) { Log.Error("[BleedRecord] " + ex); }
        }

        static void AddStatusEffect4_Postfix(Stats __instance, Spell spell, Character _specificCaster)
        {
            RecordBleedSkill(__instance, spell, _specificCaster);
        }

        static void AddStatusEffect5_Postfix(Stats __instance, Spell spell, Character _specificCaster)
        {
            RecordBleedSkill(__instance, spell, _specificCaster);
        }

        // ============================================================
        // BleedDamageMe prefix/postfix
        // ============================================================

        static void BleedDamageMe_Prefix(Character __instance, Character _attacker)
        {
            try
            {
                if (_attacker != null) return;

                // Build the owner queue once per target per frame.
                // TickEffects iterates slots 0-29 and calls BleedDamageMe for each
                // active bleed, so the queue order matches the call order.
                int frame = UnityEngine.Time.frameCount;
                // Periodically prune stale entries from _bleedSkillMap (at most once per frame)
                if (frame != _bleedMapCleanupFrame && _bleedSkillMap.Count > 16)
                {
                    _bleedMapCleanupFrame = frame;
                    _staleBleedKeys.Clear();
                    foreach (var kvp in _bleedSkillMap)
                    {
                        if (ReferenceEquals(kvp.Key.Target, null) || !kvp.Key.Target
                            || ReferenceEquals(kvp.Key.Owner, null) || !kvp.Key.Owner)
                            _staleBleedKeys.Add(kvp.Key);
                    }
                    for (int j = 0; j < _staleBleedKeys.Count; j++)
                        _bleedSkillMap.Remove(_staleBleedKeys[j]);
                }

                if (frame != _bleedQueueFrame || _bleedQueueTarget != __instance)
                {
                    _bleedQueue.Clear();
                    _bleedQueueFrame = frame;
                    _bleedQueueTarget = __instance;

                    var stats = __instance.MyStats;
                    if (stats != null)
                    {
                        var effects = stats.StatusEffects;
                        if (effects != null)
                        {
                            for (int i = 0; i < effects.Length; i++)
                            {
                                if (effects[i] != null && effects[i].Effect != null
                                    && effects[i].Effect.BleedDamagePercent > 0)
                                {
                                    // Look up the originating skill name from our map;
                                    // falls back to the bleed spell's own SpellName if
                                    // we didn't capture the skill at application time.
                                    var spell = effects[i].Effect;
                                    var owner = effects[i].Owner;
                                    string skillName = null;
                                    var key = new BleedKey { Target = __instance, Effect = spell, Owner = owner };
                                    _bleedSkillMap.TryGetValue(key, out skillName);

                                    _bleedQueue.Enqueue(new BleedInfo
                                    {
                                        Owner = owner,
                                        SpellName = skillName ?? spell.SpellName
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception) { }
        }

        static void BleedDamageMe_Postfix(
            int __result,
            Character __instance,
            int _incdmg,
            Character _attacker)
        {
            try
            {
                if (__result <= 0) return;

                // Use the actual attacker if provided, otherwise dequeue the
                // next bleed info (matched by slot order in TickEffects)
                var effectiveAttacker = _attacker;
                string source = "Bleed";

                if (effectiveAttacker == null && _bleedQueue.Count > 0)
                {
                    var info = _bleedQueue.Dequeue();
                    effectiveAttacker = info.Owner;
                    source = !string.IsNullOrEmpty(info.SpellName)
                        ? "Bleed:" + info.SpellName
                        : "Bleed";
                }

                CombatEventBus.EmitDamage(new CombatEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = "Damage",
                    SourceId = EntityRegistry.ResolveId(effectiveAttacker),
                    TargetId = EntityRegistry.ResolveId(__instance),
                    DamageType = "Physical",
                    RawAmount = _incdmg,
                    FinalAmount = __result,
                    Critical = false,
                    Source = source
                }, effectiveAttacker, __instance);
            }
            catch (Exception) { }
        }

        // ============================================================
        // TickEffects prefix/postfix — DOT tick attribution
        // ============================================================

        static void TickEffects_Prefix(Stats __instance)
        {
            try
            {
                _inTickEffects = true;
                _tickEffectsTarget = __instance.Myself;

                // Return all current queues to the pool, then clear the dictionary
                foreach (var kvp in _dotQueues)
                {
                    kvp.Value.Clear();
                    _dotQueuePool.Add(kvp.Value);
                }
                _dotQueues.Clear();

                var effects = __instance.StatusEffects;
                if (effects == null) return;

                for (int i = 0; i < effects.Length; i++)
                {
                    if (effects[i] == null || effects[i].Effect == null) continue;
                    var effect = effects[i].Effect;
                    if (effect.TargetDamage > 0 && effects[i].Duration > 0f)
                    {
                        var attacker = effects[i].CreditDPS;
                        if (attacker == null) continue;

                        var key = new DotKey { Attacker = attacker, DmgType = effect.MyDamageType };
                        Queue<string> queue;
                        if (!_dotQueues.TryGetValue(key, out queue))
                        {
                            // Grab a queue from the pool or create a new one
                            if (_dotQueuePool.Count > 0)
                            {
                                queue = _dotQueuePool[_dotQueuePool.Count - 1];
                                _dotQueuePool.RemoveAt(_dotQueuePool.Count - 1);
                            }
                            else
                            {
                                queue = new Queue<string>();
                            }
                            _dotQueues[key] = queue;
                        }
                        queue.Enqueue(effect.SpellName ?? "Unknown");
                    }
                }
            }
            catch (Exception) { }
        }

        static void TickEffects_Postfix()
        {
            _inTickEffects = false;
            _tickEffectsTarget = null;
        }

        static void SelfDamageMe_Postfix(
            int __result,
            Character __instance)
        {
            try
            {
                if (__result <= 0) return;

                var id = EntityRegistry.ResolveId(__instance);
                CombatEventBus.EmitDamage(new CombatEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = "Damage",
                    SourceId = id,
                    TargetId = id,
                    DamageType = "Physical",
                    RawAmount = __result,
                    FinalAmount = __result,
                    Critical = false,
                    Source = "SelfDamage:Stance"
                }, __instance, __instance);
            }
            catch (Exception) { }
        }

        static void SelfDamageMeFlat_Postfix(
            int __result,
            Character __instance,
            int _incDmg)
        {
            try
            {
                if (__result <= 0) return;

                var id = EntityRegistry.ResolveId(__instance);
                CombatEventBus.EmitDamage(new CombatEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = "Damage",
                    SourceId = id,
                    TargetId = id,
                    DamageType = "Physical",
                    RawAmount = _incDmg,
                    FinalAmount = __result,
                    Critical = false,
                    Source = "SelfDamage:Flat"
                }, __instance, __instance);
            }
            catch (Exception) { }
        }

        static void DamageShieldTaken_Prefix(
            Character __instance,
            int _dmg,
            Stats _giver)
        {
            try
            {
                if (_dmg <= 0) return;

                var sourceChar = _giver != null ? _giver.Myself : null;

                CombatEventBus.EmitDamage(new CombatEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = "Reflect",
                    SourceId = EntityRegistry.ResolveId(sourceChar),
                    TargetId = EntityRegistry.ResolveId(__instance),
                    DamageType = "Magic",
                    RawAmount = _dmg,
                    FinalAmount = _dmg,
                    Critical = false,
                    Source = "DamageShield"
                }, sourceChar, __instance);
            }
            catch (Exception) { }
        }

        static void EnvironmentalDamageMe_Postfix(
            int __result,
            Character __instance,
            int _dmg)
        {
            try
            {
                if (__result == -1) return;

                CombatEventBus.EmitDamage(new CombatEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Type = "Damage",
                    SourceId = "Environment",
                    TargetId = EntityRegistry.ResolveId(__instance),
                    DamageType = "Physical",
                    RawAmount = _dmg,
                    FinalAmount = _dmg,
                    Critical = false,
                    Source = "Environmental"
                }, target: __instance);
            }
            catch (Exception) { }
        }
    }
}
