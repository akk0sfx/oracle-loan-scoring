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

| Suite | Project | Needs | What it checks |
|-------|---------|-------|----------------|
| Unit | `ScoringApi.UnitTests` | nothing | reference calculator on golden vectors, annuity and rounding, validator boundaries, Oracle error classification, the whole HTTP pipeline with a fake repository (contract JSON shape, 400/401/404/500/503, correlation id, OpenAPI) |
| Integration | `ScoringApi.IntegrationTests` | Docker | real API → ODP.NET → `PKG_LOAN_SCORING` in a throwaway `gvenzl/oracle-free:23-slim` container (Testcontainers) |
| PL/SQL | `oracle/tests` | running compose `oracle` | package in isolation, see [Oracle](#oracle) |

```bash
dotnet test scoring-api/ScoringApi.sln --filter "Category!=Integration"
dotnet test scoring-api/ScoringApi.sln --filter "Category=Integration"
```

The integration suite starts one Oracle container per run (≈10–20 s with a cached image) and
initializes it exactly like docker-compose: `oracle/init` is mounted into
`/container-entrypoint-initdb.d`.

### Golden vectors

[`tests/golden-vectors.json`](tests/golden-vectors.json) holds 26 reference cases: all purposes, all
decisions, DTI just below/above 0.30 and 0.50 (nearest cent), terms 6/60/61/84, amounts
50 000/3 000 000/3 000 001/5 000 000, the reachable neighbours of every score threshold and the
lowest/highest possible scores.

The scoring algorithm exists in four places (PL/SQL, C# reference, Creatio C# mock, JS preview).
The same vectors are checked against the C# reference (unit) and against Oracle through the real
API (integration), so any divergence — rounding, a threshold, the order of reasons — fails a test.
The expected values were generated by the C# reference (`dotnet run scripts/generate-golden-vectors.cs`)
and independently confirmed by the Python reference (`scripts/scoring_reference.py`) and by hand for
three cases; do not regenerate them blindly after changing the calculator.

Not every boundary from the algorithm is reachable: scores move in steps (220..850 only), so
499, 699, 799 and the clamp to 0/1000 cannot occur, and DTI of exactly 0.30/0.50 is not
representable with income in cents. The vectors use the nearest reachable values instead.

### Coverage

```bash
dotnet test scoring-api/ScoringApi.sln --settings scoring-api/coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory TestResults
```

Cobertura files land in `TestResults/*/coverage.cobertura.xml` (generated code is excluded).
Optional HTML report:

```bash
dotnet tool install --global dotnet-reportgenerator-globaltool
reportgenerator -reports:"TestResults/*/coverage.cobertura.xml" -targetdir:TestResults/report -reporttypes:Html
```

## What I learned

_TODO._

## Limitations

_TODO._
