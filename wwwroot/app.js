'use strict';
/* FallenSec UI — © 2026 Fallen */
const $ = s => document.querySelector(s), $$ = s => [...document.querySelectorAll(s)];
const host = window.chrome && window.chrome.webview;
const DEMO = !host;
function send(o) { if (host) host.postMessage(o); else demoHandle(o); }

// ---------------------------------------------------------------- language (ro / en)
const LANG = (() => {
  const q = new URLSearchParams(location.search).get('lang');
  if (q === 'ro' || q === 'en') return q;
  return (navigator.language || '').toLowerCase().startsWith('ro') ? 'ro' : 'en';
})();
const EN = LANG === 'en';
const tr = (ro, en) => EN ? en : ro;
const LOCALE = EN ? 'en-GB' : 'ro-RO';
document.documentElement.lang = LANG;
if (EN) {
  $$('[data-en]').forEach(e => e.textContent = e.dataset.en);
  $$('[data-en-ph]').forEach(e => e.placeholder = e.dataset.enPh);
  $$('[data-en-title]').forEach(e => e.title = e.dataset.enTitle);
}

const CHANGELOG = [
  { v: '1.2', d: '13.11.2026', items: [
    ['Interfață în engleză și română: limba se alege automat după Windows și se poate schimba din Setări → General', 'English and Romanian interface: the language follows Windows and can be changed in Settings → General'],
    ['Notificările, meniul din tray și mesajele de stare sunt traduse și ele', 'Notifications, the tray menu and status messages are translated too'],
  ]},
  { v: '1.1', d: '08.11.2026', items: [
    ['Blocare nouă direct prin Windows Filtering Platform (WFP): funcționează și când Windows Firewall e oprit, inclusiv alături de Bitdefender, ESET, Kaspersky și alte firewall-uri', 'New blocking engine built directly on the Windows Filtering Platform (WFP): it works even when Windows Firewall is off, including alongside Bitdefender, ESET, Kaspersky and other firewalls'],
    ['Filtrele WFP sunt persistente: blocările rămân active după închiderea aplicației și după repornirea PC-ului', 'WFP filters are persistent: blocks stay active after the app is closed and after a reboot'],
    ['Regulile din v1.0 (Windows Firewall) sunt mutate automat în WFP; motorul vechi rămâne disponibil în Setări → Blocare', 'Rules from v1.0 (Windows Firewall) are moved to WFP automatically; the old engine is still available in Settings → Blocking'],
    ['Detectează firewall-urile terțe active și explică exact ce se întâmplă cu blocarea', 'Detects active third-party firewalls and explains exactly how they affect blocking'],
    ['Hartă: cifrele de sus (conexiuni, aplicații, țări, locații) sunt clickabile și deschid liste detaliate', 'Map: the counters at the top (connections, apps, countries, locations) are clickable and open detailed lists'],
    ['Hartă: click pe o țară sau locație din listă face zoom pe hartă și arată aplicațiile și conexiunile de acolo, cu buton Înapoi', 'Map: clicking a country or location in the list zooms the map and shows the apps and connections there, with a Back button'],
    ['Aplicații: sortare după orice coloană, prin click pe capul de tabel (încă un click inversează ordinea)', 'Apps: sort by any column by clicking its header (click again to reverse the order)'],
    ['Conexiuni: filtru Toate / Internet / LAN, cu numărul de conexiuni pe fiecare buton', 'Connections: All / Internet / LAN filter, with the connection count on each button'],
    ['Indicator de sortare (▴/▾) pe coloana activă', 'Sort indicator (▴/▾) on the active column'],
  ]},
  { v: '1.0', d: '08.11.2026', items: [
    ['Prima versiune FallenSec', 'First FallenSec release'],
    ['Hartă a lumii cu toate conexiunile live: arce animate de la tine către fiecare destinație', 'World map of all live connections: animated arcs from you to every destination'],
    ['Listă de aplicații cu viteze ↓/↑ în timp real, conexiuni, țări și slider de internet ON/OFF', 'App list with real-time ↓/↑ speeds, connections, countries and an Internet ON/OFF switch'],
    ['Blocarea taie instant și conexiunile TCP deja deschise', 'Blocking also cuts TCP connections that are already open'],
    ['Tabel de conexiuni: proces, port, domeniu (din DNS), IP, oraș, țară, stare, viteze', 'Connections table: process, port, domain (from DNS), IP, city, country, state, speeds'],
    ['Reguli detaliate: per aplicație sau pentru toate, pe IP/CIDR, domeniu (cu subdomenii), țară, porturi, protocol, direcție', 'Detailed rules, per app or for all apps: IP/CIDR, domain (with subdomains), country, ports, protocol, direction'],
    ['Istoric cu grafice: trafic în timp, top aplicații, țări, destinații, porturi și jurnal de evenimente', 'History with charts: traffic over time, top apps, countries, destinations, ports and an event log'],
    ['Notificare când o aplicație iese pentru prima dată pe internet', 'Notification when an app goes online for the first time'],
    ['Buton de panică: taie tot internetul, LAN-ul rămâne funcțional', 'Panic button: cuts all internet traffic while the LAN keeps working'],
    ['Pornire cu Windows, geolocalizare offline DB-IP', 'Start with Windows, offline DB-IP geolocation'],
    ['Iconiță în system tray: minimizarea și închiderea ascund aplicația lângă ceas, buton „Tray” în bara de sus, meniu cu Deschide / Panică / Ieșire și vitezele live în tooltip', 'System tray icon: minimizing or closing hides the app next to the clock, a "Tray" button in the top bar, a menu with Open / Panic / Exit and live speeds in the tooltip'],
  ]},
];

const COUNTRY_CODES = 'AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM JO JP KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR MS MT MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO RS RU RW SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT TV TW TZ UA UG UM US UY UZ VA VC VE VG VI VN VU WF WS XK YE YT ZA ZM ZW'.split(' ');
const PORTS = { 20: 'FTP', 21: 'FTP', 22: 'SSH', 23: 'Telnet', 25: 'SMTP', 53: 'DNS', 67: 'DHCP', 68: 'DHCP', 80: 'HTTP', 110: 'POP3', 123: 'NTP', 137: 'NetBIOS', 138: 'NetBIOS', 143: 'IMAP', 443: 'HTTPS', 445: 'SMB', 465: 'SMTPS', 587: 'SMTP', 853: 'DNS/TLS', 993: 'IMAPS', 995: 'POP3S', 1900: 'SSDP', 3389: 'RDP', 3478: 'STUN', 5228: 'Google Push', 5353: 'mDNS', 5355: 'LLMNR', 8080: 'HTTP-alt', 8443: 'HTTPS-alt', 27017: 'MongoDB', 3306: 'MySQL' };

let regionNames; try { regionNames = new Intl.DisplayNames([LANG], { type: 'region' }); } catch { }
const LAN_NAME = tr('Rețea locală', 'Local network');
const ccName = cc => { if (!cc) return ''; if (cc === 'LAN') return LAN_NAME; try { return regionNames ? regionNames.of(cc) : cc; } catch { return cc; } };
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
function fmtB(b) { b = +b || 0; if (b < 1) return '0 B'; const u = ['B', 'KB', 'MB', 'GB', 'TB']; let i = 0; while (b >= 1024 && i < 4) { b /= 1024; i++; } return (i ? b.toFixed(b < 10 ? 1 : 0) : Math.round(b)) + ' ' + u[i]; }
const fmtR = b => fmtB(b) + '/s';
function fmtDur(s) { if (s < 60) return s + 's'; if (s < 3600) return Math.floor(s / 60) + 'm ' + (s % 60) + 's'; const h = Math.floor(s / 3600); return h < 48 ? h + 'h ' + Math.floor(s % 3600 / 60) + 'm' : Math.floor(h / 24) + tr('z ', 'd ') + (h % 24) + 'h'; }
function fmtTime(ts) { const d = new Date(ts * 1000); return d.toLocaleString(LOCALE, { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }); }
const ccBadge = cc => cc ? `<span class="cc ${cc === 'LAN' ? 'lan' : ''}" title="${esc(ccName(cc))}">${esc(cc)}</span>` : '';
const DEFAULT_ICON = 'data:image/svg+xml;utf8,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="#56638a" stroke-width="1.6"><rect x="3" y="4" width="18" height="16" rx="2.5"/><path d="M3 8.5h18"/><circle cx="6" cy="6.3" r=".6" fill="#56638a"/></svg>');
const SYS_ICON = 'data:image/svg+xml;utf8,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path fill="#3b82f6" d="M3 5.5 10.5 4.4v7.1H3zm8.5-1.2L21 3v8.5h-9.5zM3 12.5h7.5v7.1L3 18.5zm8.5 0H21V21l-9.5-1.3z"/></svg>');
const icon = key => S.icons.get(key) || (key === 'system' || key?.startsWith('name:') ? SYS_ICON : DEFAULT_ICON);
const SVG = {
  chev: '<svg viewBox="0 0 24 24"><path d="m9 6 6 6-6 6"/></svg>',
  chevD: '<svg viewBox="0 0 24 24"><path d="m6 9 6 6 6-6"/></svg>',
  x: '<svg viewBox="0 0 24 24"><path d="M18 6 6 18M6 6l12 12"/></svg>',
  ban: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="m5.7 5.7 12.6 12.6"/></svg>',
  edit: '<svg viewBox="0 0 24 24"><path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z"/></svg>',
  trash: '<svg viewBox="0 0 24 24"><path d="M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6"/></svg>',
  folder: '<svg viewBox="0 0 24 24"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/></svg>',
};

const S = {
  apps: [], appMap: new Map(), conns: [], frozen: null, status: {}, home: null, hist: [], up: 0, down: 0,
  rules: [], settings: {}, version: '1.2', view: 'map', icons: new Map(), expanded: new Set(),
  mapSel: null, mapApp: null, appFilter: 'all', appSort: { k: 'rate', d: -1 }, connProto: 'all', connNet: 'all', connSort: { k: 'down', d: -1 },
  histHours: 24, histApp: '', history: null, appList: [],
};
const pending = new Map(); // key → {block, until}

// ---------------------------------------------------------------- messages
if (host) host.addEventListener('message', e => onMessage(e.data));
function onMessage(m) {
  switch (m.type) {
    case 'state': onState(m); break;
    case 'init':
      S.version = m.version; S.settings = m.settings || {}; S.rules = m.rules || [];
      $('#ver').textContent = 'v' + m.version;
      renderSettings(); renderAbout(); renderRules(); break;
    case 'rules': S.rules = m.rules; renderRules(); updateBadges(); if (S.view === 'apps' || S.view === 'map') schedule(); break;
    case 'toast': toast(m.text, m.kind); if (m.kind === 'ok' && Modal.pendingSave) { Modal.pendingSave = false; Modal.close(); } break;
    case 'history': S.history = m; renderHistory(); break;
    case 'appList': S.appList = m.apps; fillHistApps(); Modal.onAppList?.(); break;
    case 'browsedApp': Modal.onBrowsed?.(m); break;
  }
}

function onState(m) {
  for (const a of m.apps) if (a.icon) S.icons.set(a.key, a.icon);
  const now = Date.now();
  for (const a of m.apps) {
    const p = pending.get(a.key);
    if (p) { if (p.until < now || p.block === a.blocked) pending.delete(a.key); else a.blocked = p.block; }
  }
  S.apps = m.apps; S.appMap = new Map(m.apps.map(a => [a.key, a]));
  S.conns = m.conns; S.status = m.status; S.home = m.home; S.hist = m.hist; S.up = m.up; S.down = m.down;
  S.sessUp = m.sessUp; S.sessDown = m.sessDown;
  updateChrome();
  schedule();
}

// ---------------------------------------------------------------- render loop
let rafPending = false, mouseDown = false, deferred = false;
document.addEventListener('pointerdown', () => mouseDown = true, true);
document.addEventListener('pointerup', () => { mouseDown = false; if (deferred) { deferred = false; setTimeout(schedule, 30); } }, true);
function schedule() {
  if (rafPending) return;
  rafPending = true;
  requestAnimationFrame(() => {
    rafPending = false;
    if (mouseDown) { deferred = true; return; }
    try { renderView(); } catch (e) { console.error(e); }
  });
}
function renderView() {
  switch (S.view) {
    case 'map': renderMap(); break;
    case 'apps': renderApps(); break;
    case 'conns': renderConns(); break;
  }
}

// ---------------------------------------------------------------- navigation
const TITLES = EN
  ? { map: 'Connection map', apps: 'Apps', conns: 'Connections', history: 'History', rules: 'Rules', settings: 'Settings', about: 'About' }
  : { map: 'Harta conexiunilor', apps: 'Aplicații', conns: 'Conexiuni', history: 'Istoric', rules: 'Reguli', settings: 'Setări', about: 'Despre' };
function go(view) {
  S.view = view;
  $$('#nav a').forEach(a => a.classList.toggle('active', a.dataset.view === view));
  $$('.view').forEach(v => v.classList.toggle('active', v.id === 'view-' + view));
  $('#viewTitle').textContent = TITLES[view];
  if (view === 'history') { send({ cmd: 'appList' }); loadHistory(); }
  if (view === 'map') setTimeout(() => { Map2.resize(); schedule(); }, 10);
  schedule();
}
$$('#nav a').forEach(a => a.onclick = () => go(a.dataset.view));
function segInit(id, cb) {
  const el = $(id);
  el.addEventListener('click', e => {
    const b = e.target.closest('button'); if (!b) return;
    el.querySelectorAll('button').forEach(x => x.classList.toggle('on', x === b));
    cb(b.dataset.v);
  });
}

