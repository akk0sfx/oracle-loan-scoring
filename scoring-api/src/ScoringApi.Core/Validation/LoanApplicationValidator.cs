using ScoringApi.Core.Scoring;

namespace ScoringApi.Core.Validation;

/// <summary>
/// Validation rules of CONTRACTS.md, section 2.3 (same on client and server) plus the API
/// rules of section 4 (unknown purposeCode, empty applicationId). Inputs are nullable so a
/// missing JSON field is reported as a field error rather than a deserialization failure.
/// </summary>
public static class LoanApplicationValidator
{
    public const decimal MinAmount = 50_000m;
    public const decimal MaxAmount = 5_000_000m;
    public const int MinTermMonths = 6;
    public const int MaxTermMonths = 84;
    public const decimal MaxMonthlyIncome = 10_000_000m;

    public static IReadOnlyList<ValidationError> Validate(
        string? applicationId, decimal? amount, int? termMonths, decimal? monthlyIncome, string? purposeCode)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(applicationId))
        {
            errors.Add(new("applicationId", "Application id is required."));
        }
        else if (!Guid.TryParse(applicationId, out _))
        {
            errors.Add(new("applicationId", "Application id must be a GUID."));
        }

        if (amount is null)
        {
            errors.Add(new("amount", "Amount is required."));
        }
        else if (amount < MinAmount || amount > MaxAmount)
        {
            errors.Add(new("amount", $"Amount must be between {MinAmount:0} and {MaxAmount:0}."));
        }

        if (termMonths is null)
        {
            errors.Add(new("termMonths", "Term is required."));
        }
        else if (termMonths < MinTermMonths || termMonths > MaxTermMonths)
        {
            errors.Add(new("termMonths", $"Term must be between {MinTermMonths} and {MaxTermMonths} months."));
        }

        if (monthlyIncome is null)
        {
            errors.Add(new("monthlyIncome", "Monthly income is required."));
        }
        else if (monthlyIncome <= 0 || monthlyIncome > MaxMonthlyIncome)
        {
            errors.Add(new("monthlyIncome", $"Monthly income must be greater than 0 and at most {MaxMonthlyIncome:0}."));
        }

        if (string.IsNullOrWhiteSpace(purposeCode))
        {
            errors.Add(new("purposeCode", "Purpose code is required."));
        }
        else if (!PurposeCodes.IsKnown(purposeCode))
        {
            errors.Add(new("purposeCode", $"Unknown purpose code. Allowed: {string.Join(", ", PurposeCodes.BaseRates.Keys.Order())}."));
        }

        return errors;
    }
}
