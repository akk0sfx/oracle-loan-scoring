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
