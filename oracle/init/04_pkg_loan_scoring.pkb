-- =============================================================================
-- 04_pkg_loan_scoring.pkb — BODY of package PKG_LOAN_SCORING.
--
-- The body implements what the specification (.pks) declares and may contain private
-- subprograms that are invisible outside the package (calc_score, calc_rate,
-- calc_max_amount below). Like private methods of a C# class, they can be changed or
-- renamed without touching the contract or invalidating callers.
--
-- Why there is no COMMIT here. EVALUATE only writes a row to SCORING_LOG; the transaction
-- is committed by the caller (Scoring API), as stated in the contract, section 6. Reasons:
--   * the transaction belongs to whoever knows the business operation boundaries. If the
--     API fails after EVALUATE before sending a response, it rolls back, and the log holds
--     no calculation the client never learned about;
--   * a COMMIT inside the procedure would also commit OTHER changes made earlier in the
--     same session. A procedure has no right to decide that for its caller;
--   * tests can call EVALUATE and undo everything with one ROLLBACK, leaving no garbage.
--
-- Runs as SYS in CDB$ROOT, so switch first (ADR-003).
-- =============================================================================

WHENEVER SQLERROR EXIT SQL.SQLCODE

ALTER SESSION SET CONTAINER = FREEPDB1;
ALTER SESSION SET CURRENT_SCHEMA = SCORING;

