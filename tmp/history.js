
const workflowId = '@workflowId';
let historyTimer = null;

async function api(path) {
    const response = await fetch(path);
    if (!response.ok) throw new Error(await response.text());
    return response;
}

function htmlEscape(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

function formatDate(value) {
    if (!value) return '-';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return htmlEscape(value);
    return date.toLocaleString([], {
        year: 'numeric', month: 'short', day: '2-digit',
        hour: '2-digit', minute: '2-digit', second: '2-digit'
    });
}

function duration(start, end) {
    if (!start) return '-';
    const startDate = new Date(start);
    const endDate = end ? new Date(end) : new Date();
    if (Number.isNaN(startDate.getTime()) || Number.isNaN(endDate.getTime())) return '-';
    const seconds = Math.max(0, Math.round((endDate.getTime() - startDate.getTime()) / 1000));
    if (seconds < 60) return seconds + 's';
    const minutes = Math.floor(seconds / 60);
    const remaining = seconds % 60;
    return minutes + 'm ' + remaining + 's';
}

function statusBadge(status) {
    const normalized = (status || '').toLowerCase();
    const colors = {
        queued: 'text-bg-secondary',
        running: 'text-bg-primary',
        completed: 'text-bg-success',
        failed: 'text-bg-danger'
    };
    const color = colors[normalized] || 'text-bg-light';
    return '<span class="badge ' + color + ' status-pill">' + htmlEscape(status || 'Unknown') + '</span>';
}

function runStatusClass(status) {
    const normalized = (status || '').toLowerCase();
    return 'run-' + (['queued', 'running', 'completed', 'failed'].includes(normalized) ? normalized : 'queued');
}

function runStatusColor(status) {
    const normalized = (status || '').toLowerCase();
    const colors = {
        queued: 'background:#6c757d;',
        running: 'background:#0d6efd;',
        completed: 'background:#198754;',
        failed: 'background:#dc3545;'
    };
    return (colors[normalized] || colors.queued);
}

function renderMessage(message) {
    let role = (message.role || 'assistant').toLowerCase();
    let roleClass = 'message-other';
    if (role === 'user') roleClass = 'message-user';
    if (role === 'assistant') roleClass = 'message-assistant';
    let text = htmlEscape(message.content || '');
    text = text.replace(/file:([0-9a-fA-F-]{36})/g, function(match, fileId) {
        return '<a class="btn btn-sm btn-outline-success ms-2" href="/api/files/' + fileId + '/download">Download file</a>';
    });
    return '<div class="message-item ' + roleClass + '">' +
        '<div class="d-flex justify-content-between align-items-center mb-1">' +
            '<span class="fw-semibold text-capitalize">' + htmlEscape(message.role || 'assistant') + '</span>' +
            '<small class="text-muted">' + formatDate(message.createdAt) + '</small>' +
        '</div>' +
        '<div class="text-break">' + text + '</div>' +
    '</div>';
}

async function loadWorkflowDetails() {
    try {
        const response = await api('/api/workflow/' + workflowId);
        const data = await response.json();
        document.getElementById('workflowName').textContent = data.name || 'Workflow';
        document.getElementById('workflowSubtitle').textContent = 'Run history, status, timing, messages and generated files.';
        document.getElementById('workflowUser').textContent = data.userInstruction || '-';
        document.getElementById('workflowSystem').textContent = data.systemInstruction || '-';
        const modelText = data.modelName || 'default';
        let metaText =
            'Tools: ' + (Array.isArray(data.tools) ? data.tools.length : 0) +
            ' · RAG buckets: ' + (Array.isArray(data.ragBuckets) ? data.ragBuckets.length : 0) +
            ' · Attached files: ' + (Array.isArray(data.files) ? data.files.length : 0) +
            ' · Model: ' + modelText;

        let scheduleText = 'No schedule configured.';
        try {
            const scheduleResponse = await api('/api/workflow/' + workflowId + '/schedules');
            const schedules = await scheduleResponse.json();
            if (Array.isArray(schedules) && schedules.length > 0) {
                const nextRunTimes = schedules
                    .filter(function(s) { return s.enabled && s.nextRunAt; })
                    .map(function(s) { return s.nextRunAt; })
                    .sort();
                if (nextRunTimes.length > 0) {
                    scheduleText = 'Next scheduled run: ' + formatDate(nextRunTimes[0]);
                } else {
                    scheduleText = 'Scheduled item found, but no upcoming run time is available.';
                }
            }
        } catch (error) {
            scheduleText = 'Schedule information could not be loaded.';
        }

        const badge = function(label, value, cssClass) {
            return '<span class="badge ' + cssClass + ' me-1 mb-1">' + htmlEscape(label) + ': ' + htmlEscape(value) + '</span>';
        };

        document.getElementById('workflowMeta').innerHTML =
            badge('Tools', Array.isArray(data.tools) ? data.tools.length : 0, 'text-bg-light border') +
            badge('RAG buckets', Array.isArray(data.ragBuckets) ? data.ragBuckets.length : 0, 'text-bg-light border') +
            badge('Attached files', Array.isArray(data.files) ? data.files.length : 0, 'text-bg-light border') +
            badge('Model', modelText, 'text-bg-primary') +
            badge('Next run', scheduleText.replace('Next scheduled run: ', '').replace('No schedule configured.', 'Not scheduled'), scheduleText.startsWith('Next') ? 'text-bg-success' : 'text-bg-secondary');
    } catch (error) {
        document.getElementById('workflowName').textContent = 'Workflow';
        document.getElementById('workflowSubtitle').textContent = 'Could not load workflow details.';
    }
}

async function showHistory() {
    try {
    if (historyTimer) {
        clearTimeout(historyTimer);
        historyTimer = null;
    }

    let runs = [];
    let chain = [];
    try {
        const workflowsResponse = await api('/api/workflow');
        const workflows = await workflowsResponse.json();
        const byId = new Map((Array.isArray(workflows) ? workflows : []).map(w => [w.id, w]));
        const parentOf = {};
        for (const w of (Array.isArray(workflows) ? workflows : [])) {
            if (w.nextWorkflowId) parentOf[w.nextWorkflowId] = w.id;
        }

        let rootId = workflowId;
        while (parentOf[rootId]) rootId = parentOf[rootId];

        chain = [];
        const visited = new Set();
        let currentId = rootId;
        while (currentId && byId.has(currentId) && !visited.has(currentId)) {
            visited.add(currentId);
            chain.push(byId.get(currentId));
            currentId = byId.get(currentId).nextWorkflowId;
        }

        const workflowIds = chain.length ? chain.map(w => w.id) : [workflowId];
        const stepByWorkflow = new Map(chain.map((workflow, index) => [workflow.id, index + 1]));
        for (const id of workflowIds) {
            const response = await api('/api/workflow/' + id + '/runs?page=1&pageSize=50');
            const pageRuns = await response.json();
            if (Array.isArray(pageRuns)) {
                pageRuns.forEach(run => {
                    run.workflowId = id;
                    run.workflowName = byId.has(id) ? byId.get(id).name : id;
                    run.workflowStep = stepByWorkflow.get(id) || 1;
                    run.chainLength = chain.length || 1;
                });
                runs.push(...pageRuns);
            }
        }

        const runById = new Map(runs.map(run => [String(run.id), run]));
        function findRootRun(run, seen = new Set()) {
            let current = run;
            while (current && current.parentRunId && runById.has(String(current.parentRunId)) && !seen.has(String(current.id))) {
                seen.add(String(current.id));
                current = runById.get(String(current.parentRunId));
            }
            return current ? current.id : run.id;
        }
        runs.forEach(run => { run.rootRunId = findRootRun(run); });
        runs.sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt));
    } catch (error) {
        document.getElementById('historyContent').innerHTML =
            '<div class="alert alert-danger">Failed to load runs: ' + htmlEscape(error.message) + '</div>';
        historyTimer = setTimeout(showHistory, 4000);
        return;
    }

    if (!Array.isArray(runs) || runs.length === 0) {
        document.getElementById('historyContent').innerHTML =
            '<div class="card p-4 text-center text-muted">No workflow runs yet. Start one from the Workflows page.</div>';
        return;
    }

    let html = '';

    async function renderRunCard(run, isMain) {
        let cardHtml = '';
        cardHtml += '<div class="timeline-item ' + runStatusClass(run.status) + '">';
        cardHtml += '<span class="timeline-dot" style="' + runStatusColor(run.status) + '"></span>';
        cardHtml += '<div class="card run-card ' + runStatusClass(run.status) + ' mb-4 shadow-sm">';
        cardHtml += '  <div class="card-body">';
        cardHtml += '    <div class="d-flex flex-wrap justify-content-between align-items-start gap-2 mb-3">';
        cardHtml += '      <div>';
        cardHtml += '        <div class="d-flex align-items-center gap-2 flex-wrap mb-1">';
        cardHtml += '          ' + statusBadge(run.status);
        cardHtml += '          <span class="badge ' + (isMain ? 'text-bg-primary' : 'text-bg-secondary') + '">' + htmlEscape(run.workflowName || 'Workflow') + '</span>';
        if (!isMain) cardHtml += '          <span class="badge text-bg-light border">Dependent workflow</span>';
        cardHtml += '          <span class="badge text-bg-light border">Step ' + (run.workflowStep || 1) + ' of ' + (run.chainLength || 1) + '</span>';
        cardHtml += '        </div>';
        cardHtml += '        <div class="fw-semibold">Run ' + htmlEscape(String(run.id).substring(0, 8)) + '</div>';
        cardHtml += '        <small class="text-muted d-block mt-1">Run ID: ' + htmlEscape(run.id) + '</small>';
        cardHtml += '      </div>';
        cardHtml += '      <div class="text-end small">';
        cardHtml += '        <div><b>Duration:</b> ' + duration(run.startedAt, run.completedAt) + '</div>';
        cardHtml += '        <div><b>Model:</b> ' + htmlEscape(run.modelId || 'default') + '</div>';
        cardHtml += '        <div><b>Session:</b> ' + htmlEscape(run.sessionId || '-') + '</div>';
        cardHtml += '      </div>';
        cardHtml += '    </div>';

        cardHtml += '    <div class="row g-3 small mb-3">';
        cardHtml += '      <div class="col-md-4"><div class="text-muted">Created</div><div>' + formatDate(run.createdAt) + '</div></div>';
        cardHtml += '      <div class="col-md-4"><div class="text-muted">Started</div><div>' + formatDate(run.startedAt) + '</div></div>';
        cardHtml += '      <div class="col-md-4"><div class="text-muted">Completed</div><div>' + formatDate(run.completedAt) + '</div></div>';
        cardHtml += '    </div>';

        if (run.error) {
            cardHtml += '    <div class="alert alert-danger small mb-3"><b>Error:</b> ' + htmlEscape(run.error) + '</div>';
        }

        try {
            const messageResponse = await api('/api/workflow/runs/' + run.id + '/messages');
            const messages = await messageResponse.json();
            if (Array.isArray(messages) && messages.length > 0) {
                cardHtml += '    <div class="mt-2">';
                for (const message of messages) {
                    cardHtml += renderMessage(message);
                }
                cardHtml += '    </div>';
            } else {
                cardHtml += '    <div class="text-muted small fst-italic">No messages recorded for this run yet.</div>';
            }
        } catch (error) {
            cardHtml += '    <div class="alert alert-warning small mb-0">Failed to load messages: ' + htmlEscape(error.message) + '</div>';
        }
        cardHtml += '  </div>';
        cardHtml += '</div>';
        cardHtml += '</div>';
        return cardHtml;
    }

    const displayRuns = runs
        .filter(run => String(run.workflowId) === String(workflowId))
        .sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt));

    html += '<div class="timeline mb-4">';
    if (displayRuns.length === 0) {
        html += '<div class="card p-4 text-center text-muted">No runs for this workflow yet.</div>';
    } else {
        for (const run of displayRuns) {
            html += await renderRunCard(run, true);
        }
    }
    html += '</div>';

    document.getElementById('historyContent').innerHTML = html;

    const hasActive = runs.some(function(run) {
        return run.status === 'Queued' || run.status === 'Running';
    });
    if (hasActive) {
        historyTimer = setTimeout(showHistory, 3000);
    }
    } catch (error) {
        console.error(error);
        document.getElementById('historyContent').innerHTML =
            '<div class="alert alert-danger">History failed: ' + htmlEscape(error && error.message ? error.message : error) + '</div>';
    }
}

document.addEventListener('DOMContentLoaded', function() {
    loadWorkflowDetails();
    showHistory();
});
