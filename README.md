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

For `dotnet run` the API needs Oracle and two environment variables — see [Scoring API](#scoring-api).

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

ASP.NET Core minimal API (.NET 10) over the Oracle package; contract — [CONTRACTS.md](docs/CONTRACTS.md) section 4.

### Run with Docker (API + Oracle)

```bash
cp .env.example .env
docker compose up -d --build
```

The API waits for Oracle to become healthy and listens on `http://localhost:8080`.

### Run locally (`dotnet run`, Oracle in Docker)

```bash
docker compose up -d oracle
export ConnectionStrings__Oracle="User Id=SCORING;Password=ScoringDev123;Data Source=//localhost:1521/FREEPDB1"
export Scoring__ApiKey="dev-key-change-me"
dotnet run --project scoring-api/src/ScoringApi
```

Listens on `http://localhost:5077` (Development).

### Configuration

| Variable                      | Meaning                                                    |
|-------------------------------|------------------------------------------------------------|
| `ConnectionStrings__Oracle`   | ODP.NET connection string (required, validated at startup) |
| `Scoring__ApiKey`             | expected `X-Api-Key` value (required, validated at startup) |
| `ASPNETCORE_ENVIRONMENT`      | `Development` enables the Scalar UI                        |

In Docker Compose they come from `.env`: `APP_USER_PASSWORD`, `SCORING_API_KEY`, `ASPNETCORE_ENVIRONMENT`.
`appsettings.json` contains no secrets.

### Endpoints

| Method | Path                                        | Auth        |
|--------|---------------------------------------------|-------------|
| POST   | `/api/v1/scoring/evaluate`                  | `X-Api-Key` |
| GET    | `/api/v1/scoring/{applicationId}/history`   | `X-Api-Key` |
| GET    | `/health`                                   | none        |
| GET    | `/openapi/v1.json`                          | none        |
| GET    | `/scalar` (Development only)                | none        |

Errors are RFC 9457 problem details with a `code` extension (`VALIDATION_ERROR`, `UNAUTHORIZED`,
`DATABASE_UNAVAILABLE`, `INTERNAL_ERROR`). `X-Correlation-Id` is accepted or generated and returned.

### Examples

```bash
curl -s -X POST http://localhost:8080/api/v1/scoring/evaluate \
  -H "X-Api-Key: dev-key-change-me" -H "Content-Type: application/json" \
  -d '{"applicationId":"6f1c2b9e-0000-0000-0000-000000000001","amount":500000.00,"termMonths":24,"monthlyIncome":120000.00,"purposeCode":"CONSUMER"}'
```

```bash
curl -s http://localhost:8080/api/v1/scoring/6f1c2b9e-0000-0000-0000-000000000001/history -H "X-Api-Key: dev-key-change-me"
```

```bash
curl -s http://localhost:8080/health
```

All request variants (success, every 400, 401, history, health): [docs/scoring-api.http](docs/scoring-api.http).

## Tests

_TODO._

## What I learned

_TODO._

## Limitations

_TODO._
