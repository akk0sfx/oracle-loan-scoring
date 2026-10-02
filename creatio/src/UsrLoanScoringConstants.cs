namespace Terrasoft.Configuration.UsrLoanScoring
{
    using System.Collections.Generic;

    // =========================================================================================
    // All names and codes of docs/CONTRACTS.md used by the package. Other files reference these
    // constants only, so a renamed column or code is changed in exactly one place.
    //
    // Why "const string" and not enums: Creatio compares lookups by UsrCode text (contract 2.1),
    // column names are strings in the Entity API, and constants are inlined at compile time.
    //
    // Style note: Creatio compiles configuration with its own compiler settings, so the package
    // uses conservative C# (no records, switch expressions, file-scoped namespaces) — ADR-016.
    // =========================================================================================

    /// <summary>Schema (object) names.</summary>
    public static class UsrSchemaNames
    {
        public const string LoanApplication = "UsrLoanApplication";
        public const string LoanStatus = "UsrLoanStatus";
        public const string LoanPurpose = "UsrLoanPurpose";
        public const string DecisionHistory = "UsrLoanDecisionHistory";
    }

    /// <summary>
    /// Column names. For lookup columns the Entity API stores the value in "{Name}Id"
    /// (e.g. UsrStatusId), which is what SetColumnValue / GetTypedColumnValue&lt;Guid&gt; use.
    /// </summary>
    public static class UsrColumnNames
    {
        public const string Id = "Id";
        public const string UsrCode = "UsrCode";

        // UsrLoanApplication
        public const string Number = "UsrNumber";
        public const string ContactId = "UsrContactId";
        public const string PurposeId = "UsrPurposeId";
        public const string Amount = "UsrAmount";
        public const string TermMonths = "UsrTermMonths";
        public const string MonthlyIncome = "UsrMonthlyIncome";
        public const string StatusId = "UsrStatusId";
        public const string Score = "UsrScore";
        public const string Decision = "UsrDecision";
        public const string Rate = "UsrRate";
        public const string MonthlyPayment = "UsrMonthlyPayment";
        public const string MaxApprovedAmount = "UsrMaxApprovedAmount";
        public const string ScoreReasons = "UsrScoreReasons";
        public const string ScoredOn = "UsrScoredOn";
        public const string DecisionComment = "UsrDecisionComment";

        // UsrLoanDecisionHistory
        public const string HistoryApplicationId = "UsrApplicationId";
        public const string HistoryStatusFromId = "UsrStatusFromId";
        public const string HistoryStatusToId = "UsrStatusToId";
        public const string HistoryScore = "UsrScore";
        public const string HistoryComment = "UsrComment";
    }

    /// <summary>UsrLoanStatus.UsrCode values (contract 2.1).</summary>
    public static class UsrLoanStatusCodes
    {
        public const string New = "NEW";
        public const string Scoring = "SCORING";
        public const string Review = "REVIEW";
        public const string Approved = "APPROVED";
        public const string Rejected = "REJECTED";
    }

    /// <summary>UsrLoanPurpose.UsrCode values (contract 2.2).</summary>
    public static class UsrLoanPurposeCodes
    {
        public const string Consumer = "CONSUMER";
        public const string Car = "CAR";
        public const string Mortgage = "MORTGAGE";
        public const string Refinance = "REFINANCE";
        public const string Other = "OTHER";
    }

    /// <summary>Scoring decisions (contract 5, step 4) as stored in UsrDecision.</summary>
    public static class UsrScoringDecisions
    {
        public const string Approve = "APPROVE";
        public const string Review = "REVIEW";
        public const string Reject = "REJECT";
    }

    /// <summary>ScoreResult.errorCode values (contract 3).</summary>
    public static class UsrScoreErrorCodes
    {
        public const string AppNotFound = "APP_NOT_FOUND";
        public const string InvalidStatus = "INVALID_STATUS";
        public const string ValidationError = "VALIDATION_ERROR";
        public const string ScoringUnavailable = "SCORING_UNAVAILABLE";
        public const string ScoringError = "SCORING_ERROR";
    }

    /// <summary>System setting codes (contract 2.5).</summary>
    public static class UsrSysSettingCodes
    {
        public const string ScoringApiUrl = "UsrScoringApiUrl";
        public const string ScoringApiKey = "UsrScoringApiKey";
        public const string ScoringUseMock = "UsrScoringUseMock";
        public const string ScoringTimeoutSec = "UsrScoringTimeoutSec";
        public const string LastNumber = "UsrLoanApplicationLastNumber";
    }

    /// <summary>Scoring API wire constants (contract 4).</summary>
    public static class UsrScoringApiConstants
    {
        public const string EvaluatePath = "/api/v1/scoring/evaluate";
        public const string ApiKeyHeader = "X-Api-Key";
        public const string CorrelationIdHeader = "X-Correlation-Id";
        public const int DefaultTimeoutSec = 10;
    }

    /// <summary>Loan application rules shared by the service and the listener (contract 2.1, 2.3).</summary>
    public static class UsrLoanApplicationRules
    {
        public const decimal MinAmount = 50000m;
        public const decimal MaxAmount = 5000000m;
        public const int MinTermMonths = 6;
        public const int MaxTermMonths = 84;
        public const decimal MaxMonthlyIncome = 10000000m;

        public const string NumberPrefix = "LA-";
        public const string NumberFormat = "D6";   // LA-000001

        /// <summary>Allowed status transitions: from -> set of to (contract 2.1).</summary>
        public static readonly IDictionary<string, ISet<string>> AllowedTransitions =
            new Dictionary<string, ISet<string>> {
                { UsrLoanStatusCodes.New, new HashSet<string> { UsrLoanStatusCodes.Scoring } },
                { UsrLoanStatusCodes.Scoring, new HashSet<string> {
                    UsrLoanStatusCodes.Approved, UsrLoanStatusCodes.Review,
                    UsrLoanStatusCodes.Rejected, UsrLoanStatusCodes.New } },
                { UsrLoanStatusCodes.Review, new HashSet<string> {
                    UsrLoanStatusCodes.Approved, UsrLoanStatusCodes.Rejected } }
            };

        public static bool IsTransitionAllowed(string fromCode, string toCode) {
            ISet<string> targets;
            return AllowedTransitions.TryGetValue(fromCode ?? string.Empty, out targets) && targets.Contains(toCode);
        }

        /// <summary>
        /// Range checks of contract 2.3 (identical on client and server).
        /// Returns human-readable messages; an empty list means the values are valid.
        /// </summary>
        public static IList<string> ValidateRanges(decimal amount, int termMonths, decimal monthlyIncome) {
            var errors = new List<string>();
            if (amount < MinAmount || amount > MaxAmount) {
                errors.Add(string.Format("Amount must be between {0:0} and {1:0}.", MinAmount, MaxAmount));
            }
            if (termMonths < MinTermMonths || termMonths > MaxTermMonths) {
                errors.Add(string.Format("Term must be between {0} and {1} months.", MinTermMonths, MaxTermMonths));
            }
            if (monthlyIncome <= 0m || monthlyIncome > MaxMonthlyIncome) {
                errors.Add(string.Format("Monthly income must be greater than 0 and at most {0:0}.", MaxMonthlyIncome));
            }
            return errors;
        }
    }
}
