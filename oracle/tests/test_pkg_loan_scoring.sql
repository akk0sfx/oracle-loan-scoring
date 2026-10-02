-- =============================================================================
-- test_pkg_loan_scoring.sql — tests for PKG_LOAN_SCORING in plain PL/SQL.
--
-- Run: scripts/oracle-test.sh (connects as SCORING to FREEPDB1).
-- Expected numbers are computed independently: scripts/scoring_reference.py.
--
-- Layout:
--   * TEST_ASSERT — a temporary package with assert_equals. Anonymous blocks cannot
--     share subprograms, so the common helper has to be a stored object.
--     A package is needed for overloading: standalone procedures cannot be overloaded.
--   * One anonymous block — one scenario. A failed assert raises an error, and
--     WHENEVER SQLERROR EXIT FAILURE ROLLBACK ends sqlplus with a non-zero code.
--   * EVALUATE writes to SCORING_LOG. The final ROLLBACK removes all test rows.
--     ROLLBACK comes BEFORE DROP PACKAGE: DDL in Oracle does an implicit COMMIT, and in
--     the opposite order the test rows would be committed.
-- =============================================================================

WHENEVER SQLERROR EXIT FAILURE ROLLBACK
WHENEVER OSERROR EXIT FAILURE
SET SERVEROUTPUT ON SIZE UNLIMITED
SET FEEDBACK OFF
SET VERIFY OFF

PROMPT === Check: package compiled without errors
DECLARE
    l_invalid PLS_INTEGER;
BEGIN
    SELECT COUNT(*) INTO l_invalid
      FROM USER_OBJECTS
     WHERE OBJECT_NAME = 'PKG_LOAN_SCORING' AND STATUS <> 'VALID';
    IF l_invalid > 0 THEN
        RAISE_APPLICATION_ERROR(-20999, 'PKG_LOAN_SCORING is INVALID — see USER_ERRORS');
    END IF;
END;
/

-- ---------------------------------------------------------------------------
-- Helper (DDL before the first EVALUATE, so the implicit COMMIT commits nothing).
-- ---------------------------------------------------------------------------
CREATE OR REPLACE PACKAGE TEST_ASSERT AS
    -- Package state is used on purpose: the counter lives for one sqlplus session.
    g_passed PLS_INTEGER := 0;
    PROCEDURE assert_equals(p_name VARCHAR2, p_expected NUMBER,   p_actual NUMBER);
    PROCEDURE assert_equals(p_name VARCHAR2, p_expected VARCHAR2, p_actual VARCHAR2);
    PROCEDURE pass(p_name VARCHAR2);
    PROCEDURE fail(p_name VARCHAR2, p_message VARCHAR2);
END TEST_ASSERT;
/
CREATE OR REPLACE PACKAGE BODY TEST_ASSERT AS
    PROCEDURE pass(p_name VARCHAR2) IS
    BEGIN
        g_passed := g_passed + 1;
        DBMS_OUTPUT.PUT_LINE('  PASS  ' || p_name);
    END pass;

    PROCEDURE fail(p_name VARCHAR2, p_message VARCHAR2) IS
    BEGIN
        DBMS_OUTPUT.PUT_LINE('  FAIL  ' || p_name || ': ' || p_message);
        RAISE_APPLICATION_ERROR(-20999, 'FAIL ' || p_name || ': ' || p_message);
    END fail;

    -- NULL = NULL is UNKNOWN in SQL, so two NULLs are compared explicitly:
    -- for REJECT the expected rate is exactly NULL.
    PROCEDURE assert_equals(p_name VARCHAR2, p_expected NUMBER, p_actual NUMBER) IS
    BEGIN
        IF (p_expected = p_actual) OR (p_expected IS NULL AND p_actual IS NULL) THEN
            pass(p_name);
        ELSE
            fail(p_name, 'expected ' || NVL(TO_CHAR(p_expected), 'NULL')
                      || ', got ' || NVL(TO_CHAR(p_actual), 'NULL'));
        END IF;
    END assert_equals;

    PROCEDURE assert_equals(p_name VARCHAR2, p_expected VARCHAR2, p_actual VARCHAR2) IS
    BEGIN
        IF (p_expected = p_actual) OR (p_expected IS NULL AND p_actual IS NULL) THEN
            pass(p_name);
        ELSE
            fail(p_name, 'expected "' || NVL(p_expected, 'NULL')
                      || '", got "' || NVL(p_actual, 'NULL') || '"');
        END IF;
    END assert_equals;
END TEST_ASSERT;
/

