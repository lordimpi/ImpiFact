// End-of-test summary shared by the scenarios: the full k6 summary as JSON in RESULTS_DIR, and a compact table per
// endpoint tag on stdout. It is written here instead of importing the k6-summary jslib because the load-test network
// has no internet access (see load-tests/compose.load.yml), so remote modules cannot be fetched.

import { RESULTS_DIR } from './config.js';

function format(value, digits = 1) {
  return value === undefined || value === null ? '-' : Number(value).toFixed(digits);
}

function endpointsOf(data) {
  const names = new Set();
  for (const key of Object.keys(data.metrics)) {
    const match = /^http_req_duration\{endpoint:([^}]+)\}$/.exec(key);
    if (match) {
      names.add(match[1]);
    }
  }
  return [...names].sort();
}

function thresholdLines(data) {
  const lines = [];
  for (const [name, metric] of Object.entries(data.metrics)) {
    for (const [expression, result] of Object.entries(metric.thresholds || {})) {
      lines.push(`  ${result.ok ? 'PASS' : 'FAIL'}  ${name}: ${expression}`);
    }
  }
  return lines.sort();
}

export function compactSummary(data, title) {
  const seconds = data.state.testRunDurationMs / 1000;
  const lines = [];
  lines.push('');
  lines.push(`=== ${title}: ${format(seconds, 0)} s, max VUs ${data.metrics.vus_max ? data.metrics.vus_max.values.max : '-'} ===`);
  lines.push('endpoint                         requests     rps   p50 ms   p95 ms   p99 ms   max ms  failed %');
  for (const endpoint of endpointsOf(data)) {
    const duration = data.metrics[`http_req_duration{endpoint:${endpoint}}`].values;
    const reqs = data.metrics[`http_reqs{endpoint:${endpoint}}`];
    const failed = data.metrics[`http_req_failed{endpoint:${endpoint}}`];
    const count = reqs ? reqs.values.count : 0;
    lines.push(
      [
        endpoint.padEnd(30),
        String(count).padStart(10),
        format(reqs ? reqs.values.rate : 0, 1).padStart(7),
        format(duration.med).padStart(8),
        format(duration['p(95)']).padStart(8),
        format(duration['p(99)']).padStart(8),
        format(duration.max).padStart(8),
        format(failed ? failed.values.rate * 100 : 0, 2).padStart(9),
      ].join(' '),
    );
  }
  const total = data.metrics.http_reqs;
  lines.push(`all requests: ${total ? total.values.count : 0} (${format(total ? total.values.rate : 0)} rps)`);
  lines.push('thresholds:');
  lines.push(...thresholdLines(data));
  lines.push('');
  return lines.join('\n');
}

/** handleSummary body: `<RESULTS_DIR>/<name>-summary.json` plus the compact table on stdout. */
export function writeSummary(data, name, title) {
  const summaryName = __ENV.SUMMARY_NAME || name;
  return {
    [`${RESULTS_DIR}/${summaryName}-summary.json`]: JSON.stringify(data, null, 2),
    stdout: compactSummary(data, title),
  };
}
