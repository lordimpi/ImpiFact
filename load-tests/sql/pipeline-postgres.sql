-- What happened behind the API after a load run, on PostgreSQL: documents by status, the outbox by type and status,
-- and how long the Worker took from issuing to the DIAN decision (here, the fake DIAN).
--   load-tests/run.sh compose exec -T postgres psql -U impifact -d impifact < load-tests/sql/pipeline-postgres.sql
-- Statuses: documents 2 Queued, 3 Sent, 4 Accepted…; outbox 0 Pending, 1 Processing, 2 Done, 3 Failed.

SELECT status, count(*) AS documents
FROM documents
GROUP BY status
ORDER BY status;

SELECT type, status, count(*) AS messages, max(attempts) AS max_attempts
FROM outbox_messages
GROUP BY type, status
ORDER BY type, status;

SELECT count(*) AS accepted,
       round(percentile_cont(0.50) WITHIN GROUP (ORDER BY extract(epoch FROM accepted_at - created_at))::numeric, 1) AS p50_s,
       round(percentile_cont(0.95) WITHIN GROUP (ORDER BY extract(epoch FROM accepted_at - created_at))::numeric, 1) AS p95_s,
       round(max(extract(epoch FROM accepted_at - created_at))::numeric, 1) AS max_s,
       min(created_at) AS first_created,
       max(accepted_at) AS last_accepted
FROM documents
WHERE accepted_at IS NOT NULL;
