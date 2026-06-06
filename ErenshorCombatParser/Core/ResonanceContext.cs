namespace ErenshorCombatParser.Core
{
    /// <summary>
    /// Simple static flag indicating that the current spell resolution is a
    /// resonance proc. Set by a prefix on SpellVessel.ResolveSpell, consumed
    /// by HealPatches.HealMe_Full_Postfix, and cleared by a postfix on the
    /// same method.
    /// </summary>
    public static class ResonanceContext
    {
        public static bool IsResonance;

        public static void Reset()
        {
            IsResonance = false;
        }
    }
}