CREATE OR REPLACE PACKAGE BODY PKG_LOAN_SCORING
AS
    -- Error code for violated internal invariants. It is not among the contract codes:
    -- user input cannot reach it, and the API will answer 500.
    -- TODO(verify): agree on the code with the author — docs/OPEN_QUESTIONS.md, Q-001.
    C_ERR_INTERNAL CONSTANT PLS_INTEGER := -20000;

    -- -------------------------------------------------------------------------
    -- Private helper: append a reason code to the comma-separated list.
    -- -------------------------------------------------------------------------
    PROCEDURE append_reason(p_reasons IN OUT NOCOPY VARCHAR2, p_code IN VARCHAR2)
    IS
    BEGIN
        -- NOCOPY hints the compiler to pass the string by reference instead of copying it.
        -- The gain is tiny for short strings, but it is the idiom for IN OUT strings and collections.
        p_reasons := CASE WHEN p_reasons IS NULL THEN p_code ELSE p_reasons || ',' || p_code END;
    END append_reason;

    -- -------------------------------------------------------------------------
    -- CALC_ANNUITY (public).
    -- -------------------------------------------------------------------------
    FUNCTION CALC_ANNUITY(p_amount NUMBER, p_rate NUMBER, p_term NUMBER) RETURN NUMBER
    IS
        l_r NUMBER;
    BEGIN
        IF p_term IS NULL OR p_term <= 0 THEN
            RAISE_APPLICATION_ERROR(-20002, 'Loan term must be greater than 0 months, got: '
                || NVL(TO_CHAR(p_term), 'NULL'));
        END IF;
        -- With r = 0 the denominator 1 - (1 + 0)^(-n) is 0 and the formula divides by zero.
        -- The contract has no interest-free loans, so this is an error rather than amount / n.
        IF p_rate IS NULL OR p_rate <= 0 THEN
            RAISE_APPLICATION_ERROR(C_ERR_INTERNAL, 'Rate must be greater than 0, got: '
                || NVL(TO_CHAR(p_rate), 'NULL'));
        END IF;

        l_r := p_rate / 12 / 100;
        -- NUMBER is decimal with ~38 significant digits (like C# decimal, unlike double),
        -- so the result matches the reference down to the kopeck. POWER with an integer
        -- exponent is well defined for negative exponents too.
        RETURN p_amount * l_r / (1 - POWER(1 + l_r, -p_term));
    END CALC_ANNUITY;

    -- -------------------------------------------------------------------------
    -- Steps 1–3: score and reasons.
    -- A function with an OUT parameter can only be called from PL/SQL (not from SQL).
    -- That is fine for a private function: score and reasons are produced in one pass
    -- over the rules without duplicating the conditions.
    -- -------------------------------------------------------------------------
    FUNCTION calc_score(
        p_amount  IN NUMBER, p_term IN NUMBER, p_income IN NUMBER, p_purpose IN VARCHAR2,
        o_reasons OUT VARCHAR2) RETURN NUMBER
    IS
        l_base_payment NUMBER;
        l_dti          NUMBER;
        l_score        NUMBER := C_BASE_SCORE;
    BEGIN
        -- Step 1. Estimated payment at a fixed 20% rate: DTI must not depend on the
        -- discount, which itself depends on the score.
        -- Not rounded: the contract rounds only the final payment (step 6).
        l_base_payment := CALC_ANNUITY(p_amount, C_DTI_BASE_RATE, p_term);

        -- Step 2. Debt-to-income — the share of income consumed by the payment.
        l_dti := l_base_payment / p_income;

        -- Step 3. Rules are applied in contract order, which also yields the required reasons order.
        IF l_dti < C_DTI_LOW_LIMIT THEN
            l_score := l_score + C_DTI_LOW_BONUS;
            append_reason(o_reasons, 'DTI_LOW');
        ELSIF l_dti <= C_DTI_HIGH_LIMIT THEN
            l_score := l_score + C_DTI_MEDIUM_BONUS;
            append_reason(o_reasons, 'DTI_MEDIUM');
        ELSE
            l_score := l_score + C_DTI_HIGH_PENALTY;
            append_reason(o_reasons, 'DTI_HIGH');
        END IF;

        IF p_term > C_LONG_TERM_LIMIT THEN
            l_score := l_score + C_LONG_TERM_PENALTY;
            append_reason(o_reasons, 'LONG_TERM');
        END IF;

        IF p_amount > C_LARGE_AMOUNT_LIMIT THEN
            l_score := l_score + C_LARGE_AMOUNT_PENALTY;
            append_reason(o_reasons, 'LARGE_AMOUNT');
        END IF;

        l_score := l_score + CASE p_purpose
                                 WHEN 'MORTGAGE'  THEN C_BONUS_MORTGAGE
                                 WHEN 'CAR'       THEN C_BONUS_CAR
                                 WHEN 'REFINANCE' THEN C_BONUS_REFINANCE
                                 WHEN 'CONSUMER'  THEN C_BONUS_CONSUMER
                                 WHEN 'OTHER'     THEN C_BONUS_OTHER
                             END;
        -- A CASE expression without ELSE yields NULL for a code that exists in RATE_GRID but
        -- is unknown to the algorithm (e.g. a purpose added by UPDATE without changing the
        -- package). An explicit error beats a NULL score.
        IF l_score IS NULL THEN
            RAISE_APPLICATION_ERROR(-20004, 'No scoring bonus defined for loan purpose ' || p_purpose);
        END IF;
        append_reason(o_reasons, 'PURPOSE_' || p_purpose);

        -- With the current bonuses the score is always within 220..850, so the clamp never fires.
        -- It stays as a safeguard in case thresholds or bonuses change.
        RETURN GREATEST(C_MIN_SCORE, LEAST(C_MAX_SCORE, l_score));
    END calc_score;

    -- -------------------------------------------------------------------------
    -- Step 5: final rate. NULL for REJECT — no loan is issued.
    -- -------------------------------------------------------------------------
    FUNCTION calc_rate(p_base_rate IN NUMBER, p_score IN NUMBER, p_decision IN VARCHAR2) RETURN NUMBER
    IS
    BEGIN
        IF p_decision = C_DECISION_REJECT THEN
            RETURN NULL;
        END IF;

        RETURN p_base_rate - CASE
                                 WHEN p_score >= C_DISCOUNT_HIGH_SCORE THEN C_DISCOUNT_HIGH
                                 WHEN p_score >= C_APPROVE_THRESHOLD   THEN C_DISCOUNT_MID
                                 ELSE 0
                             END;
    END calc_rate;

    -- -------------------------------------------------------------------------
    -- Step 7: maximum amount at which the payment equals 40% of income.
    -- The annuity is linear in the amount: payment = amount * k, where k = CALC_ANNUITY(1, rate, term).
    -- So the inverse problem is a plain division, no numerical methods needed.
    -- -------------------------------------------------------------------------
    FUNCTION calc_max_amount(p_rate IN NUMBER, p_term IN NUMBER, p_income IN NUMBER) RETURN NUMBER
    IS
        l_raw NUMBER;
    BEGIN
        IF p_rate IS NULL THEN
            RETURN 0;   -- REJECT
        END IF;

        l_raw := C_MAX_PAYMENT_SHARE * p_income / CALC_ANNUITY(1, p_rate, p_term);
        -- Round down to the step, not ROUND: approving more than calculated is not allowed.
        -- The cap equals the upper bound of the application amount (C_MAX_AMOUNT = 5 000 000).
        RETURN LEAST(FLOOR(l_raw / C_MAX_AMOUNT_STEP) * C_MAX_AMOUNT_STEP, C_MAX_AMOUNT);
    END calc_max_amount;

    -- -------------------------------------------------------------------------
    -- EVALUATE (public).
    -- -------------------------------------------------------------------------
    PROCEDURE EVALUATE(
        p_application_id IN VARCHAR2, p_amount IN NUMBER, p_term IN NUMBER,
        p_income IN NUMBER, p_purpose IN VARCHAR2,
        o_score OUT NUMBER, o_decision OUT VARCHAR2, o_rate OUT NUMBER,
        o_payment OUT NUMBER, o_max_amount OUT NUMBER, o_reasons OUT VARCHAR2,
        o_log_id OUT NUMBER)
    IS
        l_base_rate RATE_GRID.BASE_RATE%TYPE;   -- %TYPE: the type follows the table column
    BEGIN
        -- ---- Input validation. Each violation has its own contract code ----
        -- In Oracle the empty string '' IS NULL, so one IS NULL check is enough;
        -- TRIM also catches a string of spaces only.
        IF TRIM(p_application_id) IS NULL THEN
            RAISE_APPLICATION_ERROR(-20005, 'Application id (application_id) is empty');
        END IF;

        IF p_amount IS NULL OR p_amount < C_MIN_AMOUNT OR p_amount > C_MAX_AMOUNT THEN
            RAISE_APPLICATION_ERROR(-20001, 'Loan amount must be between ' || C_MIN_AMOUNT
                || ' and ' || C_MAX_AMOUNT || ', got: ' || NVL(TO_CHAR(p_amount), 'NULL'));
        END IF;

        -- p_term <> TRUNC(p_term): otherwise a fractional term would be silently rounded when
        -- inserted into TERM_MONTHS NUMBER(3), and the log would show a term not used in the calculation.
        IF p_term IS NULL OR p_term < C_MIN_TERM OR p_term > C_MAX_TERM OR p_term <> TRUNC(p_term) THEN
            RAISE_APPLICATION_ERROR(-20002, 'Loan term must be a whole number of months between '
                || C_MIN_TERM || ' and ' || C_MAX_TERM || ', got: ' || NVL(TO_CHAR(p_term), 'NULL'));
        END IF;

        IF p_income IS NULL OR p_income <= 0 OR p_income > C_MAX_INCOME THEN
            RAISE_APPLICATION_ERROR(-20003, 'Monthly income must be greater than 0 and at most '
                || C_MAX_INCOME || ', got: ' || NVL(TO_CHAR(p_income), 'NULL'));
        END IF;

        -- The purpose is validated against the table, not a list in code: RATE_GRID is the source of rates.
        BEGIN
            SELECT BASE_RATE INTO l_base_rate FROM RATE_GRID WHERE PURPOSE_CODE = p_purpose;
        EXCEPTION
            -- SELECT INTO with no rows raises NO_DATA_FOUND. Translate it into the contract code
            -- so the API answers 400 VALIDATION_ERROR rather than 500.
            WHEN NO_DATA_FOUND THEN
                RAISE_APPLICATION_ERROR(-20004, 'Unknown loan purpose: ' || NVL(p_purpose, 'NULL'));
        END;

        -- ---- Steps 1–3: score and reasons ----
        o_score := calc_score(p_amount, p_term, p_income, p_purpose, o_reasons);

        -- ---- Step 4: decision ----
        o_decision := CASE
                          WHEN o_score >= C_APPROVE_THRESHOLD THEN C_DECISION_APPROVE
                          WHEN o_score >= C_REVIEW_THRESHOLD  THEN C_DECISION_REVIEW
                          ELSE C_DECISION_REJECT
                      END;

        -- ---- Step 5: rate ----
        o_rate := calc_rate(l_base_rate, o_score, o_decision);

        -- ---- Step 6: payment. Oracle ROUND on NUMBER rounds half away from zero ----
        o_payment := CASE WHEN o_rate IS NOT NULL
                          THEN ROUND(CALC_ANNUITY(p_amount, o_rate, p_term), 2) END;

        -- ---- Step 7: maximum approved amount ----
        o_max_amount := calc_max_amount(o_rate, p_term, p_income);

        -- ---- Log ----
        -- RETURNING ... INTO returns the generated IDENTITY without a second query.
        INSERT INTO SCORING_LOG (
            APPLICATION_ID, AMOUNT, TERM_MONTHS, MONTHLY_INCOME, PURPOSE_CODE,
            SCORE, DECISION, RATE, MONTHLY_PAYMENT, MAX_APPROVED_AMOUNT, REASONS)
        VALUES (
            p_application_id, p_amount, p_term, p_income, p_purpose,
            o_score, o_decision, o_rate, o_payment, o_max_amount, o_reasons)
        RETURNING ID INTO o_log_id;

        -- COMMIT is intentionally absent — see the file header.
    END EVALUATE;

    -- -------------------------------------------------------------------------
    -- GET_HISTORY (public).
    -- SYS_REFCURSOR is a pointer to an open cursor. Data is not copied into package
    -- memory; the client (ODP.NET in the API) reads rows itself, like a DbDataReader.
    -- -------------------------------------------------------------------------
    FUNCTION GET_HISTORY(p_application_id VARCHAR2) RETURN SYS_REFCURSOR
    IS
        l_cursor SYS_REFCURSOR;
    BEGIN
        IF TRIM(p_application_id) IS NULL THEN
            RAISE_APPLICATION_ERROR(-20005, 'Application id (application_id) is empty');
        END IF;

        -- Columns are listed explicitly instead of SELECT *: adding a column to the table
        -- will not shift the field order the API reads.
        -- ID DESC is the secondary sort key: with equal CREATED_AT (fast inserts)
        -- the order stays deterministic.
        OPEN l_cursor FOR
            SELECT ID, APPLICATION_ID, AMOUNT, TERM_MONTHS, MONTHLY_INCOME, PURPOSE_CODE,
                   SCORE, DECISION, RATE, MONTHLY_PAYMENT, MAX_APPROVED_AMOUNT, REASONS, CREATED_AT
              FROM SCORING_LOG
             WHERE APPLICATION_ID = p_application_id
             ORDER BY CREATED_AT DESC, ID DESC;

        RETURN l_cursor;
    END GET_HISTORY;
END PKG_LOAN_SCORING;
/

SHOW ERRORS
