using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ErenshorCombatParser.Core;
using ErenshorCombatParser.Models;

namespace ErenshorCombatParser.UI
{
    public class CombatWindow
    {
        // --- Data model ---

        private class EntityDmg
        {
            public long Total;
            public int Hits;
            public int Crits;
            public int Misses;
            public int Resists;
            public Dictionary<string, long> ByType = new Dictionary<string, long>();
            public Dictionary<string, SourceDetail> BySource = new Dictionary<string, SourceDetail>();
        }

        private class SourceDetail
        {
            public long Total;
            public int Hits;
            public int Crits;
            public string DmgType;
            public bool SelfDmg;
        }

        private class EntityHeal
        {
            public long Total;
            public long Overhealing;
            public int Casts;
            public Dictionary<string, SpellDetail> BySpell = new Dictionary<string, SpellDetail>();
        }

        private class SpellDetail
        {
            public long Total;
            public int Casts;
            public long Overhealing;
        }

        private class EncEntityDmg
        {
            public long Dmg;
            public int Hits;
            public int Crits;
            public Dictionary<string, long> ByType = new Dictionary<string, long>();
            public Dictionary<string, SourceDetail> BySource = new Dictionary<string, SourceDetail>();
        }

        private class EncStats
        {
            public long TotalDmg;
            public Dictionary<string, EncEntityDmg> ByEntity = new Dictionary<string, EncEntityDmg>();
        }

        // --- State ---

        private readonly Dictionary<string, EntityDmg> _dmgDealt = new Dictionary<string, EntityDmg>();
        private readonly Dictionary<string, EntityHeal> _healDone = new Dictionary<string, EntityHeal>();
        private readonly Dictionary<int, EncStats> _encStats = new Dictionary<int, EncStats>();
        private readonly Dictionary<string, EntitySnapshot> _entities = new Dictionary<string, EntitySnapshot>();

        private long _firstTimestamp = long.MaxValue;
        private long _lastTimestamp;

        // UI state
        public bool Visible;
        public Rect WindowRect;
        private int _tabIndex;
        private readonly string[] _tabNames = { "Overview", "Damage", "Healing", "Encounters" };
        private Vector2 _scrollPos;
        private readonly HashSet<string> _expanded = new HashSet<string>();

        // Cached display strings (updated every 0.5s)
        private float _lastCacheTime;
        private string _cachedSessionTime = "0:00";
        private string _cachedEncTime = "0:00";
        private string _cachedTotalDmg = "0";
        private string _cachedTotalHeal = "0";
        private string _cachedGroupDps = "0.0";
        private string _cachedTotalHits = "0";

        // Colors matching the HTML report
        private static readonly Color ColPhys = new Color(0.973f, 0.443f, 0.443f);    // #f87171
        private static readonly Color ColMagic = new Color(0.376f, 0.647f, 0.980f);   // #60a5fa
        private static readonly Color ColElem = new Color(0.984f, 0.573f, 0.235f);    // #fb923c
        private static readonly Color ColVoid = new Color(0.753f, 0.522f, 0.988f);    // #c084fc
        private static readonly Color ColPoison = new Color(0.290f, 0.871f, 0.502f);  // #4ade80
        private static readonly Color ColHeal = new Color(0.290f, 0.871f, 0.502f);    // #4ade80
        private static readonly Color ColText = new Color(0.820f, 0.835f, 0.859f);    // #d1d5db
        private static readonly Color ColDim = new Color(0.522f, 0.541f, 0.576f);     // #858a93
        private static readonly Color ColAccent = new Color(0.486f, 0.612f, 1f);      // #7c9cff

        // Window ID (unique per mod)
        private readonly int _windowId;

        // GUIStyle cache
        private GUIStyle _headerStyle;
        private GUIStyle _rowStyle;
        private GUIStyle _tabActiveStyle;
        private GUIStyle _tabInactiveStyle;
        private bool _stylesInitialized;

        // Resize state
        private bool _isResizing;
        private Vector2 _resizeDragStart;
        private Vector2 _resizeOrigSize;
        private const float ResizeHandleSize = 16f;
        private const float MinWidth = 300f;
        private const float MinHeight = 200f;