// ---------------------------------------------------------------- chrome (topbar, chips, banners)
let lastChips = '', lastBanners = '';
function updateChrome() {
  $('#tb-down').textContent = $('#sb-down').textContent = fmtR(S.down);
  $('#tb-up').textContent = $('#sb-up').textContent = fmtR(S.up);
  const st = S.status || {};
  const chip = (cls, txt, title) => `<span class="chip ${cls}" title="${esc(title || '')}"><i></i>${txt}</span>`;
  const chips = [
    chip(st.admin ? 'ok' : 'bad', 'Admin', st.admin ? tr('Rulează ca administrator', 'Running as administrator') : tr('Nu rulează ca administrator', 'Not running as administrator')),
    chip(st.etw ? 'ok' : 'bad', tr('Trafic', 'Traffic'), st.etw ? tr('Monitorizare trafic ETW activă', 'ETW traffic monitoring active') : (st.etwError || tr('ETW inactiv', 'ETW inactive'))),
    chip(st.dns ? 'ok' : 'warn', 'DNS', st.dns ? tr('Numele domeniilor sunt preluate din DNS', 'Domain names are taken from DNS') : tr('Monitorizare DNS inactivă', 'DNS monitoring inactive')),
    chip(st.geoReady ? 'ok' : 'warn', 'Geo', st.geo),
    chip(st.block?.level || 'warn', tr('Blocare', 'Blocking') + (st.block?.engine ? ': ' + (st.block.engine === 'WFP' ? 'WFP' : 'WinFW') : ''), st.block?.text || ''),
  ].join('');
  if (chips !== lastChips) { $('#chips').innerHTML = chips; lastChips = chips; }

  const b = [];
  if (st.panic) b.push(`<div class="banner err"><b>${tr('Internetul este tăiat.', 'Internet is cut off.')}</b> ${tr('Toate conexiunile către internet sunt blocate (LAN-ul funcționează).', 'All internet connections are blocked (the LAN still works).')} <button class="btn danger sm" data-act="panicOff">${tr('Restabilește internetul', 'Restore internet')}</button></div>`);
  if (st.admin === false) b.push(`<div class="banner err">${tr('FallenSec nu rulează ca administrator — blocarea, regulile și vitezele sunt dezactivate. Repornește aplicația cu „Run as administrator”.', 'FallenSec is not running as administrator — blocking, rules and speeds are disabled. Restart the app with "Run as administrator".')}</div>`);
  if (st.block?.banner) b.push(`<div class="banner warn">${esc(st.block.banner)} ${st.block.canEnableWinFw ? `<button class="btn sm" data-act="enableFw">${tr('Pornește Windows Firewall', 'Turn on Windows Firewall')}</button>` : ''}${st.block.engine !== 'WFP' ? ` <button class="btn sm" data-act="useWfp">${tr('Folosește WFP', 'Use WFP')}</button>` : ''}</div>`);
  if (st.admin && st.etwError) b.push(`<div class="banner warn">${esc(st.etwError)}</div>`);
  if (st.rules) b.push(`<div class="banner err">${esc(st.rules)}</div>`);
  if (st.geo && !st.geoReady) b.push(`<div class="banner info">${esc(st.geo)} — ${tr('harta se va popula după descărcarea bazei de geolocalizare.', 'the map will fill in once the geolocation database is downloaded.')}</div>`);
  const bs = b.join('');
  if (bs !== lastBanners) { $('#banners').innerHTML = bs; lastBanners = bs; }

  const pb = $('#panicBtn');
  pb.classList.toggle('on', !!st.panic);
  $('#panicTxt').textContent = st.panic ? tr('Internet TĂIAT', 'Internet CUT') : tr('Taie internetul', 'Cut internet');
  const bi = $('#blockInfo'); if (bi && st.block && bi.textContent !== st.block.text) bi.textContent = st.block.text;
  drawSpark();
  updateBadges();
}
function updateBadges() {
  $('#nb-apps').textContent = S.apps.filter(a => a.conns > 0).length || '';
  $('#nb-conns').textContent = S.conns.length || '';
  $('#nb-rules').textContent = S.rules.length || '';
}
$('#banners').addEventListener('click', e => {
  const a = e.target.closest('[data-act]')?.dataset.act;
  if (a === 'panicOff') send({ cmd: 'panic', on: false });
  if (a === 'enableFw') send({ cmd: 'enableFirewall' });
  if (a === 'useWfp') send({ cmd: 'blockEngine', engine: 'wfp' });
});
$('#toTray').onclick = () => send({ cmd: 'hideToTray' });
$('#panicBtn').onclick = () => {
  if (S.status.panic) { send({ cmd: 'panic', on: false }); return; }
  confirmBox(tr('Tai tot internetul?', 'Cut off all internet access?'), tr('Toate aplicațiile vor pierde instant accesul la internet (inclusiv browserul). Rețeaua locală rămâne funcțională. Poți restabili oricând din același buton sau din iconița din tray.', 'Every app loses internet access instantly (the browser too). The local network keeps working. You can restore it at any time from the same button or from the tray icon.'), tr('Taie internetul', 'Cut internet'), () => send({ cmd: 'panic', on: true }), true);
};

function drawSpark() {
  const svg = d3.select('#spark'); const W = svg.node().clientWidth || 200, H = 34;
  const data = S.hist || []; if (data.length < 2) { svg.selectAll('*').remove(); return; }
  const max = d3.max(data, d => Math.max(d[1], d[2])) || 1;
  const x = d3.scaleLinear().domain([0, data.length - 1]).range([0, W]);
  const y = d3.scaleLinear().domain([0, max]).range([H - 2, 3]);
  const area = i => d3.area().x((d, j) => x(j)).y0(H).y1(d => y(d[i])).curve(d3.curveMonotoneX)(data);
  const line = i => d3.line().x((d, j) => x(j)).y(d => y(d[i])).curve(d3.curveMonotoneX)(data);
  svg.html(`<defs><linearGradient id="gd" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#22d3ee" stop-opacity=".35"/><stop offset="1" stop-color="#22d3ee" stop-opacity="0"/></linearGradient>
  <linearGradient id="gu" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#f472b6" stop-opacity=".3"/><stop offset="1" stop-color="#f472b6" stop-opacity="0"/></linearGradient></defs>
  <path d="${area(2)}" fill="url(#gd)"/><path d="${line(2)}" fill="none" stroke="#22d3ee" stroke-width="1.3"/>
  <path d="${area(1)}" fill="url(#gu)"/><path d="${line(1)}" fill="none" stroke="#f472b6" stroke-width="1.1"/>`);
}

// ---------------------------------------------------------------- apps view
$('#appSearch').oninput = schedule;
segInit('#appFilter', v => { S.appFilter = v; schedule(); });
$('#appsTable thead').addEventListener('click', e => {
  const th = e.target.closest('th[data-s]'); if (!th) return;
  const k = th.dataset.s;
  S.appSort = { k, d: S.appSort.k === k ? -S.appSort.d : (['name', 'net'].includes(k) ? 1 : -1) };
  schedule();
});
function sortMark(table, st) {
  $$(table + ' th[data-s]').forEach(th => { const on = th.dataset.s === st.k; th.classList.toggle('sorted', on); th.classList.toggle('asc', on && st.d > 0); th.classList.toggle('desc', on && st.d < 0); });
}

function rateCell(v, max, cls) {
  const w = max > 0 ? Math.max(v > 0 ? 3 : 0, Math.round(36 * v / max)) : 0;
  return `<span class="rate ${cls}">${v > 0 ? fmtR(v) : '<span class="dim">—</span>'}<i class="bar" style="width:${w}px"></i></span>`;
}

function renderApps() {
  const q = $('#appSearch').value.trim().toLowerCase();
  let list = S.apps.filter(a => a.key !== 'idle');
  if (S.appFilter === 'active') list = list.filter(a => a.conns > 0);
  if (S.appFilter === 'blocked') list = list.filter(a => a.blocked);
  if (S.appFilter === 'user') list = list.filter(a => !a.sys);
  if (q) list = list.filter(a => (a.name + ' ' + a.path + ' ' + (a.publisher || '')).toLowerCase().includes(q));
  // sortare după capul de tabel (implicit: viteză totală), cu departajare stabilă
  const { k: sk, d: sd } = S.appSort;
  const rate = a => a.up + a.down, tot = a => a.totUp + a.totDown;
  const val = {
    rate, name: a => a.name.toLowerCase(), conns: a => a.conns, cc: a => a.countries.filter(c => c !== 'LAN').length,
    down: a => a.down, up: a => a.up, tot, net: a => (a.blocked ? 0 : 1),
  }[sk] || rate;
  list.sort((a, b) => {
    const x = val(a), y = val(b);
    const c = typeof x === 'string' ? x.localeCompare(y, LANG) : x - y;
    return c * sd || rate(b) - rate(a) || b.conns - a.conns || tot(b) - tot(a) || a.name.localeCompare(b.name);
  });
  sortMark('#appsTable', S.appSort);
  $('#appCount').textContent = plural(list.length, tr('aplicație', 'app'), tr('aplicații', 'apps'));
  const maxD = d3.max(list, a => a.down) || 0, maxU = d3.max(list, a => a.up) || 0;
  const rows = list.map(a => {
    const exp = S.expanded.has(a.key);
    const cs = a.countries.filter(c => c !== 'LAN');
    const tags = (a.blocked ? `<span class="tag red">${tr('BLOCAT', 'BLOCKED')}</span>` : '') + (a.rules ? `<span class="tag amber">${tr('reguli', 'rules')}</span>` : '') +
      (a.sys ? `<span class="tag">${tr('sistem', 'system')}</span>` : '') + (a.listen.length ? `<span class="tag cyan" title="${esc(a.listen.join(', '))}">${tr('ascultă', 'listening')} ${a.listen.length}</span>` : '');
    let html = `<tr class="${a.blocked ? 'blocked' : ''} ${a.conns === 0 && !a.blocked ? 'inactive' : ''}" data-key="${esc(a.key)}">
      <td style="width:34px"><button class="icon-btn" data-act="expand">${exp ? SVG.chevD : SVG.chev}</button></td>
      <td><div class="app-cell"><img class="app-ico" src="${icon(a.key)}"><div class="app-txt"><div class="appname">${esc(a.name)}${tags}</div><div class="apppath" title="${esc(a.path)}">${esc(a.publisher ? a.publisher + ' · ' : '')}${esc(a.path)}</div></div></div></td>
      <td class="r">${a.conns || '<span class="dim">0</span>'}</td>
      <td>${cs.slice(0, 5).map(ccBadge).join('')}${cs.length > 5 ? `<span class="dim">+${cs.length - 5}</span>` : ''}</td>
      <td class="r dn">${rateCell(a.down, maxD, 'dn')}</td>
      <td class="r up">${rateCell(a.up, maxU, 'up')}</td>
      <td class="r">${fmtB(a.totUp + a.totDown)}</td>
      <td class="c">${switchHtml(a)}</td></tr>`;
    if (exp) html += `<tr class="expand" data-key="${esc(a.key)}"><td colspan="8">${appDetail(a)}</td></tr>`;
    return html;
  });
  $('#appsTable tbody').innerHTML = rows.join('') || `<tr><td colspan="8" class="empty">${S.apps.length ? tr('Nicio aplicație nu corespunde filtrului.', 'No app matches the filter.') : tr('Se încarcă...', 'Loading...')}</td></tr>`;
}
function switchHtml(a) {
  return `<label class="sw ${a.canBlock ? '' : 'disabled'}" title="${a.blocked ? tr('Internet BLOCAT — apasă pentru a permite', 'Internet BLOCKED — click to allow') : tr('Internet permis — apasă pentru a bloca', 'Internet allowed — click to block')}"><input type="checkbox" data-act="toggle" ${a.blocked ? '' : 'checked'}><span></span></label>`;
}
function appDetail(a) {
  const conns = S.conns.filter(c => c.app === a.key);
  const rows = conns.slice(0, 60).map(c => `<tr><td>${ccBadge(c.cc)} ${esc(c.city || ccName(c.cc) || '')}</td><td><span class="host" title="${esc(c.host || '')}">${c.host ? esc(c.host) : '<span class="dim">—</span>'}</span></td>
    <td class="mono dim">${esc(c.ip)}:${c.rport}</td><td>${c.proto}</td><td class="dn">${c.down ? fmtR(c.down) : ''}</td><td class="up">${c.up ? fmtR(c.up) : ''}</td>
    <td><button class="icon-btn" title="${tr('Blochează această destinație', 'Block this destination')}" data-act="blockDest" data-id="${esc(c.id)}">${SVG.ban}</button></td></tr>`).join('');
  return `<div class="exp">
    <div class="exp-actions">
      <button class="btn sm" data-act="reveal">${tr('Deschide locația', 'Open file location')}</button>
      <button class="btn sm" data-act="ruleFor">${tr('Regulă detaliată pentru aplicație', 'Detailed rule for this app')}</button>
      <button class="btn sm" data-act="histFor">${tr('Istoric aplicație', 'App history')}</button>
      ${a.listen.length ? `<span class="muted" style="align-self:center">${tr('Porturi ascultate', 'Listening ports')}: <span class="mono">${esc(a.listen.join(', '))}</span></span>` : ''}
    </div>
    ${conns.length ? `<table>${rows}</table>${conns.length > 60 ? `<div class="dim">+${conns.length - 60} ${tr('conexiuni', 'connections')}</div>` : ''}` : `<div class="dim">${tr('Nicio conexiune activă acum.', 'No active connections right now.')}</div>`}
  </div>`;
}

