// Scenario 2: queries. A mix of what the panel and an integration read:
// - GET /api/v1/documents with filters (kind, status, dates, customer, number) and cursor paging (first page, some
//   with include_total=true, then up to two more pages through next_cursor);
// - GET /api/v1/documents/{id} (document detail);
// - GET /api/v1/dashboard/summary (dashboard).
//
// setup() makes sure the company has at least SEED_DOCS documents (it issues the missing ones) and collects their ids.
//
// Run (from the repository root; see docs/Pruebas de carga.md):
//   docker compose ... run --rm -e IMPIFACT_API_KEY k6 run scenarios/queries.js
// Optional: -e VUS=40 -e RAMP_UP=1m -e HOLD=3m -e RAMP_DOWN=30s -e SEED_DOCS=300 -e SUMMARY_NAME=queries

import http from 'k6/http';
import { check, fail } from 'k6';
import { BASE_URL, SUMMARY_TREND_STATS, authHeaders, endpointThresholds, intEnv, rampingStages } from '../lib/config.js';
import { CUSTOMER_POOL, colombiaNow, customerNit, issueInvoice } from '../lib/invoice.js';
import { writeSummary } from '../lib/summary.js';

const SEED_DOCS = intEnv('SEED_DOCS', 300);
const LIST_PATH = '/api/v1/documents';
const DETAIL_PATH = '/api/v1/documents/{id}';
const DASHBOARD_PATH = '/api/v1/dashboard/summary';

export const options = {
  scenarios: {
    queries: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: rampingStages({ vus: 40, rampUp: '1m', hold: '3m', rampDown: '30s' }),
      gracefulRampDown: '30s',
    },
  },
  thresholds: Object.assign(
    {},
    endpointThresholds('list_documents', 500, 1000),
    endpointThresholds('list_documents_next_page', 500, 1000),
    endpointThresholds('get_document', 300, 600),
    endpointThresholds('dashboard_summary', 800, 1500),
  ),
  summaryTrendStats: SUMMARY_TREND_STATS,
  // setup() may issue a few hundred invoices on a fresh stack.
  setupTimeout: '10m',
};

function get(path, query, endpoint, name) {
  const url = query ? `${BASE_URL}${path}?${query}` : `${BASE_URL}${path}`;
  // `name` groups the URLs of one endpoint (each query string would otherwise be its own series).
  return http.get(url, { headers: authHeaders(), tags: { endpoint, name } });
}

function listPage(query) {
  // Tagged apart so setup traffic stays out of the thresholds.
  const response = get(LIST_PATH, query, 'setup_list', `GET ${LIST_PATH} (setup)`);
  if (response.status !== 200) {
    fail(`setup: list returned ${response.status}: ${String(response.body).slice(0, 300)}`);
  }
  return response.json();
}

export function setup() {
  let total = listPage('limit=1&include_total=true').total_count || 0;
  for (let index = total; index < SEED_DOCS; index++) {
    const response = issueInvoice(index, 'setup_issue');
    if (response.status !== 201 && response.status !== 200) {
      fail(`setup: issuing returned ${response.status}: ${String(response.body).slice(0, 300)}`);
    }
  }

  const ids = [];
  const numbers = [];
  let cursor = null;
  do {
    const page = listPage(`limit=200${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
    for (const item of page.items) {
      ids.push(item.id);
      numbers.push(String(item.number));
    }
    cursor = page.next_cursor;
  } while (cursor && ids.length < 2000);

  total = listPage('limit=1&include_total=true').total_count;
  console.log(`setup: ${total} documents in the company, ${ids.length} ids collected`);
  return { ids, numbers };
}

function pick(list) {
  return list[Math.floor(Math.random() * list.length)];
}

/** A random combination of the list filters, as the panel builds it. */
function listQuery(data) {
  const today = colombiaNow().date;
  const params = ['limit=50'];
  const roll = Math.random();
  if (roll < 0.25) {
    params.push('kind=invoice');
  } else if (roll < 0.45) {
    params.push(`status=${pick(['Queued', 'Accepted', 'Queued,Accepted', 'Sent,Unconfirmed'])}`);
  } else if (roll < 0.65) {
    params.push(`from=${today}`, `to=${today}`);
  } else if (roll < 0.8) {
    params.push(`customer=${customerNit(Math.floor(Math.random() * CUSTOMER_POOL))}`);
  } else if (roll < 0.9) {
    params.push(`customer=${encodeURIComponent('carga')}`);
  } else {
    params.push(`number=${pick(data.numbers)}`);
  }
  if (Math.random() < 0.3) {
    params.push('include_total=true');
  }
  return params.join('&');
}

function browseList(data) {
  const query = listQuery(data);
  let response = get(LIST_PATH, query, 'list_documents', `GET ${LIST_PATH}`);
  check(response, { 'list 200': (r) => r.status === 200 }, { endpoint: 'list_documents' });
  for (let page = 0; page < 2 && response.status === 200; page++) {
    const cursor = response.json('next_cursor');
    if (!cursor) {
      break;
    }
    response = get(LIST_PATH, `limit=50&cursor=${encodeURIComponent(cursor)}`, 'list_documents_next_page', `GET ${LIST_PATH} (next page)`);
    check(response, { 'next page 200': (r) => r.status === 200 }, { endpoint: 'list_documents_next_page' });
  }
}

export default function (data) {
  const roll = Math.random();
  if (roll < 0.4) {
    browseList(data);
  } else if (roll < 0.8) {
    const id = pick(data.ids);
    const response = get(DETAIL_PATH.replace('{id}', id), null, 'get_document', `GET ${DETAIL_PATH}`);
    check(response, { 'detail 200': (r) => r.status === 200 && r.json('id') === id }, { endpoint: 'get_document' });
  } else {
    const response = get(DASHBOARD_PATH, null, 'dashboard_summary', `GET ${DASHBOARD_PATH}`);
    check(response, { 'dashboard 200': (r) => r.status === 200 }, { endpoint: 'dashboard_summary' });
  }
}

export function handleSummary(data) {
  return writeSummary(data, 'queries', 'Queries');
}