-- ===========================================================================
PROMPT === CALC_ANNUITY: 100 000 at 12% for 12 months
BEGIN
    TEST_ASSERT.assert_equals('annuity rounded to 2',
        8884.88, ROUND(PKG_LOAN_SCORING.CALC_ANNUITY(100000, 12, 12), 2));
    -- Full precision: the function must not round by itself.
    TEST_ASSERT.assert_equals('annuity not rounded inside (10 digits)',
        8884.8788678342, ROUND(PKG_LOAN_SCORING.CALC_ANNUITY(100000, 12, 12), 10));
END;
/

PROMPT === CALC_ANNUITY: term <= 0 -> -20002
BEGIN
    DECLARE l NUMBER; BEGIN
        l := PKG_LOAN_SCORING.CALC_ANNUITY(100000, 12, 0);
        TEST_ASSERT.fail('annuity term 0', 'no exception raised');
    EXCEPTION WHEN PKG_LOAN_SCORING.E_INVALID_TERM THEN TEST_ASSERT.pass('annuity term 0 -> -20002');
    END;
END;
/

PROMPT === CALC_ANNUITY: rate <= 0 -> error
DECLARE
    l NUMBER;
BEGIN
    l := PKG_LOAN_SCORING.CALC_ANNUITY(100000, 0, 12);
    TEST_ASSERT.fail('annuity rate 0', 'no exception raised');
EXCEPTION
    WHEN OTHERS THEN
        -- WHEN OTHERS is safe here: fail() above raises -20999, which is re-raised.
        IF SQLCODE = -20999 THEN RAISE; END IF;
        TEST_ASSERT.assert_equals('annuity rate 0 -> -20000', -20000, SQLCODE);
END;
/

-- ===========================================================================
PROMPT === EVALUATE: APPROVE (500 000 / 24 / 120 000 / CONSUMER)
DECLARE
    l_score NUMBER; l_decision VARCHAR2(10); l_rate NUMBER; l_payment NUMBER;
    l_max NUMBER; l_reasons VARCHAR2(400); l_log_id NUMBER;
    l_row SCORING_LOG%ROWTYPE;
BEGIN
    PKG_LOAN_SCORING.EVALUATE('TEST-APPROVE', 500000, 24, 120000, 'CONSUMER',
        l_score, l_decision, l_rate, l_payment, l_max, l_reasons, l_log_id);
    TEST_ASSERT.assert_equals('approve score',      800,        l_score);
    TEST_ASSERT.assert_equals('approve decision',   'APPROVE',  l_decision);
    TEST_ASSERT.assert_equals('approve rate',       19.90,      l_rate);
    TEST_ASSERT.assert_equals('approve payment',    25423.48,   l_payment);
    TEST_ASSERT.assert_equals('approve max amount', 944000,     l_max);
    TEST_ASSERT.assert_equals('approve reasons',    'DTI_LOW,PURPOSE_CONSUMER', l_reasons);

    -- Log: the row holds the same values the procedure returned.
    SELECT * INTO l_row FROM SCORING_LOG WHERE ID = l_log_id;
    TEST_ASSERT.assert_equals('approve log application_id', 'TEST-APPROVE', l_row.APPLICATION_ID);
    TEST_ASSERT.assert_equals('approve log score',    800,       l_row.SCORE);
    TEST_ASSERT.assert_equals('approve log payment',  25423.48,  l_row.MONTHLY_PAYMENT);
    TEST_ASSERT.assert_equals('approve log reasons',  'DTI_LOW,PURPOSE_CONSUMER', l_row.REASONS);
END;
/

PROMPT === EVALUATE: REVIEW (1 000 000 / 36 / 100 000 / CAR)
DECLARE
    l_score NUMBER; l_decision VARCHAR2(10); l_rate NUMBER; l_payment NUMBER;
    l_max NUMBER; l_reasons VARCHAR2(400); l_log_id NUMBER;
BEGIN
    PKG_LOAN_SCORING.EVALUATE('TEST-REVIEW', 1000000, 36, 100000, 'CAR',
        l_score, l_decision, l_rate, l_payment, l_max, l_reasons, l_log_id);
    TEST_ASSERT.assert_equals('review score',      680,        l_score);
    TEST_ASSERT.assert_equals('review decision',   'REVIEW',   l_decision);
    TEST_ASSERT.assert_equals('review rate (no discount)', 17.90, l_rate);
    TEST_ASSERT.assert_equals('review payment',    36102.25,   l_payment);
    TEST_ASSERT.assert_equals('review max amount', 1107000,    l_max);
    TEST_ASSERT.assert_equals('review reasons',    'DTI_MEDIUM,PURPOSE_CAR', l_reasons);