function toggleApp(key, allow) {
  const a = S.appMap.get(key); if (!a) return;
  const doIt = () => {
    pending.set(key, { block: !allow, until: Date.now() + 4000 });
    a.blocked = !allow;
    send({ cmd: 'toggleApp', key, block: !allow });
    schedule();
  };
  if (!allow && (a.sys || key === 'system')) {
    confirmBox(tr('Blochezi o componentă Windows?', 'Block a Windows component?'), tr(`„${a.name}” face parte din sistem. Blocarea poate opri Windows Update, DNS, sincronizarea orei sau alte servicii. Continui?`, `"${a.name}" is part of the system. Blocking it can stop Windows Update, DNS, time sync or other services. Continue?`), tr('Blochează', 'Block'), doIt, true, () => schedule());
  } else doIt();
}

function tableClick(e) {
  const t = e.target.closest('[data-act]'); if (!t) return;
  const key = t.closest('[data-key]')?.dataset.key; const a = key && S.appMap.get(key);
  switch (t.dataset.act) {
    case 'expand': S.expanded.has(key) ? S.expanded.delete(key) : S.expanded.add(key); schedule(); break;
    case 'toggle': toggleApp(key, t.checked); break;
    case 'reveal': if (a) send({ cmd: 'reveal', path: a.path }); break;
    case 'ruleFor': if (a) Modal.rule({ appPath: a.path, appName: a.name }); break;
    case 'histFor': S.histApp = key; go('history'); break;
    case 'blockDest': blockDestMenu(t.dataset.id); break;
    case 'kill': send({ cmd: 'killConn', id: t.dataset.id }); break;
  }
}
$('#appsTable').addEventListener('click', tableClick);

// ---------------------------------------------------------------- connections view
$('#connSearch').oninput = schedule;
$('#connActive').onchange = schedule;
$('#connPause').onchange = e => { S.frozen = e.target.checked ? S.conns : null; schedule(); };
segInit('#connProto', v => { S.connProto = v; schedule(); });
segInit('#connNet', v => { S.connNet = v; schedule(); });
const isLan = c => c.cc === 'LAN';
$('#connsTable thead').addEventListener('click', e => {
  const th = e.target.closest('th[data-s]'); if (!th) return;
  const k = th.dataset.s; S.connSort = { k, d: S.connSort.k === k ? -S.connSort.d : (['app', 'host', 'cc', 'proto', 'state'].includes(k) ? 1 : -1) };
  schedule();
});
function renderConns() {
  const q = $('#connSearch').value.trim().toLowerCase();
  let list = (S.frozen || S.conns).slice();
  // numărătoare pe butoane (după protocol, înainte de filtrul de rețea)
  const byProto = S.connProto === 'all' ? list : list.filter(c => c.proto === S.connProto);
  const nLan = byProto.filter(isLan).length;
  const cnt = { all: byProto.length, inet: byProto.length - nLan, lan: nLan };
  $$('#connNet button').forEach(b => {
    const t = { all: tr('Toate', 'All'), inet: 'Internet', lan: 'LAN' }[b.dataset.v];
    const h = `${t}<span class="n">${cnt[b.dataset.v]}</span>`;
    if (b.innerHTML !== h) b.innerHTML = h;
  });
  if (S.connNet === 'inet') list = list.filter(c => !isLan(c));
  if (S.connNet === 'lan') list = list.filter(isLan);
  if (S.connProto !== 'all') list = list.filter(c => c.proto === S.connProto);
  if ($('#connActive').checked) list = list.filter(c => c.up + c.down > 0);
  const appName = k => S.appMap.get(k)?.name || k;
  if (q) list = list.filter(c => [appName(c.app), c.host, c.ip, c.cc, ccName(c.cc), c.city, c.country, String(c.rport), String(c.lport), c.state, c.proto].join(' ').toLowerCase().includes(q));
  const { k, d } = S.connSort;
  const val = {
    app: c => appName(c.app).toLowerCase(), proto: c => c.proto, host: c => (c.host || c.ip), rport: c => c.rport, cc: c => c.cc + (c.city || ''),
    state: c => c.state, down: c => c.down, up: c => c.up, tot: c => c.totUp + c.totDown, age: c => c.age,
  }[k];
  list.sort((a, b) => { const x = val(a), y = val(b); return (x < y ? -1 : x > y ? 1 : 0) * d || (b.totUp + b.totDown) - (a.totUp + a.totDown); });
  sortMark('#connsTable', S.connSort);
  $('#connCount').textContent = plural(list.length, tr('conexiune', 'connection'), tr('conexiuni', 'connections')) + (S.frozen ? tr(' (pauză)', ' (paused)') : '');
  const shown = list.slice(0, 500);
  $('#connsTable tbody').innerHTML = shown.map(c => {
    const a = S.appMap.get(c.app);
    const svc = PORTS[c.rport] ? `<span class="dim"> ${PORTS[c.rport]}</span>` : '';
    return `<tr class="${c.blocked ? 'blocked' : ''}" data-key="${esc(c.app)}">
      <td><div class="app-cell"><img class="app-ico sm" src="${icon(c.app)}"><div class="appname" style="max-width:220px" title="${esc(a?.path || '')} (PID ${c.pid})">${esc(a?.name || c.app)}</div></div></td>
      <td>${c.proto}</td>
      <td><div class="app-txt"><span class="host" title="${esc(c.host || '')}">${esc(c.host || c.ip)}</span>${c.host ? `<span class="mono dim">${esc(c.ip)}</span>` : ''}</div></td>
      <td class="r mono">${c.rport}${svc}</td>
      <td>${ccBadge(c.cc)} <span class="muted">${esc(c.city || ccName(c.cc))}</span></td>
      <td><span class="state ${c.state}">${c.state}</span></td>
      <td class="r dn">${c.down ? fmtR(c.down) : '<span class="dim">—</span>'}</td>
      <td class="r up">${c.up ? fmtR(c.up) : '<span class="dim">—</span>'}</td>
      <td class="r">${fmtB(c.totUp + c.totDown)}</td>
      <td class="r muted">${fmtDur(c.age)}</td>
      <td style="white-space:nowrap">${c.proto === 'TCP' ? `<button class="icon-btn" title="${tr('Închide conexiunea', 'Close connection')}" data-act="kill" data-id="${esc(c.id)}">${SVG.x}</button>` : ''}<button class="icon-btn" title="${tr('Blochează...', 'Block...')}" data-act="blockDest" data-id="${esc(c.id)}">${SVG.ban}</button></td></tr>`;
  }).join('') || `<tr><td colspan="11" class="empty">${tr('Nicio conexiune.', 'No connections.')}</td></tr>`;
}
$('#connsTable').addEventListener('click', tableClick);

function blockDestMenu(id) {
  const c = (S.frozen || S.conns).find(x => x.id === id); if (!c) return;
  const a = S.appMap.get(c.app);
  const appName = a?.name || c.app, canApp = a?.canBlock;
  const dest = c.host || c.ip;
  const base = { appPath: canApp ? a.path : null, appName: canApp ? appName : null, direction: 'out' };
  const opts = [
    canApp && { t: tr(`Blochează IP-ul ${c.ip} doar pentru ${appName}`, `Block IP ${c.ip} for ${appName} only`), r: { ...base, target: 'ip', value: c.ip } },
    canApp && c.host && { t: tr(`Blochează domeniul ${baseDomain(c.host)} pentru ${appName}`, `Block domain ${baseDomain(c.host)} for ${appName}`), r: { ...base, target: 'domain', value: baseDomain(c.host) } },
    canApp && c.cc && c.cc !== 'LAN' && { t: tr(`Blochează toată țara ${ccName(c.cc)} pentru ${appName}`, `Block the whole country ${ccName(c.cc)} for ${appName}`), r: { ...base, target: 'country', value: c.cc } },
    { t: c.host ? tr(`Blochează domeniul ${baseDomain(c.host)} pentru toate aplicațiile`, `Block domain ${baseDomain(c.host)} for all apps`) : tr(`Blochează IP-ul ${c.ip} pentru toate aplicațiile`, `Block IP ${c.ip} for all apps`), r: { appPath: null, appName: null, direction: 'out', target: c.host ? 'domain' : 'ip', value: c.host ? baseDomain(c.host) : c.ip } },
    canApp && { t: tr(`Blochează portul ${c.rport} (${c.proto}) pentru ${appName}`, `Block port ${c.rport} (${c.proto}) for ${appName}`), r: { ...base, target: 'any', protocol: c.proto.toLowerCase(), ports: String(c.rport) } },
    canApp && !c.blocked && { t: tr(`Taie complet internetul pentru ${appName}`, `Cut off all internet access for ${appName}`), app: c.app },
  ].filter(Boolean);
  Modal.open(`<h2>${tr('Blochează conexiunea', 'Block connection')}</h2>
    <div class="preview"><b>${esc(appName)}</b> → ${esc(dest)}:${c.rport} ${c.cc ? '(' + esc(c.city ? c.city + ', ' : '') + esc(ccName(c.cc)) + ')' : ''}</div>
    <div style="display:flex;flex-direction:column;gap:8px">${opts.map((o, i) => `<button class="btn" style="text-align:left" data-i="${i}">${esc(o.t)}</button>`).join('')}</div>
    <div class="modal-actions"><button class="btn ghost" data-close>${tr('Renunță', 'Cancel')}</button></div>`);
  $('#modalBox').querySelectorAll('[data-i]').forEach(b => b.onclick = () => {
    const o = opts[+b.dataset.i];
    if (o.app) { Modal.close(); toggleApp(o.app, false); } else Modal.rule(o.r);
  });
}
function baseDomain(h) {
  const p = h.split('.'); if (p.length <= 2) return h;
  const two = p.slice(-2).join('.');
  if (/^(co|com|net|org|gov|ac|edu)\.[a-z]{2}$/.test(two)) return p.slice(-3).join('.');
  return two;
}

// ---------------------------------------------------------------- map
const Map2 = {
  ready: false, k: 1,
  init() {
    const svg = d3.select('#map');
    this.svg = svg;
    this.g = svg.append('g');
    this.proj = d3.geoNaturalEarth1();
    this.path = d3.geoPath(this.proj);
    this.gSphere = this.g.append('path').attr('class', 'sphere');
    this.gGrat = this.g.append('path').attr('class', 'graticule');
    this.gCountries = this.g.append('g');
    this.gArcs = this.g.append('g');
    this.gNodes = this.g.append('g');
    this.gHome = this.g.append('g').attr('class', 'home');
    this.gHome.append('circle').attr('class', 'ring');
    this.gHome.append('circle').attr('class', 'dot');
    this.zoom = d3.zoom().scaleExtent([1, 16]).on('zoom', e => {
      this.g.attr('transform', e.transform); this.k = e.transform.k; this.rescale();
    });
    svg.call(this.zoom).on('dblclick.zoom', null);
    svg.on('click', e => { if (e.target === svg.node() || e.target.classList.contains('country') || e.target.classList.contains('sphere')) { S.mapSel = null; S.mapCountry = null; S.mapPanel = null; renderMapDetail(); schedule(); } });
    $('#mapReset').onclick = () => svg.transition().duration(500).call(this.zoom.transform, d3.zoomIdentity);
    fetch('lib/countries-50m.json').then(r => r.json()).then(topo => {
      this.features = topojson.feature(topo, topo.objects.countries).features;
      this.countryPaths = this.gCountries.selectAll('path').data(this.features).join('path').attr('class', 'country');
      this.ready = true; this.resize(); schedule();
    });
    new ResizeObserver(() => this.resize()).observe($('#mapWrap'));
  },
  resize() {
    if (!this.ready) return;
    const w = $('#mapWrap').clientWidth, h = $('#mapWrap').clientHeight; if (!w || !h) return;
    if (w === this.w && h === this.h) return;
    this.w = w; this.h = h;
    const side = $('#mapSide').offsetWidth + 24;
    this.proj.fitExtent([[16, 60], [w - side - 10, h - 50]], { type: 'Sphere' });
    this.gSphere.attr('d', this.path({ type: 'Sphere' }));
    this.gGrat.attr('d', this.path(d3.geoGraticule10()));
    this.countryPaths.attr('d', this.path);
    this.locCountry = new Map();
    renderMap();
  },
  rescale() {
    const k = this.k;
    this.gNodes.selectAll('circle.core').attr('r', d => d.r / k);
    this.gNodes.selectAll('circle.halo').attr('r', d => d.r * 2.3 / k);
    this.gHome.select('.dot').attr('r', 5 / k);
    this.gHome.select('.ring').attr('r', 6 / k);
  },
  countryOf(n) {
    if (!this.locCountry.has(n.key)) {
      let idx = -1;
      for (let i = 0; i < this.features.length; i++) if (d3.geoContains(this.features[i], [n.lon, n.lat])) { idx = i; break; }
      this.locCountry.set(n.key, idx);
    }
    return this.locCountry.get(n.key);
  },
};

function arcPath(a, b) {
  const dx = b[0] - a[0], dy = b[1] - a[1], dist = Math.hypot(dx, dy);
  const mx = (a[0] + b[0]) / 2, my = (a[1] + b[1]) / 2;
  const off = Math.min(dist * 0.28, 160);
  let nx = -dy / dist, ny = dx / dist; if (ny > 0) { nx = -nx; ny = -ny; } // curbă spre nord
  return `M${a[0]},${a[1]}Q${mx + nx * off},${my + ny * off} ${b[0]},${b[1]}`;
}

