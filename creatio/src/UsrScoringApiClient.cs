namespace Terrasoft.Configuration.UsrLoanScoring
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using Terrasoft.Core;
    using Terrasoft.Core.Configuration;

    /// <summary>Outcome kind of a Scoring API call.</summary>
    public enum UsrScoringCallStatus
    {
        Success,
        /// <summary>Network failure or timeout -> SCORING_UNAVAILABLE.</summary>
        Unavailable,
        /// <summary>API answered 4xx/5xx or an unreadable body -> SCORING_ERROR.</summary>
        Error
    }

    public class UsrScoringCallResult
    {
        public UsrScoringCallStatus Status { get; set; }
        public UsrScoringOutput Output { get; set; }
        /// <summary>Safe, short description for the caller; details go to the log only.</summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// HTTP client of the Scoring API (contract 4): POST {UsrScoringApiUrl}/api/v1/scoring/evaluate.
    /// </summary>
    public class UsrScoringApiClient
    {
        // One HttpClient for the whole application: creating one per call exhausts sockets under
        // load (each instance keeps its own connection pool). Timeout is Infinite here and the
        // per-call timeout comes from UsrScoringTimeoutSec via a CancellationTokenSource, because
        // HttpClient.Timeout cannot be changed after the first request while the system setting can.
        private static readonly HttpClient SharedClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        // TODO(verify): Common.Logging is the logging facade of Creatio on .NET Framework; confirm it
        // is available to configuration code on Creatio .NET 8 — docs/OPEN_QUESTIONS.md, Q-008.
        private static readonly global::Common.Logging.ILog Log = global::Common.Logging.LogManager.GetLogger("UsrLoanScoring");

        private readonly UserConnection _userConnection;

        public UsrScoringApiClient(UserConnection userConnection) {
            _userConnection = userConnection;
        }

        public UsrScoringCallResult Evaluate(Guid applicationId, UsrScoringInput input, string correlationId) {
            // Read on every call: an administrator may switch URL / key / timeout without a restart.
            // TODO(verify): SysSettings.GetValue<T>(UserConnection, string, T) overload — Q-009.
            // TODO(verify): GetValue returns the decrypted value of an encrypted (SecureText) setting
            // in server code — docs/OPEN_QUESTIONS.md, Q-009.
            var baseUrl = SysSettings.GetValue(_userConnection, UsrSysSettingCodes.ScoringApiUrl, string.Empty);
            var apiKey = SysSettings.GetValue(_userConnection, UsrSysSettingCodes.ScoringApiKey, string.Empty);
            var timeoutSec = SysSettings.GetValue(_userConnection, UsrSysSettingCodes.ScoringTimeoutSec,
                UsrScoringApiConstants.DefaultTimeoutSec);
            if (timeoutSec <= 0) {
                timeoutSec = UsrScoringApiConstants.DefaultTimeoutSec;
            }

            // A wrong UsrScoringApiUrl is a configuration error, not an outage -> SCORING_ERROR.
            Uri endpoint;
            if (!Uri.TryCreate(baseUrl.TrimEnd('/') + UsrScoringApiConstants.EvaluatePath, UriKind.Absolute, out endpoint)
                    || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)) {
                Log.ErrorFormat("System setting {0} is not an absolute http(s) URL. CorrelationId={1}",
                    UsrSysSettingCodes.ScoringApiUrl, correlationId);
                return Failure(UsrScoringCallStatus.Error, "Scoring service URL is not configured correctly.");
            }

            var requestBody = JsonConvert.SerializeObject(new EvaluateRequest {
                ApplicationId = applicationId.ToString(),
                Amount = input.Amount,
                TermMonths = input.TermMonths,
                MonthlyIncome = input.MonthlyIncome,
                PurposeCode = input.PurposeCode
            });

            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec))) {
                request.Headers.Add(UsrScoringApiConstants.ApiKeyHeader, apiKey);
                request.Headers.Add(UsrScoringApiConstants.CorrelationIdHeader, correlationId);
                request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                string responseBody;
                try {
                    // The Creatio service method is synchronous, so the call blocks here.
                    // ConfigureAwait(false) avoids capturing a synchronization context.
                    response = SharedClient.SendAsync(request, timeout.Token).ConfigureAwait(false).GetAwaiter().GetResult();
                    responseBody = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                } catch (OperationCanceledException) {
                    Log.WarnFormat("Scoring API timed out after {0}s. ApplicationId={1}, CorrelationId={2}",
                        timeoutSec, applicationId, correlationId);
                    return Failure(UsrScoringCallStatus.Unavailable, "Scoring service did not respond in time.");
                } catch (HttpRequestException ex) {
                    Log.Warn(string.Format("Scoring API unreachable. ApplicationId={0}, CorrelationId={1}",
                        applicationId, correlationId), ex);
                    return Failure(UsrScoringCallStatus.Unavailable, "Scoring service is unreachable.");
                }

                using (response) {
                    if (!response.IsSuccessStatusCode) {
                        // The ProblemDetails body has no personal data (contract 4), so it is safe to log.
                        Log.ErrorFormat("Scoring API returned {0}. ApplicationId={1}, CorrelationId={2}, Body={3}",
                            (int)response.StatusCode, applicationId, correlationId, responseBody);
                        return Failure(UsrScoringCallStatus.Error,
                            string.Format("Scoring service returned HTTP {0}.", (int)response.StatusCode));
                    }
                    try {
                        var dto = JsonConvert.DeserializeObject<EvaluateResponse>(responseBody);
                        return new UsrScoringCallResult {
                            Status = UsrScoringCallStatus.Success,
                            Output = new UsrScoringOutput {
                                Score = dto.Score,
                                Decision = dto.Decision,
                                Rate = dto.Rate,
                                MonthlyPayment = dto.MonthlyPayment,
                                MaxApprovedAmount = dto.MaxApprovedAmount,
                                Reasons = dto.Reasons ?? new List<string>()
                            }
                        };
                    } catch (JsonException ex) {
                        Log.Error(string.Format("Scoring API returned an unreadable body. CorrelationId={0}", correlationId), ex);
                        return Failure(UsrScoringCallStatus.Error, "Scoring service returned an invalid response.");
                    }
                }
            }
        }

        private static UsrScoringCallResult Failure(UsrScoringCallStatus status, string message) {
            return new UsrScoringCallResult { Status = status, ErrorMessage = message };
        }

        // Wire DTOs: JSON names exactly as in contract 4 (camelCase).
        private class EvaluateRequest
        {
            [JsonProperty("applicationId")] public string ApplicationId { get; set; }
            [JsonProperty("amount")] public decimal Amount { get; set; }
            [JsonProperty("termMonths")] public int TermMonths { get; set; }
            [JsonProperty("monthlyIncome")] public decimal MonthlyIncome { get; set; }
            [JsonProperty("purposeCode")] public string PurposeCode { get; set; }
        }

        private class EvaluateResponse
        {
            [JsonProperty("score")] public int Score { get; set; }
            [JsonProperty("decision")] public string Decision { get; set; }
            [JsonProperty("rate")] public decimal? Rate { get; set; }
            [JsonProperty("monthlyPayment")] public decimal? MonthlyPayment { get; set; }
            [JsonProperty("maxApprovedAmount")] public decimal MaxApprovedAmount { get; set; }
            [JsonProperty("reasons")] public List<string> Reasons { get; set; }
        }
    }
}
