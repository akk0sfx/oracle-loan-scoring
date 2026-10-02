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
