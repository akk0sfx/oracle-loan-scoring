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