function mapNodes() {
  const sel = S.mapApp;
  const groups = new Map();
  for (const c of S.conns) {
    if (c.lat == null || (sel && c.app !== sel)) continue;
    const key = c.lat.toFixed(1) + ',' + c.lon.toFixed(1);
    let g = groups.get(key);
    if (!g) { g = { key, lat: c.lat, lon: c.lon, city: c.city, country: c.country, cc: c.cc, conns: [], apps: new Set(), rate: 0, up: 0, down: 0, tot: 0, blocked: 0 }; groups.set(key, g); }
    g.conns.push(c); g.apps.add(c.app); g.rate += c.up + c.down; g.up += c.up; g.down += c.down; g.tot += c.totUp + c.totDown; if (c.blocked) g.blocked++;
  }
  return [...groups.values()];
}

function renderMap() {
  renderMapSide();
  const M = Map2; if (!M.ready || !M.w) return;
  const nodes = mapNodes();
  for (const n of nodes) {
    const p = M.proj([n.lon, n.lat]); n.x = p[0]; n.y = p[1];
    n.cls = n.blocked === n.conns.length ? 'blocked' : n.rate > 0 ? 'active' : 'idle';
    n.r = 2.4 + Math.min(5, Math.sqrt(n.conns.length) * 1.1) + (n.rate > 0 ? Math.min(5, Math.log10(1 + n.rate / 1024) * 1.7) : 0);
  }
  const h = S.home ? M.proj([S.home.lon, S.home.lat]) : null;
  const arcNodes = h ? nodes.filter(n => Math.hypot(n.x - h[0], n.y - h[1]) > 1.5) : [];
  M.gArcs.selectAll('path').data(arcNodes, d => d.key).join('path')
    .attr('class', d => 'arc ' + d.cls + (S.mapSel === d.key ? ' sel' : ''))
    .attr('d', d => arcPath(h, [d.x, d.y]))
    .style('stroke-width', d => (d.cls === 'active' ? 1.3 + Math.min(3.2, Math.log10(1 + d.rate / 1024) * 1.2) : 1.1) + 'px');
  const ng = M.gNodes.selectAll('g.node').data(nodes, d => d.key).join(enter => {
    const g = enter.append('g');
    g.append('circle').attr('class', 'halo');
    g.append('circle').attr('class', 'core');
    g.on('mouseenter mousemove', (e, d) => showTip(e, d)).on('mouseleave', hideTip)
      .on('click', (e, d) => { e.stopPropagation(); S.mapSel = S.mapSel === d.key ? null : d.key; renderMapDetail(); schedule(); });
    return g;
  });
  ng.attr('class', d => 'node ' + d.cls).attr('transform', d => `translate(${d.x},${d.y})`);
  ng.select('.core').attr('r', d => d.r / M.k).style('stroke-width', '1.2px');
  ng.select('.halo').attr('r', d => d.r * 2.3 / M.k);
  if (h) { M.gHome.style('display', null).attr('transform', `translate(${h[0]},${h[1]})`); M.rescale(); } else M.gHome.style('display', 'none');
  const hot = new Set(nodes.map(n => M.countryOf(n)));
  M.countryPaths.classed('hot', (d, i) => hot.has(i));
  // statistici
  const ccs = new Set(nodes.map(n => n.cc).filter(c => c && c !== 'LAN'));
  const apps = new Set(); nodes.forEach(n => n.apps.forEach(a => apps.add(a)));
  const stats = [
    ['conns', nodes.reduce((s, n) => s + n.conns.length, 0), tr('conexiuni pe hartă', 'connections on map')],
    ['apps', apps.size, tr('aplicații', 'apps')], ['countries', ccs.size, tr('țări', 'countries')], ['locs', nodes.length, tr('locații', 'locations')],
  ];
  const sh = stats.map(([k, v, l]) => `<div class="stat click ${S.mapPanel === k && !S.mapSel && !S.mapCountry ? 'on' : ''}" data-panel="${k}" title="${tr('Click pentru detalii', 'Click for details')}"><b>${v}</b><span>${l}</span></div>`).join('') +
    (S.mapApp ? `<div class="stat click" data-panel="clearApp"><b style="font-size:13px">${esc(S.appMap.get(S.mapApp)?.name || '')}</b><span>${tr('filtru activ — click pentru toate', 'filter on — click to show all')}</span></div>` : '');
  const msEl = $('#mapStats'); if (msEl._h !== sh) { msEl.innerHTML = sh; msEl._h = sh; }
  if (S.mapSel || S.mapPanel || S.mapCountry) renderMapDetail(nodes);
}

$('#mapStats').addEventListener('click', e => {
  const p = e.target.closest('[data-panel]')?.dataset.panel; if (!p) return;
  if (p === 'clearApp') { S.mapApp = null; schedule(); return; }
  const same = S.mapPanel === p && !S.mapSel && !S.mapCountry;
  S.mapPanel = same ? null : p; S.mapSel = null; S.mapCountry = null;
  renderMapDetail(); schedule();
});

/** Zoom lin pe un set de locații (ținând cont de panourile din dreapta). */
function zoomToNodes(ns) {
  const M = Map2; if (!M.ready || !ns.length || !M.w) return;
  const pts = ns.map(n => M.proj([n.lon, n.lat]));
  const xs = pts.map(p => p[0]), ys = pts.map(p => p[1]);
  const x0 = Math.min(...xs), x1 = Math.max(...xs), y0 = Math.min(...ys), y1 = Math.max(...ys);
  const used = $('#mapSide').offsetWidth + ($('#mapDetail').classList.contains('hidden') ? 0 : $('#mapDetail').offsetWidth) + 50;
  const W = Math.max(200, M.w - used), H = M.h - 80;
  const k = Math.max(1, Math.min(5, 0.55 * Math.min(W / Math.max(x1 - x0, 1), H / Math.max(y1 - y0, 1))));
  const t = d3.zoomIdentity.translate(W / 2 + 10, H / 2 + 50).scale(k).translate(-(x0 + x1) / 2, -(y0 + y1) / 2);
  M.svg.transition().duration(650).call(M.zoom.transform, t);
}

/** Agregă conexiunile de pe hartă după o cheie (aplicație / țară). */
function aggregate(conns, keyFn) {
  const m = new Map();
  for (const c of conns) {
    const k = keyFn(c);
    let g = m.get(k);
    if (!g) { g = { key: k, conns: [], apps: new Set(), ccs: new Set(), locs: new Set(), up: 0, down: 0, tot: 0, blocked: 0 }; m.set(k, g); }
    g.conns.push(c); g.apps.add(c.app); if (c.cc) g.ccs.add(c.cc); g.locs.add(c.lat.toFixed(1) + ',' + c.lon.toFixed(1));
    g.up += c.up; g.down += c.down; g.tot += c.totUp + c.totDown; if (c.blocked) g.blocked++;
  }
  return [...m.values()].sort((a, b) => (b.up + b.down) - (a.up + a.down) || b.conns.length - a.conns.length || b.tot - a.tot);
}
const rates = (d, u, tot) => `<span class="num"><span class="dn">${d ? '↓' + fmtR(d) : ''}</span> <span class="up">${u ? '↑' + fmtR(u) : ''}</span>${tot != null ? `<br><span class="dim">Σ ${fmtB(tot)}</span>` : ''}</span>`;
const plural = (n, one, many) => n === 1 ? `1 ${one}` : `${n} ${many}`;
// cuvinte folosite des în panourile hărții
const WD = EN
  ? { conn: 'connection', conns: 'connections', app: 'app', apps: 'apps', cty: 'country', ctys: 'countries', loc: 'location', locs: 'locations', cx: 'conn.', total: 'total', block: 'Block...' }
  : { conn: 'conexiune', conns: 'conexiuni', app: 'aplicație', apps: 'aplicații', cty: 'țară', ctys: 'țări', loc: 'locație', locs: 'locații', cx: 'conex.', total: 'total', block: 'Blochează...' };

function panelHtml(nodes) {
  const all = nodes.flatMap(n => n.conns);
  const tD = all.reduce((s, c) => s + c.down, 0), tU = all.reduce((s, c) => s + c.up, 0);
  const head = (title, sub) => `<h3>${title}</h3><div class="muted" style="margin-bottom:8px">${sub} · <span class="dn">↓ ${fmtR(tD)}</span> <span class="up">↑ ${fmtR(tU)}</span></div>`;
  const appNm = k => esc(S.appMap.get(k)?.name || k);
  switch (S.mapPanel) {
    case 'conns': {
      const list = all.slice().sort((a, b) => (b.up + b.down) - (a.up + a.down) || (b.totUp + b.totDown) - (a.totUp + a.totDown));
      return head(tr('Conexiuni pe hartă', 'Connections on the map'), plural(list.length, WD.conn, WD.conns)) + list.slice(0, 250).map(c =>
        `<div class="mp-row ${c.blocked ? 'blocked' : ''}" data-act="goloc" data-loc="${c.lat.toFixed(1)},${c.lon.toFixed(1)}" data-key="${esc(c.app)}" title="${tr('Click: arată locația pe hartă', 'Click: show the location on the map')}">
          <img class="app-ico sm" src="${icon(c.app)}">
          <div class="nm"><div><b>${appNm(c.app)}</b> <span class="dim">→</span> <span title="${esc(c.host || '')}">${esc(c.host || c.ip)}</span><span class="dim">:${c.rport}</span></div>
          <div class="sub">${ccBadge(c.cc)} ${esc(c.city || ccName(c.cc))} · ${c.proto} · ${WD.total} ${fmtB(c.totUp + c.totDown)}</div></div>
          ${rates(c.down, c.up)}<button class="icon-btn" data-act="blockDest" data-id="${esc(c.id)}" title="${WD.block}">${SVG.ban}</button></div>`).join('') +
        (list.length > 250 ? `<div class="dim" style="padding:6px">+${list.length - 250} ${tr('conexiuni (vezi pagina Conexiuni)', 'connections (see the Connections page)')}</div>` : '');
    }
    case 'apps': {
      const list = aggregate(all, c => c.app);
      return head(tr('Aplicații pe hartă', 'Apps on the map'), plural(list.length, WD.app, WD.apps)) + list.map(g => {
        const a = S.appMap.get(g.key) || { key: g.key, name: g.key, canBlock: false };
        const ccs = [...g.ccs].filter(c => c !== 'LAN');
        return `<div class="mp-row ${a.blocked ? 'blocked' : ''}" data-act="goapp" data-key="${esc(g.key)}" title="${tr('Click: arată doar această aplicație pe hartă', 'Click: show only this app on the map')}">
          <img class="app-ico" src="${icon(g.key)}">
          <div class="nm"><div><b>${esc(a.name)}</b></div>
          <div class="sub">${g.conns.length} ${WD.cx} · ${plural(ccs.length, WD.cty, WD.ctys)}</div>
          <div class="app-strip">${ccs.slice(0, 8).map(ccBadge).join('')}${ccs.length > 8 ? `<span class="dim">+${ccs.length - 8}</span>` : ''}</div></div>
          ${rates(g.down, g.up)}${switchHtml(a)}</div>`;
      }).join('');
    }
    case 'countries': {
      const list = aggregate(all, c => c.cc || '??');
      return head(tr('Țări', 'Countries'), plural(list.length, WD.cty, WD.ctys)) + list.map(g =>
        `<div class="mp-row ${g.blocked === g.conns.length ? 'blocked' : ''}" data-act="gocc" data-cc="${esc(g.key)}" title="${tr('Click: detalii țară', 'Click: country details')}">
          <span class="cc-big">${ccBadge(g.key)}</span>
          <div class="nm"><div><b>${esc(ccName(g.key) || g.key)}</b></div>
          <div class="sub">${g.conns.length} ${WD.cx} · ${plural(g.apps.size, WD.app, WD.apps)} · ${plural(g.locs.size, WD.loc, WD.locs)}</div>
          <div class="app-strip">${[...g.apps].slice(0, 10).map(k => `<img class="app-ico xs" src="${icon(k)}" title="${appNm(k)}">`).join('')}</div></div>
          ${rates(g.down, g.up, g.tot)}</div>`).join('');
    }
    case 'locs': {
      const list = nodes.slice().sort((a, b) => b.rate - a.rate || b.conns.length - a.conns.length || b.tot - a.tot);
      return head(tr('Locații', 'Locations'), plural(list.length, WD.loc, WD.locs)) + list.map(n =>
        `<div class="mp-row ${n.cls === 'blocked' ? 'blocked' : ''}" data-act="goloc" data-loc="${n.key}" title="${tr('Click: detalii locație', 'Click: location details')}">
          <span class="cc-big">${ccBadge(n.cc)}</span>
          <div class="nm"><div><b>${esc(n.city || ccName(n.cc))}</b>${n.city ? ` <span class="dim">${esc(ccName(n.cc))}</span>` : ''}</div>
          <div class="sub">${n.conns.length} ${WD.cx} · ${plural(n.apps.size, WD.app, WD.apps)}</div>
          <div class="app-strip">${[...n.apps].slice(0, 10).map(k => `<img class="app-ico xs" src="${icon(k)}" title="${appNm(k)}">`).join('')}</div></div>
          ${rates(n.down, n.up, n.tot)}</div>`).join('');
    }
  }
  return null;
}

