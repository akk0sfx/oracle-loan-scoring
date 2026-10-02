# DECISIONS

Log of non-obvious decisions (ADR-lite). Newest entries at the bottom.

## Template

```
## ADR-NNN: <short summary of the decision>
- Date: YYYY-MM-DD
- Decision: ...
- Why: ...
- Alternatives: ... (and why they were rejected)
```

---

## ADR-001: Classic .sln solution format instead of .slnx
- Date: 2026-10-02
- Decision: `scoring-api/ScoringApi.sln` is created with `dotnet new sln --format sln`.
- Why: in .NET 10 `dotnet new sln` creates `.slnx` by default, while CONTRACTS.md (section 7)
  fixes the name `ScoringApi.sln`. The classic format is read by every IDE and CI without caveats.
- Alternatives: `.slnx` — more compact and merge-friendly, but would require a contract change.

## ADR-002: /health via built-in ASP.NET Core Health Checks
- Date: 2026-10-02
- Decision: `AddHealthChecks()` + `MapHealthChecks("/health")` with a custom `ResponseWriter`
  that writes `{"status":"Healthy"}`.
- Why: the Oracle check is later added with a single `.AddCheck(...)` line, and the framework sets
  200/503 itself. The default writer returns plain text, so a custom JSON writer is needed.
- Alternatives: `MapGet("/health", ...)` — simpler now, but 503 and DB checks would be hand-written.

## ADR-003: Init scripts switch to FREEPDB1 and set CURRENT_SCHEMA = SCORING
- Date: 2026-10-02
- Decision: every `oracle/init/*.sql` starts with `ALTER SESSION SET CONTAINER = FREEPDB1;`
  and `ALTER SESSION SET CURRENT_SCHEMA = SCORING;`.
- Why: verified in the gvenzl/oracle-free sources (`container-entrypoint.sh`): init `.sql` files run as
  `sqlplus -s / as sysdba`, i.e. as SYS in CDB$ROOT, while `APP_USER` (SCORING) is created earlier
  in PDB FREEPDB1. Switching the container and the default schema puts the objects into
  FREEPDB1.SCORING, owned by SCORING, while keeping the plain `.sql` files the contract lists.
- Consequences: the scripts are bootstrap scripts for the container. Re-running them by hand as
  SCORING fails on `ALTER SESSION SET CONTAINER` (needs SYS); to re-initialize use
  `docker compose down -v && docker compose up -d oracle`.
- Alternatives: an executable `.sh` init script that connects as SCORING — cleaner ownership, but the
  `.sql` files in the same folder would still be run by the image as SYS, so they would have to move
  out of `oracle/init`, contradicting contract section 7.

## ADR-004: Package is installed by 03_install_pkg_loan_scoring.sql
- Date: 2026-10-02
- Decision: added `oracle/init/03_install_pkg_loan_scoring.sql`, which runs `@@03_pkg_loan_scoring.pks`
  and `@@04_pkg_loan_scoring.pkb`.
- Why: the image runs only `*.sql`, `*.sql.gz`, `*.sql.zip` and `*.sh` and logs
  "ignoring" for `.pks`/`.pkb` (confirmed in the container log). The file names are fixed by
  the contract, so a small wrapper is the least intrusive fix.
- Alternatives: renaming to `.sql` (contract change); a `.sh` wrapper (works too, but SQL*Plus
  `@@` keeps everything in SQL).

## ADR-005: Literal reading of ambiguous points of scoring algorithm (section 5)
- Date: 2026-10-02
- Decision: `basePayment` is NOT rounded before DTI (step 6 is the only explicit rounding);
  `maxApprovedAmount` has no lower bound (may be below 50 000 or below the requested amount);
  a term must be a whole number (fractional term -> -20002).
- Why: the contract text says nothing more; the literal reading is the safest until clarified.
  Test vectors are chosen far from DTI thresholds and rounding midpoints, so the result does not
  depend on these choices. See OPEN_QUESTIONS Q-002, Q-003.
