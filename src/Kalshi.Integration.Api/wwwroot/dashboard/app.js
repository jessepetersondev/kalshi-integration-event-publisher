const endpoints = {
  orders: '/api/v1/dashboard/orders',
  positions: '/api/v1/dashboard/positions',
  events: '/api/v1/dashboard/events?limit=50',
  audit: (category, hours, limit) => `/api/v1/dashboard/audit-records?hours=${encodeURIComponent(hours)}&limit=${encodeURIComponent(limit)}${category ? `&category=${encodeURIComponent(category)}` : ''}`,
  issues: (category, hours) => `/api/v1/dashboard/issues?hours=${encodeURIComponent(hours)}${category ? `&category=${encodeURIComponent(category)}` : ''}`,
  devToken: '/api/v1/auth/dev-token',
};

const accessTokenStorageKey = 'kalshi.integration.dashboard.access-token';

function setStatus(id, text) {
  document.getElementById(id).textContent = text;
}

function setText(id, text) {
  document.getElementById(id).textContent = text;
}

function setVisibility(id, visible) {
  document.getElementById(id).classList.toggle('hidden', !visible);
}

function fmtDate(value) {
  if (!value) return '—';
  return new Date(value).toLocaleString();
}

function fmtNumber(value) {
  return new Intl.NumberFormat().format(Number(value ?? 0));
}

function escapeHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function normalizeToken(value) {
  return String(value ?? '')
    .trim()
    .toLowerCase()
    .replaceAll('_', '')
    .replaceAll(' ', '')
    .replace(/[^a-z0-9-]/g, '');
}

function titleCase(value) {
  return String(value ?? '—')
    .replaceAll('_', ' ')
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/\b\w/g, char => char.toUpperCase());
}

function renderBadge(value, kind, fallback = '—') {
  const label = value ? titleCase(value) : fallback;
  const token = normalizeToken(value || fallback);
  return `<span class="badge badge-${kind} badge-${kind}-${token}">${escapeHtml(label)}</span>`;
}

function getAccessToken() {
  return localStorage.getItem(accessTokenStorageKey)?.trim() || '';
}

function saveAccessToken(value) {
  const token = String(value ?? '').trim();
  if (token) {
    localStorage.setItem(accessTokenStorageKey, token);
  } else {
    localStorage.removeItem(accessTokenStorageKey);
  }

  const field = document.getElementById('access-token');
  if (field) field.value = token;
  setAuthStatus(token ? 'Token saved locally.' : 'No token saved yet.');
}

function setAuthStatus(text) {
  const node = document.getElementById('auth-status');
  if (node) node.textContent = text;
}

function createAuthorizedHeaders() {
  const token = getAccessToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

async function fetchJson(url, options = {}) {
  const response = await fetch(url, {
    ...options,
    headers: {
      ...createAuthorizedHeaders(),
      ...(options.headers || {}),
    },
  });

  if (response.status === 401) {
    throw new Error('Unauthorized. Save a valid bearer token or issue a local dev token first.');
  }

  if (response.status === 403) {
    throw new Error('Forbidden. Your token is valid but missing the required role/policy.');
  }

  if (!response.ok) {
    throw new Error(`Request failed with status ${response.status}`);
  }

  return response.json();
}

async function issueDevelopmentToken() {
  const response = await fetchJson(endpoints.devToken, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ roles: ['admin', 'operator', 'trader', 'integration'], subject: 'dashboard-operator' }),
  });

  saveAccessToken(response.accessToken);
  setAuthStatus('Local development token issued and saved.');
}

async function loadCollection({ url, bodyId, emptyId, errorId, statusId, emptyMessage, renderRow }) {
  const body = document.getElementById(bodyId);
  body.innerHTML = '';
  setVisibility(emptyId, false);
  setVisibility(errorId, false);
  setStatus(statusId, 'Loading…');

  try {
    const items = await fetchJson(url);

    if (!items.length) {
      setVisibility(emptyId, true);
      document.getElementById(emptyId).textContent = emptyMessage;
      setStatus(statusId, 'Empty');
      return [];
    }

    body.innerHTML = items.map(renderRow).join('');
    setStatus(statusId, `${items.length} item${items.length === 1 ? '' : 's'}`);
    return items;
  } catch (error) {
    const errorNode = document.getElementById(errorId);
    errorNode.textContent = error.message;
    setVisibility(errorId, true);
    setStatus(statusId, 'Error');
    return [];
  }
}

async function loadOrders() {
  return loadCollection({
    url: endpoints.orders,
    bodyId: 'orders-body',
    emptyId: 'orders-empty',
    errorId: 'orders-error',
    statusId: 'orders-status',
    emptyMessage: 'No real order activity yet.',
    renderRow: item => `
      <tr>
        <td class="monospace">${escapeHtml(item.id)}</td>
        <td>
          <div class="primary-text">${escapeHtml(item.ticker)}</div>
          <div class="secondary-text">${escapeHtml(item.strategyName || '—')}</div>
        </td>
        <td>${renderBadge(item.side, 'side')}</td>
        <td>${escapeHtml(fmtNumber(item.quantity))}</td>
        <td>${renderBadge(item.status, 'status')}</td>
        <td>${escapeHtml(fmtNumber(item.filledQuantity))}</td>
        <td>${escapeHtml(fmtDate(item.updatedAt))}</td>
      </tr>`
  });
}

