# PerfectParse

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for **Erenshor** that tracks real-time combat events and generates self-contained HTML reports with DPS calculations, damage/healing breakdowns, and encounter tracking.

## Features

- Intercepts all damage and healing events via Harmony patches
- Tracks melee, ranged, skills, spells, wands, bleeds, reflects, and Finale instant-kills
- Healing tracking with spell attribution, HoT ticks, and overhealing
- Entity identification: player, sim party members, pets, and NPCs
- Automatic and manual encounter boundaries with idle timeout
- JSONL event logging (async, non-blocking background writer)
- Self-contained HTML reports with:
  - 5 tabs: Overview, Damage, Healing, Encounters, NPCs/Enemies
  - Expandable per-character breakdowns
  - DPS by session and encounter time
  - Damage type color coding
  - Replay mode with adjustable playback speed
  - Sortable columns

## Requirements

- Erenshor (Steam)
- BepInEx 5.4.x (x64) — **not** 6.x
- .NET Framework 4.7.2 SDK (for building)

## Building

```bash
dotnet build ErenshorCombatParser/ErenshorCombatParser.csproj -c Release
dotnet build PerfectParseReport/PerfectParseReport.csproj -c Release
```

The mod DLL requires references to BepInEx, Harmony, and Erenshor game assemblies. The `.csproj` expects these at standard paths under the game's install directory — update `HintPath` entries if your game is installed elsewhere.

Output:
- `ErenshorCombatParser/bin/Release/net472/PerfectParse.dll` — the mod plugin
- `PerfectParseReport/bin/Release/net472/PerfectParseReport.exe` — standalone report generator

## Installation

Copy `PerfectParse.dll` and `PerfectParseReport.exe` into:

```
<Game Folder>/BepInEx/plugins/PerfectParse/
```

**Required config change** — in `BepInEx/config/BepInEx.cfg`, under `[Chainloader]`:

```ini
HideManagerGameObject = true
```

Without this, Harmony patches cannot intercept Erenshor's methods and the mod will silently do nothing.

## Usage

| Hotkey | Action |
|--------|--------|
| F9 | Toggle manual encounter start/stop |
| F10 | Generate HTML report from current session |

Logs and reports are saved to `BepInEx/plugins/PerfectParse/logs/`.

**Standalone report generator:** Run `PerfectParseReport.exe` to convert the latest JSONL log into an HTML report without the game running. Supports drag-and-drop or command line: `PerfectParseReport.exe <file.jsonl> [output.html]`.

## Configuration

Generated at `BepInEx/config/com.erenshor.perfectparse.cfg` after first run:

| Section | Key | Default | Description |
|---------|-----|---------|-------------|
| Hotkeys | EncounterToggle | F9 | Manual encounter key |
| Hotkeys | GenerateReport | F10 | Report generation key |
| Encounters | IdleTimeout | 5.0 | Seconds before auto-ending encounter |
| General | EnableLogging | true | Master logging toggle |
| General | OutputDirectory | *(blank)* | Custom output path |
| Filters | LogEnvironmental | false | Log environmental damage |
| Filters | LogNPCvsNPC | false | Log NPC-on-NPC combat |

## Project Structure

```
ErenshorCombatParser/        Main mod (BepInEx plugin DLL)
  Plugin.cs                  Entry point, config, hotkeys
  Core/                      Event bus, entity registry, encounter tracker, combat context
  Patches/                   Harmony patches for damage, healing, context, finale
  Models/                    CombatEvent, HealEvent, Encounter
  IO/                        JSONL writer, HTML report generator, HTML template

PerfectParseReport/          Standalone CLI report generator (EXE)

```

## License

[MIT](LICENSE)
