using System.Text;

namespace ErenshorCombatParser.IO
{
    public static class HtmlTemplate
    {
        public static string Build(string eventsJson, string entityJson, string encounterJson)
        {
            var sb = new StringBuilder(4096);
            sb.Append(@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<title>PerfectParse — Combat Report</title>
<style>
:root {
  --bg: #141517;
  --surface: #1e2023;
  --surface2: #27292d;
  --border: #33363b;
  --accent: #7c9cff;
  --text: #d1d5db;
  --text-dim: #858a93;
  --phys: #f87171;
  --magic: #60a5fa;
  --elem: #fb923c;
  --void: #c084fc;
  --poison: #4ade80;
  --heal: #4ade80;
}
* { box-sizing: border-box; margin: 0; padding: 0; }
body {
  font-family: 'Segoe UI', Consolas, monospace;
  background: var(--bg);
  color: var(--text);
  min-height: 100vh;
}
.header {
  background: var(--surface);
  padding: 16px 24px;
  border-bottom: 1px solid var(--border);
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.header h1 { font-size: 1.4em; color: var(--accent); }
.header .meta { color: var(--text-dim); font-size: 0.85em; }
.tabs {
  display: flex;
  background: var(--surface);
  padding: 0 24px;
  gap: 4px;
}
.tab {
  padding: 10px 20px;
  cursor: pointer;
  color: var(--text-dim);
  border-bottom: 2px solid transparent;
  transition: all 0.2s;
}
.tab:hover { color: var(--text); }
.tab.active { color: var(--accent); border-bottom-color: var(--accent); }
.content { padding: 24px; }
.panel { display: none; }
.panel.active { display: block; }
.mode-toggle {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
}
.mode-btn {
  padding: 6px 16px;
  background: var(--surface);
  border: 1px solid var(--border);
  color: var(--text-dim);
  cursor: pointer;
  border-radius: 4px;
}
.mode-btn.active {
  background: var(--accent);
  color: #141517;
  border-color: var(--accent);
}
table {
  width: 100%;
  border-collapse: collapse;
  margin-top: 12px;
}
th, td {
  padding: 8px 12px;
  text-align: left;
  border-bottom: 1px solid var(--border);
}
th {
  background: var(--surface);
  cursor: pointer;
  user-select: none;
  white-space: nowrap;
}
th:hover { color: var(--accent); }
tr:hover { background: var(--surface); }
.num { text-align: right; font-variant-numeric: tabular-nums; }
.bar {
  height: 4px;
  background: var(--accent);
  border-radius: 2px;
  margin-top: 2px;
}
.expandable { cursor: pointer; }
.expandable td:first-child::before { content: '▸ '; color: var(--accent); }
.expandable.open td:first-child::before { content: '▾ '; }
.detail-row { display: none; }
.detail-row.open { display: table-row; }
.detail-row td { padding-left: 36px; color: var(--text-dim); }
.dmg-phys { color: var(--phys); }
.dmg-magic { color: var(--magic); }
.dmg-elem { color: var(--elem); }
.dmg-void { color: var(--void); }
.dmg-poison { color: var(--poison); }
.dmg-heal { color: var(--heal); }
.replay-controls {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
  padding: 12px;
  background: var(--surface);
  border-radius: 6px;
}
.replay-controls button {
  padding: 6px 14px;
  background: var(--accent);
  color: #141517;
  border: none;
  border-radius: 4px;
  cursor: pointer;
}
.replay-controls input[type=range] { flex: 1; }
.replay-controls select {
  background: var(--surface2);
  color: var(--text);
  border: 1px solid var(--border);
  padding: 4px 8px;
  border-radius: 4px;
}
.stat-cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 12px;
  margin-bottom: 20px;
}
.stat-card {
  background: var(--surface);
  padding: 16px;
  border-radius: 6px;
  border-left: 3px solid var(--accent);
}
.stat-card .label { color: var(--text-dim); font-size: 0.8em; margin-bottom: 4px; }
.stat-card .value { font-size: 1.6em; font-weight: bold; }
</style>
</head>
<body>

<div class=""header"">
  <h1>PerfectParse</h1>
  <div class=""meta"" id=""reportMeta""></div>
</div>

<div class=""tabs"">
  <div class=""tab active"" data-tab=""overview"">Overview</div>
  <div class=""tab"" data-tab=""damage"">Damage</div>
  <div class=""tab"" data-tab=""healing"">Healing</div>
  <div class=""tab"" data-tab=""encounters"">Encounters</div>
  <div class=""tab"" data-tab=""npcs"">NPCs / Enemies</div>
</div>

<div class=""content"">

<!-- Overview -->
<div class=""panel active"" id=""panel-overview"">
  <div class=""mode-toggle"">
    <button class=""mode-btn active"" data-mode=""final"">Final Results</button>
    <button class=""mode-btn"" data-mode=""replay"">Replay</button>
  </div>
  <div id=""replay-ui"" style=""display:none"">
    <div class=""replay-controls"">
      <button id=""replayPlayBtn"">▶ Play</button>
      <input type=""range"" id=""replaySlider"" min=""0"" max=""100"" value=""0"">
      <span id=""replayTime"">0:00</span>
      <select id=""replaySpeed"">
        <option value=""1"">1x</option>
        <option value=""2"">2x</option>
        <option value=""5"" selected>5x</option>
        <option value=""10"">10x</option>
        <option value=""20"">20x</option>
      </select>
    </div>
  </div>
  <div class=""stat-cards"" id=""overviewCards""></div>
  <h3>Damage Dealt — All Characters</h3>
  <table id=""overviewDmgTable"">
    <thead><tr>
      <th>Character</th><th>Class</th><th>Lvl</th>
      <th class=""num"">Total Dmg</th><th class=""num"">DPS (Session)</th>
      <th class=""num"">DPS (Encounter)</th><th class=""num"">Hits</th>
      <th class=""num"">Crits</th><th>Top Source</th>
    </tr></thead>
    <tbody id=""overviewDmgBody""></tbody>
  </table>
</div>

<!-- Damage -->
<div class=""panel"" id=""panel-damage"">
  <h3>Damage Breakdown</h3>
  <table id=""dmgTable"">
    <thead><tr>
      <th>Character</th><th>Class</th>
      <th class=""num"">Total Dealt</th><th class=""num"">Total Taken</th>
      <th class=""num"">Phys Dealt</th><th class=""num"">Magic Dealt</th>
      <th class=""num"">Elem Dealt</th><th class=""num"">Misses</th>
      <th class=""num"">Resists</th>
    </tr></thead>
    <tbody id=""dmgBody""></tbody>
  </table>
</div>

<!-- Healing -->
<div class=""panel"" id=""panel-healing"">
  <h3>Healing Breakdown</h3>
  <table id=""healTable"">
    <thead><tr>
      <th>Character</th><th>Class</th>
      <th class=""num"">Total Healed</th><th class=""num"">Heals Cast</th>
      <th class=""num"">HPS (Session)</th><th class=""num"">HPS (Encounter)</th>
      <th class=""num"">Overhealing</th><th>Top Spell</th>
    </tr></thead>
    <tbody id=""healBody""></tbody>
  </table>
</div>

<!-- Encounters -->
<div class=""panel"" id=""panel-encounters"">
  <h3>Encounter Log</h3>
  <table id=""encTable"">
    <thead><tr>
      <th>#</th><th>Label</th><th>Duration</th>
      <th class=""num"">Total Damage</th><th class=""num"">Group DPS</th>
      <th>Manual</th>
    </tr></thead>
    <tbody id=""encBody""></tbody>
  </table>
</div>

<!-- NPCs -->
<div class=""panel"" id=""panel-npcs"">
  <h3>NPCs / Enemies</h3>
  <table id=""npcTable"">
    <thead><tr>
      <th>Name</th><th>Lvl</th>
      <th class=""num"">Dmg Dealt</th><th class=""num"">Dmg Taken</th>
      <th class=""num"">Heals Recv</th><th>Top Attacker</th>
    </tr></thead>
    <tbody id=""npcBody""></tbody>
  </table>
</div>

</div>

<script>
// ============================================================
// Embedded data from the JSONL log
// ============================================================
const RAW_EVENTS = ");
            sb.Append(eventsJson);
            sb.Append(@";
const ENTITIES = ");
            sb.Append(entityJson);
            sb.Append(@";
const ENCOUNTERS = ");
            sb.Append(encounterJson);
            sb.Append(@";

// ============================================================
// Utility
// ============================================================
function fmt(n) { return n != null ? n.toLocaleString() : '0'; }
function fmtDps(d) { return d != null ? d.toFixed(1) : '0.0'; }
function fmtTime(ms) {
  let s = Math.floor(ms / 1000);
  let m = Math.floor(s / 60); s %= 60;
  return m + ':' + (s < 10 ? '0' : '') + s;
}
function dmgClass(t) {
  if (!t) return '';
  const m = { Physical:'dmg-phys', Magic:'dmg-magic', Elemental:'dmg-elem', Void:'dmg-void', Poison:'dmg-poison' };
  return m[t] || '';
}
function entityName(id) {
  const e = ENTITIES[id];
  if (e) return e.name;
  if (!id) return '??';
  return id;
}
function entityClass(id) {
  const e = ENTITIES[id];
  return e ? (e.class || '') : '';
}
function entityLevel(id) {
  const e = ENTITIES[id];
  return e ? (e.level || '') : '';
}
function entityType(id) {
  const e = ENTITIES[id];
  return e ? e.type : 'NPC';
}
function isPlayerSide(id) {
  if (!id) return false;
  const t = entityType(id);
  return t === 'Player' || t === 'SimPlayer' || t === 'Pet';
}

// ============================================================
// Aggregation
// ============================================================
function aggregate(events) {
  const dmgDealt = {};   // id -> { total, byType:{}, bySource:{}, hits, crits, misses, resists }
  const dmgTaken = {};   // id -> { total, byAttacker:{} }
  const healDone = {};   // id -> { total, overhealing, casts, bySpell:{} }
  const healRecv = {};   // id -> { total }
  const encDmg = {};     // encId -> total
  const encByEntity = {}; // encId -> { entityId -> { dmg, hits, crits } }
  let firstT = Infinity, lastT = 0;
  let encTime = 0;

  for (const enc of ENCOUNTERS) {
    if (enc.end > 0) encTime += (enc.end - enc.start);
  }

  for (const ev of events) {
    if (ev.t < firstT) firstT = ev.t;
    if (ev.t > lastT) lastT = ev.t;

    if (ev.ev === 'combat') {
      const src = ev.src || '??';
      const tgt = ev.tgt || '??';
      if (!dmgDealt[src]) dmgDealt[src] = { total:0, byType:{}, bySource:{}, sourceDetail:{}, hits:0, crits:0, misses:0, resists:0 };
      if (!dmgTaken[tgt]) dmgTaken[tgt] = { total:0, byAttacker:{} };

      if (ev.type === 'Damage' || ev.type === 'Finale' || ev.type === 'Reflect') {
        const amt = ev.final || 0;
        const s = ev.source || 'Unknown';
        const isSelfDmg = s.startsWith('SelfDamage:') || s === 'Environmental';
        // Self/environmental damage: track in breakdown but exclude from DPS totals
        if (!isSelfDmg) {
          dmgDealt[src].total += amt;
          dmgDealt[src].hits++;
          if (ev.crit) dmgDealt[src].crits++;
        }
        const dt = ev.dmgType || 'Physical';
        dmgDealt[src].byType[dt] = (dmgDealt[src].byType[dt] || 0) + amt;
        dmgDealt[src].bySource[s] = (dmgDealt[src].bySource[s] || 0) + amt;
        // Per-source detail: hits, crits, dmgType
        if (!dmgDealt[src].sourceDetail[s]) dmgDealt[src].sourceDetail[s] = { total:0, hits:0, crits:0, dmgType:dt, selfDmg:isSelfDmg };
        dmgDealt[src].sourceDetail[s].total += amt;
        dmgDealt[src].sourceDetail[s].hits++;
        if (ev.crit) dmgDealt[src].sourceDetail[s].crits++;
        dmgTaken[tgt].total += amt;
        // Per-attacker breakdown for NPC tab
        if (!dmgTaken[tgt].byAttacker[src]) dmgTaken[tgt].byAttacker[src] = 0;
        dmgTaken[tgt].byAttacker[src] += amt;
        if (ev.enc > 0 && !isSelfDmg) {
          encDmg[ev.enc] = (encDmg[ev.enc] || 0) + amt;
          if (!encByEntity[ev.enc]) encByEntity[ev.enc] = {};
          if (!encByEntity[ev.enc][src]) encByEntity[ev.enc][src] = { dmg:0, hits:0, crits:0 };
          encByEntity[ev.enc][src].dmg += amt;
          encByEntity[ev.enc][src].hits++;
          if (ev.crit) encByEntity[ev.enc][src].crits++;
        }
      } else if (ev.type === 'Miss') {
        dmgDealt[src].misses++;
      } else if (ev.type === 'Resist') {
        dmgDealt[src].resists++;
      }
    } else if (ev.ev === 'heal') {
      const src = ev.src || '??';
      const tgt = ev.tgt || '??';
      if (!healDone[src]) healDone[src] = { total:0, overhealing:0, casts:0, bySpell:{}, spellDetail:{} };
      if (!healRecv[tgt]) healRecv[tgt] = { total:0 };
      const amt = ev.actual || 0;
      const raw = ev.raw || 0;
      healDone[src].total += amt;
      healDone[src].overhealing += (raw - amt);
      healDone[src].casts++;
      const sp = ev.spell || 'Unknown';
      healDone[src].bySpell[sp] = (healDone[src].bySpell[sp] || 0) + amt;
      if (!healDone[src].spellDetail[sp]) healDone[src].spellDetail[sp] = { total:0, casts:0, overhealing:0 };
      healDone[src].spellDetail[sp].total += amt;
      healDone[src].spellDetail[sp].casts++;
      healDone[src].spellDetail[sp].overhealing += (raw - amt);
      healRecv[tgt].total += amt;
    }
  }

  const sessionMs = lastT > firstT ? (lastT - firstT) : 1;
  const encMs = encTime > 0 ? encTime : sessionMs;

  return { dmgDealt, dmgTaken, healDone, healRecv, encDmg, encByEntity, sessionMs, encMs, firstT, lastT };
}

// ============================================================
// Rendering
// ============================================================
function renderAll(events) {
  const agg = aggregate(events);
  renderOverview(agg);
  renderDamage(agg);
  renderHealing(agg);
  renderEncounters(agg);
  renderNpcs(agg);
}

function renderOverview(agg) {
  let totalDmg = 0, totalHeals = 0, totalHits = 0;
  for (const id in agg.dmgDealt) { if (isPlayerSide(id)) totalDmg += agg.dmgDealt[id].total; }
  for (const id in agg.healDone) { if (isPlayerSide(id)) totalHeals += agg.healDone[id].total; }
  for (const id in agg.dmgDealt) { if (isPlayerSide(id)) totalHits += agg.dmgDealt[id].hits; }

  document.getElementById('overviewCards').innerHTML =
    card('Total Damage', fmt(totalDmg)) +
    card('Group DPS', fmtDps(totalDmg / (agg.sessionMs / 1000))) +
    card('Total Healing', fmt(totalHeals)) +
    card('Session Duration', fmtTime(agg.sessionMs)) +
    card('Encounter Time', fmtTime(agg.encMs)) +
    card('Total Hits', fmt(totalHits));

  const rows = Object.keys(agg.dmgDealt)
    .filter(isPlayerSide)
    .sort((a, b) => agg.dmgDealt[b].total - agg.dmgDealt[a].total);

  const maxDmg = rows.length > 0 ? agg.dmgDealt[rows[0]].total : 1;
  let html = '';
  for (let ri = 0; ri < rows.length; ri++) {
    const id = rows[ri];
    const d = agg.dmgDealt[id];
    const topSrc = Object.entries(d.bySource).sort((a,b) => b[1]-a[1])[0];
    const sessionDps = d.total / (agg.sessionMs / 1000);
    const encDps = d.total / (agg.encMs / 1000);
    const gid = 'ov-' + ri;
    html += '<tr class=""expandable"" data-group=""' + gid + '"">';
    html += '<td>' + entityName(id) + '</td>';
    html += '<td>' + entityClass(id) + '</td>';
    html += '<td>' + entityLevel(id) + '</td>';
    html += '<td class=""num"">' + fmt(d.total) + '<div class=""bar"" style=""width:' + (d.total/maxDmg*100) + '%""></div></td>';
    html += '<td class=""num"">' + fmtDps(sessionDps) + '</td>';
    html += '<td class=""num"">' + fmtDps(encDps) + '</td>';
    html += '<td class=""num"">' + fmt(d.hits) + '</td>';
    html += '<td class=""num"">' + fmt(d.crits) + '</td>';
    html += '<td>' + (topSrc ? topSrc[0] : '-') + '</td>';
    html += '</tr>';
    // Expandable detail: breakdown by source
    const sources = Object.entries(d.sourceDetail).sort((a,b) => b[1].total - a[1].total);
    for (const [sName, sd] of sources) {
      const pctLabel = sd.selfDmg ? 'n/a' : (d.total > 0 ? (sd.total / d.total * 100).toFixed(1) + '%' : '0.0%');
      html += '<tr class=""detail-row"" data-group=""' + gid + '"">';
      html += '<td class=""' + dmgClass(sd.dmgType) + '"">' + sName + '</td>';
      html += '<td></td><td></td>';
      html += '<td class=""num"">' + fmt(sd.total) + ' (' + pctLabel + ')</td>';
      html += '<td></td><td></td>';
      html += '<td class=""num"">' + fmt(sd.hits) + '</td>';
      html += '<td class=""num"">' + fmt(sd.crits) + '</td>';
      html += '<td></td>';
      html += '</tr>';
    }
  }
  document.getElementById('overviewDmgBody').innerHTML = html;

  document.getElementById('reportMeta').textContent =
    'Generated ' + new Date().toLocaleString() + ' | Events: ' + RAW_EVENTS.length;
}

function card(label, value) {
  return '<div class=""stat-card""><div class=""label"">' + label + '</div><div class=""value"">' + value + '</div></div>';
}

function renderDamage(agg) {
  const ids = Object.keys(agg.dmgDealt)
    .filter(isPlayerSide)
    .sort((a,b) => agg.dmgDealt[b].total - agg.dmgDealt[a].total);

  let html = '';
  for (let ri = 0; ri < ids.length; ri++) {
    const id = ids[ri];
    const d = agg.dmgDealt[id];
    const taken = agg.dmgTaken[id] ? agg.dmgTaken[id].total : 0;
    const gid = 'dmg-' + ri;
    html += '<tr class=""expandable"" data-group=""' + gid + '"">';
    html += '<td>' + entityName(id) + '</td>';
    html += '<td>' + entityClass(id) + '</td>';
    html += '<td class=""num"">' + fmt(d.total) + '</td>';
    html += '<td class=""num"">' + fmt(taken) + '</td>';
    html += '<td class=""num ' + dmgClass('Physical') + '"">' + fmt(d.byType.Physical || 0) + '</td>';
    html += '<td class=""num ' + dmgClass('Magic') + '"">' + fmt(d.byType.Magic || 0) + '</td>';
    html += '<td class=""num ' + dmgClass('Elemental') + '"">' + fmt(d.byType.Elemental || 0) + '</td>';
    html += '<td class=""num"">' + fmt(d.misses) + '</td>';
    html += '<td class=""num"">' + fmt(d.resists) + '</td>';
    html += '</tr>';
    // Expandable: by damage type, then by source within each type
    const types = Object.entries(d.byType).sort((a,b) => b[1] - a[1]);
    for (const [dtName, dtTotal] of types) {
      const pct = d.total > 0 ? (dtTotal / d.total * 100).toFixed(1) : '0.0';
      html += '<tr class=""detail-row"" data-group=""' + gid + '"">';
      html += '<td class=""' + dmgClass(dtName) + '"" style=""font-weight:bold"">' + dtName + '</td>';
      html += '<td></td>';
      html += '<td class=""num"">' + fmt(dtTotal) + ' (' + pct + '%)</td>';
      html += '<td colspan=""6""></td></tr>';
      // Sources under this damage type
      const sourcesOfType = Object.entries(d.sourceDetail)
        .filter(([,sd]) => sd.dmgType === dtName)
        .sort((a,b) => b[1].total - a[1].total);
      for (const [sName, sd] of sourcesOfType) {
        const sPctLabel = sd.selfDmg ? 'n/a' : (dtTotal > 0 ? (sd.total / dtTotal * 100).toFixed(1) + '%' : '0.0%');
        html += '<tr class=""detail-row"" data-group=""' + gid + '"">';
        html += '<td style=""padding-left:48px"">' + sName + '</td>';
        html += '<td></td>';
        html += '<td class=""num"">' + fmt(sd.total) + ' (' + sPctLabel + ')</td>';
        html += '<td></td><td></td><td></td><td></td>';
        html += '<td class=""num"">' + fmt(sd.hits) + ' hits</td>';
        html += '<td class=""num"">' + fmt(sd.crits) + ' crits</td>';
        html += '</tr>';
      }
    }
  }
  document.getElementById('dmgBody').innerHTML = html;
}

function renderHealing(agg) {
  const ids = Object.keys(agg.healDone)
    .filter(isPlayerSide)
    .sort((a,b) => agg.healDone[b].total - agg.healDone[a].total);

  let html = '';
  for (let ri = 0; ri < ids.length; ri++) {
    const id = ids[ri];
    const h = agg.healDone[id];
    const topSpell = Object.entries(h.bySpell).sort((a,b) => b[1]-a[1])[0];
    const sessionHps = h.total / (agg.sessionMs / 1000);
    const encHps = h.total / (agg.encMs / 1000);
    const gid = 'heal-' + ri;
    html += '<tr class=""expandable"" data-group=""' + gid + '"">';
    html += '<td>' + entityName(id) + '</td>';
    html += '<td>' + entityClass(id) + '</td>';
    html += '<td class=""num dmg-heal"">' + fmt(h.total) + '</td>';
    html += '<td class=""num"">' + fmt(h.casts) + '</td>';
    html += '<td class=""num"">' + fmtDps(sessionHps) + '</td>';
    html += '<td class=""num"">' + fmtDps(encHps) + '</td>';
    html += '<td class=""num"">' + fmt(h.overhealing) + '</td>';
    html += '<td>' + (topSpell ? topSpell[0] : '-') + '</td>';
    html += '</tr>';
    // Expandable: by spell
    const spells = Object.entries(h.spellDetail).sort((a,b) => b[1].total - a[1].total);
    for (const [spName, sp] of spells) {
      const pct = h.total > 0 ? (sp.total / h.total * 100).toFixed(1) : '0.0';
      html += '<tr class=""detail-row"" data-group=""' + gid + '"">';
      html += '<td class=""dmg-heal"">' + spName + '</td>';
      html += '<td></td>';
      html += '<td class=""num"">' + fmt(sp.total) + ' (' + pct + '%)</td>';
      html += '<td class=""num"">' + fmt(sp.casts) + '</td>';
      html += '<td></td><td></td>';
      html += '<td class=""num"">' + fmt(sp.overhealing) + '</td>';
      html += '<td></td>';
      html += '</tr>';
    }
  }
  document.getElementById('healBody').innerHTML = html;
}

function renderEncounters(agg) {
  let html = '';
  for (let ri = 0; ri < ENCOUNTERS.length; ri++) {
    const enc = ENCOUNTERS[ri];
    const dur = enc.end > 0 ? (enc.end - enc.start) : 0;
    const dmg = agg.encDmg[enc.id] || 0;
    const dps = dur > 0 ? dmg / (dur / 1000) : 0;
    const gid = 'enc-' + ri;
    const entities = agg.encByEntity[enc.id] || {};
    html += '<tr class=""expandable"" data-group=""' + gid + '"">';
    html += '<td>' + enc.id + '</td>';
    html += '<td>' + (enc.label || '') + '</td>';
    html += '<td>' + fmtTime(dur) + '</td>';
    html += '<td class=""num"">' + fmt(dmg) + '</td>';
    html += '<td class=""num"">' + fmtDps(dps) + '</td>';
    html += '<td>' + (enc.manual ? 'Yes' : '') + '</td>';
    html += '</tr>';
    // Expandable: per-character damage in this encounter, separated by side
    const allChars = Object.entries(entities).sort((a,b) => b[1].dmg - a[1].dmg);
    const playerSide = allChars.filter(([eid]) => isPlayerSide(eid));
    const npcSide = allChars.filter(([eid]) => !isPlayerSide(eid));
    // Player/Sim/Pet section
    for (const [eid, ed] of playerSide) {
      const charDps = dur > 0 ? ed.dmg / (dur / 1000) : 0;
      const pct = dmg > 0 ? (ed.dmg / dmg * 100).toFixed(1) : '0.0';
      html += '<tr class=""detail-row"" data-group=""' + gid + '"">';
      html += '<td></td>';
      html += '<td>' + entityName(eid) + '</td>';
      html += '<td></td>';
      html += '<td class=""num"">' + fmt(ed.dmg) + ' (' + pct + '%)</td>';
      html += '<td class=""num"">' + fmtDps(charDps) + '</td>';
      html += '<td>' + fmt(ed.hits) + ' hits / ' + fmt(ed.crits) + ' crits</td>';
      html += '</tr>';
    }
    // Separator + NPC section
    if (npcSide.length > 0) {
      html += '<tr class=""detail-row"" data-group=""' + gid + '"" style=""border-top:2px solid var(--accent)"">';
      html += '<td></td><td style=""color:var(--accent);font-weight:bold"">Enemies</td>';
      html += '<td></td><td></td><td></td><td></td></tr>';
      for (const [eid, ed] of npcSide) {
        const charDps = dur > 0 ? ed.dmg / (dur / 1000) : 0;
        const pct = dmg > 0 ? (ed.dmg / dmg * 100).toFixed(1) : '0.0';
        html += '<tr class=""detail-row"" data-group=""' + gid + '"">';
        html += '<td></td>';
        html += '<td style=""color:var(--text-dim)"">' + entityName(eid) + '</td>';
        html += '<td></td>';
        html += '<td class=""num"">' + fmt(ed.dmg) + ' (' + pct + '%)</td>';
        html += '<td class=""num"">' + fmtDps(charDps) + '</td>';
        html += '<td>' + fmt(ed.hits) + ' hits / ' + fmt(ed.crits) + ' crits</td>';
        html += '</tr>';
      }
    }
  }
  document.getElementById('encBody').innerHTML = html;
}

function renderNpcs(agg) {
  const npcIds = new Set();
  for (const id in agg.dmgDealt) { if (!isPlayerSide(id) && id !== 'Environment') npcIds.add(id); }
  for (const id in agg.dmgTaken) { if (!isPlayerSide(id) && id !== 'Environment') npcIds.add(id); }

  const sorted = Array.from(npcIds).sort((a,b) => {
    const ta = agg.dmgTaken[a] ? agg.dmgTaken[a].total : 0;
    const tb = agg.dmgTaken[b] ? agg.dmgTaken[b].total : 0;
    return tb - ta;
  });

  let html = '';
  for (let ri = 0; ri < sorted.length; ri++) {
    const id = sorted[ri];
    const dealt = agg.dmgDealt[id] ? agg.dmgDealt[id].total : 0;
    const takenObj = agg.dmgTaken[id];
    const taken = takenObj ? takenObj.total : 0;
    const healed = agg.healRecv[id] ? agg.healRecv[id].total : 0;
    const gid = 'npc-' + ri;

    // Find top attacker from per-attacker breakdown
    let topAttacker = '-';
    const attackers = takenObj ? Object.entries(takenObj.byAttacker).sort((a,b) => b[1]-a[1]) : [];
    if (attackers.length > 0) topAttacker = entityName(attackers[0][0]);

    html += '<tr class=""expandable"" data-group=""' + gid + '"">';
    html += '<td>' + entityName(id) + '</td>';
    html += '<td>' + entityLevel(id) + '</td>';
    html += '<td class=""num"">' + fmt(dealt) + '</td>';
    html += '<td class=""num"">' + fmt(taken) + '</td>';
    html += '<td class=""num"">' + fmt(healed) + '</td>';
    html += '<td>' + topAttacker + '</td>';
    html += '</tr>';
    // Expandable: damage taken by attacker
    for (const [atkId, atkDmg] of attackers) {
      const pct = taken > 0 ? (atkDmg / taken * 100).toFixed(1) : '0.0';
      html += '<tr class=""detail-row"" data-group=""' + gid + '"">';
      html += '<td>' + entityName(atkId) + '</td>';
      html += '<td></td>';
      html += '<td></td>';
      html += '<td class=""num"">' + fmt(atkDmg) + ' (' + pct + '%)</td>';
      html += '<td></td><td></td>';
      html += '</tr>';
    }
  }
  document.getElementById('npcBody').innerHTML = html;
}

// ============================================================
// Tab switching
// ============================================================
document.querySelectorAll('.tab').forEach(tab => {
  tab.addEventListener('click', () => {
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    document.querySelectorAll('.panel').forEach(p => p.classList.remove('active'));
    tab.classList.add('active');
    document.getElementById('panel-' + tab.dataset.tab).classList.add('active');
  });
});

// ============================================================
// Mode toggle (Final / Replay)
// ============================================================
let replayTimer = null;
let replayIndex = 0;
let replayPlaying = false;

document.querySelectorAll('.mode-btn').forEach(btn => {
  btn.addEventListener('click', () => {
    document.querySelectorAll('.mode-btn').forEach(b => b.classList.remove('active'));
    btn.classList.add('active');
    const mode = btn.dataset.mode;
    document.getElementById('replay-ui').style.display = mode === 'replay' ? 'block' : 'none';
    if (mode === 'final') {
      stopReplay();
      renderAll(RAW_EVENTS);
    } else {
      replayIndex = 0;
      updateReplaySlider();
      renderAll([]);
    }
  });
});

function stopReplay() {
  if (replayTimer) { clearInterval(replayTimer); replayTimer = null; }
  replayPlaying = false;
  const btn = document.getElementById('replayPlayBtn');
  if (btn) btn.textContent = '▶ Play';
}

function updateReplaySlider() {
  const slider = document.getElementById('replaySlider');
  if (slider) {
    slider.max = RAW_EVENTS.length;
    slider.value = replayIndex;
  }
  updateReplayTime();
}

function updateReplayTime() {
  const el = document.getElementById('replayTime');
  if (!el || RAW_EVENTS.length === 0) return;
  if (replayIndex > 0 && replayIndex <= RAW_EVENTS.length) {
    const first = RAW_EVENTS[0].t;
    const cur = RAW_EVENTS[replayIndex - 1].t;
    el.textContent = fmtTime(cur - first);
  } else {
    el.textContent = '0:00';
  }
}

document.getElementById('replayPlayBtn').addEventListener('click', () => {
  if (replayPlaying) {
    stopReplay();
  } else {
    if (replayIndex >= RAW_EVENTS.length) {
      replayIndex = 0;
      updateReplaySlider();
      renderAll([]);
    }
    replayPlaying = true;
    document.getElementById('replayPlayBtn').textContent = '⏸ Pause';
    const speed = parseInt(document.getElementById('replaySpeed').value) || 5;
    const interval = Math.max(10, 50 / speed);
    const step = Math.max(1, Math.floor(speed / 2));
    replayTimer = setInterval(() => {
      replayIndex = Math.min(replayIndex + step, RAW_EVENTS.length);
      updateReplaySlider();
      renderAll(RAW_EVENTS.slice(0, replayIndex));
      if (replayIndex >= RAW_EVENTS.length) stopReplay();
    }, interval);
  }
});

document.getElementById('replaySlider').addEventListener('input', (e) => {
  stopReplay();
  replayIndex = parseInt(e.target.value);
  updateReplayTime();
  renderAll(RAW_EVENTS.slice(0, replayIndex));
});

// ============================================================
// Column sorting
// ============================================================
document.querySelectorAll('th').forEach(th => {
  th.addEventListener('click', () => {
    const table = th.closest('table');
    const tbody = table.querySelector('tbody');
    const rows = Array.from(tbody.querySelectorAll('tr'));
    const idx = Array.from(th.parentNode.children).indexOf(th);
    const isNum = th.classList.contains('num');
    const asc = th.dataset.sort === 'asc';
    th.dataset.sort = asc ? 'desc' : 'asc';

    rows.sort((a, b) => {
      let va = a.children[idx].textContent.trim().replace(/,/g, '');
      let vb = b.children[idx].textContent.trim().replace(/,/g, '');
      if (isNum) { va = parseFloat(va) || 0; vb = parseFloat(vb) || 0; }
      if (asc) return va > vb ? 1 : va < vb ? -1 : 0;
      return va < vb ? 1 : va > vb ? -1 : 0;
    });

    rows.forEach(r => tbody.appendChild(r));
  });
});

// ============================================================
// Expandable row click handler (event delegation)
// ============================================================
document.addEventListener('click', (e) => {
  const row = e.target.closest('tr.expandable');
  if (!row) return;
  const gid = row.dataset.group;
  if (!gid) return;
  row.classList.toggle('open');
  const details = document.querySelectorAll('tr.detail-row[data-group=""' + gid + '""]');
  details.forEach(d => d.classList.toggle('open'));
});

// ============================================================
// Initial render
// ============================================================
renderAll(RAW_EVENTS);
</script>
</body>
</html>");
            return sb.ToString();
        }
    }
}