        // Config callback for saving window position
        public Action<Rect> OnWindowMoved;

        public CombatWindow()
        {
            _windowId = "com.erenshor.perfectparse.combatwindow".GetHashCode();
        }

        // --- Event handlers (called from game thread) ---

        public void OnCombatEvent(CombatEvent evt)
        {
            string src = evt.SourceId ?? "??";
            if (!_dmgDealt.TryGetValue(src, out var d))
            {
                d = new EntityDmg();
                _dmgDealt[src] = d;
            }

            if (evt.Timestamp < _firstTimestamp) _firstTimestamp = evt.Timestamp;
            if (evt.Timestamp > _lastTimestamp) _lastTimestamp = evt.Timestamp;

            if (evt.Type == "Damage" || evt.Type == "Finale" || evt.Type == "Reflect")
            {
                int amt = evt.FinalAmount;
                string source = evt.Source ?? "Unknown";
                bool isSelfDmg = source.StartsWith("SelfDamage:") || source == "Environmental";
                string dt = evt.DamageType ?? "Physical";

                if (!isSelfDmg)
                {
                    d.Total += amt;
                    d.Hits++;
                    if (evt.Critical) d.Crits++;
                }

                if (d.ByType.ContainsKey(dt))
                    d.ByType[dt] += amt;
                else
                    d.ByType[dt] = amt;

                if (!d.BySource.TryGetValue(source, out var sd))
                {
                    sd = new SourceDetail { DmgType = dt, SelfDmg = isSelfDmg };
                    d.BySource[source] = sd;
                }
                sd.Total += amt;
                sd.Hits++;
                if (evt.Critical) sd.Crits++;

                // Encounter stats
                if (evt.EncounterId > 0 && !isSelfDmg)
                {
                    if (!_encStats.TryGetValue(evt.EncounterId, out var es))
                    {
                        es = new EncStats();
                        _encStats[evt.EncounterId] = es;
                    }
                    es.TotalDmg += amt;

                    if (!es.ByEntity.TryGetValue(src, out var eed))
                    {
                        eed = new EncEntityDmg();
                        es.ByEntity[src] = eed;
                    }
                    eed.Dmg += amt;
                    eed.Hits++;
                    if (evt.Critical) eed.Crits++;

                    if (eed.ByType.ContainsKey(dt))
                        eed.ByType[dt] += amt;
                    else
                        eed.ByType[dt] = amt;

                    if (!eed.BySource.TryGetValue(source, out var esd))
                    {
                        esd = new SourceDetail { DmgType = dt };
                        eed.BySource[source] = esd;
                    }
                    esd.Total += amt;
                    esd.Hits++;
                    if (evt.Critical) esd.Crits++;
                }
            }
            else if (evt.Type == "Miss")
            {
                d.Misses++;
            }
            else if (evt.Type == "Resist")
            {
                d.Resists++;
            }
        }

        public void OnHealEvent(HealEvent evt)
        {
            string src = evt.SourceId ?? "??";
            if (!_healDone.TryGetValue(src, out var h))
            {
                h = new EntityHeal();
                _healDone[src] = h;
            }

            if (evt.Timestamp < _firstTimestamp) _firstTimestamp = evt.Timestamp;
            if (evt.Timestamp > _lastTimestamp) _lastTimestamp = evt.Timestamp;

            int amt = evt.ActualAmount;
            int raw = evt.RawAmount;
            h.Total += amt;
            h.Overhealing += (raw - amt);
            h.Casts++;

            string spell = evt.SpellName ?? "Unknown";
            if (!h.BySpell.TryGetValue(spell, out var sp))
            {
                sp = new SpellDetail();
                h.BySpell[spell] = sp;
            }
            sp.Total += amt;
            sp.Casts++;
            sp.Overhealing += (raw - amt);
        }

        public void OnEntityEvent(EntitySnapshot snapshot)
        {
            _entities[snapshot.Id] = snapshot;
        }