/** Bloc „aplicații + conexiuni” comun pentru detaliile de locație și țară. */
function appsBlock(conns, perApp = 8) {
  const byApp = new Map();
  for (const c of conns) { if (!byApp.has(c.app)) byApp.set(c.app, []); byApp.get(c.app).push(c); }
  return [...byApp.entries()].sort((x, y) => y[1].reduce((s, c) => s + c.up + c.down, 0) - x[1].reduce((s, c) => s + c.up + c.down, 0)).map(([k, cs]) => {
    const a = S.appMap.get(k) || { key: k, name: k, canBlock: false };
    return `<div class="md-app" data-key="${esc(k)}"><div class="md-app-h"><img class="app-ico" src="${icon(k)}"><span class="nm" title="${esc(a.path || '')}">${esc(a.name)}</span>${switchHtml(a)}</div>
      ${cs.slice(0, perApp).map(c => `<div class="md-conn"><span class="host" style="max-width:190px" title="${esc(c.host || '')}">${esc(c.host || c.ip)}:${c.rport}</span><span class="num"><span class="dn">${c.down ? fmtR(c.down) : ''}</span> <span class="up">${c.up ? fmtR(c.up) : ''}</span> <button class="icon-btn" data-act="blockDest" data-id="${esc(c.id)}" title="${WD.block}">${SVG.ban}</button></span></div>`).join('')}
      ${cs.length > perApp ? `<div class="md-conn dim">+${cs.length - perApp} ${WD.conns}</div>` : ''}</div>`;
  }).join('');
}

function countryHtml(nodes) {
  const cc = S.mapCountry;
  const ns = nodes.filter(n => (n.cc || '??') === cc);
  const conns = ns.flatMap(n => n.conns);
  if (!conns.length) return `<h3>${ccBadge(cc)} ${esc(ccName(cc))}</h3><div class="dim">${tr('Nu mai există conexiuni active către această țară.', 'There are no more active connections to this country.')}</div>`;
  const d = conns.reduce((s, c) => s + c.down, 0), u = conns.reduce((s, c) => s + c.up, 0), tot = conns.reduce((s, c) => s + c.totUp + c.totDown, 0);
  const apps = new Set(conns.map(c => c.app));
  return `<h3>${ccBadge(cc)} ${esc(ccName(cc) || cc)}</h3>
    <div class="muted" style="margin-bottom:8px">${plural(conns.length, WD.conn, WD.conns)} · ${plural(apps.size, WD.app, WD.apps)} · ${WD.total} ${fmtB(tot)} · <span class="dn">↓ ${fmtR(d)}</span> <span class="up">↑ ${fmtR(u)}</span></div>
    ${cc !== 'LAN' && cc !== '??' ? `<button class="btn sm danger" data-act="blockCountry" data-cc="${esc(cc)}" style="margin-bottom:8px">${tr('Regulă: blochează', 'Rule: block')} ${esc(ccName(cc))}...</button>` : ''}
    ${ns.length > 1 ? `<div class="md-sec">${tr('Locații', 'Locations')}</div>` + ns.sort((a, b) => b.rate - a.rate || b.conns.length - a.conns.length).map(n =>
      `<div class="mp-row slim" data-act="goloc" data-loc="${n.key}"><div class="nm"><div><b>${esc(n.city || ccName(n.cc))}</b> <span class="dim">${plural(n.conns.length, WD.conn, WD.conns)}</span></div></div>${rates(n.down, n.up)}</div>`).join('') : ''}
    <div class="md-sec">${tr('Aplicații', 'Apps')}</div>${appsBlock(conns, 6)}`;
}

function locHtml(nodes) {
  const n = nodes.find(x => x.key === S.mapSel);
  if (!n) return `<h3>${tr('Locație', 'Location')}</h3><div class="dim">${tr('Conexiunile de aici s-au închis.', 'The connections here have closed.')}</div>`;
  return `<h3>${ccBadge(n.cc)} ${esc(n.city || ccName(n.cc))}</h3>
    <div class="muted" style="margin-bottom:8px">${esc(ccName(n.cc))} · ${plural(n.conns.length, WD.conn, WD.conns)} · ${WD.total} ${fmtB(n.tot)} · <span class="dn">↓ ${fmtR(n.down)}</span> <span class="up">↑ ${fmtR(n.up)}</span></div>
    ${n.cc && n.cc !== 'LAN' ? `<button class="btn sm danger" data-act="blockCountry" data-cc="${n.cc}" style="margin-bottom:6px">${tr('Regulă: blochează', 'Rule: block')} ${esc(ccName(n.cc))}...</button>` : ''}
    ${appsBlock(n.conns)}`;
}

function showTip(e, d) {
  const tip = $('#mapTip');
  const byApp = new Map();
  for (const c of d.conns) { const a = byApp.get(c.app) || { n: 0, up: 0, down: 0, hosts: new Set() }; a.n++; a.up += c.up; a.down += c.down; a.hosts.add(c.host || c.ip); byApp.set(c.app, a); }
  const rows = [...byApp.entries()].sort((x, y) => (y[1].up + y[1].down) - (x[1].up + x[1].down)).slice(0, 7).map(([k, a]) =>
    `<div class="row"><span style="display:flex;gap:6px;align-items:center;min-width:0"><img class="app-ico sm" src="${icon(k)}"><span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:150px">${esc(S.appMap.get(k)?.name || k)}</span></span><span class="num"><span class="dn">${a.down ? fmtR(a.down) : ''}</span> <span class="up">${a.up ? fmtR(a.up) : ''}</span> <span class="dim">${a.n}×</span></span></div>
     <div class="dim" style="font-size:11px;margin:-1px 0 3px 24px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc([...a.hosts].slice(0, 3).join(', '))}</div>`).join('');
  tip.innerHTML = `<h4>${ccBadge(d.cc)} ${esc(d.city || '')}${d.city ? ', ' : ''}${esc(ccName(d.cc))}</h4>
    <div class="muted" style="margin-bottom:6px">${plural(d.conns.length, WD.conn, WD.conns)} · ${plural(d.apps.size, WD.app, WD.apps)} · ${WD.total} ${fmtB(d.tot)}</div>${rows}
    <div class="dim" style="margin-top:4px">${tr('Click pentru detalii și blocare', 'Click for details and blocking')}</div>`;
  const r = $('#mapWrap').getBoundingClientRect();
  let x = e.clientX - r.left + 14, y = e.clientY - r.top + 14;
  if (x + 340 > r.width - 340) x = e.clientX - r.left - 354;
  if (y + tip.offsetHeight > r.height) y = r.height - tip.offsetHeight - 10;
  tip.style.left = x + 'px'; tip.style.top = y + 'px'; tip.classList.add('show');
}
function hideTip() { $('#mapTip').classList.remove('show'); }

function renderMapDetail(nodes) {
  const box = $('#mapDetail');
  if (!S.mapSel && !S.mapCountry && !S.mapPanel) { box.classList.add('hidden'); box.dataset.k = ''; return; }
  nodes = nodes || mapNodes();
  const body = S.mapSel ? locHtml(nodes) : S.mapCountry ? countryHtml(nodes) : panelHtml(nodes);
  const canBack = !!(S.mapSel && (S.mapCountry || S.mapPanel)) || !!(!S.mapSel && S.mapCountry && S.mapPanel);
  const html = `<div class="md-top">${canBack ? `<button class="btn ghost sm" data-act="back">${tr('← Înapoi', '← Back')}</button>` : '<span></span>'}<button class="icon-btn" data-act="close" title="${tr('Închide', 'Close')}">${SVG.x}</button></div>${body}`;
  // păstrăm poziția de scroll la actualizările live (o resetăm doar când se schimbă panoul)
  const key = `${S.mapSel}|${S.mapCountry}|${S.mapPanel}`;
  const st = box.scrollTop;
  if (box._h !== html) { box.innerHTML = html; box._h = html; }
  box.classList.remove('hidden');
  if (box.dataset.k === key) box.scrollTop = st; else { box.scrollTop = 0; box.dataset.k = key; }
}
$('#mapDetail').addEventListener('click', e => {
  // click pe eticheta switch-ului: lăsăm input-ul să trateze evenimentul
  if (e.target.closest('.sw') && !e.target.matches('input')) return;
  const t = e.target.closest('[data-act]'); if (!t) return;
  const nodes = () => mapNodes();
  switch (t.dataset.act) {
    case 'close': S.mapSel = null; S.mapCountry = null; S.mapPanel = null; break;
    case 'back': if (S.mapSel) S.mapSel = null; else S.mapCountry = null; break;
    case 'goloc': {
      S.mapSel = t.dataset.loc;
      const n = nodes().find(x => x.key === S.mapSel); if (n) setTimeout(() => zoomToNodes([n]), 20);
      break;
    }
    case 'gocc': {
      S.mapCountry = t.dataset.cc; S.mapSel = null;
      const ns = nodes().filter(n => (n.cc || '??') === S.mapCountry); setTimeout(() => zoomToNodes(ns), 20);
      break;
    }
    case 'goapp': S.mapApp = S.mapApp === t.dataset.key ? null : t.dataset.key; break;
    case 'blockCountry': Modal.rule({ target: 'country', value: t.dataset.cc, direction: 'out' }); return;
    default: tableClick(e); return;
  }
  renderMapDetail(); schedule();
});

function renderMapSide() {
  const q = $('#mapSearch').value.trim().toLowerCase();
  let list = S.apps.filter(a => a.key !== 'idle' && (a.conns > 0 || a.blocked));
  if (q) list = list.filter(a => (a.name + ' ' + a.path).toLowerCase().includes(q));
  list.sort((a, b) => (b.up + b.down) - (a.up + a.down) || b.conns - a.conns || a.name.localeCompare(b.name));
  $('#mapApps').innerHTML = list.map(a => `<div class="ms-item ${a.blocked ? 'blocked' : ''} ${S.mapApp === a.key ? 'sel' : ''}" data-key="${esc(a.key)}">
    <img class="app-ico" src="${icon(a.key)}"><div class="nm"><div title="${esc(a.path)}">${esc(a.name)}</div>
    <div>${a.blocked ? `<span style="color:#fca5a5">${tr('blocat', 'blocked')}</span> · ` : ''}${a.conns} ${WD.cx} ${a.down ? '· <span class="dn">↓' + fmtR(a.down) + '</span>' : ''} ${a.up ? '<span class="up">↑' + fmtR(a.up) + '</span>' : ''}</div></div>
    ${switchHtml(a)}</div>`).join('') || `<div class="empty">${tr('Nicio aplicație conectată.', 'No connected apps.')}</div>`;
}
$('#mapSearch').oninput = schedule;
$('#mapApps').addEventListener('click', e => {
  const t = e.target.closest('[data-act]');
  if (t) { tableClick(e); return; }
  if (e.target.closest('.sw')) return;
  const it = e.target.closest('.ms-item'); if (!it) return;
  S.mapApp = S.mapApp === it.dataset.key ? null : it.dataset.key; S.mapSel = null; renderMapDetail(); schedule();
});

