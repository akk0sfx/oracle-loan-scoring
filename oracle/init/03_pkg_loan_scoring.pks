-- =============================================================================
-- 03_pkg_loan_scoring.pks — SPECIFICATION of package PKG_LOAN_SCORING (CONTRACTS.md, section 6).
--
-- Specification vs body. An Oracle package consists of two separately compiled parts:
--   * specification (PACKAGE) — the public interface: signatures, constants, exceptions.
--     Think of it as a C# interface plus public static members.
--   * body (PACKAGE BODY, the .pkb file) — implementation and private subprograms.
-- Why split them: callers (our API, other packages) depend only on the specification.
-- Recompiling the body does not invalidate dependent objects; changing the specification
-- invalidates all of them. So the specification is the contract and changes rarely.
--
-- Constants. Thresholds and bonuses of the algorithm live here, not in the body, because:
--   1) every number from contract section 5 gets a name, and the formula in the body reads as text;
--   2) tests and other PL/SQL can refer to PKG_LOAN_SCORING.C_... instead of duplicating the number.
-- The price: editing a constant in the specification recompiles everything that depends on it.
--
-- Package state. Package-level variables (in the spec or the body) live for the whole
-- session: they are initialized on first use and keep their values between calls.
-- That is typically used for caches (e.g. load RATE_GRID into a collection once).
-- Here there is deliberately NO mutable state, only CONSTANTs. The Scoring API uses a
-- connection pool, so one Oracle session serves different HTTP requests and package state
-- would "leak" between them. Also, recompiling a package makes every session that holds
-- state fail with ORA-04068 "existing state of packages has been discarded".
--
-- Runs as SYS in CDB$ROOT, so switch first (ADR-003).
-- =============================================================================

WHENEVER SQLERROR EXIT SQL.SQLCODE

ALTER SESSION SET CONTAINER = FREEPDB1;
ALTER SESSION SET CURRENT_SCHEMA = SCORING;

