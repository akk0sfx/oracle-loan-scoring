# OPEN QUESTIONS

Questions where there is no certainty about the Creatio, clio or Oracle API.
Such places in code are marked `// TODO(verify): ...` (`-- TODO(verify): ...` in SQL).

## Template

```
## Q-NNN: <question>
- Date: YYYY-MM-DD
- Where: <file / component>
- Context: what we are trying to do and what is in doubt
- Status: open | closed (answer, source)
```

## Status summary (review 2026-10-02, before publication)

No question is fully closed yet. Open items are listed in README, section
"Limitations and what I would do in production".

| Id | Topic | Status | Blocked by |
|----|-------|--------|------------|
| Q-001 | error code for an invalid rate in CALC_ANNUITY | open | author's decision on the contract |
| Q-002 | rounding of basePayment before DTI | open | author's decision on the contract |
| Q-003 | lower bound of maxApprovedAmount | open | author's decision on the contract |
| Q-004 | example numbers in CONTRACTS.md | open | author's decision on the contract |
| Q-005 | ODP.NET "database unavailable" codes | partly verified (50201 observed and covered by an integration test) | DNS failure, wrong password, pool timeout not reproduced |
| Q-006 | `code` for 404 / 405 | open | author's decision on the contract |
| Q-007 | time zone of CREATED_AT | open | author's decision on the contract |
| Q-008..Q-018 | Creatio platform details | open | first compilation and run on a Creatio stand |

---

## Q-001: Error code for invalid rate in CALC_ANNUITY
- Date: 2026-10-02
- Where: oracle/init/04_pkg_loan_scoring.pkb (`C_ERR_INTERNAL`)
- Context: CALC_ANNUITY must reject rate <= 0 (division by zero). The contract defines only
  -20001..-20005, none of which means "invalid rate". Currently -20000 is raised; the API would map
  it to 500 INTERNAL_ERROR, which is correct because user input cannot produce it. Add -20000
  (or another code) to CONTRACTS.md section 6?
- Status: open

## Q-002: Is basePayment rounded before computing DTI?
- Date: 2026-10-02
- Where: CONTRACTS.md section 5, step 1–2; all four implementations
- Context: "Money rounding — to 2 decimals" may or may not apply to basePayment. Near DTI = 0.30 / 0.50
  Oracle NUMBER, C# decimal and JS double can disagree when unrounded values are compared.
  Rounding basePayment to 2 decimals would make the comparison deterministic.
- Status: open (current: not rounded, ADR-005)

## Q-003: Lower bound for maxApprovedAmount
- Date: 2026-10-02
- Where: CONTRACTS.md section 5, step 7
- Context: for REVIEW/APPROVE the value can be below 50 000 (the minimum application amount).
  Keep as is, clamp to 50 000, or turn such cases into REJECT?
- Status: open (current: no lower bound, ADR-005)

## Q-004: Example numbers in CONTRACTS.md sections 3–4 do not match the algorithm
- Date: 2026-10-02
- Where: CONTRACTS.md sections 3 and 4
- Context: for the example input (500 000 / 24 / 120 000 / CONSUMER) the algorithm gives score 800,
  rate 19.90, payment 25 423.48, max amount 944 000 — not 790 / 20.90 / 25 740.12 / 940 000.
  The examples are marked illustrative; should they be replaced with the real values?
- Status: open

## Q-005: Full list of ODP.NET Managed "database unavailable" error numbers
- Date: 2026-10-02
- Where: scoring-api/src/ScoringApi/Infrastructure/ApiExceptionHandler.cs (`ConnectivityErrors`)
- Context: with Oracle stopped, ODP.NET Managed throws `OracleException` Number 50201 wrapping
  ORA-12537 (verified). The list also contains classic ORA/TNS numbers (12541, 12514, 1017, ...) and
  50000 for a pool timeout, which were not reproduced. Should a DNS failure (container removed),
  a wrong password and a pool timeout be checked explicitly?
- Status: open (50201 verified on 2026-10-02)

## Q-006: "code" for framework errors not listed in the contract
- Date: 2026-10-02
- Where: scoring-api/src/ScoringApi/Infrastructure/ErrorCodes.cs
- Context: the contract requires "code" in all errors but lists only VALIDATION_ERROR, UNAUTHORIZED,
  DATABASE_UNAVAILABLE, INTERNAL_ERROR. Unknown routes and wrong methods return NOT_FOUND and
  METHOD_NOT_ALLOWED. Add them to CONTRACTS.md section 4?
- Status: open

