-- =============================================================================
-- 02_seed.sql — RATE_GRID base rates (docs/CONTRACTS.md, section 2.2).
--
-- The script is idempotent: MERGE inserts missing rows and updates the rate of existing
-- ones, so a re-run neither fails on the PK nor creates duplicates.
-- Container and schema switch — same as in 01_schema.sql (ADR-003).
-- =============================================================================

WHENEVER SQLERROR EXIT SQL.SQLCODE

ALTER SESSION SET CONTAINER = FREEPDB1;
ALTER SESSION SET CURRENT_SCHEMA = SCORING;

MERGE INTO RATE_GRID t
USING (
    -- The source is a plain SELECT FROM DUAL glued with UNION ALL,
    -- so MERGE works on a set of rows rather than a single one.
              SELECT 'CONSUMER'  AS PURPOSE_CODE, 21.90 AS BASE_RATE FROM DUAL
    UNION ALL SELECT 'CAR',                       17.90              FROM DUAL
    UNION ALL SELECT 'MORTGAGE',                  14.50              FROM DUAL
    UNION ALL SELECT 'REFINANCE',                 19.50              FROM DUAL
    UNION ALL SELECT 'OTHER',                     24.90              FROM DUAL
) s
ON (t.PURPOSE_CODE = s.PURPOSE_CODE)
WHEN MATCHED THEN
    UPDATE SET t.BASE_RATE = s.BASE_RATE
WHEN NOT MATCHED THEN
    INSERT (PURPOSE_CODE, BASE_RATE) VALUES (s.PURPOSE_CODE, s.BASE_RATE);

-- Reference data is committed right away. An init script is a standalone sqlplus session;
-- there is no "caller" that would commit for us.
COMMIT;
