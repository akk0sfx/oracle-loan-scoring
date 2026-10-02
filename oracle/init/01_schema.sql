-- =============================================================================
-- 01_schema.sql — tables of the SCORING schema (docs/CONTRACTS.md, section 6).
--
-- How the script reaches the database: the gvenzl/oracle-free image runs *.sql from
-- /container-entrypoint-initdb.d with `sqlplus -s / as sysdba`, i.e. as SYS and in the
-- root container CDB$ROOT. Our objects must live in PDB FREEPDB1, schema SCORING,
-- so the first two commands switch the container and the default schema.
-- Details: docs/DECISIONS.md, ADR-003.
-- =============================================================================

-- The image does not stop initialization on a SQL error. This directive at least aborts
-- the current script instead of leaving half-created objects.
WHENEVER SQLERROR EXIT SQL.SQLCODE

ALTER SESSION SET CONTAINER = FREEPDB1;
-- CURRENT_SCHEMA changes the default schema for unqualified names, not the user.
-- Objects are created by SYS but owned by SCORING.
ALTER SESSION SET CURRENT_SCHEMA = SCORING;

-- -----------------------------------------------------------------------------
-- RATE_GRID — base rates per loan purpose.
-- Rates live in a table rather than hard-coded in the package, so a rate can be
-- changed with a plain UPDATE without recompiling PL/SQL.
-- -----------------------------------------------------------------------------
CREATE TABLE RATE_GRID (
    PURPOSE_CODE VARCHAR2(20) NOT NULL,
    BASE_RATE    NUMBER(5,2)  NOT NULL,
    CONSTRAINT PK_RATE_GRID PRIMARY KEY (PURPOSE_CODE),
    -- A zero rate would cause division by zero in the annuity formula.
    CONSTRAINT CK_RATE_GRID_BASE_RATE CHECK (BASE_RATE > 0)
);

COMMENT ON TABLE  RATE_GRID              IS 'Base annual rates per loan purpose';
COMMENT ON COLUMN RATE_GRID.PURPOSE_CODE IS 'Loan purpose code (= UsrLoanPurpose.UsrCode in Creatio)';
COMMENT ON COLUMN RATE_GRID.BASE_RATE    IS 'Base rate, % per annum, before the high-score discount';

-- -----------------------------------------------------------------------------
-- SCORING_LOG — log of every calculation. Rows are written by PKG_LOAN_SCORING.EVALUATE.
-- -----------------------------------------------------------------------------
CREATE TABLE SCORING_LOG (
    -- IDENTITY (Oracle 12c+) replaces the SEQUENCE + trigger pair.
    -- ALWAYS forbids inserting IDs by hand, so keys never diverge from the sequence.
    ID                  NUMBER GENERATED ALWAYS AS IDENTITY,
    -- Creatio application GUID as text (36 chars with dashes).
    -- No foreign key: applications live in another system.
    APPLICATION_ID      VARCHAR2(36)  NOT NULL,
    AMOUNT              NUMBER(14,2),
    TERM_MONTHS         NUMBER(3),
    MONTHLY_INCOME      NUMBER(14,2),
    PURPOSE_CODE        VARCHAR2(20),
    SCORE               NUMBER(4),
    DECISION            VARCHAR2(10),
    RATE                NUMBER(5,2),
    MONTHLY_PAYMENT     NUMBER(14,2),
    MAX_APPROVED_AMOUNT NUMBER(14,2),
    REASONS             VARCHAR2(400),
    CREATED_AT          TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL,
    CONSTRAINT PK_SCORING_LOG PRIMARY KEY (ID),
    -- CHECK is the last line of defence: even a bug in the package cannot put garbage in the log.
    -- NULL passes a CHECK, so these constraints do not make the columns mandatory.
    CONSTRAINT CK_SCORING_LOG_DECISION CHECK (DECISION IN ('APPROVE', 'REVIEW', 'REJECT')),
    CONSTRAINT CK_SCORING_LOG_SCORE    CHECK (SCORE BETWEEN 0 AND 1000)
);

-- History is queried by application (GET_HISTORY); without an index that is a full table scan.
CREATE INDEX IX_SCORING_LOG_APP ON SCORING_LOG (APPLICATION_ID);

COMMENT ON TABLE  SCORING_LOG                     IS 'Scoring calculation log, one row per EVALUATE call';
COMMENT ON COLUMN SCORING_LOG.ID                  IS 'Surrogate key (identity)';
COMMENT ON COLUMN SCORING_LOG.APPLICATION_ID      IS 'UsrLoanApplication Id in Creatio (GUID as string)';
COMMENT ON COLUMN SCORING_LOG.AMOUNT              IS 'Requested amount, RUB';
COMMENT ON COLUMN SCORING_LOG.TERM_MONTHS         IS 'Term, months';
COMMENT ON COLUMN SCORING_LOG.MONTHLY_INCOME      IS 'Monthly income, RUB';
COMMENT ON COLUMN SCORING_LOG.PURPOSE_CODE        IS 'Loan purpose code';
COMMENT ON COLUMN SCORING_LOG.SCORE               IS 'Score 0..1000';
COMMENT ON COLUMN SCORING_LOG.DECISION            IS 'Decision: APPROVE / REVIEW / REJECT';
COMMENT ON COLUMN SCORING_LOG.RATE                IS 'Final rate, % per annum; NULL for REJECT';
COMMENT ON COLUMN SCORING_LOG.MONTHLY_PAYMENT     IS 'Monthly payment, RUB; NULL for REJECT';
COMMENT ON COLUMN SCORING_LOG.MAX_APPROVED_AMOUNT IS 'Maximum approved amount, RUB; 0 for REJECT';
COMMENT ON COLUMN SCORING_LOG.REASONS             IS 'Comma-separated reason codes';
COMMENT ON COLUMN SCORING_LOG.CREATED_AT          IS 'Calculation timestamp (database server time)';
