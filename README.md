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

_TODO._

## Scoring API

_TODO._

## Tests

_TODO._

## What I learned

_TODO._

## Limitations

_TODO._