        public void Reset()
        {
            _dmgDealt.Clear();
            _healDone.Clear();
            _encStats.Clear();
            _entities.Clear();
            _expanded.Clear();
            _firstTimestamp = long.MaxValue;
            _lastTimestamp = 0;
            _lastCacheTime = 0;
        }

        // --- Helpers ---

        private static bool IsPlayerSide(string id)
        {
            return id == "Player" || id.StartsWith("Sim:") || id.StartsWith("Pet:");
        }

        private string EntityName(string id)
        {
            if (_entities.TryGetValue(id, out var e))
                return e.DisplayName;
            // Fallback: parse from ID
            if (id.StartsWith("NPC:") || id.StartsWith("Pet:") || id.StartsWith("Sim:"))
            {
                int lastColon = id.LastIndexOf(':');
                if (lastColon > 0) return id.Substring(lastColon + 1);
            }
            return id;
        }

        private string EntityClass(string id)
        {
            if (_entities.TryGetValue(id, out var e) && e.ClassName != null)
                return e.ClassName;
            return "";
        }

        private static Color DmgTypeColor(string dmgType)
        {
            switch (dmgType)
            {
                case "Physical": return ColPhys;
                case "Magic": return ColMagic;
                case "Elemental": return ColElem;
                case "Void": return ColVoid;
                case "Poison": return ColPoison;
                default: return ColText;
            }
        }

        private static string FmtNum(long n)
        {
            if (n >= 1000000) return (n / 1000000.0).ToString("F1") + "M";
            if (n >= 1000) return (n / 1000.0).ToString("F1") + "k";
            return n.ToString("N0");
        }

        private static string FmtDps(double dps)
        {
            if (dps >= 1000) return (dps / 1000.0).ToString("F1") + "k";
            return dps.ToString("F1");
        }

        private static string FmtTime(long ms)
        {
            if (ms <= 0) return "0:00";
            int totalSec = (int)(ms / 1000);
            int min = totalSec / 60;
            int sec = totalSec % 60;
            return min + ":" + sec.ToString("D2");
        }

        private long GetEncounterTimeMs()
        {
            long total = 0;
            foreach (var enc in EncounterTracker.AllEncounters)
            {
                long end = enc.EndMs > 0 ? enc.EndMs : _lastTimestamp;
                if (end > enc.StartMs) total += (end - enc.StartMs);
            }
            return total > 0 ? total : 1;
        }

        private long GetSessionMs()
        {
            if (_lastTimestamp <= _firstTimestamp) return 1;
            return _lastTimestamp - _firstTimestamp;
        }

        private void UpdateCache()
        {
            if (Time.time - _lastCacheTime < 0.5f) return;
            _lastCacheTime = Time.time;

            long sessionMs = GetSessionMs();
            long encMs = GetEncounterTimeMs();

            _cachedSessionTime = FmtTime(sessionMs);
            _cachedEncTime = FmtTime(encMs);

            long totalDmg = 0;
            long totalHeal = 0;
            int totalHits = 0;
            foreach (var kvp in _dmgDealt)
            {
                if (IsPlayerSide(kvp.Key))
                {
                    totalDmg += kvp.Value.Total;
                    totalHits += kvp.Value.Hits;
                }
            }
            foreach (var kvp in _healDone)
            {
                if (IsPlayerSide(kvp.Key))
                    totalHeal += kvp.Value.Total;
            }

            _cachedTotalDmg = FmtNum(totalDmg);
            _cachedTotalHeal = FmtNum(totalHeal);
            _cachedGroupDps = FmtDps(totalDmg / (sessionMs / 1000.0));
            _cachedTotalHits = FmtNum(totalHits);
        }

        // --- IMGUI Rendering ---

        private void InitStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };

            _rowStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                richText = true
            };

            _tabActiveStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 11
            };

            _tabInactiveStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11
            };
        }

        public void Draw()
        {
            if (!Visible) return;
            InitStyles();
            UpdateCache();

            var savedSkin = GUI.skin;
            var savedColor = GUI.color;

            // Handle resize dragging (must be processed before GUI.Window eats the events)
            HandleResize();

            WindowRect = GUI.Window(_windowId, WindowRect, DrawWindowContents, "PerfectParse \u2014 Live");

            // Clamp to screen
            WindowRect.x = Mathf.Clamp(WindowRect.x, 0, Screen.width - 100);
            WindowRect.y = Mathf.Clamp(WindowRect.y, 0, Screen.height - 50);

            GUI.skin = savedSkin;
            GUI.color = savedColor;
        }

        private void HandleResize()
        {
            // Resize grip area in screen coordinates (bottom-right corner of window)
            var gripRect = new Rect(
                WindowRect.xMax - ResizeHandleSize,
                WindowRect.yMax - ResizeHandleSize,
                ResizeHandleSize,
                ResizeHandleSize);

            var e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 0 && gripRect.Contains(e.mousePosition))
            {
                _isResizing = true;
                _resizeDragStart = e.mousePosition;
                _resizeOrigSize = new Vector2(WindowRect.width, WindowRect.height);
                e.Use();
            }
            else if (_isResizing)
            {
                if (e.type == EventType.MouseDrag || e.type == EventType.MouseMove)
                {
                    Vector2 delta = e.mousePosition - _resizeDragStart;
                    WindowRect.width = Mathf.Max(MinWidth, _resizeOrigSize.x + delta.x);
                    WindowRect.height = Mathf.Max(MinHeight, _resizeOrigSize.y + delta.y);
                    e.Use();
                }
                if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)
                {
                    _isResizing = false;
                    e.Use();
                }
            }
        }

        private void DrawWindowContents(int id)
        {
            // Tab bar
            GUILayout.BeginHorizontal();
            for (int i = 0; i < _tabNames.Length; i++)
            {
                var style = i == _tabIndex ? _tabActiveStyle : _tabInactiveStyle;
                if (GUILayout.Button(_tabNames[i], style, GUILayout.Height(24)))
                    _tabIndex = i;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reset", GUILayout.Width(50), GUILayout.Height(24)))
                Reset();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Content area
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            switch (_tabIndex)
            {
                case 0: DrawOverview(); break;
                case 1: DrawDamage(); break;
                case 2: DrawHealing(); break;
                case 3: DrawEncounters(); break;
            }

            GUILayout.EndScrollView();

            // Draw resize grip in bottom-right corner
            var savedColor2 = GUI.color;
            GUI.color = ColDim;
            var gripLocal = new Rect(WindowRect.width - ResizeHandleSize - 2, WindowRect.height - ResizeHandleSize - 2, ResizeHandleSize, ResizeHandleSize);
            GUI.Label(gripLocal, "\u2921"); // ⤡ diagonal arrow
            GUI.color = savedColor2;

            // Make window draggable (title bar only — resize area handled separately)
            GUI.DragWindow(new Rect(0, 0, WindowRect.width, 20));
        }

        // --- Overview Tab ---

        private void DrawOverview()
        {
            // Summary cards row
            GUILayout.BeginHorizontal();
            DrawCard("Total Damage", _cachedTotalDmg);
            DrawCard("Group DPS", _cachedGroupDps);
            DrawCard("Total Healing", _cachedTotalHeal);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawCard("Session", _cachedSessionTime);
            DrawCard("Enc Time", _cachedEncTime);
            DrawCard("Hits", _cachedTotalHits);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Per-character DPS table
            var savedColor = GUI.color;
            GUI.color = ColAccent;
            GUILayout.Label("Player Damage Breakdown", _headerStyle);
            GUI.color = savedColor;

            // Header row
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _headerStyle, GUILayout.Width(120));
            GUILayout.Label("Class", _headerStyle, GUILayout.Width(80));
            GUILayout.Label("Damage", _headerStyle, GUILayout.Width(70));
            GUILayout.Label("DPS", _headerStyle, GUILayout.Width(60));
            GUILayout.Label("Hits", _headerStyle, GUILayout.Width(50));
            GUILayout.Label("Crits", _headerStyle, GUILayout.Width(50));
            GUILayout.Label("Top Source", _headerStyle, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            var sorted = _dmgDealt
                .Where(kvp => IsPlayerSide(kvp.Key))
                .OrderByDescending(kvp => kvp.Value.Total)
                .ToList();

            long sessionMs = GetSessionMs();

            foreach (var kvp in sorted)
            {
                var d = kvp.Value;
                double dps = d.Total / (sessionMs / 1000.0);
                string topSrc = d.BySource.Count > 0
                    ? d.BySource.OrderByDescending(s => s.Value.Total).First().Key
                    : "-";

                string groupId = "ov-" + kvp.Key;
                bool isExpanded = _expanded.Contains(groupId);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(isExpanded ? "\u25BC" : "\u25B6", GUILayout.Width(18), GUILayout.Height(18)))
                    ToggleExpanded(groupId);
                GUILayout.Label(EntityName(kvp.Key), _rowStyle, GUILayout.Width(100));
                GUILayout.Label(EntityClass(kvp.Key), _rowStyle, GUILayout.Width(80));
                GUILayout.Label(FmtNum(d.Total), _rowStyle, GUILayout.Width(70));
                GUILayout.Label(FmtDps(dps), _rowStyle, GUILayout.Width(60));
                GUILayout.Label(d.Hits.ToString(), _rowStyle, GUILayout.Width(50));
                GUILayout.Label(d.Crits.ToString(), _rowStyle, GUILayout.Width(50));
                GUILayout.Label(topSrc, _rowStyle, GUILayout.Width(100));
                GUILayout.EndHorizontal();

                if (isExpanded)
                    DrawSourceBreakdown(d, "ov-src-" + kvp.Key);
            }
        }

        // --- Damage Tab ---

        private void DrawDamage()
        {
            var savedColor = GUI.color;

            // Header
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _headerStyle, GUILayout.Width(120));
            GUILayout.Label("Class", _headerStyle, GUILayout.Width(80));
            GUILayout.Label("Damage", _headerStyle, GUILayout.Width(70));
            GUI.color = ColPhys;
            GUILayout.Label("Phys", _headerStyle, GUILayout.Width(55));
            GUI.color = ColMagic;
            GUILayout.Label("Magic", _headerStyle, GUILayout.Width(55));
            GUI.color = ColElem;
            GUILayout.Label("Elem", _headerStyle, GUILayout.Width(55));
            GUI.color = savedColor;
            GUILayout.Label("Miss", _headerStyle, GUILayout.Width(45));
            GUILayout.Label("Resist", _headerStyle, GUILayout.Width(45));
            GUILayout.EndHorizontal();

            var sorted = _dmgDealt
                .Where(kvp => IsPlayerSide(kvp.Key))
                .OrderByDescending(kvp => kvp.Value.Total)
                .ToList();

            foreach (var kvp in sorted)
            {
                var d = kvp.Value;
                string groupId = "dmg-" + kvp.Key;
                bool isExpanded = _expanded.Contains(groupId);

                long phys = 0, magic = 0, elem = 0;
                d.ByType.TryGetValue("Physical", out phys);
                d.ByType.TryGetValue("Magic", out magic);
                d.ByType.TryGetValue("Elemental", out elem);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(isExpanded ? "\u25BC" : "\u25B6", GUILayout.Width(18), GUILayout.Height(18)))
                    ToggleExpanded(groupId);
                GUILayout.Label(EntityName(kvp.Key), _rowStyle, GUILayout.Width(100));
                GUILayout.Label(EntityClass(kvp.Key), _rowStyle, GUILayout.Width(80));
                GUILayout.Label(FmtNum(d.Total), _rowStyle, GUILayout.Width(70));
                GUI.color = ColPhys;
                GUILayout.Label(FmtNum(phys), _rowStyle, GUILayout.Width(55));
                GUI.color = ColMagic;
                GUILayout.Label(FmtNum(magic), _rowStyle, GUILayout.Width(55));
                GUI.color = ColElem;
                GUILayout.Label(FmtNum(elem), _rowStyle, GUILayout.Width(55));
                GUI.color = savedColor;
                GUILayout.Label(d.Misses.ToString(), _rowStyle, GUILayout.Width(45));
                GUILayout.Label(d.Resists.ToString(), _rowStyle, GUILayout.Width(45));
                GUILayout.EndHorizontal();

                if (isExpanded)
                    DrawSourceBreakdown(d, "dmg-src-" + kvp.Key);
            }
        }

        // --- Shared: Source breakdown (type -> source) ---

        private void DrawSourceBreakdown(EntityDmg d, string prefix)
        {
            var savedColor = GUI.color;
            var sortedTypes = d.ByType.OrderByDescending(t => t.Value).ToList();

            foreach (var typeKvp in sortedTypes)
            {
                string dtName = typeKvp.Key;
                long dtTotal = typeKvp.Value;
                string pct = d.Total > 0 ? (dtTotal * 100.0 / d.Total).ToString("F1") : "0.0";

                GUI.color = DmgTypeColor(dtName);
                GUILayout.BeginHorizontal();
                GUILayout.Space(28);
                GUILayout.Label(dtName, _headerStyle, GUILayout.Width(90));
                GUILayout.Label(FmtNum(dtTotal) + " (" + pct + "%)", _rowStyle, GUILayout.Width(120));
                GUILayout.EndHorizontal();
                GUI.color = savedColor;

                // Sources under this type
                var sources = d.BySource
                    .Where(s => s.Value.DmgType == dtName)
                    .OrderByDescending(s => s.Value.Total)
                    .ToList();

                foreach (var srcKvp in sources)
                {
                    var sd = srcKvp.Value;
                    string sPct = sd.SelfDmg ? "n/a" : (dtTotal > 0 ? (sd.Total * 100.0 / dtTotal).ToString("F1") + "%" : "0.0%");

                    GUILayout.BeginHorizontal();
                    GUILayout.Space(48);
                    GUILayout.Label(srcKvp.Key, _rowStyle, GUILayout.Width(120));
                    GUILayout.Label(FmtNum(sd.Total) + " (" + sPct + ")", _rowStyle, GUILayout.Width(100));
                    GUILayout.Label(sd.Hits + " hits", _rowStyle, GUILayout.Width(60));
                    GUILayout.Label(sd.Crits + " crits", _rowStyle, GUILayout.Width(60));
                    GUILayout.EndHorizontal();
                }
            }
        }

        // --- Healing Tab ---

        private void DrawHealing()
        {
            var savedColor = GUI.color;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _headerStyle, GUILayout.Width(120));
            GUILayout.Label("Class", _headerStyle, GUILayout.Width(80));
            GUI.color = ColHeal;
            GUILayout.Label("Healing", _headerStyle, GUILayout.Width(70));
            GUI.color = savedColor;
            GUILayout.Label("Casts", _headerStyle, GUILayout.Width(50));
            GUILayout.Label("HPS", _headerStyle, GUILayout.Width(60));
            GUILayout.Label("Overheal", _headerStyle, GUILayout.Width(70));
            GUILayout.Label("Top Spell", _headerStyle, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            var sorted = _healDone
                .Where(kvp => IsPlayerSide(kvp.Key))
                .OrderByDescending(kvp => kvp.Value.Total)
                .ToList();

            long sessionMs = GetSessionMs();

            foreach (var kvp in sorted)
            {
                var h = kvp.Value;
                double hps = h.Total / (sessionMs / 1000.0);
                string topSpell = h.BySpell.Count > 0
                    ? h.BySpell.OrderByDescending(s => s.Value.Total).First().Key
                    : "-";

                string groupId = "heal-" + kvp.Key;
                bool isExpanded = _expanded.Contains(groupId);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(isExpanded ? "\u25BC" : "\u25B6", GUILayout.Width(18), GUILayout.Height(18)))
                    ToggleExpanded(groupId);
                GUILayout.Label(EntityName(kvp.Key), _rowStyle, GUILayout.Width(100));
                GUILayout.Label(EntityClass(kvp.Key), _rowStyle, GUILayout.Width(80));
                GUI.color = ColHeal;
                GUILayout.Label(FmtNum(h.Total), _rowStyle, GUILayout.Width(70));
                GUI.color = savedColor;
                GUILayout.Label(h.Casts.ToString(), _rowStyle, GUILayout.Width(50));
                GUILayout.Label(FmtDps(hps), _rowStyle, GUILayout.Width(60));
                GUILayout.Label(FmtNum(h.Overhealing), _rowStyle, GUILayout.Width(70));
                GUILayout.Label(topSpell, _rowStyle, GUILayout.Width(100));
                GUILayout.EndHorizontal();

                if (isExpanded)
                {
                    var sortedSpells = h.BySpell.OrderByDescending(s => s.Value.Total).ToList();
                    foreach (var spKvp in sortedSpells)
                    {
                        var sp = spKvp.Value;
                        string pct = h.Total > 0 ? (sp.Total * 100.0 / h.Total).ToString("F1") + "%" : "0.0%";
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(28);
                        GUI.color = ColHeal;
                        GUILayout.Label(spKvp.Key, _rowStyle, GUILayout.Width(120));
                        GUI.color = savedColor;
                        GUILayout.Label(FmtNum(sp.Total) + " (" + pct + ")", _rowStyle, GUILayout.Width(100));
                        GUILayout.Label(sp.Casts + " casts", _rowStyle, GUILayout.Width(60));
                        GUILayout.Label(FmtNum(sp.Overhealing) + " oh", _rowStyle, GUILayout.Width(70));
                        GUILayout.EndHorizontal();
                    }
                }
            }
        }

        // --- Encounters Tab ---

        private void DrawEncounters()
        {
            var savedColor = GUI.color;

            GUILayout.BeginHorizontal();
            GUILayout.Label("#", _headerStyle, GUILayout.Width(30));
            GUILayout.Label("Label", _headerStyle, GUILayout.Width(100));
            GUILayout.Label("Duration", _headerStyle, GUILayout.Width(65));
            GUILayout.Label("Damage", _headerStyle, GUILayout.Width(70));
            GUILayout.Label("DPS", _headerStyle, GUILayout.Width(60));
            GUILayout.Label("Manual", _headerStyle, GUILayout.Width(50));
            GUILayout.EndHorizontal();

            foreach (var enc in EncounterTracker.AllEncounters)
            {
                long endMs = enc.EndMs > 0 ? enc.EndMs : _lastTimestamp;
                long dur = endMs > enc.StartMs ? endMs - enc.StartMs : 0;
                _encStats.TryGetValue(enc.Id, out var es);
                long dmg = es != null ? es.TotalDmg : 0;
                double dps = dur > 0 ? dmg / (dur / 1000.0) : 0;

                string groupId = "enc-" + enc.Id;
                bool isExpanded = _expanded.Contains(groupId);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(isExpanded ? "\u25BC" : "\u25B6", GUILayout.Width(18), GUILayout.Height(18)))
                    ToggleExpanded(groupId);
                GUILayout.Label(enc.Id.ToString(), _rowStyle, GUILayout.Width(25));
                GUILayout.Label(enc.Label ?? "", _rowStyle, GUILayout.Width(100));
                GUILayout.Label(FmtTime(dur), _rowStyle, GUILayout.Width(65));
                GUILayout.Label(FmtNum(dmg), _rowStyle, GUILayout.Width(70));
                GUILayout.Label(FmtDps(dps), _rowStyle, GUILayout.Width(60));
                GUILayout.Label(enc.Manual ? "Yes" : "", _rowStyle, GUILayout.Width(50));
                GUILayout.EndHorizontal();

                if (isExpanded && es != null)
                {
                    var allChars = es.ByEntity.OrderByDescending(e => e.Value.Dmg).ToList();
                    var playerSide = allChars.Where(e => IsPlayerSide(e.Key)).ToList();
                    var npcSide = allChars.Where(e => !IsPlayerSide(e.Key)).ToList();

                    foreach (var charKvp in playerSide)
                        DrawEncCharRow(charKvp, dmg, dur, groupId);

                    if (npcSide.Count > 0)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(28);
                        GUI.color = ColAccent;
                        GUILayout.Label("--- Enemies ---", _headerStyle);
                        GUI.color = savedColor;
                        GUILayout.EndHorizontal();

                        foreach (var charKvp in npcSide)
                            DrawEncCharRow(charKvp, dmg, dur, groupId);
                    }
                }
            }
        }

        private void DrawEncCharRow(KeyValuePair<string, EncEntityDmg> charKvp, long totalEncDmg, long dur, string parentGroup)
        {
            var savedColor = GUI.color;
            var ed = charKvp.Value;
            double charDps = dur > 0 ? ed.Dmg / (dur / 1000.0) : 0;
            string pct = totalEncDmg > 0 ? (ed.Dmg * 100.0 / totalEncDmg).ToString("F1") : "0.0";

            string charGroup = parentGroup + "-" + charKvp.Key;
            bool isCharExpanded = _expanded.Contains(charGroup);

            bool isNpc = !IsPlayerSide(charKvp.Key);

            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            if (GUILayout.Button(isCharExpanded ? "\u25BC" : "\u25B6", GUILayout.Width(18), GUILayout.Height(18)))
                ToggleExpanded(charGroup);
            if (isNpc) GUI.color = ColDim;
            GUILayout.Label(EntityName(charKvp.Key), _rowStyle, GUILayout.Width(100));
            GUI.color = savedColor;
            GUILayout.Label(FmtNum(ed.Dmg) + " (" + pct + "%)", _rowStyle, GUILayout.Width(100));
            GUILayout.Label(FmtDps(charDps) + " dps", _rowStyle, GUILayout.Width(70));
            GUILayout.Label(ed.Hits + " hits / " + ed.Crits + " crits", _rowStyle, GUILayout.Width(110));
            GUILayout.EndHorizontal();

            if (isCharExpanded)
            {
                // Type -> source breakdown
                var sortedTypes = ed.ByType.OrderByDescending(t => t.Value).ToList();
                foreach (var typeKvp in sortedTypes)
                {
                    string dtName = typeKvp.Key;
                    long dtTotal = typeKvp.Value;
                    string dtPct = ed.Dmg > 0 ? (dtTotal * 100.0 / ed.Dmg).ToString("F1") : "0.0";

                    GUI.color = DmgTypeColor(dtName);
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(48);
                    GUILayout.Label(dtName, _headerStyle, GUILayout.Width(90));
                    GUILayout.Label(FmtNum(dtTotal) + " (" + dtPct + "%)", _rowStyle, GUILayout.Width(120));
                    GUILayout.EndHorizontal();
                    GUI.color = savedColor;

                    var sources = ed.BySource
                        .Where(s => s.Value.DmgType == dtName)
                        .OrderByDescending(s => s.Value.Total)
                        .ToList();

                    foreach (var srcKvp in sources)
                    {
                        var sd = srcKvp.Value;
                        string sPct = dtTotal > 0 ? (sd.Total * 100.0 / dtTotal).ToString("F1") + "%" : "0.0%";

                        GUILayout.BeginHorizontal();
                        GUILayout.Space(68);
                        GUILayout.Label(srcKvp.Key, _rowStyle, GUILayout.Width(120));
                        GUILayout.Label(FmtNum(sd.Total) + " (" + sPct + ")", _rowStyle, GUILayout.Width(100));
                        GUILayout.Label(sd.Hits + " hits", _rowStyle, GUILayout.Width(60));
                        GUILayout.Label(sd.Crits + " crits", _rowStyle, GUILayout.Width(60));
                        GUILayout.EndHorizontal();
                    }
                }
            }
        }

        // --- Helpers ---

        private void DrawCard(string label, string value)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(80));
            var savedColor = GUI.color;
            GUI.color = ColDim;
            GUILayout.Label(label, _rowStyle);
            GUI.color = ColAccent;
            GUILayout.Label(value, _headerStyle);
            GUI.color = savedColor;
            GUILayout.EndVertical();
        }

        private void ToggleExpanded(string groupId)
        {
            if (!_expanded.Remove(groupId))
                _expanded.Add(groupId);
        }
    }
}