// ---------------------------------------------------------------- history
segInit('#histRange', v => { S.histHours = +v; loadHistory(); });
$('#histApp').onchange = e => { S.histApp = e.target.value; loadHistory(); };
$('#histRefresh').onclick = loadHistory;
function loadHistory() { send({ cmd: 'history', hours: S.histHours, app: S.histApp || '' }); }
function fillHistApps() {
  const sel = $('#histApp');
  const items = new Map();
  for (const a of S.appList) items.set(a.key, a.name);
  for (const a of S.apps) if (a.canBlock) items.set(a.key, a.name);
  if (S.history) for (const a of S.history.apps) items.set(a.key, a.name);
  if (S.histApp && !items.has(S.histApp)) items.set(S.histApp, S.appMap.get(S.histApp)?.name || S.histApp);
  sel.innerHTML = `<option value="">${tr('Toate aplicațiile', 'All apps')}</option>` + [...items.entries()].sort((a, b) => a[1].localeCompare(b[1])).map(([k, n]) => `<option value="${esc(k)}">${esc(n)}</option>`).join('');
  sel.value = S.histApp;
}
function hbars(items, colorize = true) {
  const max = d3.max(items, i => i.up + i.down) || 1;
  return items.map(i => `<div class="hbar">${i.ic ? `<img class="app-ico sm ic" src="${i.ic}">` : `<span class="ic">${i.badge || ''}</span>`}
    <div class="lbl" title="${esc(i.title || i.label)}">${i.label}</div>
    <div class="val">${fmtB(i.up + i.down)}</div>
    <div class="track"><i style="width:${100 * i.down / max}%;background:var(--dn)"></i><i style="width:${100 * i.up / max}%;background:var(--up)"></i></div></div>`).join('') || `<div class="empty">${tr('Fără date încă.', 'No data yet.')}</div>`;
}
function renderHistory() {
  const h = S.history; if (!h) return;
  fillHistApps();
  const totD = d3.sum(h.timeline, d => d.down), totU = d3.sum(h.timeline, d => d.up);
  $('#histKpis').innerHTML = `<div class="kpi"><span>${tr('Download total', 'Total download')}</span><b class="dn">${fmtB(totD)}</b></div>
    <div class="kpi"><span>${tr('Upload total', 'Total upload')}</span><b class="up">${fmtB(totU)}</b></div>
    <div class="kpi"><span>${tr('Aplicații', 'Apps')}</span><b>${h.apps.length}</b></div>
    <div class="kpi"><span>${tr('Țări', 'Countries')}</span><b>${h.countries.filter(c => c.cc && c.cc !== 'LAN').length}</b></div>
    <div class="kpi"><span>${tr('Destinații', 'Destinations')}</span><b>${h.hosts.length}${h.hosts.length >= 60 ? '+' : ''}</b></div>`;
  drawHistChart(h);
  $('#histApps').innerHTML = hbars(h.apps.slice(0, 12).map(a => ({ ic: a.icon || icon(a.key), label: esc(a.name), title: a.path, up: a.up, down: a.down })));
  $('#histCountries').innerHTML = hbars(h.countries.slice(0, 12).map(c => ({ badge: ccBadge(c.cc || '?'), label: esc(ccName(c.cc) || tr('Necunoscut', 'Unknown')) + ` <span class="dim">· ${plural(c.apps, WD.app, WD.apps)}</span>`, up: c.up, down: c.down })));
  $('#histHosts').innerHTML = hbars(h.hosts.map(x => ({ badge: ccBadge(x.cc), label: `<span class="mono">${esc(x.host)}</span> <span class="dim">· ${plural(x.apps, WD.app, WD.apps)}</span>`, title: x.host, up: x.up, down: x.down })));
  const EV = EN
    ? { new_app: 'New app', block: 'Blocked', unblock: 'Unblocked', panic_on: 'Panic', panic_off: 'Internet restored' }
    : { new_app: 'Aplicație nouă', block: 'Blocat', unblock: 'Deblocat', panic_on: 'Panică', panic_off: 'Internet restabilit' };
  $('#histEvents').innerHTML = h.events.map(e => `<div class="ev"><time>${fmtTime(e.ts)}</time><span class="k ${e.kind}">${EV[e.kind] || e.kind}</span><span>${e.app ? `<b>${esc(e.appName || e.app.split('\\').pop())}</b> ` : ''}<span class="muted">${esc(e.detail || '')}</span></span></div>`).join('') || `<div class="empty">${tr('Niciun eveniment în această perioadă.', 'No events in this period.')}</div>`;
  $('#histPorts').innerHTML = h.ports.map(p => `<div class="port"><b>${p.port}</b> ${p.proto}${PORTS[p.port] ? ' · ' + PORTS[p.port] : ''} <span class="muted">· ${fmtB(p.bytes)}</span></div>`).join('') || `<div class="dim">${tr('Fără date încă.', 'No data yet.')}</div>`;
}
function drawHistChart(h) {
  const svg = d3.select('#histChart'); svg.selectAll('*').remove();
  const W = svg.node().clientWidth || 800, H = 240, m = { l: 62, r: 14, t: 10, b: 24 };
  const step = h.bucket, now = Date.now() / 1000, from = Math.floor((now - h.hours * 3600) / step) * step;
  const map = new Map(h.timeline.map(d => [d.t, d]));
  const data = []; for (let t = from; t <= now; t += step) { const d = map.get(t); data.push({ t: new Date(t * 1000), up: d ? d.up : 0, down: d ? d.down : 0 }); }
  if (data.length < 2) return;
  const x = d3.scaleTime().domain([data[0].t, data[data.length - 1].t]).range([m.l, W - m.r]);
  const maxD = d3.max(data, d => d.down) || 1024, maxU = d3.max(data, d => d.up) || 1024;
  const mid = m.t + (H - m.b - m.t) * 0.66;
  const yD = d3.scaleLinear().domain([0, maxD]).range([mid, m.t]).nice();
  const yU = d3.scaleLinear().domain([0, maxU]).range([mid, H - m.b]).nice();
  const y = v => v >= 0 ? yD(v) : yU(-v);
  const tD = yD.ticks(4).filter(v => v > 0), tU = yU.ticks(2).filter(v => v > 0);
  const gl = svg.append('g').attr('class', 'gridline');
  [...tD.map(v => yD(v)), ...tU.map(v => yU(v))].forEach(py => gl.append('line').attr('x1', m.l).attr('x2', W - m.r).attr('y1', py).attr('y2', py));
  svg.append('g').attr('class', 'axis').attr('transform', `translate(${m.l},0)`).call(d3.axisLeft(yD).tickValues(tD).tickFormat(v => fmtB(v)).tickSizeOuter(0));
  svg.append('g').attr('class', 'axis').attr('transform', `translate(${m.l},0)`).call(d3.axisLeft(yU).tickValues(tU).tickFormat(v => fmtB(v)).tickSizeOuter(0));
  svg.append('g').attr('class', 'axis').attr('transform', `translate(0,${H - m.b})`).call(d3.axisBottom(x).ticks(Math.max(3, Math.floor(W / 110))).tickSizeOuter(0)
    .tickFormat(d => h.hours > 48 ? d3.timeFormat('%d.%m')(d) : d3.timeFormat('%H:%M')(d)));
  svg.append('line').attr('x1', m.l).attr('x2', W - m.r).attr('y1', y(0)).attr('y2', y(0)).attr('stroke', '#27365c');
  const defs = svg.append('defs');
  defs.html(`<linearGradient id="hgd" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#22d3ee" stop-opacity=".55"/><stop offset="1" stop-color="#22d3ee" stop-opacity=".05"/></linearGradient>
    <linearGradient id="hgu" x1="0" y1="1" x2="0" y2="0"><stop offset="0" stop-color="#f472b6" stop-opacity=".55"/><stop offset="1" stop-color="#f472b6" stop-opacity=".05"/></linearGradient>`);
  const curve = d3.curveMonotoneX;
  svg.append('path').attr('fill', 'url(#hgd)').attr('d', d3.area().x(d => x(d.t)).y0(y(0)).y1(d => y(d.down)).curve(curve)(data));
  svg.append('path').attr('fill', 'none').attr('stroke', '#22d3ee').attr('stroke-width', 1.5).attr('d', d3.line().x(d => x(d.t)).y(d => y(d.down)).curve(curve)(data));
  svg.append('path').attr('fill', 'url(#hgu)').attr('d', d3.area().x(d => x(d.t)).y0(y(0)).y1(d => y(-d.up)).curve(curve)(data));
  svg.append('path').attr('fill', 'none').attr('stroke', '#f472b6').attr('stroke-width', 1.5).attr('d', d3.line().x(d => x(d.t)).y(d => y(-d.up)).curve(curve)(data));
  const vl = svg.append('line').attr('y1', m.t).attr('y2', H - m.b).attr('stroke', '#7f8db3').attr('stroke-dasharray', '3 3').style('opacity', 0);
  const lbl = svg.append('text').attr('fill', '#dfe7ff').attr('font-size', 11.5).attr('y', m.t + 12).style('opacity', 0);
  const bis = d3.bisector(d => d.t).center;
  svg.append('rect').attr('x', m.l).attr('y', m.t).attr('width', W - m.l - m.r).attr('height', H - m.t - m.b).attr('fill', 'transparent')
    .on('mousemove', e => {
      const i = bis(data, x.invert(d3.pointer(e)[0])); const d = data[i]; const px = x(d.t);
      vl.attr('x1', px).attr('x2', px).style('opacity', 1);
      const t = (h.hours > 48 ? d3.timeFormat('%d.%m %H:%M') : d3.timeFormat('%H:%M'))(d.t);
      lbl.text(`${t}   ↓ ${fmtB(d.down)}   ↑ ${fmtB(d.up)}`).attr('x', Math.min(px + 8, W - 240)).style('opacity', 1);
    }).on('mouseleave', () => { vl.style('opacity', 0); lbl.style('opacity', 0); });
}
window.addEventListener('resize', () => { if (S.view === 'history' && S.history) drawHistChart(S.history); });

// ---------------------------------------------------------------- rules
$('#newRule').onclick = () => Modal.rule({});
$('#ruleSearch').oninput = renderRules;
const DIR = EN ? { out: 'outbound', in: 'inbound', both: 'outbound + inbound' } : { out: 'ieșire', in: 'intrare', both: 'ieșire + intrare' };
function ruleText(r) {
  const tgt = (EN
    ? { any: 'anywhere', ip: `IP ${r.value}`, domain: `domain ${r.value} (+ subdomains)`, country: `country ${ccName(r.value)}` }
    : { any: 'oriunde', ip: `IP ${r.value}`, domain: `domeniul ${r.value} (+ subdomenii)`, country: `țara ${ccName(r.value)}` })[r.target] || r.target;
  const ports = r.ports ? ` · port ${r.ports}` : '';
  const proto = r.protocol && r.protocol !== 'any' ? ` · ${r.protocol.toUpperCase()}` : '';
  return { tgt, extra: `${DIR[r.direction] || r.direction}${proto}${ports}` };
}
function renderRules() {
  const q = ($('#ruleSearch').value || '').toLowerCase();
  let list = S.rules.slice().sort((a, b) => (a.kind === b.kind ? 0 : a.kind === 'app' ? -1 : 1) || new Date(b.created) - new Date(a.created));
  if (q) list = list.filter(r => JSON.stringify(r).toLowerCase().includes(q));
  $('#rulesList').innerHTML = list.map(r => {
    const t = ruleText(r);
    const appIco = r.appPath ? icon(r.appPath.toLowerCase()) : null;
    const title = r.kind === 'app'
      ? `<span class="pill red">${tr('Internet tăiat complet', 'Internet fully cut off')}</span> ${esc(r.appName || r.appPath)}`
      : `<span class="pill red">${tr('Blochează', 'Block')}</span> ${esc(r.appName || tr('Toate aplicațiile', 'All apps'))} <span class="muted">→</span> ${esc(t.tgt)}`;
    const sub = r.kind === 'app' ? `${esc(r.appPath)}` : `${esc(t.extra)}${r.appPath ? ' · ' + esc(r.appPath) : ''}${r.target === 'domain' ? ` · ${r.resolvedIps?.length || 0} ${tr('IP-uri cunoscute', 'known IPs')}` : ''}`;
    return `<div class="rule ${r.enabled ? '' : 'off'}" data-id="${r.id}">
      ${appIco ? `<img class="app-ico" src="${appIco}">` : `<span class="app-ico" style="display:grid;place-items:center;color:var(--muted)">${SVG.ban.replace('<svg', '<svg width="20" height="20" fill="none" stroke="currentColor" stroke-width="2"')}</span>`}
      <div class="rtxt"><div class="rtitle">${title}</div><div class="rsub" title="${esc(sub)}">${sub}${r.note ? ' · ' + esc(r.note) : ''} · ${r.fwCount || 0} ${tr('reguli firewall', 'firewall filters')}</div>${r.error ? `<div class="err">⚠ ${esc(r.error)}</div>` : ''}</div>
      <label class="sw neutral" title="${tr('Activă / inactivă', 'Enabled / disabled')}"><input type="checkbox" data-act="en" ${r.enabled ? 'checked' : ''}><span></span></label>
      ${r.kind === 'custom' ? `<button class="icon-btn" data-act="edit" title="${tr('Editează', 'Edit')}">${SVG.edit}</button>` : ''}
      <button class="icon-btn" data-act="del" title="${tr('Șterge', 'Delete')}">${SVG.trash}</button></div>`;
  }).join('') || `<div class="empty">${tr('Nicio regulă încă.<br><br>Folosește sliderul din Aplicații ca să tai internetul unei aplicații, sau creează o regulă detaliată (pe domeniu, IP, țară, port).', 'No rules yet.<br><br>Use the switch on the Apps page to cut an app off the internet, or create a detailed rule (by domain, IP, country, port).')}</div>`;
}
$('#rulesList').addEventListener('click', e => {
  const t = e.target.closest('[data-act]'); if (!t) return;
  const id = t.closest('.rule').dataset.id; const r = S.rules.find(x => x.id === id);
  if (t.dataset.act === 'en') send({ cmd: 'toggleRule', id, enabled: t.checked });
  if (t.dataset.act === 'edit') Modal.rule(r);
  if (t.dataset.act === 'del') confirmBox(tr('Ștergi regula?', 'Delete this rule?'), r.kind === 'app' ? tr(`Aplicația „${r.appName}” va avea din nou acces la internet.`, `"${r.appName}" will have internet access again.`) : tr('Regula va fi eliminată, iar blocarea ei încetează imediat.', 'The rule is removed and its blocking stops immediately.'), tr('Șterge', 'Delete'), () => send({ cmd: 'deleteRule', id }), true);
});