## Q-007: Time zone of SCORING_LOG.CREATED_AT
- Date: 2026-10-02
- Where: CONTRACTS.md section 6, oracle/init/01_schema.sql
- Context: TIMESTAMP without zone filled by SYSTIMESTAMP is the DB host local time; the API assumes UTC
  (ADR-008). Correct for the container, wrong for a DB host in another zone. Switch the default to
  `SYS_EXTRACT_UTC(SYSTIMESTAMP)`?
- Status: open

## Q-008: Configuration compiler and logging facade on Creatio .NET 8
- Date: 2026-10-02
- Where: creatio/src/*.cs
- Context: which C# language version Creatio uses to compile configuration on .NET 8, and whether
  `global::Common.Logging.LogManager.GetLogger(...)` is available there (it is the facade on .NET Framework).
- Status: open — will be answered by the first compilation on the stand

## Q-009: SysSettings API details
- Date: 2026-10-02
- Where: UsrScoringApiClient.cs, UsrLoanApplicationEventListener.cs
- Context: (a) overload `SysSettings.GetValue<T>(UserConnection, string, T)`; (b) GetValue of an
  encrypted (SecureText) setting returns the decrypted value in server code; (c) `SysSettings.SetDefValue`
  stores the value for all users.
- Status: open

## Q-010: Does Entity.Save enforce record rights with UseAdminRights = true?
- Date: 2026-10-02
- Where: UsrLoanScoringService.cs (ADR-019)
- Context: expected: a user without edit rights on the application gets an error on Save and without read
  rights gets APP_NOT_FOUND. Needs a test with a restricted user on the stand.
- Status: open

## Q-011: DateTime from server code for a Date/time column
- Date: 2026-10-02
- Where: UsrLoanScoringService.cs (UsrScoredOn)
- Context: the service writes `DateTime.UtcNow`. Confirm Creatio treats the value as UTC and shows the
  user's local time (otherwise use the user's time zone conversion).
- Status: open

## Q-012: Old column values in OnSaved
- Date: 2026-10-02
- Where: UsrLoanApplicationEventListener.OnSaved
- Context: history detection compares `GetTypedOldColumnValue` with the current value in OnSaved. If old
  values are already reset at that point, no history is written; then the status change has to be
  detected in OnSaving/OnUpdating and passed on (e.g. via a transaction-scoped marker).
- Status: open — check: change a status, look at the UsrLoanDecisionHistory detail

## Q-013: Concurrent Score calls for the same application
- Date: 2026-10-02
- Where: UsrLoanScoringService.cs, step 2–4
- Context: two calls can both read NEW and both move to SCORING (the second save sees NEW -> SCORING
  as allowed). The API is then called twice. Fix: conditional update `SET status = SCORING WHERE
  status = NEW` (Update query) and treat 0 affected rows as INVALID_STATUS. Contract change? (behaviour only)
- Status: open

## Q-014: Name of the decision history detail schema
- Date: 2026-10-02
- Where: UsrLoanApplication1Page.js (`HISTORY_DETAIL_SCHEMA`), UsrLoanDecisionHistoryDetail.js
- Context: the wizard generates the detail schema name. The code assumes `UsrLoanDecisionHistoryDetail`;
  replace it with the real name after the package export.
- Status: open

## Q-015: lookupListConfig.columns on record load
- Date: 2026-10-02
- Where: UsrLoanApplication1Page.js (UsrStatus, UsrPurpose)
- Context: the page reads `this.get("UsrStatus").UsrCode`. Confirm the extra column is loaded with the
  record, not only in the lookup selection window. If not, load the codes with an EntitySchemaQuery in
  onEntityInitialized.
- Status: open — check in DevTools: `this.get("UsrStatus")` on the page view model

## Q-016: BasePageV2 methods used by the scoring button
- Date: 2026-10-02
- Where: UsrLoanApplication1Page.js
- Context: `isChanged()`, `save({isSilent, callback, scope})`, `onSaved`, `reloadEntity()`,
  `showBodyMask()/hideBodyMask()`, `showInformationDialog()` in this Creatio version.
- Status: open

## Q-017: Container of the page action buttons
- Date: 2026-10-02
- Where: UsrLoanApplication1Page.js (SendToScoringButton, parentName "LeftContainer")
- Context: the button must be next to Save/Close both when the page is opened alone and in combined
  (section + card) mode; the latter may use "CombinedModeActionButtonsCardLeftContainer".
- Status: open

## Q-018: Default sorting of a grid detail
- Date: 2026-10-02
- Where: UsrLoanDecisionHistoryDetail.js
- Context: `getGridDataColumns` with `orderPosition` / `orderDirection` on CreatedOn is assumed to set
  ORDER BY CreatedOn DESC.
- Status: open