async function loadPositions() {
  return loadCollection({
    url: endpoints.positions,
    bodyId: 'positions-body',
    emptyId: 'positions-empty',
    errorId: 'positions-error',
    statusId: 'positions-status',
    emptyMessage: 'No open positions yet.',
    renderRow: item => `
      <tr>
        <td class="primary-text">${escapeHtml(item.ticker)}</td>
        <td>${renderBadge(item.side, 'side')}</td>
        <td>${escapeHtml(fmtNumber(item.contracts))}</td>
        <td>${escapeHtml(item.averagePrice)}</td>
        <td>${escapeHtml(fmtDate(item.asOf))}</td>
      </tr>`
  });
}

async function loadEvents() {
  return loadCollection({
    url: endpoints.events,
    bodyId: 'events-body',
    emptyId: 'events-empty',
    errorId: 'events-error',
    statusId: 'events-status',
    emptyMessage: 'No execution events yet.',
    renderRow: item => `
      <tr>
        <td>${escapeHtml(fmtDate(item.occurredAt))}</td>
        <td>${escapeHtml(item.ticker)}</td>
        <td>${renderBadge(item.status, 'status')}</td>
        <td>${escapeHtml(fmtNumber(item.filledQuantity))}</td>
        <td class="monospace">${escapeHtml(item.orderId)}</td>
      </tr>`
  });
}

async function loadAuditRecords() {
  const category = document.getElementById('audit-category').value;
  const hours = document.getElementById('audit-hours').value || '24';

  return loadCollection({
    url: endpoints.audit(category, hours, 100),
    bodyId: 'audit-body',
    emptyId: 'audit-empty',
    errorId: 'audit-error',
    statusId: 'audit-status',
    emptyMessage: 'No recent audit records.',
    renderRow: item => `
      <tr>
        <td>${escapeHtml(fmtDate(item.occurredAt))}</td>
        <td>${renderBadge(item.category, 'category')}</td>
        <td class="primary-text">${escapeHtml(titleCase(item.action))}</td>
        <td>${renderBadge(item.outcome, 'outcome')}</td>
        <td class="monospace">${escapeHtml(item.correlationId)}</td>
        <td class="monospace">${escapeHtml(item.idempotencyKey ?? '—')}</td>
        <td class="monospace">${escapeHtml(item.resourceId ?? '—')}</td>
        <td>${escapeHtml(item.details)}</td>
      </tr>`
  });
}

async function loadIssues() {
  const category = document.getElementById('issue-category').value;
  const hours = document.getElementById('issue-hours').value || '24';

  return loadCollection({
    url: endpoints.issues(category, hours),
    bodyId: 'issues-body',
    emptyId: 'issues-empty',
    errorId: 'issues-error',
    statusId: 'issues-status',
    emptyMessage: 'No recent issues.',
    renderRow: item => `
      <tr>
        <td>${escapeHtml(fmtDate(item.occurredAt))}</td>
        <td>${renderBadge(item.category, 'category')}</td>
        <td>${renderBadge(item.severity, 'severity')}</td>
        <td>${escapeHtml(item.source)}</td>
        <td>
          <div class="primary-text">${escapeHtml(item.message)}</div>
          <div class="secondary-text">${escapeHtml(item.details ?? '—')}</div>
        </td>
        <td class="wrap-details">${escapeHtml(item.details ?? '—')}</td>
      </tr>`
  });
}

function updateSummary({ orders, positions, events, issues }) {
  const activeOrders = orders.filter(order => !['filled', 'settled', 'rejected', 'cancelled', 'canceled'].includes(normalizeToken(order.status))).length;
  const totalContracts = positions.reduce((sum, position) => sum + Number(position.contracts || 0), 0);
  const openPositions = positions.filter(position => Number(position.contracts || 0) > 0).length;

  setText('metric-active-orders', fmtNumber(activeOrders));
  setText('metric-total-orders', `${fmtNumber(orders.length)} total orders`);
  setText('metric-open-positions', fmtNumber(openPositions));
  setText('metric-total-contracts', `${fmtNumber(totalContracts)} contracts`);
  setText('metric-recent-events', fmtNumber(events.length));
  setText('metric-recent-issues', fmtNumber(issues.length));
  setText('last-refresh', `Last refreshed ${new Date().toLocaleTimeString()}`);
}

async function refreshAll() {
  const [orders, positions, events, auditRecords, issues] = await Promise.all([
    loadOrders(),
    loadPositions(),
    loadEvents(),
    loadAuditRecords(),
    loadIssues(),
  ]);

  updateSummary({ orders, positions, events, issues, auditRecords });
}

document.getElementById('refresh-all').addEventListener('click', refreshAll);
document.getElementById('audit-refresh').addEventListener('click', async () => {
  const [orders, positions, events, issues] = await Promise.all([loadOrders(), loadPositions(), loadEvents(), loadIssues()]);
  const auditRecords = await loadAuditRecords();
  updateSummary({ orders, positions, events, issues, auditRecords });
});
document.getElementById('issues-refresh').addEventListener('click', async () => {
  const [orders, positions, events, auditRecords] = await Promise.all([loadOrders(), loadPositions(), loadEvents(), loadAuditRecords()]);
  const issues = await loadIssues();
  updateSummary({ orders, positions, events, issues, auditRecords });
});

refreshAll();