// ---------------------------------------------------------------- modal
const Modal = {
  open(html) { $('#modalBox').innerHTML = html; $('#modal').classList.remove('hidden'); $('#modalBox').querySelectorAll('[data-close]').forEach(b => b.onclick = () => this.close()); },
  close() { $('#modal').classList.add('hidden'); this.onAppList = this.onBrowsed = null; this.pendingSave = false; },
  rule(init) {
    const r = Object.assign({ kind: 'custom', enabled: true, direction: 'out', protocol: 'any', target: 'any', value: '', ports: '', note: '', appPath: null, appName: null }, init || {});
    if (r.kind === 'app') return;
    const isEdit = !!r.id;
    const extraApps = new Map();
    if (r.appPath) extraApps.set(r.appPath, r.appName || r.appPath);
    const appOptions = () => {
      const m = new Map();
      for (const a of S.apps) if (a.canBlock) m.set(a.path, a.name);
      for (const a of S.appList) m.set(a.path, a.name);
      for (const x of S.rules) if (x.appPath) m.set(x.appPath, x.appName || x.appPath);
      for (const [p, n] of extraApps) m.set(p, n);
      return `<option value="">${tr('— Toate aplicațiile —', '— All apps —')}</option>` + [...m.entries()].sort((a, b) => a[1].localeCompare(b[1])).map(([p, n]) => `<option value="${esc(p)}" ${p === r.appPath ? 'selected' : ''}>${esc(n)} — ${esc(p.split('\\').pop())}</option>`).join('') + `<option value="__browse">${tr('📁 Alege alt fișier .exe...', '📁 Choose another .exe file...')}</option>`;
    };
    const countries = COUNTRY_CODES.map(c => [c, ccName(c)]).filter(x => x[1] && x[1] !== x[0]).sort((a, b) => a[1].localeCompare(b[1], LANG));
    this.open(`<h2>${isEdit ? tr('Editează regula', 'Edit rule') : tr('Regulă nouă de blocare', 'New block rule')}</h2>
      <div class="form-row"><label>${tr('Aplicație', 'App')}</label><select id="fApp">${appOptions()}</select><span class="hint">${tr('Lasă „Toate aplicațiile” pentru o regulă globală (ex: blochează un domeniu pentru tot PC-ul).', 'Leave "All apps" for a global rule (e.g. block a domain for the whole PC).')}</span></div>
      <div class="form-row"><label>${tr('Destinație', 'Destination')}</label>
        <div class="seg" id="fTarget"><button data-v="any">${tr('Oriunde', 'Anywhere')}</button><button data-v="domain">${tr('Domeniu', 'Domain')}</button><button data-v="ip">${tr('IP / rețea', 'IP / network')}</button><button data-v="country">${tr('Țară', 'Country')}</button></div>
        <input type="text" id="fValue" placeholder="">
        <select id="fCountry" class="hidden">${countries.map(([c, n]) => `<option value="${c}">${esc(n)} (${c})</option>`).join('')}</select>
        <span class="hint" id="fHint"></span></div>
      <div class="form-row"><label>${tr('Direcție', 'Direction')}</label><div class="seg" id="fDir"><button data-v="out">${tr('Ieșire (spre internet)', 'Outbound (to the internet)')}</button><button data-v="in">${tr('Intrare (spre PC)', 'Inbound (to this PC)')}</button><button data-v="both">${tr('Ambele', 'Both')}</button></div></div>
      <div class="form-row"><label>${tr('Protocol și porturi', 'Protocol and ports')}</label><div class="form-inline"><div class="seg" id="fProto" style="flex:none"><button data-v="any">${tr('Oricare', 'Any')}</button><button data-v="tcp">TCP</button><button data-v="udp">UDP</button></div>
        <input type="text" id="fPorts" placeholder="${tr('toate porturile — sau ex: 443, 80, 6881-6889', 'all ports — or e.g. 443, 80, 6881-6889')}"></div></div>
      <div class="form-row"><label>${tr('Notă (opțional)', 'Note (optional)')}</label><input type="text" id="fNote" placeholder="${tr('ex: fără reclame în aplicație', 'e.g. no in-app ads')}"></div>
      <div class="preview" id="fPreview"></div>
      <div class="modal-actions"><button class="btn ghost" data-close>${tr('Renunță', 'Cancel')}</button><button class="btn primary" id="fSave">${isEdit ? tr('Salvează', 'Save') : tr('Creează regula', 'Create rule')}</button></div>`);
    const segSet = (id, v) => $(id).querySelectorAll('button').forEach(b => b.classList.toggle('on', b.dataset.v === v));
    const HINTS = EN ? {
      any: 'Blocks every destination (combine with ports to block only certain services).',
      domain: 'E.g. facebook.com — all subdomains are blocked too. IPs are learned automatically from DNS.',
      ip: 'E.g. 1.2.3.4 · 10.0.0.0/8 · 1.1.1.1-1.1.1.9 · several separated by commas.',
      country: 'All IP ranges assigned to the country (from the DB-IP database).',
    } : {
      any: 'Blochează orice destinație (combină cu porturi pentru a bloca doar anumite servicii).',
      domain: 'Ex: facebook.com — blochează și toate subdomeniile. IP-urile sunt învățate automat din DNS.',
      ip: 'Ex: 1.2.3.4 · 10.0.0.0/8 · 1.1.1.1-1.1.1.9 · mai multe separate prin virgulă.',
      country: 'Toate intervalele IP alocate țării (din baza DB-IP).',
    };
    const upd = () => {
      segSet('#fTarget', r.target); segSet('#fDir', r.direction); segSet('#fProto', r.protocol);
      $('#fValue').classList.toggle('hidden', r.target === 'any' || r.target === 'country');
      $('#fCountry').classList.toggle('hidden', r.target !== 'country');
      $('#fValue').placeholder = r.target === 'domain' ? tr('ex: facebook.com', 'e.g. facebook.com') : tr('ex: 1.2.3.4 sau 192.168.0.0/16', 'e.g. 1.2.3.4 or 192.168.0.0/16');
      $('#fHint').textContent = HINTS[r.target];
      const appTxt = r.appPath ? (extraApps.get(r.appPath) || $('#fApp').selectedOptions[0]?.textContent.split(' — ')[0]) : tr('toate aplicațiile', 'all apps');
      const tgt = (EN
        ? { any: 'any destination', domain: r.value ? `domain ${r.value} and its subdomains` : 'domain ...', ip: r.value || 'IP ...', country: `country ${ccName(r.value)}` }
        : { any: 'orice destinație', domain: r.value ? `domeniul ${r.value} și subdomeniile lui` : 'domeniul ...', ip: r.value || 'IP ...', country: `țara ${ccName(r.value)}` })[r.target];
      $('#fPreview').innerHTML = `${tr('Blochează', 'Block')} <b>${esc(appTxt)}</b> → <b>${esc(tgt)}</b>, ${DIR[r.direction]}${r.protocol !== 'any' ? ', ' + r.protocol.toUpperCase() : ''}${r.ports ? ', port ' + esc(r.ports) : tr(', toate porturile', ', all ports')}.`;
    };
    $('#fValue').value = r.target === 'country' ? '' : (r.value || '');
    if (r.target === 'country' && r.value) $('#fCountry').value = r.value; else if (r.target === 'country') r.value = $('#fCountry').value;
    $('#fPorts').value = r.ports || ''; $('#fNote').value = r.note || '';
    const segBind = (id, prop) => $(id).addEventListener('click', e => { const b = e.target.closest('button'); if (!b) return; r[prop] = b.dataset.v; if (prop === 'target') { r.value = r.target === 'country' ? $('#fCountry').value : $('#fValue').value.trim(); } upd(); });
    segBind('#fTarget', 'target'); segBind('#fDir', 'direction'); segBind('#fProto', 'protocol');
    $('#fValue').oninput = e => { r.value = e.target.value.trim(); upd(); };
    $('#fCountry').onchange = e => { r.value = e.target.value; upd(); };
    $('#fPorts').oninput = e => { r.ports = e.target.value.trim(); upd(); };
    $('#fApp').onchange = e => {
      if (e.target.value === '__browse') { e.target.value = r.appPath || ''; send({ cmd: 'browseApp' }); return; }
      r.appPath = e.target.value || null; upd();
    };
    this.onBrowsed = m => { extraApps.set(m.path, m.name); r.appPath = m.path; $('#fApp').innerHTML = appOptions(); upd(); };
    this.onAppList = () => { const v = $('#fApp').value; $('#fApp').innerHTML = appOptions(); $('#fApp').value = v; };
    send({ cmd: 'appList' });
    $('#fSave').onclick = () => {
      const opt = $('#fApp').selectedOptions[0];
      const out = {
        id: r.id, kind: 'custom', enabled: r.enabled !== false, appPath: r.appPath || null,
        appName: r.appPath ? (extraApps.get(r.appPath) || opt?.textContent.split(' — ')[0]) : null,
        direction: r.direction, protocol: r.protocol, target: r.target,
        value: r.target === 'any' ? null : (r.target === 'country' ? $('#fCountry').value : $('#fValue').value.trim()),
        ports: $('#fPorts').value.trim() || null, note: $('#fNote').value.trim() || null, created: r.created,
      };
      if (!out.id) delete out.id; if (!out.created) delete out.created;
      this.pendingSave = true;
      send({ cmd: 'saveRule', rule: out });
    };
    upd();
  },
};
$('#modal').addEventListener('mousedown', e => { if (e.target.id === 'modal') Modal.close(); });
document.addEventListener('keydown', e => {
  if (e.key === 'Escape' && !$('#modal').classList.contains('hidden')) Modal.close();
  if (e.ctrlKey && e.key === 'f') { const s = { apps: '#appSearch', conns: '#connSearch', map: '#mapSearch', rules: '#ruleSearch' }[S.view]; if (s) { e.preventDefault(); $(s).focus(); } }
});
function confirmBox(title, text, okText, onOk, danger, onCancel) {
  Modal.open(`<h2>${esc(title)}</h2><p class="muted" style="line-height:1.6">${esc(text)}</p>
    <div class="modal-actions"><button class="btn ghost" id="cCancel">${tr('Renunță', 'Cancel')}</button><button class="btn ${danger ? 'danger' : 'primary'}" id="cOk">${esc(okText)}</button></div>`);
  $('#cOk').onclick = () => { Modal.close(); onOk(); };
  $('#cCancel').onclick = () => { Modal.close(); onCancel && onCancel(); };
}
function toast(text, kind = 'info') {
  const t = document.createElement('div'); t.className = 'toast ' + kind; t.textContent = text;
  $('#toasts').appendChild(t); setTimeout(() => t.remove(), kind === 'error' ? 6500 : 3500);
}

// ---------------------------------------------------------------- settings
function renderSettings() {
  const s = S.settings;
  const row = (key, title, desc) => `<div class="set"><div class="st"><b>${title}</b><span>${desc}</span></div><label class="sw neutral"><input type="checkbox" data-k="${key}" ${s[key] ? 'checked' : ''}><span></span></label></div>`;
  const lang = s.language === 'ro' || s.language === 'en' ? s.language : 'auto';
  $('#settingsForm').innerHTML = `
    <div class="set-group"><h3>${tr('General', 'General')}</h3>
      <div class="set"><div class="st"><b>${tr('Limba', 'Language')}</b><span>${tr('Automat = limba Windows (română sau engleză).', 'Auto = the Windows language (Romanian if Windows is in Romanian, otherwise English).')}</span></div>
        <div class="seg" id="langSel"><button data-v="auto" class="${lang === 'auto' ? 'on' : ''}">${tr('Automat', 'Auto')}</button><button data-v="ro" class="${lang === 'ro' ? 'on' : ''}">Română</button><button data-v="en" class="${lang === 'en' ? 'on' : ''}">English</button></div></div>
      ${row('startWithWindows', tr('Pornește cu Windows', 'Start with Windows'), tr('Rulează automat la logare (cu drepturi de administrator, fără prompt UAC).', 'Runs automatically at sign-in (with administrator rights, no UAC prompt).'))}
      ${row('startMinimized', tr('Pornește minimizat în tray', 'Start minimized to tray'), tr('Fereastra nu se deschide la pornire; FallenSec rulează în fundal.', 'The window does not open at startup; FallenSec runs in the background.'))}
      ${row('minimizeToTray', tr('Minimizarea trimite în tray', 'Minimize to tray'), tr('Butonul de minimizare ascunde fereastra lângă ceas, nu în taskbar.', 'The minimize button hides the window next to the clock instead of the taskbar.'))}
      ${row('closeToTray', tr('Închiderea ferestrei trimite în tray', 'Close to tray'), tr('Monitorizarea și istoricul continuă în fundal.', 'Monitoring and history keep running in the background.'))}
      ${row('notifyNewApps', tr('Notificare la aplicații noi', 'Notify about new apps'), tr('Afișează o notificare când o aplicație iese pe internet pentru prima dată.', 'Shows a notification when an app goes online for the first time.'))}
      ${row('showLan', tr('Arată conexiunile din rețeaua locală', 'Show local network connections'), tr('Include imprimante, NAS, router etc.', 'Includes printers, NAS, router, etc.'))}
    </div>
    <div class="set-group"><h3>${tr('Blocare', 'Blocking')}</h3>
      <div class="set"><div class="st"><b>${tr('Motor de blocare', 'Blocking engine')}</b><span id="blockInfo">${esc(S.status.block?.text || '')}</span></div>
        <div class="seg" id="blockEngine"><button data-v="wfp" class="${s.blockEngine !== 'winfw' ? 'on' : ''}" title="${tr('Windows Filtering Platform – merge și cu Bitdefender, ESET, Kaspersky etc.', 'Windows Filtering Platform – also works with Bitdefender, ESET, Kaspersky, etc.')}">${tr('WFP (recomandat)', 'WFP (recommended)')}</button><button data-v="winfw" class="${s.blockEngine === 'winfw' ? 'on' : ''}" title="${tr('Reguli clasice în Windows Firewall (grupul FallenSec) – necesită Windows Firewall pornit', 'Classic Windows Firewall rules (FallenSec group) – requires Windows Firewall to be on')}">Windows Firewall</button></div></div>
      <div class="set"><div class="st"><b>Windows Firewall</b><span>${tr('Necesar doar dacă motorul de blocare este „Windows Firewall”.', 'Only needed when the blocking engine is "Windows Firewall".')}</span></div><button class="btn sm" id="fwOn">${tr('Pornește pe toate profilurile', 'Turn on for all profiles')}</button></div>
    </div>
    <div class="set-group"><h3>${tr('Hartă', 'Map')}</h3>
      ${row('homeAuto', tr('Detectează automat locația mea', 'Detect my location automatically'), tr('Folosește IP-ul public pentru punctul de plecare de pe hartă.', 'Uses your public IP for the starting point on the map.'))}
      <div class="set ${s.homeAuto ? 'hidden' : ''}" id="homeManual"><div class="st"><b>${tr('Locație manuală', 'Manual location')}</b><span>${tr('Latitudine, longitudine și nume.', 'Latitude, longitude and name.')}</span></div>
        <input type="number" step="0.01" id="hLat" value="${s.homeLat ?? 44.43}" style="width:100px"><input type="number" step="0.01" id="hLon" value="${s.homeLon ?? 26.10}" style="width:100px">
        <input type="text" id="hCity" value="${esc(s.homeCity ?? 'București')}" style="width:140px"></div>
    </div>
    <div class="set-group"><h3>${tr('Istoric și date', 'History and data')}</h3>
      <div class="set"><div class="st"><b>${tr('Păstrează istoricul', 'Keep history for')}</b><span>${tr('Datele mai vechi sunt șterse automat.', 'Older data is deleted automatically.')}</span></div><input type="number" min="1" max="365" id="hDays" value="${s.historyDays ?? 30}" style="width:80px"> <span class="muted">${tr('zile', 'days')}</span></div>
      <div class="set"><div class="st"><b>${tr('Șterge istoricul', 'Clear history')}</b><span>${tr('Elimină traficul și evenimentele salvate.', 'Removes the saved traffic and events.')}</span></div><button class="btn danger sm" id="clearHist">${tr('Șterge', 'Clear')}</button></div>
      <div class="set"><div class="st"><b>${tr('Baza de geolocalizare', 'Geolocation database')}</b><span>${esc(S.status.geo || '')}</span></div><button class="btn sm" id="geoUpd">${tr('Actualizează acum', 'Update now')}</button></div>
    </div>
    <div class="set-group"><h3>${tr('Aplicație', 'Application')}</h3>
      <div class="set"><div class="st"><b>${tr('Ieșire din FallenSec', 'Exit FallenSec')}</b><span>${tr('Regulile de blocare rămân active chiar și după ieșire și după repornirea PC-ului.', 'Block rules stay active even after you exit and after a reboot.')}</span></div><button class="btn sm" id="exitApp">${tr('Ieșire', 'Exit')}</button></div>
    </div>`;
  const save = () => send({ cmd: 'saveSettings', settings: S.settings });
  segInit('#langSel', v => { S.settings.language = v; send({ cmd: 'setLanguage', language: v }); });
  $$('#settingsForm input[data-k]').forEach(i => i.onchange = () => { S.settings[i.dataset.k] = i.checked; if (i.dataset.k === 'homeAuto') $('#homeManual').classList.toggle('hidden', i.checked); save(); });
  ['hLat', 'hLon', 'hCity'].forEach(id => $('#' + id).onchange = () => { S.settings.homeLat = +$('#hLat').value; S.settings.homeLon = +$('#hLon').value; S.settings.homeCity = $('#hCity').value; save(); });
  $('#hDays').onchange = e => { S.settings.historyDays = Math.max(1, Math.min(365, +e.target.value || 30)); save(); };
  $('#clearHist').onclick = () => confirmBox(tr('Ștergi tot istoricul?', 'Clear all history?'), tr('Traficul și evenimentele salvate vor fi eliminate definitiv.', 'The saved traffic and events will be permanently removed.'), tr('Șterge', 'Clear'), () => send({ cmd: 'clearHistory' }), true);
  $('#geoUpd').onclick = () => send({ cmd: 'geoUpdate' });
  $('#fwOn').onclick = () => send({ cmd: 'enableFirewall' });
  segInit('#blockEngine', v => { S.settings.blockEngine = v; send({ cmd: 'blockEngine', engine: v }); });
  $('#exitApp').onclick = () => confirmBox(tr('Ieși din FallenSec?', 'Exit FallenSec?'), tr('Monitorizarea se oprește. Aplicațiile blocate rămân blocate.', 'Monitoring stops. Blocked apps stay blocked.'), tr('Ieșire', 'Exit'), () => send({ cmd: 'exit' }));
}

