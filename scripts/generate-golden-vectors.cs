#:project ../scoring-api/src/ScoringApi.Core/ScoringApi.Core.csproj
// Generates tests/golden-vectors.json from the reference ScoringCalculator.
//
// Run from the repository root:  dotnet run scripts/generate-golden-vectors.cs
//
// WARNING: regenerating makes the vectors agree with the current calculator by construction.
// Only regenerate to ADD cases, review the diff of existing cases, and re-check new ones
// independently (scripts/scoring_reference.py, hand arithmetic) before committing.
using System.Text;
using System.Text.Json;
using ScoringApi.Core.Scoring;

// Income that puts DTI on the given side of a threshold, as close as cents allow.
// An exact DTI of 0.30 is not representable: basePayment has many more decimals than income.
static decimal IncomeJustBelowDti(decimal amount, int term, decimal threshold) =>
    decimal.Ceiling(ScoringCalculator.Annuity(amount, 20.00m, term) / threshold * 100m) / 100m;

static decimal IncomeJustAboveDti(decimal amount, int term, decimal threshold) =>
    decimal.Floor(ScoringCalculator.Annuity(amount, 20.00m, term) / threshold * 100m) / 100m;

var cases = new List<(string Name, ScoringInput Input)>
{
    // All purposes, DTI_LOW
    ("purpose CONSUMER, score 800 (discount 2.00)", new(500_000m, 24, 120_000m, "CONSUMER")),
    ("purpose CAR, score 830", new(800_000m, 36, 150_000m, "CAR")),
    ("purpose MORTGAGE, term 60 and amount 3 000 000 not penalized, score 850", new(3_000_000m, 60, 300_000m, "MORTGAGE")),
    ("purpose REFINANCE, score 810", new(1_200_000m, 48, 200_000m, "REFINANCE")),
    ("purpose OTHER, score 770 (just below 800, discount 1.00)", new(300_000m, 12, 100_000m, "OTHER")),

    // DTI thresholds (nearest cent on each side)
    ("DTI just below 0.30 -> DTI_LOW", new(1_000_000m, 36, IncomeJustBelowDti(1_000_000m, 36, 0.30m), "CONSUMER")),
    ("DTI just above 0.30 -> DTI_MEDIUM", new(1_000_000m, 36, IncomeJustAboveDti(1_000_000m, 36, 0.30m), "CONSUMER")),
    ("DTI just below 0.50 -> DTI_MEDIUM", new(1_000_000m, 36, IncomeJustBelowDti(1_000_000m, 36, 0.50m), "CONSUMER")),
    ("DTI just above 0.50 -> DTI_HIGH", new(1_000_000m, 36, IncomeJustAboveDti(1_000_000m, 36, 0.50m), "CONSUMER")),

    // Terms and amounts
    ("term 6 and amount 50 000 (minimums)", new(50_000m, 6, 50_000m, "CONSUMER")),
    ("term 61 -> LONG_TERM", new(2_000_000m, 61, 300_000m, "CAR")),
    ("term 84 and amount 5 000 000 (maximums), max amount capped", new(5_000_000m, 84, 1_000_000m, "MORTGAGE")),
    ("amount 3 000 001 -> LARGE_AMOUNT", new(3_000_001m, 60, 400_000m, "CONSUMER")),

    // Score thresholds (reachable neighbours; 499 / 699 / 799 are not reachable)
    ("score exactly 700 via DTI_LOW + LONG_TERM + LARGE_AMOUNT", new(4_000_000m, 72, 500_000m, "CONSUMER")),
    ("score exactly 700 via DTI_MEDIUM + MORTGAGE", new(1_000_000m, 36, 90_000m, "MORTGAGE")),
    ("score 680 (highest below 700) -> REVIEW", new(1_000_000m, 36, 100_000m, "CAR")),
    ("score 650 REVIEW, no discount", new(1_000_000m, 36, 100_000m, "CONSUMER")),
    ("score 520 (lowest REVIEW)", new(4_000_000m, 72, 240_000m, "OTHER")),
    ("score 400 (highest REJECT)", new(2_000_000m, 24, 100_000m, "MORTGAGE")),
    ("score 320 REJECT", new(2_000_000m, 24, 100_000m, "OTHER")),
    ("score 330 REJECT with LONG_TERM", new(1_000_000m, 84, 20_000m, "CAR")),

    // Extremes of the reachable score range (clamp 0..1000 never fires: 220..850)
    ("lowest possible score 220", new(5_000_000m, 84, 1_000m, "OTHER")),
    ("highest possible score 850", new(100_000m, 12, 1_000_000m, "MORTGAGE")),

    // Rounding and caps
    ("fractional amount and income", new(123_456.78m, 18, 54_321.09m, "REFINANCE")),
    ("maximum income, max amount capped", new(5_000_000m, 12, 10_000_000m, "CONSUMER")),
    ("REVIEW: max approved amount below requested (rate above 20%)", new(1_000_000m, 36, 80_000m, "OTHER")),
};

static decimal Scale2(decimal value) => value + 0.00m;

using var stream = new MemoryStream();
using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
{
    writer.WriteStartArray();
    foreach (var (name, input) in cases)
    {
        var output = ScoringCalculator.Evaluate(input);

        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteStartObject("input");
        writer.WriteNumber("amount", Scale2(input.Amount));
        writer.WriteNumber("termMonths", input.TermMonths);
        writer.WriteNumber("monthlyIncome", Scale2(input.MonthlyIncome));
        writer.WriteString("purposeCode", input.PurposeCode);
        writer.WriteEndObject();
        writer.WriteStartObject("expected");
        writer.WriteNumber("score", output.Score);
        writer.WriteString("decision", output.Decision.ToCode());
        if (output.Rate is { } rate) { writer.WriteNumber("rate", Scale2(rate)); } else { writer.WriteNull("rate"); }
        if (output.MonthlyPayment is { } payment) { writer.WriteNumber("monthlyPayment", Scale2(payment)); } else { writer.WriteNull("monthlyPayment"); }
        writer.WriteNumber("maxApprovedAmount", Scale2(output.MaxApprovedAmount));
        writer.WriteStartArray("reasons");
        foreach (var reason in output.Reasons) { writer.WriteStringValue(reason); }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();

        Console.WriteLine($"{output.Score,4} {output.Decision.ToCode(),-7} {name}");
    }

    writer.WriteEndArray();
}

File.WriteAllText("tests/golden-vectors.json", Encoding.UTF8.GetString(stream.ToArray()) + "\n");
Console.WriteLine($"Wrote {cases.Count} vectors to tests/golden-vectors.json");
