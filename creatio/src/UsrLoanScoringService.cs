namespace Terrasoft.Configuration.UsrLoanScoring
{
    using System;
    using System.Runtime.Serialization;
    using System.ServiceModel;
    using System.ServiceModel.Activation;
    using System.ServiceModel.Web;
    using Terrasoft.Core.Configuration;
    using Terrasoft.Core.Entities;
    using Terrasoft.Web.Common;

    /// <summary>
    /// Response of POST /0/rest/UsrLoanScoringService/Score (contract 3).
    /// BodyStyle = Wrapped makes the platform wrap it as {"ScoreResult": {...}}.
    /// DataMember names are the JSON names, so C# property names may follow C# style.
    /// </summary>
    [DataContract]
    public class ScoreResult
    {
        [DataMember(Name = "success")] public bool Success { get; set; }
        [DataMember(Name = "errorCode")] public string ErrorCode { get; set; }
        [DataMember(Name = "errorMessage")] public string ErrorMessage { get; set; }
        [DataMember(Name = "statusCode")] public string StatusCode { get; set; }
        [DataMember(Name = "score")] public int? Score { get; set; }
        [DataMember(Name = "decision")] public string Decision { get; set; }
        [DataMember(Name = "rate")] public decimal? Rate { get; set; }
        [DataMember(Name = "monthlyPayment")] public decimal? MonthlyPayment { get; set; }
        [DataMember(Name = "maxApprovedAmount")] public decimal? MaxApprovedAmount { get; set; }
        [DataMember(Name = "reasons")] public string Reasons { get; set; }
    }

    /// <summary>
    /// Configuration web service that scores a loan application (contract 3).
    ///
    /// Security: services under /0/rest/ require an authenticated Creatio session (cookie +
    /// BPMCSRF header); anonymous calls never reach this code. Record-level access is enforced
    /// by loading and saving the application with Entity.UseAdminRights = true, i.e. with the
    /// rights of the current user — see ADR-019 and Q-010.
    /// </summary>
    [ServiceContract]
    [AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)]
    public class UsrLoanScoringService : BaseService
    {
        private static readonly global::Common.Logging.ILog Log = global::Common.Logging.LogManager.GetLogger("UsrLoanScoring");

        [OperationContract]
        [WebInvoke(Method = "POST", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json,
            BodyStyle = WebMessageBodyStyle.Wrapped)]
        public ScoreResult Score(Guid applicationId) {
            // One id per call; it also goes to the Scoring API as X-Correlation-Id, so the Creatio
            // log line and the API log line of the same scoring can be matched.
            var correlationId = Guid.NewGuid().ToString();
            var statuses = new UsrLoanStatusHelper(UserConnection);

            // ---- Step 1: read the application ----
            var application = UserConnection.EntitySchemaManager
                .GetInstanceByName(UsrSchemaNames.LoanApplication)
                .CreateEntity(UserConnection);
            // Apply the current user's record rights: an application the user cannot see is
            // reported as "not found" and its existence is not disclosed.
            application.UseAdminRights = true;
            if (applicationId == Guid.Empty || !application.FetchFromDB(applicationId)) {
                Log.InfoFormat("Score: application not found. ApplicationId={0}, CorrelationId={1}", applicationId, correlationId);
                return Error(UsrScoreErrorCodes.AppNotFound, "Loan application not found.", null);
            }

            // ---- Step 2: only NEW applications can be scored ----
            var statusCode = statuses.GetStatusCode(application.GetTypedColumnValue<Guid>(UsrColumnNames.StatusId));
            if (statusCode != UsrLoanStatusCodes.New) {
                return Error(UsrScoreErrorCodes.InvalidStatus,
                    string.Format("Only applications in status {0} can be scored; current status is {1}.",
                        UsrLoanStatusCodes.New, statusCode ?? "empty"),
                    statusCode);
            }

            // ---- Step 3: ranges (same rules as the client page and the listener) ----
            var input = new UsrScoringInput {
                Amount = application.GetTypedColumnValue<decimal>(UsrColumnNames.Amount),
                TermMonths = application.GetTypedColumnValue<int>(UsrColumnNames.TermMonths),
                MonthlyIncome = application.GetTypedColumnValue<decimal>(UsrColumnNames.MonthlyIncome),
                PurposeCode = statuses.GetPurposeCode(application.GetTypedColumnValue<Guid>(UsrColumnNames.PurposeId))
            };
            var rangeErrors = UsrLoanApplicationRules.ValidateRanges(input.Amount, input.TermMonths, input.MonthlyIncome);
            if (input.PurposeCode == null) {
                rangeErrors.Add("Loan purpose is not set.");
            }
            if (rangeErrors.Count > 0) {
                return Error(UsrScoreErrorCodes.ValidationError, string.Join(" ", rangeErrors), statusCode);
            }

            // ---- Step 4: NEW -> SCORING, saved before the call ----
            // Saving first makes the "in progress" state visible to other users and lets the
            // listener record the transition, even if the call below takes the full timeout.
            application.SetColumnValue(UsrColumnNames.StatusId, statuses.GetStatusId(UsrLoanStatusCodes.Scoring));
            application.Save();

            // ---- Step 5: API or mock ----
            var useMock = SysSettings.GetValue(UserConnection, UsrSysSettingCodes.ScoringUseMock, true);
            UsrScoringCallResult call;
            try {
                call = useMock
                    ? new UsrScoringCallResult { Status = UsrScoringCallStatus.Success, Output = UsrScoringCalculatorMock.Evaluate(input) }
                    : new UsrScoringApiClient(UserConnection).Evaluate(applicationId, input, correlationId);
            } catch (Exception ex) {
                // Anything unexpected must not leave the application stuck in SCORING.
                Log.Error(string.Format("Score: scoring failed unexpectedly. ApplicationId={0}, CorrelationId={1}",
                    applicationId, correlationId), ex);
                call = new UsrScoringCallResult { Status = UsrScoringCallStatus.Error, ErrorMessage = "Scoring failed." };
            }

            // An unknown decision from the API cannot be mapped to a status (contract 3, step 6).
            if (call.Status == UsrScoringCallStatus.Success && StatusForDecision(call.Output.Decision) == null) {
                Log.ErrorFormat("Score: unknown decision '{0}'. ApplicationId={1}, CorrelationId={2}",
                    call.Output.Decision, applicationId, correlationId);
                call = new UsrScoringCallResult { Status = UsrScoringCallStatus.Error, ErrorMessage = "Scoring service returned an unknown decision." };
            }

            // ---- Step 7: failure -> back to NEW ----
            if (call.Status != UsrScoringCallStatus.Success) {
                var errorCode = call.Status == UsrScoringCallStatus.Unavailable
                    ? UsrScoreErrorCodes.ScoringUnavailable
                    : UsrScoreErrorCodes.ScoringError;
                Log.WarnFormat("Score: {0}. ApplicationId={1}, CorrelationId={2}, Mock={3}",
                    errorCode, applicationId, correlationId, useMock);
                RollbackToNew(applicationId, statuses, correlationId);
                return Error(errorCode, call.ErrorMessage, UsrLoanStatusCodes.New);
            }

            // ---- Step 6: success -> write the result and the status by decision ----
            var output = call.Output;
            var newStatusCode = StatusForDecision(output.Decision);
            var reasons = string.Join(",", output.Reasons);
            try {
                application.SetColumnValue(UsrColumnNames.Score, output.Score);
                application.SetColumnValue(UsrColumnNames.Decision, output.Decision);
                // Creatio numeric columns hold 0 rather than NULL; for REJECT "no rate / no payment"
                // is stored as 0 while the response keeps null as the contract says (ADR-020).
                application.SetColumnValue(UsrColumnNames.Rate, output.Rate ?? 0m);
                application.SetColumnValue(UsrColumnNames.MonthlyPayment, output.MonthlyPayment ?? 0m);
                application.SetColumnValue(UsrColumnNames.MaxApprovedAmount, output.MaxApprovedAmount);
                application.SetColumnValue(UsrColumnNames.ScoreReasons, reasons);
                // TODO(verify): Creatio stores DateTime columns in UTC when set from server code — Q-011.
                application.SetColumnValue(UsrColumnNames.ScoredOn, DateTime.UtcNow);
                application.SetColumnValue(UsrColumnNames.StatusId, statuses.GetStatusId(newStatusCode));
                application.Save();
            } catch (Exception ex) {
                // The scoring itself succeeded, but the result could not be stored: never leave SCORING behind.
                Log.Error(string.Format("Score: saving the result failed. ApplicationId={0}, CorrelationId={1}",
                    applicationId, correlationId), ex);
                RollbackToNew(applicationId, statuses, correlationId);
                return Error(UsrScoreErrorCodes.ScoringError, "Scoring result could not be saved.", UsrLoanStatusCodes.New);
            }

            // Identifiers and the outcome only — no amounts or income in the log (personal data).
            Log.InfoFormat("Score: application scored. ApplicationId={0}, CorrelationId={1}, Decision={2}, Score={3}, Mock={4}",
                applicationId, correlationId, output.Decision, output.Score, useMock);

            return new ScoreResult {
                Success = true,
                StatusCode = newStatusCode,
                Score = output.Score,
                Decision = output.Decision,
                Rate = output.Rate,
                MonthlyPayment = output.MonthlyPayment,
                MaxApprovedAmount = output.MaxApprovedAmount,
                Reasons = reasons
            };
        }

        /// <summary>
        /// SCORING -> NEW (contract 2.1). A fresh entity is loaded because the one in hand may hold
        /// unsaved result values from a failed save.
        /// </summary>
        private void RollbackToNew(Guid applicationId, UsrLoanStatusHelper statuses, string correlationId) {
            try {
                var application = UserConnection.EntitySchemaManager
                    .GetInstanceByName(UsrSchemaNames.LoanApplication)
                    .CreateEntity(UserConnection);
                application.UseAdminRights = true;
                if (application.FetchFromDB(applicationId)) {
                    application.SetColumnValue(UsrColumnNames.StatusId, statuses.GetStatusId(UsrLoanStatusCodes.New));
                    application.Save();
                }
            } catch (Exception ex) {
                // Last resort: the application stays in SCORING and needs manual correction.
                Log.Error(string.Format("Score: rollback to NEW failed. ApplicationId={0}, CorrelationId={1}",
                    applicationId, correlationId), ex);
            }
        }

        private static string StatusForDecision(string decision) {
            switch (decision) {
                case UsrScoringDecisions.Approve:
                    return UsrLoanStatusCodes.Approved;
                case UsrScoringDecisions.Review:
                    return UsrLoanStatusCodes.Review;
                case UsrScoringDecisions.Reject:
                    return UsrLoanStatusCodes.Rejected;
                default:
                    return null;
            }
        }

        private static ScoreResult Error(string errorCode, string message, string statusCode) {
            return new ScoreResult { Success = false, ErrorCode = errorCode, ErrorMessage = message, StatusCode = statusCode };
        }
    }
}
