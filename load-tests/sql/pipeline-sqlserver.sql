-- The SQL Server version of pipeline-postgres.sql: documents by status, the outbox by type and status, and the time
-- from issuing to the (fake) DIAN decision.
--   load-tests/run.sh sql load-tests/sql/pipeline-sqlserver.sql   (with DB=sqlserver)
-- Statuses: documents 2 Queued, 3 Sent, 4 Accepted…; outbox 0 Pending, 1 Processing, 2 Done, 3 Failed.
SET NOCOUNT ON;

SELECT status, COUNT(*) AS documents
FROM dbo.documents WITH (NOLOCK)
GROUP BY status
ORDER BY status;

SELECT type, status, COUNT(*) AS messages, MAX(attempts) AS max_attempts
FROM dbo.outbox_messages WITH (NOLOCK)
GROUP BY type, status
ORDER BY type, status;

SELECT TOP (1)
    COUNT(*) OVER () AS accepted,
    PERCENTILE_CONT(0.50) WITHIN GROUP (ORDER BY DATEDIFF_BIG(millisecond, created_at, accepted_at)) OVER () / 1000.0 AS p50_s,
    PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY DATEDIFF_BIG(millisecond, created_at, accepted_at)) OVER () / 1000.0 AS p95_s,
    MAX(DATEDIFF_BIG(millisecond, created_at, accepted_at)) OVER () / 1000.0 AS max_s
FROM dbo.documents WITH (NOLOCK)
WHERE accepted_at IS NOT NULL;
GO