- Alternatives: round `basePayment` to 2 decimals (more portable across Oracle/C#/JS) — pending the author's decision.

## ADR-006: Integer power in decimal by exponentiation by squaring
- Date: 2026-10-02
- Decision: `ScoringCalculator.PowInt` computes `(1 + r)^n` with exponentiation by squaring entirely in
  `decimal`; the negative power is `1 / (1 + r)^n`.
- Why: `decimal` has no `Pow`. `Math.Pow` works in `double` (15–17 significant digits), which may
  differ from Oracle NUMBER (~38 digits) in the last kopeck. The exponent is always a positive
  integer (6..84), so squaring is exact in algorithm and needs only ~7 multiplications, keeping the
  accumulated `decimal` rounding error (28–29 digits) far below 0.01. Verified against the Python
  reference and Oracle on all test vectors.
- Alternatives: `Math.Pow` on double (simpler, precision risk); a naive loop of n multiplications
  (correct, but more rounding steps).

## ADR-007: History via Dapper SELECT, not GET_HISTORY REF CURSOR
- Date: 2026-10-02
- Decision: `GET /api/v1/scoring/{id}/history` reads `SCORING_LOG` with a Dapper `SELECT ... ORDER BY
  CREATED_AT DESC, ID DESC`; EVALUATE is still called as a stored procedure through `OracleCommand`.
- Why: Dapper's `DynamicParameters` cannot declare an `OracleDbType.RefCursor` OUT/return parameter;
  that needs either a custom `IDynamicParameters` or an extra package (Dapper.Oracle). A plain SELECT
  is one line, maps straight to a type and is easy to read. The price: ordering and columns are
  duplicated between the package and the API, so GET_HISTORY and the SQL must change together.
- Alternatives: `OracleCommand` + `OracleDataReader` over the REF CURSOR (no duplication, more code);
  Dapper.Oracle (extra dependency).

## ADR-008: CREATED_AT is treated as UTC
- Date: 2026-10-02
- Decision: `SCORING_LOG.CREATED_AT` (TIMESTAMP without time zone, default SYSTIMESTAMP) is returned
  as UTC (`...Z`).
- Why: SYSTIMESTAMP uses the database host time zone, and the gvenzl container runs in UTC. A TIMESTAMP
  column drops the zone, so the API has to assume one. See OPEN_QUESTIONS Q-007.
- Alternatives: `TIMESTAMP WITH TIME ZONE` / `SYS_EXTRACT_UTC(SYSTIMESTAMP)` default — contract change.

## ADR-009: Request validation in the API before the database; applicationId must be a GUID
- Date: 2026-10-02
- Decision: `LoanApplicationValidator` (Core) checks all fields first and returns 400 with per-field
  `errors`; request DTO fields are nullable so missing fields are reported, not defaulted to 0.
  `applicationId` must parse as a GUID. Package errors -20001..-20005 are still mapped to 400 as a
  second line of defence.
- Why: no database round trip for invalid input, and all errors at once instead of the first one the
  package raises. Creatio ids are GUIDs and the column is VARCHAR2(36), so a GUID check is the natural
  reading of "empty applicationId" being invalid.
- Alternatives: rely on the package only (one error at a time, needs a DB call).

## ADR-010: API key comparison over SHA-256 hashes
- Date: 2026-10-02
- Decision: the endpoint filter hashes the provided and the configured key with SHA-256 and compares
  the hashes with `CryptographicOperations.FixedTimeEquals`.
- Why: `FixedTimeEquals` is constant-time only for equal lengths and returns early otherwise, leaking
  the key length. Hashes are always 32 bytes.
- Alternatives: comparing raw bytes (length leak); ASP.NET Core authentication handler (more code for a
  single shared key).

## ADR-011: Body binding errors are thrown and mapped to VALIDATION_ERROR
- Date: 2026-10-02
- Decision: `RouteHandlerOptions.ThrowOnBadRequest = true`; `ApiExceptionHandler` maps
  `BadHttpRequestException` to 400 `VALIDATION_ERROR`.
- Why: outside Development minimal APIs answer malformed JSON with an empty 400, which has no
  `code` extension required by the contract.
- Alternatives: accept `JsonElement` and parse manually (verbose).

## ADR-012: Oracle errors are translated in the data layer
- Date: 2026-10-02
- Decision: `OracleScoringRepository` converts `OracleException` into `ScoringInputRejectedException`
  (ORA-20001..-20005) or `ScoringDatabaseUnavailableException` (connectivity codes) using
  `OracleErrorClassifier`; `ApiExceptionHandler` maps only these data-layer exceptions.
- Why: `OracleException` has no public constructor, so tests with a fake repository could not
  simulate "no connection" without reflection. The HTTP layer no longer depends on ODP.NET, and the
  classification rules are unit-tested on plain ORA numbers. Responses did not change.
- Alternatives: keep the mapping in the handler and build `OracleException` via reflection in tests
  (brittle across ODP.NET versions).

## ADR-013: Shouldly for assertions
- Date: 2026-10-02
- Decision: test projects use Shouldly 4.
- Why: MIT license. FluentAssertions 8+ is under a commercial Xceed license (paid for commercial use),
  an unnecessary licensing question for a public portfolio repository. Shouldly's failure messages
  include the asserted expression, which is enough here.
- Alternatives: FluentAssertions 7 (last Apache-2.0 version, frozen); plain xUnit `Assert`.

## ADR-014: Integration tests use a generic Testcontainers container with the compose init folder
- Date: 2026-10-02
- Decision: `OracleFixture` starts `gvenzl/oracle-free:23-slim` with `ContainerBuilder`, bind-mounts
  `oracle/init` to `/container-entrypoint-initdb.d`, waits for "DATABASE IS READY TO USE!" and fails
  if the init part of the log contains `ORA-` or compilation errors. One container per test
  collection (`ICollectionFixture`).
- Why: the database is initialized by the very same mechanism and scripts as docker-compose, so the
  tests also cover ADR-003/004. The image does not fail on a broken init script, hence the log check.
- Alternatives: Testcontainers.Oracle module (oriented to its own default image and connection
  settings); running the scripts from the test via SQL*Plus-like parsing (re-implements `@@` and `/`).

## ADR-015: Package validation errors are tested through the API with a corrupting decorator
- Date: 2026-10-02
- Decision: integration tests wrap the real repository in a decorator that breaks one field after
  the API validator has passed, so ORA-20001..-20005 really come from Oracle and go through the
  real exception handler.
- Why: the API rejects invalid input before the database (ADR-009), so these package errors are
  unreachable through a plain HTTP request, yet the mapping must be proven end to end.
- Alternatives: a test-only endpoint (test code in production assembly); testing the repository only
  (does not cover the HTTP mapping; kept as an additional test).

## ADR-016: Conservative C# in the Creatio package
- Date: 2026-10-02
- Decision: code under `creatio/` uses C# 7.3-level syntax (block namespaces, classes instead of records,
  switch statements, `string.Format`), and the `.NET` conventions of `scoring-api/` (Directory.Build.props) do not apply here.
