// Shared settings of the ImpiFact load tests. Everything that changes per run comes from k6 environment variables
// (`-e NAME=value` or the container environment); no secret is ever written in these files.

export const BASE_URL = (__ENV.BASE_URL || 'http://api:8080').replace(/\/+$/, '');

/** Where handleSummary writes the JSON summary (a bind mount of the k6 container). */
export const RESULTS_DIR = (__ENV.RESULTS_DIR || '/results').replace(/\/+$/, '');

/** A company API key with documents:read and documents:write, as printed by `migrator seed-demo`. */
export function apiKey() {
  const key = __ENV.IMPIFACT_API_KEY;
  if (!key) {
    throw new Error('Set IMPIFACT_API_KEY (the API key printed by "migrator seed-demo") in the k6 environment.');
  }
  return key;
}

export function authHeaders(extra = {}) {
  return Object.assign(
    {
      Authorization: `Bearer ${apiKey()}`,
      Accept: 'application/json',
    },
    extra,
  );
}

/** Integer from an environment variable, or the default when it is missing or not a number. */
export function intEnv(name, fallback) {
  const value = parseInt(__ENV[name], 10);
  return Number.isFinite(value) ? value : fallback;
}

/** Duration string (k6 format, for example 30s or 2m) from an environment variable, or the default. */
export function durationEnv(name, fallback) {
  return __ENV[name] || fallback;
}

/**
 * The ramping-vus stages every scenario uses: ramp up to the target, hold it, ramp down. VUS, RAMP_UP, HOLD and
 * RAMP_DOWN override the defaults of each scenario, so a short smoke run is `-e VUS=2 -e HOLD=20s`.
 */
export function rampingStages(defaults) {
  const target = intEnv('VUS', defaults.vus);
  return [
    { duration: durationEnv('RAMP_UP', defaults.rampUp), target },
    { duration: durationEnv('HOLD', defaults.hold), target },
    { duration: durationEnv('RAMP_DOWN', defaults.rampDown), target: 0 },
  ];
}

/** Latency and error thresholds for one endpoint tag, plus a request counter so the summary reports its rate. */
export function endpointThresholds(endpoint, p95Ms, p99Ms) {
  return {
    [`http_req_duration{endpoint:${endpoint}}`]: [`p(95)<${p95Ms}`, `p(99)<${p99Ms}`],
    [`http_req_failed{endpoint:${endpoint}}`]: ['rate<0.01'],
    [`checks{endpoint:${endpoint}}`]: ['rate>0.99'],
    [`http_reqs{endpoint:${endpoint}}`]: ['count>0'],
  };
}

export const SUMMARY_TREND_STATS = ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'];
