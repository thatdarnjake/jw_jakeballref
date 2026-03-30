// Jake-Ball Reference - Player Comparison App
(function () {
    'use strict';

    const MAX_PLAYERS = 5;
    const state = {
        players: [],          // array of { id, profile, filteredStats }
        activeStatTable: 'perGame',  // perGame | totals | advanced
        compareMode: 'career',
        filterValue1: '',
        filterValue2: '',
    };

    // DOM refs
    const $columns = document.getElementById('playerColumns');
    const $addBtn = document.getElementById('addPlayerBtn');
    const $modal = document.getElementById('searchModal');
    const $searchInput = document.getElementById('searchInput');
    const $searchResults = document.getElementById('searchResults');
    const $closeModal = document.getElementById('closeModal');
    const $compareMode = document.getElementById('compareMode');
    const $filterInputs = document.getElementById('filterInputs');
    const $filterValue1 = document.getElementById('filterValue1');
    const $filterValue2 = document.getElementById('filterValue2');
    const $filterRangeSep = document.getElementById('filterRangeSep');
    const $applyFilter = document.getElementById('applyFilter');

    // --- API ---
    async function searchPlayers(query) {
        const res = await fetch(`/api/players/search?q=${encodeURIComponent(query)}`);
        if (!res.ok) throw new Error('Search failed');
        return res.json();
    }

    async function getPlayer(playerId) {
        const res = await fetch(`/api/players/${encodeURIComponent(playerId)}`);
        if (!res.ok) throw new Error('Player not found');
        return res.json();
    }

    // --- Filtering ---
    function getStatTableKey() {
        switch (state.activeStatTable) {
            case 'perGame': return 'perGameStats';
            case 'totals': return 'totalStats';
            case 'advanced': return 'advancedStats';
            default: return 'perGameStats';
        }
    }

    function filterStats(profile) {
        const tableKey = getStatTableKey();
        const table = profile[tableKey];
        if (!table) return null;

        const mode = state.compareMode;
        let rows = table.rows.slice();
        const seasonHeader = table.headers[0]; // usually "Season"

        // Separate career row(s) from season rows
        const careerRows = rows.filter(r => {
            const s = r[seasonHeader] || '';
            return s.toLowerCase().includes('career') || s === '';
        });
        const seasonRows = rows.filter(r => {
            const s = r[seasonHeader] || '';
            return !s.toLowerCase().includes('career') && s !== '';
        });

        if (mode === 'career') {
            return { headers: table.headers, rows: rows, careerRows };
        }

        if (mode === 'season' && state.filterValue1) {
            const target = state.filterValue1.trim();
            const filtered = seasonRows.filter(r => (r[seasonHeader] || '').includes(target));
            return { headers: table.headers, rows: filtered.length ? filtered : seasonRows, careerRows: filtered.length ? [] : careerRows };
        }

        if (mode === 'careerYear' && state.filterValue1) {
            const yearNum = parseInt(state.filterValue1);
            if (!isNaN(yearNum) && yearNum > 0 && yearNum <= seasonRows.length) {
                const filtered = [seasonRows[yearNum - 1]];
                return { headers: table.headers, rows: filtered, careerRows: [] };
            }
        }

        if (mode === 'seasonRange' && state.filterValue1 && state.filterValue2) {
            const start = state.filterValue1.trim();
            const end = state.filterValue2.trim();
            let inRange = false;
            const filtered = seasonRows.filter(r => {
                const s = r[seasonHeader] || '';
                if (s.includes(start)) inRange = true;
                if (inRange) {
                    if (s.includes(end)) { inRange = false; return true; }
                    return true;
                }
                return false;
            });
            return { headers: table.headers, rows: filtered.length ? filtered : seasonRows, careerRows: [] };
        }

        if (mode === 'careerYearRange' && state.filterValue1 && state.filterValue2) {
            const start = parseInt(state.filterValue1);
            const end = parseInt(state.filterValue2);
            if (!isNaN(start) && !isNaN(end) && start > 0 && end >= start) {
                const filtered = seasonRows.slice(start - 1, end);
                return { headers: table.headers, rows: filtered, careerRows: [] };
            }
        }

        return { headers: table.headers, rows, careerRows };
    }

    // --- Rendering ---
    function render() {
        if (state.players.length === 0) {
            $columns.innerHTML = `
                <div class="empty-state">
                    <h2>No players loaded</h2>
                    <p>Click "Add Player" to search and compare up to ${MAX_PLAYERS} players side by side.</p>
                </div>`;
            $columns.removeAttribute('data-count');
            $addBtn.style.display = '';
            return;
        }

        $columns.setAttribute('data-count', state.players.length);
        $addBtn.style.display = state.players.length >= MAX_PLAYERS ? 'none' : '';

        $columns.innerHTML = state.players.map((p, idx) => {
            if (p.loading) {
                return `<div class="player-column">
                    <div class="loading">
                        <div class="loading-spinner"></div>
                        Loading player...
                    </div>
                </div>`;
            }

            const prof = p.profile;
            if (!prof) return '';

            const stats = filterStats(prof);
            const goldAccolades = ['MVP', 'NBA Champ', 'Finals MVP', 'DPOY'];

            return `<div class="player-column" data-idx="${idx}">
                <div class="player-header">
                    ${prof.imageUrl ? `<img class="player-photo" src="${escHtml(prof.imageUrl)}" alt="${escHtml(prof.name)}">` : '<div class="player-photo"></div>'}
                    <div class="player-info">
                        <div class="player-name" title="${escHtml(prof.name)}">${escHtml(prof.name)}</div>
                        <div class="player-meta">
                            ${prof.position ? `<span>${escHtml(prof.position)}</span>` : ''}
                            ${prof.height ? `<span>${escHtml(prof.height)}</span>` : ''}
                            ${prof.weight ? `<span>${escHtml(prof.weight)}</span>` : ''}
                        </div>
                        <div class="player-meta">
                            ${prof.college ? `<span>${escHtml(prof.college)}</span>` : ''}
                        </div>
                        ${prof.draft ? `<div class="player-meta"><span style="font-size:0.65rem">${escHtml(prof.draft)}</span></div>` : ''}
                    </div>
                    <button class="btn-remove" data-idx="${idx}" title="Remove player">&times;</button>
                </div>
                ${prof.accolades && prof.accolades.length ? `
                    <div class="accolades">
                        ${prof.accolades.map(a => {
                            const isGold = goldAccolades.some(g => a.name.includes(g));
                            return `<span class="accolade-badge${isGold ? ' gold' : ''}">${a.count > 1 ? a.count + 'x ' : ''}${escHtml(a.name)}</span>`;
                        }).join('')}
                    </div>` : ''}
                ${stats ? renderStatsTable(stats) : '<div class="loading">No stats available</div>'}
            </div>`;
        }).join('');

        // Bind remove buttons
        $columns.querySelectorAll('.btn-remove').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const idx = parseInt(e.currentTarget.dataset.idx);
                state.players.splice(idx, 1);
                render();
            });
        });
    }

    function renderStatsTable(stats) {
        if (!stats || !stats.headers.length) return '';

        // Pick key stat columns to keep it readable in narrow columns
        const headers = stats.headers;
        const seasonHeader = headers[0];

        // Separate career rows
        const seasonRows = stats.rows.filter(r => {
            const s = r[seasonHeader] || '';
            return !s.toLowerCase().includes('career');
        });
        const careerRows = stats.careerRows || [];

        return `<div class="stats-section">
            <table class="stats-table">
                <thead>
                    <tr>${headers.map(h => `<th>${escHtml(h)}</th>`).join('')}</tr>
                </thead>
                <tbody>
                    ${seasonRows.map(row => `<tr>${headers.map(h => `<td>${escHtml(row[h] || '')}</td>`).join('')}</tr>`).join('')}
                    ${careerRows.map(row => `<tr class="career-row">${headers.map(h => `<td>${escHtml(row[h] || '')}</td>`).join('')}</tr>`).join('')}
                </tbody>
            </table>
        </div>`;
    }

    function escHtml(str) {
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    // --- Search modal ---
    let searchTimeout = null;

    function openSearch() {
        $modal.style.display = '';
        $searchInput.value = '';
        $searchResults.innerHTML = '';
        setTimeout(() => $searchInput.focus(), 50);
    }

    function closeSearch() {
        $modal.style.display = 'none';
    }

    function handleSearch() {
        const q = $searchInput.value.trim();
        if (q.length < 2) {
            $searchResults.innerHTML = '<div class="search-empty">Type at least 2 characters...</div>';
            return;
        }

        $searchResults.innerHTML = '<div class="search-loading"><div class="loading-spinner"></div>Searching...</div>';

        clearTimeout(searchTimeout);
        searchTimeout = setTimeout(async () => {
            try {
                const results = await searchPlayers(q);
                if (results.length === 0) {
                    $searchResults.innerHTML = '<div class="search-empty">No players found</div>';
                    return;
                }
                $searchResults.innerHTML = results.map(r => `
                    <div class="search-result-item" data-id="${escHtml(r.playerId)}">
                        <span class="search-result-name">${escHtml(r.name)}</span>
                        <span class="search-result-years">${escHtml(r.yearsActive)}</span>
                    </div>`).join('');

                $searchResults.querySelectorAll('.search-result-item').forEach(item => {
                    item.addEventListener('click', () => selectPlayer(item.dataset.id));
                });
            } catch (err) {
                $searchResults.innerHTML = `<div class="search-empty">Error: ${escHtml(err.message)}</div>`;
            }
        }, 400);
    }

    async function selectPlayer(playerId) {
        if (state.players.length >= MAX_PLAYERS) return;
        if (state.players.some(p => p.id === playerId)) {
            closeSearch();
            return;
        }

        closeSearch();

        const idx = state.players.length;
        state.players.push({ id: playerId, loading: true, profile: null });
        render();

        try {
            const profile = await getPlayer(playerId);
            state.players[idx] = { id: playerId, loading: false, profile };
        } catch (err) {
            state.players.splice(idx, 1);
            alert(`Failed to load player: ${err.message}`);
        }
        render();
    }

    // --- Compare mode / filters ---
    function updateFilterUI() {
        const mode = $compareMode.value;
        state.compareMode = mode;

        const showInputs = mode !== 'career';
        $filterInputs.style.display = showInputs ? '' : 'none';
        $applyFilter.style.display = showInputs ? '' : 'none';

        const isRange = mode === 'seasonRange' || mode === 'careerYearRange';
        $filterValue2.style.display = isRange ? '' : 'none';
        $filterRangeSep.style.display = isRange ? '' : 'none';

        switch (mode) {
            case 'season':
                $filterValue1.placeholder = 'e.g. 2022-23';
                break;
            case 'careerYear':
                $filterValue1.placeholder = 'e.g. 2';
                break;
            case 'seasonRange':
                $filterValue1.placeholder = 'e.g. 2019-20';
                $filterValue2.placeholder = 'e.g. 2022-23';
                break;
            case 'careerYearRange':
                $filterValue1.placeholder = 'e.g. 1';
                $filterValue2.placeholder = 'e.g. 5';
                break;
        }

        if (mode === 'career') render();
    }

    function applyFilter() {
        state.filterValue1 = $filterValue1.value;
        state.filterValue2 = $filterValue2.value;
        render();
    }

    // --- Stat tab switching ---
    document.querySelectorAll('.stat-tab').forEach(tab => {
        tab.addEventListener('click', () => {
            document.querySelectorAll('.stat-tab').forEach(t => t.classList.remove('active'));
            tab.classList.add('active');
            state.activeStatTable = tab.dataset.table;
            render();
        });
    });

    // --- Event listeners ---
    $addBtn.addEventListener('click', openSearch);
    $closeModal.addEventListener('click', closeSearch);
    $modal.querySelector('.modal-backdrop').addEventListener('click', closeSearch);
    $searchInput.addEventListener('input', handleSearch);
    $compareMode.addEventListener('change', updateFilterUI);
    $applyFilter.addEventListener('click', applyFilter);

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') closeSearch();
    });

    // Init
    render();

    // Default load Embiid
    (async function() {
        try {
            const res = await fetch('/api/players/embiijo01');
            if (res.ok) {
                const profile = await res.json();
                state.players.push({ id: 'embiijo01', profile, filteredStats: null });
                applyFilter();
                render();
            }
        } catch (e) {
            console.warn('Could not load default player:', e);
        }
    })();

})();

