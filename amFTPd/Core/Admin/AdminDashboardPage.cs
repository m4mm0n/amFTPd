/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AdminDashboardPage.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-25 00:00:00
 *  Last Modified:  2026-04-25 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      Self-contained HTML/CSS/JS single-page application for the amFTPd web admin
 *      dashboard, served at /admin by the StatusEndpoint.
 *
 *      Features:
 *          - Live session viewer with transfer progress bars (auto-refresh)
 *          - Server stats overview with rolling-rate cards
 *          - Per-section stats table
 *          - Top uploaders leaderboard
 *          - Searchable user list with quick-kick
 *          - Dupe search
 *          - Recent pre list
 *          - Config rehash button
 *          - In-browser Bearer-token auth (stored in sessionStorage)
 *
 *      No external CSS/JS dependencies — everything is inline.
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */

namespace amFTPd.Core.Admin;

/// <summary>
/// Provides the self-contained HTML/CSS/JS for the amFTPd web admin dashboard SPA.
/// Served at /admin by <see cref="amFTPd.Core.Monitoring.StatusEndpoint"/>.
/// </summary>
public static class AdminDashboardPage
{
    /// <summary>
    /// Returns the complete HTML document string for the admin dashboard.
    /// </summary>
    public static string GetHtml() =>
    """
    <!DOCTYPE html>
    <html lang="en">
    <head>
      <meta charset="UTF-8">
      <meta name="viewport" content="width=device-width, initial-scale=1.0">
      <title>amFTPd Admin</title>
      <style>
        :root {
          --bg:      #0a0a0a;
          --bg2:     #111111;
          --bg3:     #181818;
          --border:  #252525;
          --text:    #c8c8c8;
          --dim:     #555;
          --acc:     #00cc66;
          --acc2:    #00aaff;
          --warn:    #ffaa00;
          --err:     #ff4444;
          --font:    'Courier New', 'Lucida Console', monospace;
        }
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body { background: var(--bg); color: var(--text); font-family: var(--font); font-size: 13px; min-height: 100vh; }

        /* ── Header ─────────────────────────────────────────────────────── */
        #hdr {
          background: var(--bg2); border-bottom: 1px solid var(--border);
          padding: 9px 18px; display: flex; align-items: center;
          justify-content: space-between; gap: 12px; flex-wrap: wrap;
        }
        #logo { color: var(--acc); font-size: 15px; font-weight: bold; letter-spacing: 1px; }
        #logo span { color: var(--dim); font-weight: normal; }
        .dot { width: 8px; height: 8px; border-radius: 50%; display: inline-block; margin-right: 5px; }
        .dot.green { background: var(--acc); box-shadow: 0 0 5px var(--acc); }
        .dot.red   { background: var(--err); }
        .dot.grey  { background: var(--dim); }
        #conn-lbl  { font-size: 12px; color: var(--dim); }
        #hdr-right { display: flex; gap: 6px; align-items: center; }
        #countdown { color: var(--dim); font-size: 11px; min-width: 36px; text-align: right; }

        /* ── Nav ─────────────────────────────────────────────────────────── */
        #nav {
          background: var(--bg2); border-bottom: 1px solid var(--border);
          padding: 0 18px; display: flex; gap: 0;
        }
        .tab {
          padding: 9px 16px; cursor: pointer; color: var(--dim);
          border-bottom: 2px solid transparent; user-select: none;
          transition: color 0.12s;
        }
        .tab:hover { color: var(--text); }
        .tab.active { color: var(--acc); border-bottom-color: var(--acc); }

        /* ── Content ─────────────────────────────────────────────────────── */
        #content { padding: 18px; }
        .panel   { display: none; }
        .panel.active { display: block; }
        .block   { margin-bottom: 26px; }
        .sec-ttl {
          color: var(--acc2); font-size: 10px; text-transform: uppercase;
          letter-spacing: 1px; padding-bottom: 7px;
          border-bottom: 1px solid var(--border); margin-bottom: 10px;
        }

        /* ── Tables ──────────────────────────────────────────────────────── */
        table { width: 100%; border-collapse: collapse; }
        th {
          background: var(--bg3); color: var(--acc2); text-align: left;
          padding: 5px 9px; border-bottom: 1px solid var(--border);
          font-size: 10px; text-transform: uppercase; letter-spacing: 0.5px;
        }
        td { padding: 5px 9px; border-bottom: 1px solid var(--border); vertical-align: middle; }
        tr:hover td { background: var(--bg3); }
        .empty { color: var(--dim); padding: 18px; text-align: center; }

        /* ── Progress ────────────────────────────────────────────────────── */
        .bar-wrap { width: 100px; background: var(--bg3); border: 1px solid var(--border); height: 10px; border-radius: 2px; overflow: hidden; display: inline-block; }
        .bar-fill { height: 100%; background: var(--acc); transition: width 0.4s; }
        .bar-fill.dn { background: var(--acc2); }

        /* ── Badges ──────────────────────────────────────────────────────── */
        .badge { display: inline-block; padding: 1px 5px; border-radius: 2px; font-size: 10px; font-weight: bold; text-transform: uppercase; }
        .badge.up   { background: #002a14; color: var(--acc);  border: 1px solid var(--acc); }
        .badge.dn   { background: #001a2a; color: var(--acc2); border: 1px solid var(--acc2); }
        .badge.idle { background: var(--bg3); color: var(--dim); border: 1px solid var(--border); }
        .badge.ok   { background: #002a14; color: var(--acc);  border: 1px solid var(--acc); }
        .badge.pend { background: #2a1e00; color: var(--warn); border: 1px solid var(--warn); }
        .badge.deny { background: #2a0000; color: var(--err);  border: 1px solid var(--err); }
        .badge.nuke { background: #2a0000; color: var(--err);  border: 1px solid var(--err); }

        /* ── Stat cards ──────────────────────────────────────────────────── */
        .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(170px, 1fr)); gap: 10px; margin-bottom: 22px; }
        .card { background: var(--bg2); border: 1px solid var(--border); border-radius: 3px; padding: 12px 14px; }
        .card-lbl { color: var(--dim); font-size: 10px; text-transform: uppercase; letter-spacing: 0.5px; }
        .card-val { color: var(--acc); font-size: 20px; margin-top: 5px; }
        .card-sub { color: var(--dim); font-size: 10px; margin-top: 1px; }

        /* ── Search / inputs ─────────────────────────────────────────────── */
        .sr { display: flex; gap: 7px; margin-bottom: 12px; align-items: center; flex-wrap: wrap; }
        input[type="text"], input[type="password"] {
          background: var(--bg2); border: 1px solid var(--border); color: var(--text);
          padding: 5px 9px; font-family: var(--font); font-size: 13px;
          border-radius: 2px; outline: none;
        }
        input:focus { border-color: var(--acc); }
        button, .btn {
          background: var(--bg3); border: 1px solid var(--border); color: var(--text);
          padding: 5px 11px; font-family: var(--font); font-size: 13px;
          cursor: pointer; border-radius: 2px; transition: border-color 0.12s, color 0.12s;
        }
        button:hover, .btn:hover { border-color: var(--acc); color: var(--acc); }
        button.pri  { border-color: var(--acc);  color: var(--acc); }
        button.dang { border-color: var(--err);  color: var(--err); }
        button.warn { border-color: var(--warn); color: var(--warn); }
        button.sm   { padding: 2px 7px; font-size: 11px; }

        /* ── Login overlay ───────────────────────────────────────────────── */
        #login {
          position: fixed; inset: 0; background: rgba(0,0,0,0.88);
          display: flex; align-items: center; justify-content: center; z-index: 100;
        }
        #lbox {
          background: var(--bg2); border: 1px solid var(--border);
          border-radius: 4px; padding: 30px 38px; width: 320px;
        }
        #lbox h2 { color: var(--acc); margin-bottom: 5px; font-size: 16px; }
        #lbox p  { color: var(--dim); font-size: 11px; margin-bottom: 18px; }
        #lbox label { display: block; color: var(--dim); font-size: 10px; text-transform: uppercase; letter-spacing: 0.4px; margin-bottom: 4px; }
        #lbox input { width: 100%; margin-bottom: 14px; }
        #lbox button { width: 100%; }
        #lerr { color: var(--err); font-size: 11px; margin-top: 7px; display: none; }

        /* ── Toast ───────────────────────────────────────────────────────── */
        #toast {
          position: fixed; bottom: 18px; right: 18px;
          background: var(--bg2); border: 1px solid var(--acc);
          color: var(--acc); padding: 8px 14px; border-radius: 3px;
          font-size: 12px; display: none; z-index: 200;
        }
        #toast.err { border-color: var(--err); color: var(--err); }

        /* ── Misc ────────────────────────────────────────────────────────── */
        .up  { color: var(--acc); }
        .dn  { color: var(--acc2); }
        .dim { color: var(--dim); }
        .w   { color: var(--warn); }
        .e   { color: var(--err); }
        .acts { display: flex; gap: 5px; }
        b { color: var(--text); }
      </style>
    </head>
    <body>

    <!-- Login -->
    <div id="login">
      <div id="lbox">
        <h2>amFTPd Admin</h2>
        <p>Enter your API auth token to continue.</p>
        <label>Auth Token</label>
        <input type="password" id="tok" placeholder="Bearer token..." autocomplete="off">
        <button class="pri" onclick="doLogin()">Connect</button>
        <div id="lerr">Authentication failed — check your token.</div>
      </div>
    </div>

    <!-- Header -->
    <div id="hdr">
      <div id="logo">&#9889; amFTPd <span>Admin</span></div>
      <div style="display:flex;align-items:center;gap:6px">
        <span class="dot grey" id="cdot"></span>
        <span id="conn-lbl">Connecting...</span>
      </div>
      <div id="hdr-right">
        <span id="countdown"></span>
        <button class="sm" onclick="mrefresh()">&#8635;</button>
        <button class="sm warn" onclick="doRehash()">Rehash</button>
        <button class="sm dang" onclick="doLogout()">Logout</button>
      </div>
    </div>

    <!-- Navigation -->
    <div id="nav">
      <div class="tab active" data-tab="sessions" onclick="goto('sessions')">Sessions</div>
      <div class="tab" data-tab="stats"    onclick="goto('stats')">Stats</div>
      <div class="tab" data-tab="users"    onclick="goto('users')">Users</div>
      <div class="tab" data-tab="dupes"    onclick="goto('dupes')">Dupes</div>
      <div class="tab" data-tab="pre"      onclick="goto('pre')">Pre</div>
    </div>

    <!-- Content -->
    <div id="content">

      <!-- Sessions -->
      <div class="panel active" id="panel-sessions">
        <div class="block">
          <div class="sec-ttl" id="sess-ttl">Active Sessions</div>
          <table>
            <thead><tr>
              <th>User</th><th>Group</th><th>Remote IP</th>
              <th>Direction</th><th>File</th><th>Progress</th><th>Actions</th>
            </tr></thead>
            <tbody id="tbody-sess"><tr><td colspan="7" class="empty">Loading...</td></tr></tbody>
          </table>
        </div>
      </div>

      <!-- Stats -->
      <div class="panel" id="panel-stats">
        <div class="block">
          <div class="sec-ttl">Server Overview</div>
          <div class="cards" id="stat-cards"></div>
        </div>
        <div class="block">
          <div class="sec-ttl">Sections</div>
          <table>
            <thead><tr>
              <th>Section</th><th>Uploads</th><th>Downloads</th>
              <th>&#8593; Bytes</th><th>&#8595; Bytes</th><th>Active</th>
            </tr></thead>
            <tbody id="tbody-sects"><tr><td colspan="6" class="empty">Loading...</td></tr></tbody>
          </table>
        </div>
        <div class="block">
          <div class="sec-ttl">Top Uploaders (all-time)</div>
          <table>
            <thead><tr>
              <th>#</th><th>User</th><th>Group</th><th>Files</th><th>&#8593; Bytes</th>
            </tr></thead>
            <tbody id="tbody-top"><tr><td colspan="5" class="empty">Loading...</td></tr></tbody>
          </table>
        </div>
      </div>

      <!-- Users -->
      <div class="panel" id="panel-users">
        <div class="block">
          <div class="sr">
            <input type="text" id="usrq" placeholder="Search users..." oninput="filterUsers()" style="width:240px">
            <span class="dim" id="ucnt" style="font-size:11px"></span>
          </div>
          <table>
            <thead><tr>
              <th>Username</th><th>Group</th><th>Credits</th>
              <th>Ratio</th><th>Status</th><th>Actions</th>
            </tr></thead>
            <tbody id="tbody-users"><tr><td colspan="6" class="empty">Loading...</td></tr></tbody>
          </table>
        </div>
      </div>

      <!-- Dupes -->
      <div class="panel" id="panel-dupes">
        <div class="block">
          <div class="sr">
            <input type="text" id="dupq" placeholder="Search releases..." style="width:300px"
                   onkeydown="if(event.key==='Enter')searchDupes()">
            <button onclick="searchDupes()">Search</button>
          </div>
          <table>
            <thead><tr>
              <th>Release</th><th>Section</th><th>Uploader</th>
              <th>Size</th><th>Added</th><th>Status</th>
            </tr></thead>
            <tbody id="tbody-dupes"><tr><td colspan="6" class="empty">Enter a search term above.</td></tr></tbody>
          </table>
        </div>
      </div>

      <!-- Pre -->
      <div class="panel" id="panel-pre">
        <div class="block">
          <div class="sec-ttl">Recent Pre Entries</div>
          <table>
            <thead><tr>
              <th>Release</th><th>Section</th><th>Group</th>
              <th>Files</th><th>Size</th><th>Status</th><th>Timestamp</th>
            </tr></thead>
            <tbody id="tbody-pre"><tr><td colspan="7" class="empty">Loading...</td></tr></tbody>
          </table>
        </div>
      </div>

    </div><!-- /content -->

    <div id="toast"></div>

    <script>
    'use strict';

    // ── State ──────────────────────────────────────────────────────────────────
    var _tok = '';
    var _tab = 'sessions';
    var _users = [];
    var _timer = null;
    var _cd = 0;
    var INTERVAL = 5;
    var BASE = window.location.origin;

    // ── Boot ───────────────────────────────────────────────────────────────────
    window.addEventListener('DOMContentLoaded', function() {
      _tok = sessionStorage.getItem('amftpd_token') || '';
      document.getElementById('tok').addEventListener('keydown', function(e) {
        if (e.key === 'Enter') doLogin();
      });
      // Try to connect (handles both auth and no-auth servers)
      tryConnect();
    });

    function tryConnect() {
      api('/api/').then(function() {
        hideLogin(); startApp();
      }).catch(function(e) {
        if (e === 401) showLogin();
        else { setConn(false); showLogin(); }
      });
    }

    function doLogin() {
      var t = document.getElementById('tok').value.trim();
      if (!t) return;
      _tok = t;
      api('/api/').then(function() {
        sessionStorage.setItem('amftpd_token', _tok);
        document.getElementById('lerr').style.display = 'none';
        hideLogin(); startApp();
      }).catch(function() {
        _tok = '';
        document.getElementById('lerr').style.display = 'block';
      });
    }

    function doLogout() {
      sessionStorage.removeItem('amftpd_token');
      _tok = ''; clearInterval(_timer); showLogin();
    }

    function hideLogin() { document.getElementById('login').style.display = 'none'; }
    function showLogin() { document.getElementById('login').style.display = 'flex'; }

    function startApp() {
      setConn(true);
      loadTab();
      startTimer();
    }

    // ── Timer ──────────────────────────────────────────────────────────────────
    function startTimer() {
      _cd = INTERVAL;
      clearInterval(_timer);
      _timer = setInterval(function() {
        _cd--;
        document.getElementById('countdown').textContent = _cd + 's';
        if (_cd <= 0) { loadTab(); _cd = INTERVAL; }
      }, 1000);
    }

    function mrefresh() { _cd = INTERVAL; loadTab(); }

    // ── Tabs ───────────────────────────────────────────────────────────────────
    function goto(tab) {
      _tab = tab;
      document.querySelectorAll('.tab').forEach(function(t) {
        t.classList.toggle('active', t.dataset.tab === tab);
      });
      document.querySelectorAll('.panel').forEach(function(p) {
        p.classList.toggle('active', p.id === 'panel-' + tab);
      });
      loadTab();
    }

    function loadTab() {
      if (_tab === 'sessions') loadSessions();
      else if (_tab === 'stats')    loadStats();
      else if (_tab === 'users')    loadUsers();
      else if (_tab === 'pre')      loadPre();
    }

    // ── API ────────────────────────────────────────────────────────────────────
    function api(path, opts) {
      opts = opts || {};
      opts.headers = Object.assign({ 'Authorization': 'Bearer ' + _tok, 'Content-Type': 'application/json' }, opts.headers || {});
      return fetch(BASE + path, opts).then(function(r) {
        if (r.status === 401) { setConn(false); return Promise.reject(401); }
        if (!r.ok) return Promise.reject(r.status);
        setConn(true);
        return r.json();
      });
    }

    function post(path, body) {
      return api(path, { method: 'POST', body: JSON.stringify(body) });
    }

    function setConn(ok) {
      var dot = document.getElementById('cdot');
      var lbl = document.getElementById('conn-lbl');
      dot.className = 'dot ' + (ok ? 'green' : 'red');
      lbl.textContent = ok ? 'Connected' : 'Disconnected';
      lbl.style.color = ok ? 'var(--acc)' : 'var(--err)';
    }

    function toast(msg, err) {
      var el = document.getElementById('toast');
      el.textContent = msg; el.className = err ? 'err' : '';
      el.style.display = 'block';
      setTimeout(function() { el.style.display = 'none'; }, 3000);
    }

    // ── Formatters ─────────────────────────────────────────────────────────────
    function fmtB(b) {
      if (b == null || b < 0) return '-';
      if (b >= 1073741824) return (b / 1073741824).toFixed(2) + ' GB';
      if (b >= 1048576)    return (b / 1048576).toFixed(2) + ' MB';
      if (b >= 1024)       return (b / 1024).toFixed(1) + ' KB';
      return b + ' B';
    }

    function fmtN(n) { return n != null ? Number(n).toLocaleString() : '-'; }

    function fmtD(s) {
      if (!s) return '-';
      var d = new Date(s);
      return d.toLocaleDateString() + ' ' + d.toLocaleTimeString();
    }

    function esc(s) {
      return String(s || '')
        .replace(/&/g,'&amp;').replace(/</g,'&lt;')
        .replace(/>/g,'&gt;').replace(/"/g,'&quot;');
    }

    function basename(p) { return String(p || '').replace(/.*[\\/]/, '') || '-'; }

    // ── Sessions ───────────────────────────────────────────────────────────────
    function loadSessions() {
      api('/api/sessions').then(function(d) {
        var ss = d.sessions || [];
        document.getElementById('sess-ttl').textContent = 'Active Sessions (' + ss.length + ')';
        var tb = document.getElementById('tbody-sess');
        if (!ss.length) { tb.innerHTML = '<tr><td colspan="7" class="empty">No active sessions.</td></tr>'; return; }
        tb.innerHTML = ss.map(function(s) {
          var dir = s.transferDir || 'idle';
          var isUp = dir === 'upload', isDn = dir === 'download';
          var badge = isUp ? '<span class="badge up">UP</span>'
                   : isDn ? '<span class="badge dn">DN</span>'
                           : '<span class="badge idle">IDLE</span>';
          var pct = s.bytesTotal > 0 ? Math.round(s.bytesCompleted / s.bytesTotal * 100) : 0;
          var bar = (isUp || isDn) && s.bytesTotal > 0
            ? '<div class="bar-wrap"><div class="bar-fill ' + (isDn ? 'dn' : '') + '" style="width:' + pct + '%"></div></div> <span class="dim">' + pct + '%</span>'
            : '<span class="dim">-</span>';
          var user = esc(s.user || '');
          return '<tr><td><b>' + user + '</b></td>'
            + '<td class="dim">' + esc(s.group || '-') + '</td>'
            + '<td class="dim">' + esc(s.remoteIp || '') + '</td>'
            + '<td>' + badge + '</td>'
            + '<td class="dim" title="' + esc(s.transferFile || '') + '">' + esc(basename(s.transferFile)) + '</td>'
            + '<td>' + bar + '</td>'
            + '<td><div class="acts"><button class="sm dang" onclick="kick(\'' + user + '\')">Kick</button></div></td>'
            + '</tr>';
        }).join('');
      }).catch(function(e) { if (e !== 401) setConn(false); });
    }

    function kick(user) {
      if (!confirm('Kick all sessions for "' + user + '"?')) return;
      post('/api/kick', { user: user }).then(function(d) {
        toast('Kicked ' + user + ' (' + (d.kicked || 0) + ' session(s))');
        loadSessions();
      }).catch(function() { toast('Kick failed', true); });
    }

    // ── Stats ──────────────────────────────────────────────────────────────────
    function loadStats() {
      Promise.all([
        api('/api/stats'),
        api('/api/stats/sections'),
        api('/api/stats/top?window=all&count=10')
      ]).then(function(res) {
        renderCards(res[0]);
        renderSects(res[1]);
        renderTop(res[2]);
      }).catch(function(e) { if (e !== 401) setConn(false); });
    }

    function renderCards(s) {
      var r = (s.rolling || {}).transfersPerSecond || {};
      document.getElementById('stat-cards').innerHTML = [
        card('Active Sessions',   s.activeSessions,         ''),
        card('Active Transfers',  s.activeTransfers,        ''),
        card('Uploaded',          fmtB(s.bytesUploaded),    ''),
        card('Downloaded',        fmtB(s.bytesDownloaded),  ''),
        card('Total Commands',    fmtN(s.totalCommands),    ''),
        card('Failed Logins',     fmtN(s.failedLogins),     ''),
        card('Transfers / 5s',    (r.s5 || 0).toFixed(2)+'/s', ''),
        card('Transfers / 1m',    (r.m1 || 0).toFixed(2)+'/s', ''),
      ].join('');
    }

    function card(lbl, val, sub) {
      return '<div class="card"><div class="card-lbl">' + lbl + '</div>'
           + '<div class="card-val">' + val + '</div>'
           + (sub ? '<div class="card-sub">' + sub + '</div>' : '')
           + '</div>';
    }

    function renderSects(d) {
      var ss = d.sections || [];
      var tb = document.getElementById('tbody-sects');
      if (!ss.length) { tb.innerHTML = '<tr><td colspan="6" class="empty">No section data.</td></tr>'; return; }
      tb.innerHTML = ss.map(function(s) {
        return '<tr><td><b>' + esc(s.section) + '</b></td>'
          + '<td class="up">' + fmtN(s.uploads) + '</td>'
          + '<td class="dn">' + fmtN(s.downloads) + '</td>'
          + '<td class="up">' + fmtB(s.bytesUploaded) + '</td>'
          + '<td class="dn">' + fmtB(s.bytesDownloaded) + '</td>'
          + '<td>' + (s.activeUsers || 0) + '</td></tr>';
      }).join('');
    }

    function renderTop(d) {
      var tt = d.top || [];
      var tb = document.getElementById('tbody-top');
      if (!tt.length) { tb.innerHTML = '<tr><td colspan="5" class="empty">No leaderboard data.</td></tr>'; return; }
      tb.innerHTML = tt.map(function(u) {
        return '<tr><td class="dim">' + u.rank + '</td>'
          + '<td><b>' + esc(u.user) + '</b></td>'
          + '<td class="dim">' + esc(u.group || '-') + '</td>'
          + '<td class="up">' + fmtN(u.files) + '</td>'
          + '<td class="up">' + fmtB(u.bytesUploaded) + '</td></tr>';
      }).join('');
    }

    // ── Users ──────────────────────────────────────────────────────────────────
    function loadUsers() {
      api('/api/users').then(function(d) {
        _users = d.users || [];
        filterUsers();
      }).catch(function(e) { if (e !== 401) setConn(false); });
    }

    function filterUsers() {
      var q = (document.getElementById('usrq').value || '').toLowerCase();
      var list = q ? _users.filter(function(u) { return (u.userName || '').toLowerCase().includes(q); }) : _users;
      document.getElementById('ucnt').textContent = list.length + ' user(s)';
      var tb = document.getElementById('tbody-users');
      if (!list.length) {
        tb.innerHTML = '<tr><td colspan="6" class="empty">' + (q ? 'No matching users.' : 'No users.') + '</td></tr>';
        return;
      }
      var show = list.slice(0, 200);
      tb.innerHTML = show.map(function(u) {
        var nm  = esc(u.userName || '');
        var st  = u.disabled ? '<span class="e">Disabled</span>' : '<span class="up">Active</span>';
        var rat = u.isNoRatio ? '<span class="dim">Free</span>' : (u.ratio != null ? u.ratio : '-');
        return '<tr><td><b>' + nm + '</b></td>'
          + '<td class="dim">' + esc(u.group || '-') + '</td>'
          + '<td class="up">' + fmtB((u.creditsKb || 0) * 1024) + '</td>'
          + '<td>' + rat + '</td>'
          + '<td>' + st + '</td>'
          + '<td><div class="acts"><button class="sm dang" onclick="kick(\'' + nm + '\')">Kick</button></div></td>'
          + '</tr>';
      }).join('');
      if (list.length > 200) {
        tb.innerHTML += '<tr><td colspan="6" class="empty dim">Showing 200 of ' + list.length + ' — use search to narrow.</td></tr>';
      }
    }

    // ── Dupes ──────────────────────────────────────────────────────────────────
    function searchDupes() {
      var q = (document.getElementById('dupq').value || '').trim();
      if (!q) return;
      var tb = document.getElementById('tbody-dupes');
      tb.innerHTML = '<tr><td colspan="6" class="empty">Searching...</td></tr>';
      api('/api/dupes/search?q=' + encodeURIComponent(q) + '&limit=50').then(function(d) {
        var rs = d.results || [];
        if (!rs.length) { tb.innerHTML = '<tr><td colspan="6" class="empty">No results.</td></tr>'; return; }
        tb.innerHTML = rs.map(function(r) {
          var st = r.isNuked ? '<span class="badge nuke">NUKED</span>' : '<span class="badge ok">OK</span>';
          return '<tr><td><b>' + esc(r.releaseName) + '</b></td>'
            + '<td class="dim">' + esc(r.section || '-') + '</td>'
            + '<td>' + esc(r.uploaderUser || '-') + '</td>'
            + '<td>' + fmtB(r.totalBytes) + '</td>'
            + '<td class="dim">' + fmtD(r.firstSeen) + '</td>'
            + '<td>' + st + '</td></tr>';
        }).join('');
      }).catch(function(e) {
        tb.innerHTML = '<tr><td colspan="6" class="empty e">Error ' + e + '</td></tr>';
      });
    }

    // ── Pre ────────────────────────────────────────────────────────────────────
    function loadPre() {
      api('/api/pres?count=50').then(function(d) {
        var ps = d.pres || [];
        var tb = document.getElementById('tbody-pre');
        if (!ps.length) { tb.innerHTML = '<tr><td colspan="7" class="empty">No pre entries.</td></tr>'; return; }
        tb.innerHTML = ps.map(function(p) {
          var s = (p.status || 'approved').toLowerCase();
          var badge = s === 'approved' ? '<span class="badge ok">Approved</span>'
                    : s === 'pending'  ? '<span class="badge pend">Pending</span>'
                                       : '<span class="badge deny">Denied</span>';
          return '<tr><td><b>' + esc(p.releaseName) + '</b></td>'
            + '<td class="dim">' + esc(p.section || '-') + '</td>'
            + '<td>' + esc(p.group || '-') + '</td>'
            + '<td class="up">' + (p.fileCount != null ? p.fileCount : '-') + '</td>'
            + '<td>' + fmtB(p.totalBytes) + '</td>'
            + '<td>' + badge + '</td>'
            + '<td class="dim">' + fmtD(p.timestamp) + '</td></tr>';
        }).join('');
      }).catch(function(e) { if (e !== 401) setConn(false); });
    }

    // ── Rehash ─────────────────────────────────────────────────────────────────
    function doRehash() {
      if (!confirm('Reload server configuration now?')) return;
      post('/api/rehash', {}).then(function(d) {
        toast(d.success ? 'Rehash OK: ' + d.message : 'Rehash failed: ' + d.message, !d.success);
      }).catch(function() { toast('Rehash request failed', true); });
    }

    </script>
    </body>
    </html>
    """;
}