- Why: Creatio compiles the configuration itself with its own compiler settings; the language version
  available to configuration code on the stand is not known (Q-008). The sources are checked locally
  as `netstandard2.0` + `LangVersion 7.3` against signature stubs.
- Alternatives: modern C# (risk of compile errors on the stand).

## ADR-017: Scoring algorithm duplicated in the Creatio mock
- Date: 2026-10-02
- Decision: `UsrScoringCalculatorMock` is a copy of `ScoringApi.Core.ScoringCalculator` (same decimal
  approach, integer power by squaring, midpoint away from zero).
- Why: a Creatio package cannot reference an assembly of this repository; shipping an external DLL in the
  package for ~100 lines adds deployment and versioning work. Parity is checked by running the mock on
  tests/golden-vectors.json (26/26 locally).
- Alternatives: external assembly in the package; always calling the API (no offline demo mode).

## ADR-018: Lookup ids resolved by UsrCode with a per-request cache
- Date: 2026-10-02
- Decision: `UsrLoanStatusHelper` loads (Id, UsrCode) of a lookup once per instance; an instance lives
  for one service call or one listener event. No static cache.
- Why: Ids of lookup records differ between environments, codes are the contract. A static cache would
  be shared between users and stale after lookup edits.
- Alternatives: hard-coded Ids (environment-specific); static cache with invalidation (more code).

