// BGA → Terra Mystica Calculator export (installing and using it: the main README, "Importing games from Board Game Arena").
// Run on either:
//   • a finished game's replay page (boardgamearena.com/tablereplay?…), right after it opens, before stepping through
//     moves: the replay frame (/archive/archive/frame.html) holds gameui.gamedatas and the whole log (g_gamelogs);
//   • a game in progress, on its game page (boardgamearena.com/<n>/terramystica?table=…): the page holds
//     gameui.gamedatas (the position now; the converter reads only its fixed parts: board, tiles, cards, bridge spots,
//     players, faction material) and the log is fetched from notificationHistory.html with history=0, which answers
//     every notification (packet_type "resend", as in replays) from packet `from` on; with history=1 it would answer
//     only the ~200 packets before `from`, and only notes with log text (no factions, no actionUsed): not usable.
// Saves bga-<table>.json holding { format, table, saved, source, logs, gamedatas }, which BgaImport reads as its only
// argument. An unfinished game converts up to its last move.
(async () => {
  const LOGS_URL = '/archive/archive/logs.html'; // replay fallback only; not confirmed
  const MAX_PAGES = 60;                          // 60 × ~200 packets: far beyond any Terra Mystica game
  try {
    const wins = [window];
    for (let i = 0; i < window.frames.length; i++) wins.push(window.frames[i]);
    let w = null;
    for (const c of wins) {
      try { if (c.gameui && c.gameui.gamedatas) { w = c; break; } } catch (e) { /* another site's frame */ }
    }
    if (!w) {
      alert('No game data on this page. Open the game itself (in progress) or its replay (finished), then try again.');
      return;
    }
    const ui = w.gameui;
    const table = String(ui.table_id || new URLSearchParams(w.location.search).get('table') || '');
    if (!table) {
      alert('Could not tell which table this is.');
      return;
    }
    const token = (w.bgaConfig && w.bgaConfig.requestToken) || '';
    const getJson = async url => {
      const response = await w.fetch(url, { credentials: 'include', headers: { 'X-Request-Token': token } });
      const body = await response.json();
      if (!body || !body.status || !body.data)
        throw new Error((body && body.error) || `${url.split('?')[0]} answered ${response.status}`);
      return body.data;
    };
    const looksLikeLog = a => Array.isArray(a) && a.length > 0 && a.some(p => p && Array.isArray(p.data));

    let logs = null, source;
    if (looksLikeLog(w.g_gamelogs)) {
      logs = w.g_gamelogs;
      source = 'replay';
    } else if (w.g_archive_mode) {
      const data = await getJson(`${LOGS_URL}?table=${encodeURIComponent(table)}&translated=true`);
      if (!Array.isArray(data.logs)) throw new Error('The replay\'s log could not be read.');
      logs = data.logs;
      source = 'replay';
    } else {
      // A game in progress: notificationHistory.html with history=0, from packet 1 on (one answer so far: 372 packets;
      // paged forwards in case BGA caps an answer).
      const game = ui.game_name || 'terramystica';
      const server = (w.location.pathname.match(/^\/(\d+)\//) || [])[1];
      const base = `${server ? '/' + server : ''}/${game}/${game}/notificationHistory.html`;
      const byId = new Map();
      let from = 1;
      for (let page = 0; page < MAX_PAGES; page++) {
        const data = await getJson(`${base}?table=${encodeURIComponent(table)}&from=${from}`
          + `&privateinc=1&history=0&noerrortracking=true&dojo.preventCache=${Date.now()}`);
        const packets = Array.isArray(data.data) ? data.data : [];
        let highest = from - 1;
        for (const p of packets) {
          const id = Number(p.packet_id);
          if (!byId.has(id)) byId.set(id, p);
          highest = Math.max(highest, id);
        }
        if (packets.length === 0 || highest < from) break; // the end (or no progress)
        from = highest + 1;
        await new Promise(r => setTimeout(r, 250)); // gently
      }
      logs = [...byId.values()].sort((a, b) => Number(a.packet_id) - Number(b.packet_id));
      if (!looksLikeLog(logs)) throw new Error('The game\'s log could not be read.');
      if (Number(logs[0].packet_id) !== 1)
        throw new Error(`The log starts at packet ${logs[0].packet_id}, not 1: the start of the game is missing.`);
      source = 'live';
    }

    const out = { format: 'bga-tm', table, saved: new Date().toISOString(), source, logs, gamedatas: ui.gamedatas };
    // The download link is never added to the page: BGA's pages catch link clicks and treat them as their own
    // navigation ("Page not found: <id>.js"). A detached link still downloads in Chrome; stopPropagation as well.
    const url = URL.createObjectURL(new Blob([JSON.stringify(out)], { type: 'application/json' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = `bga-${table}.json`;
    link.addEventListener('click', e => e.stopPropagation());
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 60000);
  } catch (e) {
    alert('Export failed: ' + (e && e.message ? e.message : e));
  }
})();
