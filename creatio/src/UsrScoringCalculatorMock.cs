namespace Terrasoft.Configuration.UsrLoanScoring
{
    using System;
    using System.Collections.Generic;

    /// <summary>Input of the scoring algorithm (contract 5).</summary>
    public class UsrScoringInput
    {
        public decimal Amount { get; set; }
        public int TermMonths { get; set; }
        public decimal MonthlyIncome { get; set; }
        public string PurposeCode { get; set; }
    }

    /// <summary>Output of the scoring algorithm. For REJECT: Rate and MonthlyPayment are null, MaxApprovedAmount is 0.</summary>
    public class UsrScoringOutput
    {
        public int Score { get; set; }
        public string Decision { get; set; }
        public decimal? Rate { get; set; }
        public decimal? MonthlyPayment { get; set; }
        public decimal MaxApprovedAmount { get; set; }
        public IList<string> Reasons { get; set; }
    }

    /// <summary>
    /// In-Creatio copy of the scoring algorithm (contract 5), used when UsrScoringUseMock = true.
    ///
    /// Deliberately duplicated from ScoringApi.Core.ScoringCalculator: a Creatio package cannot
    /// reference an assembly of this repository, and adding an external DLL to the configuration
    /// would be heavier than ~100 lines (ADR-017). Parity is guarded by tests/golden-vectors.json.
    /// Same approach as the reference: decimal everywhere, integer power by squaring, money
    /// rounded half away from zero.
    /// </summary>
    public static class UsrScoringCalculatorMock
    {
        private const decimal DtiBaseRate = 20.00m;
        private const int BaseScore = 600;
        private const decimal DtiLowLimit = 0.30m;
        private const decimal DtiHighLimit = 0.50m;
        private const int LongTermLimit = 60;
        private const decimal LargeAmountLimit = 3000000m;
        private const int MinScore = 0;
        private const int MaxScore = 1000;
        private const int ApproveThreshold = 700;
        private const int ReviewThreshold = 500;
        private const int HighDiscountScore = 800;
        private const decimal MaxPaymentShare = 0.40m;
        private const decimal MaxAmountStep = 1000m;
        private const decimal MaxAmountCap = 5000000m;

        /// <summary>Base rate, % per annum (contract 2.2; master copy is Oracle RATE_GRID).</summary>
        private static readonly IDictionary<string, decimal> BaseRates = new Dictionary<string, decimal>(StringComparer.Ordinal) {
            { UsrLoanPurposeCodes.Consumer, 21.90m },
            { UsrLoanPurposeCodes.Car, 17.90m },
            { UsrLoanPurposeCodes.Mortgage, 14.50m },
            { UsrLoanPurposeCodes.Refinance, 19.50m },
            { UsrLoanPurposeCodes.Other, 24.90m }
        };

        private static readonly IDictionary<string, int> PurposeBonuses = new Dictionary<string, int>(StringComparer.Ordinal) {
            { UsrLoanPurposeCodes.Mortgage, 50 },
            { UsrLoanPurposeCodes.Car, 30 },
            { UsrLoanPurposeCodes.Refinance, 10 },
            { UsrLoanPurposeCodes.Consumer, 0 },
            { UsrLoanPurposeCodes.Other, -30 }
        };

        public static UsrScoringOutput Evaluate(UsrScoringInput input) {
            if (input == null) {
                throw new ArgumentNullException("input");
            }
            decimal baseRate;
            if (input.PurposeCode == null || !BaseRates.TryGetValue(input.PurposeCode, out baseRate)) {
                throw new ArgumentException("Unknown purpose code '" + input.PurposeCode + "'.", "input");
            }

            var reasons = new List<string>();

            // Steps 1–2: estimated payment at 20% (not rounded) and debt-to-income.
            var basePayment = Annuity(input.Amount, DtiBaseRate, input.TermMonths);
            var dti = basePayment / input.MonthlyIncome;

            // Step 3: the rule order defines the order of reasons.
            var score = BaseScore;
            if (dti < DtiLowLimit) {
                score += 200;
                reasons.Add("DTI_LOW");
            } else if (dti <= DtiHighLimit) {
                score += 50;
                reasons.Add("DTI_MEDIUM");
            } else {
                score -= 250;
                reasons.Add("DTI_HIGH");
            }
            if (input.TermMonths > LongTermLimit) {
                score -= 50;
                reasons.Add("LONG_TERM");
            }
            if (input.Amount > LargeAmountLimit) {
                score -= 50;
                reasons.Add("LARGE_AMOUNT");
            }
            score += PurposeBonuses[input.PurposeCode];
            reasons.Add("PURPOSE_" + input.PurposeCode);
            score = Math.Max(MinScore, Math.Min(MaxScore, score));

            // Step 4.
            string decision = score >= ApproveThreshold ? UsrScoringDecisions.Approve
                : score >= ReviewThreshold ? UsrScoringDecisions.Review
                : UsrScoringDecisions.Reject;

            var output = new UsrScoringOutput { Score = score, Decision = decision, Reasons = reasons };
            if (decision == UsrScoringDecisions.Reject) {
                output.MaxApprovedAmount = 0m;
                return output;
            }

            // Step 5.
            var discount = score >= HighDiscountScore ? 2.00m : score >= ApproveThreshold ? 1.00m : 0m;
            var rate = baseRate - discount;
            output.Rate = rate;

            // Step 6.
            output.MonthlyPayment = RoundMoney(Annuity(input.Amount, rate, input.TermMonths));

            // Step 7: the annuity is linear in the amount, so the inverse is a division.
            var rawMax = MaxPaymentShare * input.MonthlyIncome / Annuity(1m, rate, input.TermMonths);
            output.MaxApprovedAmount = Math.Min(decimal.Floor(rawMax / MaxAmountStep) * MaxAmountStep, MaxAmountCap);
            return output;
        }

        /// <summary>Annuity payment, not rounded: amount * r / (1 - (1 + r)^(-term)), r = rate / 12 / 100.</summary>
        public static decimal Annuity(decimal amount, decimal ratePercent, int termMonths) {
            if (termMonths <= 0) {
                throw new ArgumentOutOfRangeException("termMonths");
            }
            if (ratePercent <= 0m) {
                throw new ArgumentOutOfRangeException("ratePercent");
            }
            var r = ratePercent / 12m / 100m;
            return amount * r / (1m - (1m / PowInt(1m + r, termMonths)));
        }

        /// <summary>Money rounding: 2 decimals, half away from zero (same as Oracle ROUND).</summary>
        public static decimal RoundMoney(decimal value) {
            return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// x^n by exponentiation by squaring in decimal. decimal has no Pow, and Math.Pow (double)
        /// would lose precision against Oracle NUMBER (ADR-006).
        /// </summary>
        private static decimal PowInt(decimal x, int n) {
            var result = 1m;
            while (n > 0) {
                if ((n & 1) == 1) {
                    result *= x;
                }
                x *= x;
                n >>= 1;
            }
            return result;
        }
    }
}
