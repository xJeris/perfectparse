# PerfectParse Code Audit — v0.2.2

Comprehensive review of the codebase for dead code, duplicate code, bugs, and security issues.

---

## Dead Code

### 1. ~~`CombatContext.Clear()` never called~~ — FIXED
- **File:** `Core/CombatContext.cs`
- The `Clear()` method existed but was never referenced. Context auto-expires after one frame via the frame count check in `Get()`, so manual clearing was unnecessary.
- **Resolution:** Removed the dead method.

### 2. ~~`OnWindowMoved` field never used~~ — FIXED
- **File:** `UI/CombatWindow.cs`
- `public Action<Rect> OnWindowMoved` was declared but never assigned or invoked. Window position persistence is handled directly in `Plugin.OnGUI()` via config entries.
- **Resolution:** Removed the dead field.

---

## Duplicate Code

### 3. ~~`EscapeJson()` duplicated 5 times~~ — FIXED
- **Files:** `Models/CombatEvent.cs`, `Models/HealEvent.cs`, `Core/EntityRegistry.cs` (x2), `IO/HtmlReportGenerator.cs`
- All five copies were identical: `s.Replace("\\", "\\\\").Replace("\"", "\\\"")`.
- **Resolution:** Extracted to `IO/JsonUtil.cs` as `JsonUtil.EscapeJson()`. All call sites updated. PerfectParseReport project updated to include the shared file.

---

## Bugs

### 4. ~~First auto-encounter event gets EncounterId = -1~~ — FIXED
- **File:** `Core/CombatEventBus.cs`
- **Severity:** Medium
- `EmitDamage()` was assigning `evt.EncounterId` *before* calling `NotifyCombatActivity()`. The first combat event that triggered auto-detection got `EncounterId = -1` because the encounter hadn't started yet.
- **Resolution:** Moved encounter ID assignment to after `NotifyCombatActivity()`, so the first event gets the correct ID.

### 5. ~~`EncounterTracker.ToJson()` doesn't escape `Label`~~ — FIXED
- **File:** `Core/EncounterTracker.cs`
- **Severity:** Low
- `e.Label` was appended directly into JSON without escaping. Currently labels are always `"Encounter N"` so it was safe, but would break if labels ever contained quotes or backslashes.
- **Resolution:** Applied `JsonUtil.EscapeJson()` to the label output.

### 6. `HealPatches` prefix can overwrite tracking data on nested calls
- **File:** `Patches/HealPatches.cs:150-157, 202-209`
- **Severity:** Low
- `_preHealHP` and `_preTickHP` dictionaries are keyed by instance ID. If `HealMe(int)` or `TickEffects` is called reentrantly on the same character before the postfix runs, the prefix overwrites the stored HP, corrupting the delta calculation. Unlikely in practice since Unity runs single-threaded and these methods aren't typically recursive, but worth noting.

### 7. Silent exception swallowing in patch handlers
- **Files:** Multiple locations in `Patches/DamagePatches.cs`, `Patches/ContextPatches.cs`, `Patches/FinalePatches.cs`
- **Severity:** Low
- Several patch methods use bare `catch (Exception) { }` with no logging. If these patches throw, the error is invisible and debugging becomes difficult. The main damage/heal handlers do log errors, but secondary handlers (BleedDamageMe, SelfDamageMe, etc.) silently swallow them.

### 8. `HtmlReportGenerator.ExtractJsonString()` doesn't handle escaped quotes
- **File:** `IO/HtmlReportGenerator.cs:166-175`
- **Severity:** Low
- The JSON string parser finds the closing quote by searching for `"` after the opening quote, but doesn't account for `\"` inside the value. If an entity name contains a quote character, the parser will truncate the value. In practice this is unlikely since entity names come from the game, but it's a latent parsing bug.

---

## Security Issues

### 9. Unvalidated output directory from config
- **File:** `Plugin.cs:73-75`
- **Severity:** Low
- The `OutputDirectory` config entry is used directly as a file path without validation. A user could set it to a UNC path (`\\server\share`) or use `..` traversal. However, since this is a local BepInEx config that the user themselves edits, the risk is self-inflicted only.

### 10. ~~Hardcoded Steam path in PerfectParseReport~~ — FIXED
- **File:** `PerfectParseReport/Program.cs`
- **Severity:** Low
- `FindLatestJsonl()` had a hardcoded `C:\Program Files (x86)\Steam\...` path. Wouldn't work if Steam was installed elsewhere or on a different drive.
- **Resolution:** Replaced with dynamic discovery via Windows registry (`HKLM\SOFTWARE\Valve\Steam\InstallPath`) and parsing `libraryfolders.vdf` to find all Steam library folders. Also searches for both "Erenshor Playtest" and "Erenshor" game names.

---

## Summary

| #  | Category   | Severity | Status | Description                                        |
|----|------------|----------|--------|----------------------------------------------------|
| 1  | Dead code  | Low      | FIXED  | `CombatContext.Clear()` unused                     |
| 2  | Dead code  | Low      | FIXED  | `OnWindowMoved` field unused                       |
| 3  | Duplicate  | Medium   | FIXED  | `EscapeJson()` copied 5 times                      |
| 4  | Bug        | Medium   | FIXED  | First encounter event gets wrong EncounterId       |
| 5  | Bug        | Low      | FIXED  | Encounter label not JSON-escaped                   |
| 6  | Bug        | Low      | Open   | HealPatches prefix overwrite on nested calls       |
| 7  | Bug        | Low      | Open   | Silent exception swallowing in some patch handlers  |
| 8  | Bug        | Low      | Open   | JSON parser doesn't handle escaped quotes          |
| 9  | Security   | Low      | Open   | Output directory config not validated               |
| 10 | Security   | Low      | FIXED  | Hardcoded Steam install path                        |
