// Scenario 1: invoice issuing. Each iteration issues one invoice with POST /api/v1/documents/invoices, the real
// endpoint: the API validates, numbers, builds and signs the UBL XML, stores it and queues it in the outbox for the
// Worker. The Worker sends it to the fake DIAN of the load-test stack (never to the real one).
//
// Run (from the repository root; see docs/Pruebas de carga.md):
//   docker compose ... run --rm -e IMPIFACT_API_KEY k6 run scenarios/issue-invoices.js
// Optional: -e VUS=20 -e RAMP_UP=1m -e HOLD=3m -e RAMP_DOWN=30s -e THINK_MS=0 -e SUMMARY_NAME=issue-invoices

import { check, sleep } from 'k6';
import { SUMMARY_TREND_STATS, endpointThresholds, intEnv, rampingStages } from '../lib/config.js';
import { CUSTOMER_POOL, issueInvoice } from '../lib/invoice.js';
import { writeSummary } from '../lib/summary.js';

const THINK_MS = intEnv('THINK_MS', 0);

export const options = {
  scenarios: {
    issue_invoices: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: rampingStages({ vus: 20, rampUp: '1m', hold: '3m', rampDown: '30s' }),
      gracefulRampDown: '30s',
    },
  },
  // Targets for the local stack: signing and storing an invoice is the heaviest synchronous call of the API.
  thresholds: endpointThresholds('issue_invoice', 1500, 3000),
  summaryTrendStats: SUMMARY_TREND_STATS,
};

export default function () {
  const index = __VU * 100000 + __ITER;
  const response = issueInvoice(index % (CUSTOMER_POOL * 1000));
  check(
    response,
    {
      'invoice issued (201 or 200)': (r) => r.status === 201 || r.status === 200,
      // The CUFE (unique invoice code) is the proof that the API built and signed the invoice.
      'response has the CUFE': (r) => typeof r.body === 'string' && /"(cufe|uuid)"\s*:\s*"[0-9a-f]{96}"/.test(r.body),
    },
    { endpoint: 'issue_invoice' },
  );
  if (response.status >= 400 && __ENV.LOG_ERRORS) {
    console.warn(`issue ${response.status}: ${String(response.body).slice(0, 400)}`);
  }
  if (THINK_MS > 0) {
    sleep(THINK_MS / 1000);
  }
}

export function handleSummary(data) {
  return writeSummary(data, 'issue-invoices', 'Invoice issuing');
}