## ADR-019: Record rights in the scoring service
- Date: 2026-10-02
- Decision: the service loads and saves the application with `Entity.UseAdminRights = true` (apply the
  current user's record permissions); `FetchFromDB` returning false (missing or not visible) is reported
  as APP_NOT_FOUND. History rows are written by the listener with `UseAdminRights = false`.
- Why: the service must not let a user score an application they cannot see or edit; authentication
  itself is enforced by /0/rest/ (session cookie + BPMCSRF). History is system data (contract 2.4).
- Status: behaviour on the stand to be confirmed with a user without edit rights (Q-010).

## ADR-020: REJECT stores 0 in UsrRate / UsrMonthlyPayment
- Date: 2026-10-02
- Decision: for REJECT the service writes 0 to the numeric columns and returns null in ScoreResult.
- Why: Creatio numeric columns hold a number (default 0), not NULL; the contract response keeps null.
  UsrDecision = REJECT tells the UI that 0 means "not applicable".
- Alternatives: leave the old values (misleading after a re-score).

## ADR-021: Listener rules split by event instead of a single OnSaving
- Date: 2026-10-02
- Decision: OnSaving — ranges; OnInserting — default status and number; OnUpdating — status
  transitions, financial lock, number immutability; OnSaved — history.
- Why: per Creatio docs, insert runs OnSaving -> OnInserting and update runs OnSaving -> OnUpdating.
  Splitting by event answers "insert or update?" without relying on undocumented state, and on insert
  OnSaving runs before the default status is set.
- Alternatives: everything in OnSaving with an insert/update check (needs an undocumented property).

## ADR-022: UsrNumber from a system setting counter (known race)
- Date: 2026-10-02
- Decision: OnInserting reads UsrLoanApplicationLastNumber, adds 1, stores it with SysSettings.SetDefValue
  and formats LA-000001.
- Why: required by the contract (2.5) and simple.
- Known issue: read-increment-write is not atomic; two parallel inserts can get the same number.
- Production options: a database sequence (Oracle/PostgreSQL) read via a custom query; a counter row
  updated with `UPDATE ... SET n = n + 1 RETURNING n` under a row lock; a unique index on UsrNumber plus
  retry on conflict.

## ADR-023: Payment preview on the page uses floating point and is never stored
- Date: 2026-10-02
- Decision: `UsrLoanScoringClientUtils.calcAnnuity` computes the "estimated payment" at the purpose base
  rate with JavaScript numbers; the value lives in a virtual attribute only.
- Why: the preview is for the user while typing; JS has no decimal type, and contract 5 makes the server
  (Oracle / C#) the source of all stored numbers. A virtual attribute cannot be saved by accident.
- Alternatives: call the server on every keystroke (load, latency); a decimal library on the client.

## ADR-024: Page layout and read-only state are defined in code, not by the wizard
- Date: 2026-10-02
- Decision: `UsrLoanApplication1Page` owns its full diff (groups, tab, detail) and binds `enabled` to the
  virtual attribute `IsFinancialEditable`; result fields and `UsrNumber` are `enabled: false`.
- Why: one attribute drives five fields and matches the server lock (listener, ADR-021); declarative
  wizard rules would duplicate the status logic. Reopening the page in the wizard may rewrite the code
  between SCHEMA_* markers, so further layout changes go through code.
- Alternatives: business rules in the wizard (no code, but the status condition lives in two places).
