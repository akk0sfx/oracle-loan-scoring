# creatio-loan-scoring

Loan applications in Creatio, scored via an ASP.NET Core (.NET 10) API and a PL/SQL package in Oracle.

## Architecture

_TODO._ See [docs/CONTRACTS.md](docs/CONTRACTS.md), section 1.

## Quick start

Requires .NET SDK 10.

```bash
dotnet build scoring-api/ScoringApi.sln
dotnet test scoring-api/ScoringApi.sln
dotnet run --project scoring-api/src/ScoringApi
```

After start: `GET http://localhost:5077/health` → `{"status":"Healthy"}`.

## Creatio

_TODO._

## Oracle

Oracle Database 23ai Free runs in Docker (`gvenzl/oracle-free:23-slim`); objects live in schema
`SCORING` of PDB `FREEPDB1`.

### Start

```bash
cp .env.example .env
docker compose up -d oracle
docker compose ps oracle
```

Wait until the status is `healthy` (the first start takes 1–2 minutes). On the first start the image
runs `oracle/init/*.sql`: tables, seed data, package. To re-run them, drop the volume:

```bash
docker compose down -v
```

### Connect (DBeaver / SQL Developer)

| Setting      | Value                                  |
|--------------|----------------------------------------|
| Host / Port  | `localhost` / `1521`                   |
| Service name | `FREEPDB1` (service name, not SID)     |
| User         | `SCORING`                              |
| Password     | `APP_USER_PASSWORD` from `.env`        |

JDBC URL: `jdbc:oracle:thin:@//localhost:1521/FREEPDB1`.

### Tests

```bash
scripts/oracle-test.sh
```

Plain PL/SQL tests (`oracle/tests/test_pkg_loan_scoring.sql`) run via sqlplus inside the container;
all test rows are rolled back. Expected numbers come from an independent reference:

```bash
python3 scripts/scoring_reference.py
```

### Package PKG_LOAN_SCORING

- `CALC_ANNUITY(amount, rate, term)` — annuity payment, unrounded.
- `EVALUATE(...)` — validates input (errors -20001..-20005), computes score, decision, rate, payment and
  maximum approved amount per [CONTRACTS.md](docs/CONTRACTS.md) section 5, writes a `SCORING_LOG` row.
  It does not commit — the caller owns the transaction.
- `GET_HISTORY(application_id)` — `SYS_REFCURSOR` over `SCORING_LOG`, newest first.

## Scoring API

_TODO._

## Tests

_TODO._

## What I learned

_TODO._

## Limitations

_TODO._