// ========================================
// Page Tabs & Standings/Teams/Trending
// (Outside IIFE so it always works)
// ========================================
(function () {
    'use strict';

    let standingsLoaded = false;
    let teamsLoaded = false;
    let leadersLoaded = false;
    let currentTeamData = null;
    let activeTeamTable = 'roster';
    const headerControls = document.getElementById('headerControls');

    // ---- Tab switching ----
    document.querySelectorAll('.page-tab').forEach(function (tab) {
        tab.addEventListener('click', function () {
            document.querySelectorAll('.page-tab').forEach(function (t) { t.classList.remove('active'); });
            document.querySelectorAll('.page').forEach(function (p) { p.classList.remove('active'); });
            tab.classList.add('active');
            var page = document.getElementById(tab.dataset.page + 'Page');
            if (page) page.classList.add('active');

            // Show player controls only on players tab
            if (headerControls) {
                headerControls.style.display = tab.dataset.page === 'players' ? '' : 'none';
            }

            // Lazy-load tab data
            if (tab.dataset.page === 'standings' && !standingsLoaded) {
                loadStandings();
                loadPlayoffs();
                standingsLoaded = true;
            }
            if (tab.dataset.page === 'teams' && !teamsLoaded) {
                loadTeamList();
                teamsLoaded = true;
            }
            if (tab.dataset.page === 'leaders' && !leadersLoaded) {
                loadLeaders();
                leadersLoaded = true;
            }
        });
    });

    // ---- Standings ----
    async function loadStandings() {
        try {
            var res = await fetch('/api/standings');
            var data = await res.json();
            renderStandingsTable('eastStandings', data.east);
            renderStandingsTable('westStandings', data.west);
        } catch (e) {
            document.getElementById('eastStandings').innerHTML = '<p class="loading-msg">Failed to load standings.</p>';
            document.getElementById('westStandings').innerHTML = '<p class="loading-msg">Failed to load standings.</p>';
        }
    }

    function renderStandingsTable(containerId, teams) {
        var container = document.getElementById(containerId);
        if (!teams || teams.length === 0) {
            container.innerHTML = '<p class="loading-msg">No data available.</p>';
            return;
        }
        var statKeys = Object.keys(teams[0]).filter(function (k) { return k !== 'rank' && k !== 'team'; });
        var displayKeys = statKeys.slice(0, 10);

        var html = '<table class="standings-table"><thead><tr><th>#</th><th>Team</th>';
        displayKeys.forEach(function (k) { html += '<th>' + k + '</th>'; });
        html += '</tr></thead><tbody>';

        teams.forEach(function (t, i) {
            var cls = '';
            if (i === 5) cls = 'playoff-line';
            if (i === 9) cls = 'playin-line';
            html += '<tr class="' + cls + '"><td>' + t.rank + '</td><td>' + t.team + '</td>';
            displayKeys.forEach(function (k) { html += '<td>' + (t[k] || '') + '</td>'; });
            html += '</tr>';
        });
        html += '</tbody></table>';
        container.innerHTML = html;
    }

    // ---- Playoffs ----
    async function loadPlayoffs() {
        try {
            var res = await fetch('/api/playoffs');
            var data = await res.json();
            renderPlayoffs(data);
        } catch (e) {
            document.getElementById('playoffsContainer').innerHTML = '<p class="loading-msg">Failed to load playoffs.</p>';
        }
    }

    function renderPlayoffs(data) {
        var container = document.getElementById('playoffsContainer');
        if (!data || !data.east) {
            container.innerHTML = '<p class="loading-msg">Playoff data not available.</p>';
            return;
        }

        var html = '';
        html += renderConferenceBracket('Eastern Conference', data.east);
        html += renderConferenceBracket('Western Conference', data.west);
        container.innerHTML = html;
    }

    function renderConferenceBracket(title, conf) {
        var html = '<div class="playoff-conf"><h3>' + title + '</h3>';

        // Clinched playoff spots (1-6)
        if (conf.playoff && conf.playoff.length) {
            html += '<div class="playoff-round"><h4>Playoff Seeds (1-6)</h4>';
            conf.playoff.forEach(function (t) {
                html += '<div class="playoff-series"><span class="teams">(' + t.seed + ') ' + t.team + '</span><span class="series-score">' + t.record + '</span></div>';
            });
            html += '</div>';
        }

        // Play-in (7-10)
        if (conf.playIn && conf.playIn.length) {
            html += '<div class="playoff-round"><h4>Play-In Tournament (7-10)</h4>';
            conf.playIn.forEach(function (t) {
                html += '<div class="playoff-series"><span class="teams">(' + t.seed + ') ' + t.team + '</span><span class="series-score">' + t.record + '</span></div>';
            });
            html += '</div>';
        }

        // Projected first round
        if (conf.firstRound && conf.firstRound.length) {
            html += '<div class="playoff-round"><h4>Projected First Round</h4>';
            conf.firstRound.forEach(function (m) {
                html += '<div class="playoff-series"><span class="teams">' + m.higher + ' vs ' + m.lower + '</span></div>';
            });
            html += '</div>';
        }

        html += '</div>';
        return html;
    }

    // ---- Teams ----
    async function loadTeamList() {
        try {
            var res = await fetch('/api/teams');
            var teams = await res.json();
            var list = document.getElementById('teamList');
            list.innerHTML = '';
            teams.forEach(function (t) {
                var el = document.createElement('div');
                el.className = 'team-list-item' + (t.code === 'PHI' ? ' active' : '');
                el.innerHTML = '<span class="team-code">' + t.code + '</span>' + t.name;
                el.addEventListener('click', function () {
                    document.querySelectorAll('.team-list-item').forEach(function (i) { i.classList.remove('active'); });
                    el.classList.add('active');
                    loadTeamProfile(t.code);
                });
                list.appendChild(el);
            });
            loadTeamProfile('PHI');
        } catch (e) {
            document.getElementById('teamList').innerHTML = '<p class="loading-msg">Failed to load teams.</p>';
        }
    }

    async function loadTeamProfile(code) {
        var container = document.getElementById('teamTableContainer');
        container.innerHTML = '<p class="team-loading">Loading...</p>';
        try {
            var res = await fetch('/api/teams/' + code);
            currentTeamData = await res.json();
            document.getElementById('teamName').textContent = currentTeamData.teamName;
            document.getElementById('teamRecord').textContent = currentTeamData.record || '';
            document.getElementById('teamSeason').textContent = currentTeamData.season || '';
            renderTeamTable();
        } catch (e) {
            container.innerHTML = '<p class="loading-msg">Failed to load team data.</p>';
        }
    }

    function renderTeamTable() {
        if (!currentTeamData) return;
        var container = document.getElementById('teamTableContainer');
        var tableData;
        if (activeTeamTable === 'roster') tableData = currentTeamData.roster;
        else if (activeTeamTable === 'perGame') tableData = currentTeamData.perGame;
        else if (activeTeamTable === 'totals') tableData = currentTeamData.totals;

        if (!tableData || !tableData.headers || tableData.rows.length === 0) {
            container.innerHTML = '<p class="loading-msg">No data available for this view.</p>';
            return;
        }
        var html = '<table class="team-table"><thead><tr>';
        tableData.headers.forEach(function (h) { html += '<th>' + h + '</th>'; });
        html += '</tr></thead><tbody>';
        tableData.rows.forEach(function (row) {
            html += '<tr>';
            tableData.headers.forEach(function (h) { html += '<td>' + (row[h] || '') + '</td>'; });
            html += '</tr>';
        });
        html += '</tbody></table>';
        container.innerHTML = html;
    }

    document.querySelectorAll('.team-stat-tab').forEach(function (tab) {
        tab.addEventListener('click', function () {
            document.querySelectorAll('.team-stat-tab').forEach(function (t) { t.classList.remove('active'); });
            tab.classList.add('active');
            activeTeamTable = tab.dataset.teamTable;
            renderTeamTable();
        });
    });

    // ---- Leaders ----
    var leadersData = null;
    var alltimeRegData = null;
    var alltimePlayoffsData = null;
    var leadersView = 'perGame';

    async function loadLeaders() {
        var grid = document.getElementById('leadersGrid');
        try {
            var res = await fetch('/api/leaders');
            leadersData = await res.json();
            renderLeaders();
        } catch (e) {
            grid.innerHTML = '<p class="loading-msg">Failed to load leaders.</p>';
        }
    }

    async function loadAllTimeLeaders(type) {
        var grid = document.getElementById('leadersGrid');
        grid.innerHTML = '<p class="loading-msg">Loading all-time leaders (this may take a moment)...</p>';
        try {
            var res = await fetch('/api/leaders/alltime?type=' + type);
            var data = await res.json();
            if (type === 'playoffs') alltimePlayoffsData = data;
            else alltimeRegData = data;
            renderLeaders();
        } catch (e) {
            grid.innerHTML = '<p class="loading-msg">Failed to load all-time leaders.</p>';
        }
    }

    function renderLeaders() {
        var grid = document.getElementById('leadersGrid');
        var cats = null;

        if (leadersView === 'perGame' && leadersData) cats = leadersData.perGame;
        else if (leadersView === 'totals' && leadersData) cats = leadersData.totals;
        else if (leadersView === 'alltimeReg' && alltimeRegData) cats = alltimeRegData.categories;
        else if (leadersView === 'alltimePlayoffs' && alltimePlayoffsData) cats = alltimePlayoffsData.categories;

        if (!cats || cats.length === 0) {
            grid.innerHTML = '<p class="loading-msg">No leader data available.</p>';
            return;
        }

        var isAllTime = leadersView.startsWith('alltime');
        var html = '';
        cats.forEach(function (cat) {
            html += '<div class="leader-card"><h3>' + cat.title + '</h3>';
            cat.entries.forEach(function (e) {
                var nameDisplay = e.player + (e.hof ? ' <span class="hof-badge">HOF</span>' : '');
                html += '<div class="leader-entry">' +
                    '<span class="leader-rank">' + e.rank + '</span>' +
                    '<span class="leader-name">' + nameDisplay + '</span>' +
                    (e.team ? '<span class="leader-team">' + e.team + '</span>' : '') +
                    '<span class="leader-value">' + e.value + '</span>' +
                    '</div>';
            });
            html += '</div>';
        });

        grid.innerHTML = html;
    }

    document.querySelectorAll('.leaders-tab').forEach(function (tab) {
        tab.addEventListener('click', function () {
            document.querySelectorAll('.leaders-tab').forEach(function (t) { t.classList.remove('active'); });
            tab.classList.add('active');
            leadersView = tab.dataset.leadersView;

            if (leadersView === 'alltimeReg' && !alltimeRegData) {
                loadAllTimeLeaders('career');
            } else if (leadersView === 'alltimePlayoffs' && !alltimePlayoffsData) {
                loadAllTimeLeaders('playoffs');
            } else {
                renderLeaders();
            }
        });
    });

})();
