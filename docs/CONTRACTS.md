# CONTRACTS — creatio-loan-scoring

This file is the single source of truth. Any contract change goes here first,
then into the code. Names, codes, types and error codes must not change without editing this file.

## 1. Architecture

Creatio (cloud)
  UsrLoanApplication1Page (Classic UI, JS/ExtJS)
    --> POST /0/rest/UsrLoanScoringService/Score        (C#, source code schema)
          --> POST {UsrScoringApiUrl}/api/v1/scoring/evaluate   (ASP.NET Core, .NET 10)
                --> PKG_LOAN_SCORING.EVALUATE           (Oracle PL/SQL)
                      --> INSERT SCORING_LOG
          (if UsrScoringUseMock = true — calculation in C# inside Creatio, no HTTP)
  UsrLoanApplicationEventListener (C#) writes the status change history
  UsrLoanApprovalProcess (business process) — manual approval of the REVIEW status

## 2. Creatio domain model

Prefix of all objects, columns and schemas — Usr. Package — UsrLoanScoring.
C# code — in namespace Terrasoft.Configuration.UsrLoanScoring.

### 2.1 Lookup UsrLoanStatus (parent: Base lookup / BaseLookup)
Extra column: UsrCode — String (50), required, unique.
Code compares statuses only by UsrCode, never by Id or Name.

| UsrCode  | Name             | Final |
|----------|------------------|-------|
| NEW      | New              | no    |
| SCORING  | Scoring          | no    |
| REVIEW   | Under review     | no    |
| APPROVED | Approved         | yes   |
| REJECTED | Rejected         | yes   |

Allowed transitions:
NEW -> SCORING (Score service)
SCORING -> APPROVED | REVIEW | REJECTED (scoring result)
SCORING -> NEW (scoring call failed, rollback)
REVIEW -> APPROVED | REJECTED (business process)
Any other transition is forbidden on the server (the listener throws an exception).

### 2.2 Lookup UsrLoanPurpose (parent: BaseLookup)
Extra column: UsrCode — String (50), required, unique.

| UsrCode   | Name                | BaseRate, % per annum |
|-----------|---------------------|-----------------------|
| CONSUMER  | Consumer            | 21.90                 |
| CAR       | Car loan            | 17.90                 |
| MORTGAGE  | Mortgage            | 14.50                 |
| REFINANCE | Refinancing         | 19.50                 |
| OTHER     | Other               | 24.90                 |

BaseRate is stored in Oracle (RATE_GRID) and duplicated in the C# mock and the JS preview.

### 2.3 Object UsrLoanApplication (parent: Base object / BaseEntity)

| Column                | Caption                 | Type                   | Req. | Filled by               |
|-----------------------|-------------------------|------------------------|------|-------------------------|
| UsrNumber             | Number                  | String (50)            | yes* | listener, format LA-000001 |
| UsrContact            | Client                  | Lookup Contact         | yes  | user                    |
| UsrPurpose            | Loan purpose            | Lookup UsrLoanPurpose  | yes  | user                    |
| UsrAmount             | Amount, ₽               | Currency (Decimal 18,2) | yes | user                    |
| UsrTermMonths         | Term, months            | Integer                | yes  | user                    |
| UsrMonthlyIncome      | Monthly income, ₽       | Currency (Decimal 18,2) | yes | user                    |
| UsrStatus             | Status                  | Lookup UsrLoanStatus   | yes  | listener / service / process |
| UsrScore              | Score                   | Integer                | no   | Score service           |
| UsrDecision           | Scoring decision        | String (20)            | no   | Score service: APPROVE / REVIEW / REJECT |
| UsrRate               | Rate, % per annum       | Decimal (18,2)         | no   | Score service           |
| UsrMonthlyPayment     | Monthly payment, ₽      | Currency               | no   | Score service           |
| UsrMaxApprovedAmount  | Max approved amount, ₽  | Currency               | no   | Score service           |
| UsrScoreReasons       | Reasons                 | String (500)           | no   | Score service, comma-separated codes |
| UsrScoredOn           | Scored on               | Date/time              | no   | Score service           |
| UsrDecisionComment    | Decision comment        | String (500)           | no   | business process        |

* UsrNumber is not editable by the user, it is generated on insert.
Primary display value — UsrNumber.
Validation (client and server identical):
- UsrAmount: 50 000 … 5 000 000
- UsrTermMonths: 6 … 84
- UsrMonthlyIncome: > 0 and <= 10 000 000
Financial fields (Contact, Purpose, Amount, TermMonths, MonthlyIncome) cannot be changed
unless the status is NEW (locked on the client and checked on the server).

### 2.4 Object UsrLoanDecisionHistory (parent: BaseEntity) — detail on the application page

| Column         | Type                             | Req. |
|----------------|----------------------------------|------|
| UsrApplication | Lookup UsrLoanApplication, cascade delete | yes |
| UsrStatusFrom  | Lookup UsrLoanStatus             | no (empty on creation) |
| UsrStatusTo    | Lookup UsrLoanStatus             | yes  |
| UsrScore       | Integer                          | no   |
| UsrComment     | String (500)                     | no   |
CreatedOn / CreatedBy — inherited. Records are created only by the listener.

### 2.5 System settings

| Code                           | Type                | Default value |
|--------------------------------|---------------------|---------------|
| UsrScoringApiUrl               | String (500)        | https://example.trycloudflare.com |
| UsrScoringApiKey               | Encrypted string    | dev-key-change-me |
| UsrScoringUseMock              | Boolean             | true          |
| UsrScoringTimeoutSec           | Integer             | 10            |
| UsrLoanApplicationLastNumber   | Integer             | 0             |

## 3. Creatio web service UsrLoanScoringService

POST {CreatioUrl}/0/rest/UsrLoanScoringService/Score
Authentication: standard Creatio session (cookie + BPMCSRF header).
BodyStyle = Wrapped, so the response is wrapped in ScoreResult.

Request:
    { "applicationId": "6f1c2b9e-0000-0000-0000-000000000001" }

Response (always HTTP 200, errors are in the body):
    {
      "ScoreResult": {
        "success": true,
        "errorCode": null,
        "errorMessage": null,
        "statusCode": "APPROVED",
        "score": 790,
        "decision": "APPROVE",
        "rate": 20.90,
        "monthlyPayment": 27012.34,
        "maxApprovedAmount": 1250000.00,
        "reasons": "DTI_LOW,PURPOSE_CONSUMER"
      }
    }
(numbers in the example are illustrative)

Error codes (errorCode):
- APP_NOT_FOUND — application not found
- INVALID_STATUS — application status is not NEW
- VALIDATION_ERROR — fields out of allowed ranges
- SCORING_UNAVAILABLE — API unreachable or timed out; status is rolled back to NEW
- SCORING_ERROR — API returned 4xx/5xx; status is rolled back to NEW

Score algorithm:
1. Read the application; missing — APP_NOT_FOUND.
2. Status is not NEW — INVALID_STATUS.
3. Check ranges — VALIDATION_ERROR.
4. Set status SCORING, save.
5. Call the API (or the mock when UsrScoringUseMock = true).
6. Success: write UsrScore, UsrDecision, UsrRate, UsrMonthlyPayment, UsrMaxApprovedAmount,
   UsrScoreReasons, UsrScoredOn; status by decision: APPROVE -> APPROVED, REVIEW -> REVIEW,
   REJECT -> REJECTED.
7. Failure: status back to NEW, return SCORING_UNAVAILABLE or SCORING_ERROR.

## 4. Scoring API (ASP.NET Core, .NET 10)

Base path: /api/v1. All responses are JSON, errors are RFC 9457 ProblemDetails
with a "code" extension. The X-Api-Key header is required for /api/v1/*.
The X-Correlation-Id header is accepted and returned (generated if absent).

### POST /api/v1/scoring/evaluate
Request:
    {
      "applicationId": "6f1c2b9e-0000-0000-0000-000000000001",
      "amount": 500000.00,
      "termMonths": 24,
      "monthlyIncome": 120000.00,
      "purposeCode": "CONSUMER"
    }
Response 200:
    {
      "applicationId": "6f1c2b9e-0000-0000-0000-000000000001",
      "score": 790,
      "decision": "APPROVE",
      "rate": 20.90,
      "monthlyPayment": 25740.12,
      "maxApprovedAmount": 940000.00,
      "reasons": ["DTI_LOW", "PURPOSE_CONSUMER"],
      "evaluatedAt": "2026-10-02T12:00:00Z"
    }
(numbers are illustrative; reference values are in tests/golden-vectors.json)
For REJECT: rate = null, monthlyPayment = null, maxApprovedAmount = 0.

Errors:
| HTTP | code                 | When |
|------|----------------------|------|
| 400  | VALIDATION_ERROR     | fields out of range, unknown purposeCode, empty applicationId |
| 401  | UNAUTHORIZED         | missing or invalid X-Api-Key |
| 503  | DATABASE_UNAVAILABLE | Oracle unavailable |
| 500  | INTERNAL_ERROR       | anything else, no exception details in the response |

### GET /api/v1/scoring/{applicationId}/history
Response 200: array of SCORING_LOG records for the application, newest first:
    [ { "id": 12, "score": 790, "decision": "APPROVE", "rate": 20.90,
        "monthlyPayment": 25740.12, "createdAt": "2026-10-02T12:00:00Z" } ]
Empty array if there are no records.

### GET /health
No key. 200 {"status":"Healthy"} or 503 {"status":"Unhealthy"} (check SELECT 1 FROM DUAL).

## 5. Scoring algorithm (identical in PL/SQL, the C# API reference, the Creatio C# mock and the JS preview)

Input: amount, termMonths, monthlyIncome, purposeCode.
Annuity: r = rate / 12 / 100; payment = amount * r / (1 - (1 + r)^(-termMonths)).
Money rounding — to 2 decimals, half away from zero (Oracle ROUND, C# MidpointRounding.AwayFromZero).

1. basePayment = annuity(amount, 20.00, termMonths)   — estimated payment for DTI.
2. dti = basePayment / monthlyIncome.
3. score = 600
   dti < 0.30           -> +200, reason DTI_LOW
   0.30 <= dti <= 0.50  -> +50,  reason DTI_MEDIUM
   dti > 0.50           -> -250, reason DTI_HIGH
   termMonths > 60      -> -50,  reason LONG_TERM
   amount > 3 000 000   -> -50,  reason LARGE_AMOUNT
   purpose: MORTGAGE +50, CAR +30, REFINANCE +10, CONSUMER 0, OTHER -30; reason PURPOSE_<CODE>
   score = clamp(score, 0, 1000)
4. decision: score >= 700 -> APPROVE; 500..699 -> REVIEW; < 500 -> REJECT.
5. rate = BaseRate(purpose) - discount; discount: score >= 800 -> 2.00; 700..799 -> 1.00; otherwise 0.
   For REJECT rate = null.
6. monthlyPayment = round(annuity(amount, rate, termMonths), 2); for REJECT null.
7. maxApprovedAmount: for REJECT 0; otherwise the amount at which the payment at rate and termMonths
   equals 0.40 * monthlyIncome, rounded down to 1000 and capped at 5 000 000.
Order of reasons: DTI_*, LONG_TERM, LARGE_AMOUNT, PURPOSE_*.

## 6. Oracle (schema SCORING)

RATE_GRID(PURPOSE_CODE VARCHAR2(20) PK, BASE_RATE NUMBER(5,2) NOT NULL)
SCORING_LOG(
  ID NUMBER GENERATED ALWAYS AS IDENTITY PK,
  APPLICATION_ID VARCHAR2(36) NOT NULL,     -- index IX_SCORING_LOG_APP
  AMOUNT NUMBER(14,2), TERM_MONTHS NUMBER(3), MONTHLY_INCOME NUMBER(14,2),
  PURPOSE_CODE VARCHAR2(20), SCORE NUMBER(4), DECISION VARCHAR2(10),
  RATE NUMBER(5,2), MONTHLY_PAYMENT NUMBER(14,2), MAX_APPROVED_AMOUNT NUMBER(14,2),
  REASONS VARCHAR2(400), CREATED_AT TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL)

PACKAGE PKG_LOAN_SCORING
  FUNCTION CALC_ANNUITY(p_amount NUMBER, p_rate NUMBER, p_term NUMBER) RETURN NUMBER;
  PROCEDURE EVALUATE(
    p_application_id IN VARCHAR2, p_amount IN NUMBER, p_term IN NUMBER,
    p_income IN NUMBER, p_purpose IN VARCHAR2,
    o_score OUT NUMBER, o_decision OUT VARCHAR2, o_rate OUT NUMBER,
    o_payment OUT NUMBER, o_max_amount OUT NUMBER, o_reasons OUT VARCHAR2,
    o_log_id OUT NUMBER);
  FUNCTION GET_HISTORY(p_application_id VARCHAR2) RETURN SYS_REFCURSOR;
Errors: -20001 invalid amount, -20002 invalid term, -20003 invalid income,
-20004 unknown purpose, -20005 empty application_id. The API maps them to 400 VALIDATION_ERROR.
EVALUATE writes the SCORING_LOG row itself; COMMIT is done by the caller (API).

## 7. Repository structure

creatio-loan-scoring/
  README.md, LICENSE, docker-compose.yml, .github/workflows/ci.yml
  docs/  CONTRACTS.md, DECISIONS.md, OPEN_QUESTIONS.md, INTERVIEW.md, screenshots/, creatio-api.http
  oracle/  init/01_schema.sql, 02_seed.sql, 03_pkg_loan_scoring.pks, 04_pkg_loan_scoring.pkb
           tests/test_pkg_loan_scoring.sql
  scoring-api/  ScoringApi.sln, src/ScoringApi, src/ScoringApi.Core,
                tests/ScoringApi.UnitTests, tests/ScoringApi.IntegrationTests
  tests/golden-vectors.json
  creatio/packages/UsrLoanScoring/   (Creatio package export)
