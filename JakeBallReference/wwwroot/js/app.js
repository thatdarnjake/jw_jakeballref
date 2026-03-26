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
})();