END;
/

PROMPT === EVALUATE: REJECT (2 000 000 / 24 / 100 000 / OTHER)
DECLARE
    l_score NUMBER; l_decision VARCHAR2(10); l_rate NUMBER; l_payment NUMBER;
    l_max NUMBER; l_reasons VARCHAR2(400); l_log_id NUMBER;
BEGIN
    PKG_LOAN_SCORING.EVALUATE('TEST-REJECT', 2000000, 24, 100000, 'OTHER',
        l_score, l_decision, l_rate, l_payment, l_max, l_reasons, l_log_id);
    TEST_ASSERT.assert_equals('reject score',      320,        l_score);
    TEST_ASSERT.assert_equals('reject decision',   'REJECT',   l_decision);
    TEST_ASSERT.assert_equals('reject rate NULL',  TO_NUMBER(NULL), l_rate);
    TEST_ASSERT.assert_equals('reject payment NULL', TO_NUMBER(NULL), l_payment);
    TEST_ASSERT.assert_equals('reject max amount 0', 0,         l_max);
    TEST_ASSERT.assert_equals('reject reasons',    'DTI_HIGH,PURPOSE_OTHER', l_reasons);
END;
/

PROMPT === EVALUATE: LONG_TERM + LARGE_AMOUNT (4 000 000 / 72 / 400 000 / MORTGAGE)
DECLARE
    l_score NUMBER; l_decision VARCHAR2(10); l_rate NUMBER; l_payment NUMBER;
    l_max NUMBER; l_reasons VARCHAR2(400); l_log_id NUMBER;
BEGIN
    PKG_LOAN_SCORING.EVALUATE('TEST-LONG-LARGE', 4000000, 72, 400000, 'MORTGAGE',
        l_score, l_decision, l_rate, l_payment, l_max, l_reasons, l_log_id);
    TEST_ASSERT.assert_equals('long/large reasons order',
        'DTI_LOW,LONG_TERM,LARGE_AMOUNT,PURPOSE_MORTGAGE', l_reasons);
    TEST_ASSERT.assert_equals('long/large score',    750,       l_score);
    TEST_ASSERT.assert_equals('long/large decision', 'APPROVE', l_decision);
    TEST_ASSERT.assert_equals('long/large rate',     13.50,     l_rate);
    TEST_ASSERT.assert_equals('long/large payment',  81355.85,  l_payment);
    -- The calculated maximum 7 866 674.74 is cut by the 5 000 000 cap.
    TEST_ASSERT.assert_equals('long/large max amount capped', 5000000, l_max);
END;
/

-- ===========================================================================
-- Clamp 0..1000. With the current bonuses the score lies within 220..850, so the public
-- EVALUATE cannot leave the range, and calc_score is private. Therefore both extremes
-- are checked: they give the expected values and stay within [0, 1000].
PROMPT === EVALUATE: lowest possible score (220)
DECLARE
    l_score NUMBER; l_decision VARCHAR2(10); l_rate NUMBER; l_payment NUMBER;
    l_max NUMBER; l_reasons VARCHAR2(400); l_log_id NUMBER;
BEGIN
    PKG_LOAN_SCORING.EVALUATE('TEST-MIN-SCORE', 5000000, 84, 1000, 'OTHER',
        l_score, l_decision, l_rate, l_payment, l_max, l_reasons, l_log_id);
    TEST_ASSERT.assert_equals('min score', 220, l_score);
    TEST_ASSERT.assert_equals('min score within [0..1000]', 1,
        CASE WHEN l_score BETWEEN PKG_LOAN_SCORING.C_MIN_SCORE AND PKG_LOAN_SCORING.C_MAX_SCORE THEN 1 ELSE 0 END);
    TEST_ASSERT.assert_equals('min score reasons',
        'DTI_HIGH,LONG_TERM,LARGE_AMOUNT,PURPOSE_OTHER', l_reasons);
END;
/

PROMPT === EVALUATE: highest possible score (850)
DECLARE
    l_score NUMBER; l_decision VARCHAR2(10); l_rate NUMBER; l_payment NUMBER;
    l_max NUMBER; l_reasons VARCHAR2(400); l_log_id NUMBER;
