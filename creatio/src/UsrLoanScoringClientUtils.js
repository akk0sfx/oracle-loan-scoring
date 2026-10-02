/**
 * UsrLoanScoringClientUtils — client-side constants and helpers of the loan scoring package.
 *
 * How Creatio loads this file (AMD):
 * - Every Classic UI client schema is an AMD module: define(name, [dependencies], factory).
 *   The name must equal the schema name; Creatio's RequireJS configuration maps a module
 *   name to the URL of the compiled schema, so "UsrLoanScoringClientUtils" in another
 *   schema's dependency list makes RequireJS fetch and run this file once and cache the result.
 * - The factory receives the dependencies in the same order and returns the module's public
 *   object. Here the only dependency is "UsrLoanScoringClientUtilsResources": for each client
 *   schema Creatio generates a "<SchemaName>Resources" module with its localizable strings in
 *   the current user's culture (resources.localizableStrings.<Name>).
 * - A module like this one is a plain shared object. Page schemas, in contrast, are loaded
 *   inside a sandbox: each opened page gets its own module instance and message bus, so two
 *   open pages never share view-model state.
 *
 * Everything computed here is a PREVIEW. The authoritative numbers are always produced on the
 * server (Scoring API / Oracle, or the C# mock); JavaScript uses binary floating point and
 * must never be the source of a stored value.
 */
define("UsrLoanScoringClientUtils", ["UsrLoanScoringClientUtilsResources"], function(resources) {
	var strings = resources.localizableStrings;

	/** UsrLoanStatus.UsrCode values (contract 2.1). */
	var StatusCodes = {
		NEW: "NEW",
		SCORING: "SCORING",
		REVIEW: "REVIEW",
		APPROVED: "APPROVED",
		REJECTED: "REJECTED"
	};

	/** Scoring decisions (contract 5, step 4). */
	var Decisions = {
		APPROVE: "APPROVE",
		REVIEW: "REVIEW",
		REJECT: "REJECT"
	};

	/** ScoreResult.errorCode values (contract 3). */
	var ErrorCodes = {
		APP_NOT_FOUND: "APP_NOT_FOUND",
		INVALID_STATUS: "INVALID_STATUS",
		VALIDATION_ERROR: "VALIDATION_ERROR",
		SCORING_UNAVAILABLE: "SCORING_UNAVAILABLE",
		SCORING_ERROR: "SCORING_ERROR"
	};

	/** Base rate, % per annum, by UsrLoanPurpose.UsrCode (contract 2.2; master copy is Oracle RATE_GRID). */
	var BaseRates = {
		CONSUMER: 21.90,
		CAR: 17.90,
		MORTGAGE: 14.50,
		REFINANCE: 19.50,
		OTHER: 24.90
	};

	/** Validation ranges (contract 2.3) — the same numbers the server checks. */
	var Limits = {
		MIN_AMOUNT: 50000,
		MAX_AMOUNT: 5000000,
		MIN_TERM: 6,
		MAX_TERM: 84,
		MAX_INCOME: 10000000
	};

	/**
	 * Annuity payment for the preview: amount * r / (1 - (1 + r)^(-term)), r = rate / 12 / 100.
	 * Returns null when the input is incomplete, so the page shows an empty field instead of NaN.
	 */
	function calcAnnuity(amount, rate, term) {
		if (!(amount > 0) || !(rate > 0) || !(term > 0)) {
			return null;
		}
		var r = rate / 12 / 100;
		var payment = amount * r / (1 - Math.pow(1 + r, -term));
		return Math.round(payment * 100) / 100;
	}

	/** Base rate of a purpose code, or null for an unknown/empty code. */
	function getBaseRate(purposeCode) {
		return BaseRates.hasOwnProperty(purposeCode) ? BaseRates[purposeCode] : null;
	}

	/** Localized text for a ScoreResult.errorCode; unknown codes get a generic message. */
	function getErrorMessage(errorCode) {
		return strings["Error_" + errorCode] || strings.Error_Unknown;
	}

	/** Localized name of a decision (APPROVE / REVIEW / REJECT). */
	function getDecisionCaption(decision) {
		return strings["Decision_" + decision] || decision;
	}

	return {
		StatusCodes: StatusCodes,
		Decisions: Decisions,
		ErrorCodes: ErrorCodes,
		BaseRates: BaseRates,
		Limits: Limits,
		calcAnnuity: calcAnnuity,
		getBaseRate: getBaseRate,
		getErrorMessage: getErrorMessage,
		getDecisionCaption: getDecisionCaption
	};
});
