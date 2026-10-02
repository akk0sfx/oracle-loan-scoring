namespace ScoringApi.Core.Validation;

/// <summary>A single validation failure. <see cref="Field"/> is the camelCase JSON field name.</summary>
public sealed record ValidationError(string Field, string Message);