// ---------------------------------------------------------------- about
function renderAbout() {
  $('#about').innerHTML = `<img src="logo.png" alt=""><h2>FallenSec</h2>
    <div class="muted">${tr('Versiunea', 'Version')} ${esc(S.version)} · ${tr('Firewall și monitor de rețea pentru Windows', 'Firewall and network monitor for Windows')}</div>
    <div class="muted" style="margin-top:4px">© 2026 Fallen · <a href="https://github.com/Fallemo/FallenSec" target="_blank">github.com/Fallemo/FallenSec</a></div>
    <div class="cl">${CHANGELOG.map(c => `<h4>${tr('Versiunea', 'Version')} ${c.v} <span class="dim" style="font-weight:400">· ${c.d}</span></h4><ul>${c.items.map(i => `<li>${esc(tr(i[0], i[1]))}</li>`).join('')}</ul>`).join('')}</div>
    <div class="credits">
      <a href="https://db-ip.com" target="_blank">IP Geolocation by DB-IP</a> (CC BY 4.0) ·
      ${tr('Hartă', 'Map')}: Natural Earth / world-atlas · ${tr('Grafice', 'Charts')}: d3.js (ISC)<br>
      ${tr('Blocarea folosește direct Windows Filtering Platform (sau, opțional, Windows Firewall). Traficul este măsurat prin Event Tracing for Windows.', 'Blocking uses the Windows Filtering Platform directly (or, optionally, Windows Firewall). Traffic is measured through Event Tracing for Windows.')}
    </div>`;
}

// ---------------------------------------------------------------- start
Map2.init();
send({ cmd: 'ready' });
if (location.hash.length > 1 && TITLES[location.hash.slice(1)]) go(location.hash.slice(1));
setInterval(() => { if (S.view === 'history') loadHistory(); }, 60000);

// ---------------------------------------------------------------- demo (doar în browser, fără aplicație)
function demoHandle(o) {
  if (o.cmd === 'ready') {
    onMessage({ type: 'init', version: '1.2', settings: { closeToTray: true, minimizeToTray: true, notifyNewApps: true, showLan: true, homeAuto: true, historyDays: 30 }, rules: [
      { id: 'a1', kind: 'app', enabled: true, appPath: 'C:\\Program Files\\Telemetry\\tele.exe', appName: 'Telemetry Agent', direction: 'both', target: 'any', fwCount: 2 },
      { id: 'b2', kind: 'custom', enabled: true, appPath: null, appName: null, direction: 'out', protocol: 'any', target: 'domain', value: 'doubleclick.net', resolvedIps: ['1.1.1.1', '2.2.2.2'], fwCount: 1 },
      { id: 'c3', kind: 'custom', enabled: false, appPath: 'C:\\Games\\game.exe', appName: 'Game', direction: 'out', protocol: 'any', target: 'country', value: 'CN', fwCount: 0 }] });
    const cities = [['US', 'Ashburn', 39.04, -77.49], ['DE', 'Frankfurt', 50.11, 8.68], ['NL', 'Amsterdam', 52.37, 4.9], ['IE', 'Dublin', 53.35, -6.26], ['US', 'San Jose', 37.34, -121.89], ['SG', 'Singapore', 1.35, 103.82], ['JP', 'Tokyo', 35.68, 139.69], ['GB', 'London', 51.51, -0.13], ['FR', 'Paris', 48.86, 2.35], ['RO', 'București', 44.43, 26.1], ['SE', 'Stockholm', 59.33, 18.07], ['BR', 'São Paulo', -23.55, -46.63], ['AU', 'Sydney', -33.87, 151.21], ['IN', 'Mumbai', 19.08, 72.88], ['CA', 'Toronto', 43.65, -79.38]];
    const apps = [['chrome', 'Google Chrome', 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe', 'Google LLC'], ['discord', 'Discord', 'C:\\Users\\me\\AppData\\Local\\Discord\\app\\Discord.exe', 'Discord Inc.'], ['steam', 'Steam', 'C:\\Program Files (x86)\\Steam\\steam.exe', 'Valve'], ['svchost', tr('Servicii Windows (svchost)', 'Windows services (svchost)'), 'C:\\Windows\\System32\\svchost.exe', 'Microsoft', true], ['spotify', 'Spotify', 'C:\\Users\\me\\AppData\\Roaming\\Spotify\\Spotify.exe', 'Spotify AB'], ['tele', 'Telemetry Agent', 'C:\\Program Files\\Telemetry\\tele.exe', null], ['code', 'Visual Studio Code', 'C:\\Users\\me\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe', 'Microsoft']];
    const hosts = ['www.google.com', 'gateway.discord.gg', 'steamcdn-a.akamaihd.net', 'settings-win.data.microsoft.com', 'audio-ak.spotifycdn.com', 'api.github.com', 'cdn.jsdelivr.net', 'graph.facebook.com', null];
    const conns = [];
    for (let i = 0; i < 46; i++) {
      const c = cities[i % cities.length], a = apps[(i * 3 + (i >> 2)) % apps.length];
      conns.push({ id: 'c' + i, app: a[2].toLowerCase(), pid: 1000 + i, proto: i % 5 ? 'TCP' : 'UDP', lport: 50000 + i, ip: `${20 + i}.${i * 3}.10.${i}`, rport: [443, 443, 80, 5228, 3478, 443][i % 6], host: hosts[i % hosts.length], cc: c[0], country: c[0], city: c[1], lat: c[2], lon: c[3], state: i % 5 ? 'ESTABLISHED' : 'UDP', up: 0, down: 0, totUp: 1e5 * i, totDown: 4e5 * i, age: 30 * i, blocked: a[0] === 'tele' });
    }
    [['192.168.1.1', 53, 'UDP', 'svchost.exe'], ['192.168.1.20', 445, 'TCP', 'svchost.exe'], ['192.168.1.35', 8009, 'TCP', 'chrome.exe'], ['239.255.255.250', 1900, 'UDP', 'svchost.exe']].forEach(([ip, rport, proto, app], j) =>
      conns.push({ id: 'l' + j, app, pid: 900 + j, proto, lport: 60000 + j, ip, rport, host: null, cc: 'LAN', country: LAN_NAME, city: null, lat: null, lon: null, state: proto === 'UDP' ? 'UDP' : 'ESTABLISHED', up: 0, down: 0, totUp: 2e4 * (j + 1), totDown: 9e4 * (j + 1), age: 400 + j * 60, blocked: false }));
    let tick = 0; const hist = [];
    setInterval(() => {
      tick++;
      for (const c of conns) { const act = !c.blocked && Math.random() < 0.35; c.down = act ? Math.random() * 2e6 * Math.random() : 0; c.up = act ? c.down * 0.1 * Math.random() : 0; c.totDown += c.down; c.totUp += c.up; c.age++; }
      const ap = apps.map(a => { const cs = conns.filter(c => c.app === a[2].toLowerCase()); return { key: a[2].toLowerCase(), name: a[1], path: a[2], publisher: a[3], sys: !!a[4], conns: cs.length, up: d3.sum(cs, c => c.up), down: d3.sum(cs, c => c.down), totUp: d3.sum(cs, c => c.totUp), totDown: d3.sum(cs, c => c.totDown), listen: a[0] === 'steam' ? ['TCP 27036', 'UDP 27036'] : [], countries: [...new Set(cs.map(c => c.cc))], blocked: a[0] === 'tele', rules: a[0] === 'chrome', canBlock: true, active: true }; });
      const up = d3.sum(conns, c => c.up), down = d3.sum(conns, c => c.down);
      hist.push([tick, up, down]); if (hist.length > 180) hist.shift();
      onState({ type: 'state', up, down, sessUp: 1e8, sessDown: 9e8, hist, status: { admin: true, etw: true, dns: true, geo: tr('Bază geo DB-IP încărcată', 'DB-IP geo database loaded'), geoReady: true, block: { level: 'ok', engine: 'WFP', text: tr('Blocarea folosește Windows Filtering Platform (demo).', 'Blocking uses the Windows Filtering Platform (demo).') }, panic: false }, home: { lat: 44.43, lon: 26.1, city: 'București' }, apps: ap, conns });
    }, 1000);
  }
  if (o.cmd === 'history') {
    const now = Math.floor(Date.now() / 1000), b = o.hours <= 1 ? 60 : o.hours <= 24 ? 900 : o.hours <= 168 ? 3600 : 21600;
    const tl = []; for (let t = Math.floor((now - o.hours * 3600) / b) * b; t <= now; t += b) tl.push({ t, down: Math.random() * 5e8 * (1 + Math.sin(t / 8000)), up: Math.random() * 6e7 });
    onMessage({ type: 'history', hours: o.hours, bucket: b, timeline: tl, apps: S.apps.map(a => ({ key: a.key, name: a.name, path: a.path, up: a.totUp, down: a.totDown })), countries: [{ cc: 'US', up: 3e8, down: 2e9, apps: 5 }, { cc: 'DE', up: 1e8, down: 9e8, apps: 3 }, { cc: 'RO', up: 5e7, down: 3e8, apps: 2 }, { cc: 'NL', up: 2e7, down: 2e8, apps: 2 }],
      hosts: [{ host: 'www.google.com', cc: 'US', up: 1e8, down: 9e8, apps: 2 }, { host: 'steamcdn-a.akamaihd.net', cc: 'DE', up: 1e6, down: 7e8, apps: 1 }], ports: [{ port: 443, proto: 'TCP', bytes: 3e9 }, { port: 80, proto: 'TCP', bytes: 2e8 }, { port: 3478, proto: 'UDP', bytes: 9e7 }],
      events: [{ ts: now - 300, kind: 'new_app', app: 'x', appName: 'Discord', detail: tr('Prima conexiune la internet', 'First internet connection') + ' → gateway.discord.gg:443 (United States)' }, { ts: now - 1200, kind: 'block', appName: 'Telemetry Agent', app: 'y', detail: tr('Aplicație blocată', 'App blocked') }] });
  }
  if (o.cmd === 'appList') onMessage({ type: 'appList', apps: S.apps.map(a => ({ key: a.key, name: a.name, path: a.path })) });
  if (o.cmd === 'toggleApp') toast('(demo) ' + (o.block ? tr('blocat', 'blocked') : tr('permis', 'allowed')), 'ok');
  if (o.cmd === 'saveRule') onMessage({ type: 'toast', kind: 'ok', text: tr('(demo) Regula a fost salvată.', '(demo) Rule saved.') });
}