CREATE OR REPLACE PACKAGE PKG_LOAN_SCORING
AS
    -- ---------------------------------------------------------------------
    -- Input ranges (CONTRACTS.md, section 2.3 "Validation").
    -- ---------------------------------------------------------------------
    C_MIN_AMOUNT        CONSTANT NUMBER := 50000;
    C_MAX_AMOUNT        CONSTANT NUMBER := 5000000;
    C_MIN_TERM          CONSTANT NUMBER := 6;
    C_MAX_TERM          CONSTANT NUMBER := 84;
    C_MAX_INCOME        CONSTANT NUMBER := 10000000;   -- income: > 0 and <= C_MAX_INCOME

    -- ---------------------------------------------------------------------
    -- Scoring algorithm (CONTRACTS.md, section 5).
    -- ---------------------------------------------------------------------
    C_DTI_BASE_RATE     CONSTANT NUMBER := 20.00;   -- rate of the estimated payment used for DTI
    C_BASE_SCORE        CONSTANT NUMBER := 600;

    C_DTI_LOW_LIMIT     CONSTANT NUMBER := 0.30;    -- dti <  0.30         -> DTI_LOW
    C_DTI_HIGH_LIMIT    CONSTANT NUMBER := 0.50;    -- 0.30 <= dti <= 0.50 -> DTI_MEDIUM, else DTI_HIGH
    C_DTI_LOW_BONUS     CONSTANT NUMBER := 200;
    C_DTI_MEDIUM_BONUS  CONSTANT NUMBER := 50;
    C_DTI_HIGH_PENALTY  CONSTANT NUMBER := -250;

    C_LONG_TERM_LIMIT   CONSTANT NUMBER := 60;      -- termMonths > 60   -> LONG_TERM
    C_LONG_TERM_PENALTY CONSTANT NUMBER := -50;
    C_LARGE_AMOUNT_LIMIT   CONSTANT NUMBER := 3000000;  -- amount > 3 000 000 -> LARGE_AMOUNT
    C_LARGE_AMOUNT_PENALTY CONSTANT NUMBER := -50;

    C_BONUS_MORTGAGE    CONSTANT NUMBER := 50;
    C_BONUS_CAR         CONSTANT NUMBER := 30;
    C_BONUS_REFINANCE   CONSTANT NUMBER := 10;
    C_BONUS_CONSUMER    CONSTANT NUMBER := 0;
    C_BONUS_OTHER       CONSTANT NUMBER := -30;

    C_MIN_SCORE         CONSTANT NUMBER := 0;
    C_MAX_SCORE         CONSTANT NUMBER := 1000;

    C_APPROVE_THRESHOLD CONSTANT NUMBER := 700;     -- score >= 700 -> APPROVE
    C_REVIEW_THRESHOLD  CONSTANT NUMBER := 500;     -- 500..699     -> REVIEW, below -> REJECT

    C_DISCOUNT_HIGH_SCORE CONSTANT NUMBER := 800;   -- score >= 800 -> discount 2.00
    C_DISCOUNT_HIGH     CONSTANT NUMBER := 2.00;
    C_DISCOUNT_MID      CONSTANT NUMBER := 1.00;    -- 700..799     -> discount 1.00

    C_MAX_PAYMENT_SHARE CONSTANT NUMBER := 0.40;    -- allowed share of income spent on the payment
    C_MAX_AMOUNT_STEP   CONSTANT NUMBER := 1000;    -- round down to 1000

    C_DECISION_APPROVE  CONSTANT VARCHAR2(10) := 'APPROVE';
    C_DECISION_REVIEW   CONSTANT VARCHAR2(10) := 'REVIEW';
    C_DECISION_REJECT   CONSTANT VARCHAR2(10) := 'REJECT';

    -- ---------------------------------------------------------------------
    -- Named exceptions for codes -20001..-20005 (CONTRACTS.md, section 6).
    -- PRAGMA EXCEPTION_INIT binds a name to an error number, so calling PL/SQL can write
    -- WHEN PKG_LOAN_SCORING.E_INVALID_AMOUNT THEN instead of inspecting SQLCODE.
    -- The body still raises via RAISE_APPLICATION_ERROR with the number, because that is
    -- the only way to attach a message text to the error.
    -- ---------------------------------------------------------------------
    E_INVALID_AMOUNT         EXCEPTION;
    PRAGMA EXCEPTION_INIT(E_INVALID_AMOUNT, -20001);
    E_INVALID_TERM           EXCEPTION;
    PRAGMA EXCEPTION_INIT(E_INVALID_TERM, -20002);
    E_INVALID_INCOME         EXCEPTION;
    PRAGMA EXCEPTION_INIT(E_INVALID_INCOME, -20003);
    E_UNKNOWN_PURPOSE        EXCEPTION;
    PRAGMA EXCEPTION_INIT(E_UNKNOWN_PURPOSE, -20004);
    E_EMPTY_APPLICATION_ID   EXCEPTION;
    PRAGMA EXCEPTION_INIT(E_EMPTY_APPLICATION_ID, -20005);

    -- Annuity payment, not rounded:
    --   r = p_rate / 12 / 100; payment = p_amount * r / (1 - (1 + r)^(-p_term)).
    -- p_rate is % per annum (20.00, not 0.20). The caller rounds, because DTI and
    -- maxApprovedAmount need full precision.
    FUNCTION CALC_ANNUITY(p_amount NUMBER, p_rate NUMBER, p_term NUMBER) RETURN NUMBER;

    -- Full scoring calculation (steps 1–7 of section 5) plus a SCORING_LOG row.
    -- Does not COMMIT: the transaction is owned by the caller (API).
    PROCEDURE EVALUATE(
        p_application_id IN VARCHAR2, p_amount IN NUMBER, p_term IN NUMBER,
        p_income IN NUMBER, p_purpose IN VARCHAR2,
        o_score OUT NUMBER, o_decision OUT VARCHAR2, o_rate OUT NUMBER,
        o_payment OUT NUMBER, o_max_amount OUT NUMBER, o_reasons OUT VARCHAR2,
        o_log_id OUT NUMBER);

    -- Calculation history for an application, newest first. The receiver closes the cursor.
    FUNCTION GET_HISTORY(p_application_id VARCHAR2) RETURN SYS_REFCURSOR;
END PKG_LOAN_SCORING;
/

-- SHOW ERRORS prints compilation errors. Without it sqlplus only says
-- "Warning: Package created with compilation errors".
SHOW ERRORS