BEGIN
    PKG_LOAN_SCORING.EVALUATE('TEST-MAX-SCORE', 100000, 12, 1000000, 'MORTGAGE',
        l_score, l_decision, l_rate, l_payment, l_max, l_reasons, l_log_id);
    TEST_ASSERT.assert_equals('max score', 850, l_score);
    TEST_ASSERT.assert_equals('max score within [0..1000]', 1,
        CASE WHEN l_score BETWEEN PKG_LOAN_SCORING.C_MIN_SCORE AND PKG_LOAN_SCORING.C_MAX_SCORE THEN 1 ELSE 0 END);
    TEST_ASSERT.assert_equals('max score rate (discount 2.00)', 12.50, l_rate);
    TEST_ASSERT.assert_equals('max score payment', 8908.29, l_payment);
    TEST_ASSERT.assert_equals('max score max amount', 4490000, l_max);
END;
/

-- ===========================================================================
-- Validation errors. Caught by the named exceptions from the specification, which
-- also verifies that each PRAGMA EXCEPTION_INIT is bound to the right code.
PROMPT === EVALUATE: -20001 amount out of range
DECLARE
    l_score NUMBER; l_decision VARCHAR2(10); l_rate NUMBER; l_payment NUMBER;
    l_max NUMBER; l_reasons VARCHAR2(400); l_log_id NUMBER;
BEGIN
    PKG_LOAN_SCORING.EVALUATE('TEST-ERR', 49999.99, 24, 100000, 'CONSUMER',
        l_score, l_decision, l_rate, l_payment, l_max, l_reasons, l_log_id);
    TEST_ASSERT.fail('-20001', 'no exception raised');
EXCEPTION
    WHEN PKG_LOAN_SCORING.E_INVALID_AMOUNT THEN TEST_ASSERT.pass('amount 49 999.99 -> -20001');
END;
/

PROMPT === EVALUATE: -20002 term out of range / fractional
DECLARE
    l_score NUMBER; l_decision VARCHAR2(10); l_rate NUMBER; l_payment NUMBER;
    l_max NUMBER; l_reasons VARCHAR2(400); l_log_id NUMBER;
BEGIN
    BEGIN
        PKG_LOAN_SCORING.EVALUATE('TEST-ERR', 500000, 85, 100000, 'CONSUMER',
            l_score, l_decision, l_rate, l_payment, l_max, l_reasons, l_log_id);
        TEST_ASSERT.fail('-20002 (85)', 'no exception raised');
    EXCEPTION
        WHEN PKG_LOAN_SCORING.E_INVALID_TERM THEN TEST_ASSERT.pass('term 85 -> -20002');
    END;
    BEGIN
        PKG_LOAN_SCORING.EVALUATE('TEST-ERR', 500000, 12.5, 100000, 'CONSUMER',
            l_score, l_decision, l_rate, l_payment, l_max, l_reasons, l_log_id);
        TEST_ASSERT.fail('-20002 (12.5)', 'no exception raised');
    EXCEPTION
        WHEN PKG_LOAN_SCORING.E_INVALID_TERM THEN TEST_ASSERT.pass('term 12.5 -> -20002');
    END;
END;
/

PROMPT === EVALUATE: -20003 income out of range
DECLARE
    l_score NUMBER; l_decision VARCHAR2(10); l_rate NUMBER; l_payment NUMBER;
    l_max NUMBER; l_reasons VARCHAR2(400); l_log_id NUMBER;
BEGIN
    BEGIN
        PKG_LOAN_SCORING.EVALUATE('TEST-ERR', 500000, 24, 0, 'CONSUMER',
            l_score, l_decision, l_rate, l_payment, l_max, l_reasons, l_log_id);
        TEST_ASSERT.fail('-20003 (0)', 'no exception raised');
    EXCEPTION
        WHEN PKG_LOAN_SCORING.E_INVALID_INCOME THEN TEST_ASSERT.pass('income 0 -> -20003');
    END;
    BEGIN
        PKG_LOAN_SCORING.EVALUATE('TEST-ERR', 500000, 24, 10000000.01, 'CONSUMER',
            l_score, l_decision, l_rate, l_payment, l_max, l_reasons, l_log_id);
        TEST_ASSERT.fail('-20003 (10M+)', 'no exception raised');
    EXCEPTION
        WHEN PKG_LOAN_SCORING.E_INVALID_INCOME THEN TEST_ASSERT.pass('income 10 000 000.01 -> -20003');
    END;
END;
/

PROMPT === EVALUATE: -20004 unknown purpose
DECLARE
    l_score NUMBER; l_decision VARCHAR2(10); l_rate NUMBER; l_payment NUMBER;
    l_max NUMBER; l_reasons VARCHAR2(400); l_log_id NUMBER;
BEGIN
    PKG_LOAN_SCORING.EVALUATE('TEST-ERR', 500000, 24, 100000, 'GOLD',
        l_score, l_decision, l_rate, l_payment, l_max, l_reasons, l_log_id);
    TEST_ASSERT.fail('-20004', 'no exception raised');
