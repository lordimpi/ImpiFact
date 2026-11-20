-- One sample of the locks around the outbox on SQL Server (load-tests/run.sh lock-watch runs it every second).
-- Columns, in CSV order:
--   at_utc               sample time
--   pending, processing  outbox rows by status (read without locks)
--   escalation_attempts  times SQL Server tried to escalate row or page locks of outbox_messages to the table
--   escalations          times it succeeded (both cumulative since the engine started; sys.dm_db_index_operational_stats)
--   table_locks          S, U, X, SIX or UIX locks held or requested on the whole outbox_messages table right now
--   row_page_locks       KEY, PAGE or RID locks on outbox_messages right now
--   blocked              requests of the database waiting on another session's lock right now
--   max_block_ms         longest of those waits
--   blocked_on           wait type and start of the statement of the longest blocked request
SET NOCOUNT ON;
DECLARE @table int = OBJECT_ID('dbo.outbox_messages');
DECLARE @db int = DB_ID();

SELECT
    CONVERT(varchar(23), SYSUTCDATETIME(), 121) AS at_utc,
    (SELECT COUNT(*) FROM dbo.outbox_messages WITH (NOLOCK) WHERE status = 0) AS pending,
    (SELECT COUNT(*) FROM dbo.outbox_messages WITH (NOLOCK) WHERE status = 1) AS processing,
    (SELECT SUM(index_lock_promotion_attempt_count) FROM sys.dm_db_index_operational_stats(@db, @table, NULL, NULL)) AS escalation_attempts,
    (SELECT SUM(index_lock_promotion_count) FROM sys.dm_db_index_operational_stats(@db, @table, NULL, NULL)) AS escalations,
    (SELECT COUNT(*) FROM sys.dm_tran_locks
        WHERE resource_database_id = @db AND resource_type = 'OBJECT' AND resource_associated_entity_id = @table
          AND request_mode IN ('S', 'U', 'X', 'SIX', 'UIX')) AS table_locks,
    (SELECT COUNT(*) FROM sys.dm_tran_locks AS l
        JOIN sys.partitions AS p ON l.resource_associated_entity_id = p.hobt_id
        WHERE l.resource_database_id = @db AND l.resource_type IN ('KEY', 'PAGE', 'RID') AND p.object_id = @table) AS row_page_locks,
    (SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id = @db AND blocking_session_id <> 0) AS blocked,
    (SELECT ISNULL(MAX(wait_time), 0) FROM sys.dm_exec_requests WHERE database_id = @db AND blocking_session_id <> 0) AS max_block_ms,
    (SELECT TOP (1) r.wait_type + ' ' + REPLACE(REPLACE(LEFT(LTRIM(t.text), 60), CHAR(10), ' '), CHAR(13), ' ')
        FROM sys.dm_exec_requests AS r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) AS t
        WHERE r.database_id = @db AND r.blocking_session_id <> 0
        ORDER BY r.wait_time DESC) AS blocked_on;
GO
