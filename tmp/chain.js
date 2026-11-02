
const selectedRunId = '@runId';
const selectedWorkflowId = '@workflowId';
let chainData = [];
let runByWorkflow = new Map();
let modal;

async function api(path) {
    const response = await fetch(path);
    if (!response.ok) throw new Error(await response.text());
    return response;
}
function htmlEscape(value) {
    return String(value ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}
function formatDate(value) {
    if (!value) return '-';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return htmlEscape(value);
    return date.toLocaleString();
}

async function loadGraph() {
    try {
        const workflowsResponse = await api('/api/workflow');
        const workflows = await workflowsResponse.json();
        const byId = new Map((Array.isArray(workflows) ? workflows : []).map(w => [w.id, w]));
        const parentOf = {};
        for (const w of (Array.isArray(workflows) ? workflows : [])) {
            if (w.nextWorkflowId) parentOf[w.nextWorkflowId] = w.id;
        }

        let rootId = selectedWorkflowId;
        while (parentOf[rootId]) rootId = parentOf[rootId];

        chainData = [];
        const visited = new Set();
        let currentId = rootId;
        while (currentId && byId.has(currentId) && !visited.has(currentId)) {
            visited.add(currentId);
            chainData.push(byId.get(currentId));
            currentId = byId.get(currentId).nextWorkflowId;
        }

        let runs = [];
        for (const workflow of chainData) {
            const response = await api('/api/workflow/' + workflow.id + '/runs?page=1&pageSize=50');
            const pageRuns = await response.json();
            if (Array.isArray(pageRuns)) {
                pageRuns.forEach(run => {
                    run.workflowId = workflow.id;
                    run.workflowName = workflow.name;
                });
                runs.push(...pageRuns);
            }
        }

        const runById = new Map(runs.map(run => [String(run.id), run]));
        function findRoot(run, seen = new Set()) {
            let current = run;
            while (current && current.parentRunId && runById.has(String(current.parentRunId)) && !seen.has(String(current.id))) {
                seen.add(String(current.id));
                current = runById.get(String(current.parentRunId));
            }
            return current;
        }
        const selectedRun = runById.get(String(selectedRunId));
        const rootRun = selectedRun ? findRoot(selectedRun) : runs[0];
        const groupRuns = runs.filter(run => rootRun && findRoot(run).id === rootRun.id);
        runByWorkflow = new Map(groupRuns.map(run => [String(run.workflowId), run]));

        let graphHtml = '';
        for (let index = 0; index < chainData.length; index++) {
            const workflow = chainData[index];
            const run = runByWorkflow.get(String(workflow.id));
            const status = (run?.status || 'Not run').toLowerCase();
            const classes = ['chain-node'];
            if (run && String(run.id) === String(selectedRunId)) classes.push('active');
            if (status === 'completed') classes.push('completed');
            if (status === 'failed') classes.push('failed');
            if (!run) classes.push('notrun');
            graphHtml += '<div class="' + classes.join(' ') + '" data-step="' + index + '" onclick="showNodeDetails(' + index + ')">';
            graphHtml += '<div class="node-title">Step ' + (index + 1) + '</div>';
            graphHtml += '<div class="node-sub">' + htmlEscape(workflow.name || 'Workflow') + '</div>';
            graphHtml += '<div class="node-sub mt-2">' + htmlEscape(run?.status || 'Not run') + '</div>';
            if (run) graphHtml += '<div class="node-sub">' + htmlEscape(String(run.id).substring(0, 8)) + '</div>';
            graphHtml += '</div>';
            if (index < chainData.length - 1) graphHtml += '<div class="chain-connector"></div>';
        }
        document.getElementById('graph').innerHTML = graphHtml || '<div class="text-muted">No chain data.</div>';
        document.getElementById('details').textContent = chainData.length ? 'Click any node for full run details.' : '';

        modal = new bootstrap.Modal(document.getElementById('nodeModal'));
    } catch (error) {
        document.getElementById('graph').innerHTML = '<div class="alert alert-danger">Graph failed: ' + htmlEscape(error.message) + '</div>';
    }
}

async function showNodeDetails(index) {
    const workflow = chainData[index];
    const run = runByWorkflow.get(String(workflow.id));
    document.getElementById('nodeModalTitle').textContent = 'Step ' + (index + 1) + ': ' + (workflow.name || 'Workflow');
    let body = '';
    body += '<div class="mb-2"><b>Status:</b> ' + htmlEscape(run?.status || 'Not run') + '</div>';
    body += '<div class="mb-2"><b>Workflow instruction:</b><br><span class="text-muted">' + htmlEscape(workflow.userInstruction || '-') + '</span></div>';
    if (run) {
        body += '<div class="mb-2"><b>Run ID:</b> ' + htmlEscape(run.id) + '</div>';
        body += '<div class="mb-2"><b>Model:</b> ' + htmlEscape(run.modelId || 'default') + '</div>';
        body += '<div class="mb-2"><b>Created:</b> ' + formatDate(run.createdAt) + '</div>';
        body += '<div class="mb-2"><b>Started:</b> ' + formatDate(run.startedAt) + '</div>';
        body += '<div class="mb-2"><b>Completed:</b> ' + formatDate(run.completedAt) + '</div>';
        body += '<div class="mb-2"><b>Session:</b> ' + htmlEscape(run.sessionId || '-') + '</div>';
        try {
            const response = await api('/api/workflow/runs/' + run.id + '/messages');
            const messages = await response.json();
            if (Array.isArray(messages) && messages.length > 0) {
                body += '<hr><div class="fw-semibold mb-2">Messages</div>';
                for (const message of messages) {
                    body += '<div class="border rounded p-2 mb-2"><b>' + htmlEscape(message.role || 'assistant') + '</b>: ' + htmlEscape(message.content || '') + '</div>';
                }
            }
        } catch (error) {
            body += '<div class="alert alert-warning small">Could not load messages.</div>';
        }
    } else {
        body += '<div class="text-muted">This step has not been run in this execution.</div>';
    }
    document.getElementById('nodeModalBody').innerHTML = body;
    modal.show();
}

document.addEventListener('DOMContentLoaded', loadGraph);