EXCEPTION
    WHEN PKG_LOAN_SCORING.E_UNKNOWN_PURPOSE THEN TEST_ASSERT.pass('purpose GOLD -> -20004');
END;
/

PROMPT === EVALUATE: -20005 empty application_id
DECLARE
    l_score NUMBER; l_decision VARCHAR2(10); l_rate NUMBER; l_payment NUMBER;
    l_max NUMBER; l_reasons VARCHAR2(400); l_log_id NUMBER;
BEGIN
    BEGIN
        PKG_LOAN_SCORING.EVALUATE(NULL, 500000, 24, 100000, 'CONSUMER',
            l_score, l_decision, l_rate, l_payment, l_max, l_reasons, l_log_id);
        TEST_ASSERT.fail('-20005 (NULL)', 'no exception raised');
    EXCEPTION
        WHEN PKG_LOAN_SCORING.E_EMPTY_APPLICATION_ID THEN TEST_ASSERT.pass('application_id NULL -> -20005');
    END;
    BEGIN
        PKG_LOAN_SCORING.EVALUATE('   ', 500000, 24, 100000, 'CONSUMER',
            l_score, l_decision, l_rate, l_payment, l_max, l_reasons, l_log_id);
        TEST_ASSERT.fail('-20005 (spaces)', 'no exception raised');
    EXCEPTION
        WHEN PKG_LOAN_SCORING.E_EMPTY_APPLICATION_ID THEN TEST_ASSERT.pass('application_id spaces -> -20005');
    END;
END;
/

-- ===========================================================================
PROMPT === GET_HISTORY: rows in descending date order
DECLARE
    l_score NUMBER; l_decision VARCHAR2(10); l_rate NUMBER; l_payment NUMBER;
    l_max NUMBER; l_reasons VARCHAR2(400);
    l_first_id  NUMBER;
    l_second_id NUMBER;
    l_cursor    SYS_REFCURSOR;
    l_row       SCORING_LOG%ROWTYPE;   -- GET_HISTORY returns all columns in table order
    l_fetched   PLS_INTEGER := 0;
    l_prev_at   TIMESTAMP;
    l_ids       VARCHAR2(100);
BEGIN
    PKG_LOAN_SCORING.EVALUATE('TEST-HISTORY', 500000, 24, 120000, 'CONSUMER',
        l_score, l_decision, l_rate, l_payment, l_max, l_reasons, l_first_id);
    -- Pause so that CREATED_AT values surely differ and the test checks ordering
    -- by date, not only the fallback ID DESC.
    DBMS_SESSION.SLEEP(0.1);
    PKG_LOAN_SCORING.EVALUATE('TEST-HISTORY', 1000000, 36, 100000, 'CAR',
        l_score, l_decision, l_rate, l_payment, l_max, l_reasons, l_second_id);

    l_cursor := PKG_LOAN_SCORING.GET_HISTORY('TEST-HISTORY');
    LOOP
        FETCH l_cursor INTO l_row;
        EXIT WHEN l_cursor%NOTFOUND;
        l_fetched := l_fetched + 1;
        IF l_prev_at IS NOT NULL AND l_row.CREATED_AT > l_prev_at THEN
            TEST_ASSERT.fail('history order', 'CREATED_AT increases but must decrease');
        END IF;
        l_prev_at := l_row.CREATED_AT;
        l_ids := l_ids || CASE WHEN l_ids IS NOT NULL THEN ',' END || l_row.ID;
    END LOOP;
    CLOSE l_cursor;

    TEST_ASSERT.assert_equals('history row count', 2, l_fetched);
    TEST_ASSERT.assert_equals('history newest first', l_second_id || ',' || l_first_id, l_ids);

    -- No rows -> empty cursor, not an error.
    l_cursor := PKG_LOAN_SCORING.GET_HISTORY('TEST-NO-SUCH-APP');
    FETCH l_cursor INTO l_row;
    TEST_ASSERT.assert_equals('history empty for unknown app', 1, CASE WHEN l_cursor%NOTFOUND THEN 1 ELSE 0 END);
    CLOSE l_cursor;
END;
/

-- ===========================================================================
PROMPT === Summary
BEGIN
    DBMS_OUTPUT.PUT_LINE('ALL TESTS PASSED: ' || TEST_ASSERT.g_passed || ' assertions');
END;
/

-- Order matters: ROLLBACK the test rows first, then DDL (implicit COMMIT).
ROLLBACK;
DROP PACKAGE TEST_ASSERT;
EXIT SUCCESS
