/**
 * UsrLoanApplication1Page — Classic UI record page of UsrLoanApplication.
 *
 * ---------------------------------------------------------------------------------------------
 * How a Classic UI page schema is built
 * ---------------------------------------------------------------------------------------------
 * The schema does not describe a whole page. It is a set of differences ("replacing schema")
 * applied on top of the parent page (BasePageV2 via the wizard-generated chain). Creatio merges
 * every section of the returned object with the parent's:
 *
 * - attributes  — view-model properties. Every column of entitySchemaName becomes an attribute
 *   automatically (UsrAmount, UsrStatus, ...); this section only adds settings to them or declares
 *   VIRTUAL ones (not stored in the database, e.g. EstimatedPayment).
 * - methods     — view-model methods; this.callParent(arguments) calls the parent implementation.
 * - diff        — changes to the parent's view tree (the "view config"). Each item is an operation:
 *     insert — add a new element with "name" into "parentName" (its "propertyName" collection,
 *              usually "items"; "tabs" for a tab panel) at optional "index";
 *     merge  — change "values" of an existing element with that "name" (e.g. hide a parent field);
 *     remove — delete an existing element (and its children) by "name";
 *     also move — relocate an existing element to another parent.
 *   The /**SCHEMA_DIFF*\/ markers let the page designer find and rewrite this array; code outside
 *   the markers is kept, code inside may be regenerated if the page is reopened in the wizard.
 * - details, businessRules, rules, messages, modules — registered details, wizard business rules,
 *   rule-based behaviour, sandbox messages, embedded modules.
 *
 * bindTo: a view property written as {bindTo: "X"} is bound to view-model attribute X (or to a
 * method X for events such as "click"). The view re-renders when the attribute changes, and an
 * editable control writes the user's input back into the attribute — two-way data binding.
 *
 * dependencies vs change events: "dependencies" in an attribute declares "when any of these columns
 * change, call methodName" in one place, and Creatio calls it after the change is applied. Subscribing
 * manually (this.on("change:UsrAmount", handler, this) in init) does the same per column but must be
 * written for each column, and forgetting to unsubscribe leaks handlers. Dependencies do not fire on
 * the initial load, so derived values are also computed in onEntityInitialized.
 * ---------------------------------------------------------------------------------------------
 */
define("UsrLoanApplication1Page", ["ServiceHelper", "UsrLoanScoringClientUtils"],
	function(ServiceHelper, ScoringUtils) {
		var Status = ScoringUtils.StatusCodes;
		var Limits = ScoringUtils.Limits;

		// TODO(verify): the name the section wizard generated for the decision history detail schema.
		// Replace in both places (details + diff) after the package export — docs/OPEN_QUESTIONS.md, Q-014.
		var HISTORY_DETAIL_SCHEMA = "UsrLoanDecisionHistoryDetail";

		return {
			entitySchemaName: "UsrLoanApplication",

			attributes: {
				// Load UsrCode together with the lookup value, so the page knows the status / purpose
				// code from this.get("UsrStatus").UsrCode without extra queries.
				// TODO(verify): lookupListConfig.columns is also applied when the record is loaded,
				// not only in the lookup selection window — Q-015.
				"UsrStatus": {
					lookupListConfig: {columns: ["UsrCode"]}
				},
				"UsrPurpose": {
					lookupListConfig: {columns: ["UsrCode"]}
				},

				// Financial fields can be edited only while the status is NEW (contract 2.3).
				"IsFinancialEditable": {
					dataValueType: Terrasoft.DataValueType.BOOLEAN,
					type: Terrasoft.ViewModelColumnType.VIRTUAL_COLUMN,
					value: true
				},
				// The scoring button is shown for a saved application in status NEW.
				"CanSendToScoring": {
					dataValueType: Terrasoft.DataValueType.BOOLEAN,
					type: Terrasoft.ViewModelColumnType.VIRTUAL_COLUMN,
					value: false
				},
				// Guard against a double click while the service call is running.
				"IsScoringInProgress": {
					dataValueType: Terrasoft.DataValueType.BOOLEAN,
					type: Terrasoft.ViewModelColumnType.VIRTUAL_COLUMN,
					value: false
				},
				// Preview of the monthly payment at the purpose base rate. The real rate and payment
				// come from the server after scoring.
				"EstimatedPayment": {
					dataValueType: Terrasoft.DataValueType.FLOAT,
					type: Terrasoft.ViewModelColumnType.VIRTUAL_COLUMN,
					value: null,
					dependencies: [
						{
							columns: ["UsrAmount", "UsrTermMonths", "UsrPurpose", "UsrStatus"],
							methodName: "refreshDerivedValues"
						}
					]
				}
			},

			messages: {},

			details: /**SCHEMA_DETAILS*/{
				"UsrLoanDecisionHistoryDetail": {
					schemaName: HISTORY_DETAIL_SCHEMA,
					entitySchemaName: "UsrLoanDecisionHistory",
					// Show only the history of this application: detail.UsrApplication = page.Id.
					filter: {
						detailColumn: "UsrApplication",
						masterColumn: "Id"
					}
				}
			}/**SCHEMA_DETAILS*/,

			businessRules: /**SCHEMA_BUSINESS_RULES*/{}/**SCHEMA_BUSINESS_RULES*/,

			// Read-only behaviour is bound in diff (enabled: bindTo / false) rather than written as rules,
			// so one attribute drives several fields and the logic stays in code.
			rules: {},

			methods: {
				/** Derived values must also be computed on load: dependencies fire only on changes. */
				onEntityInitialized: function() {
					this.callParent(arguments);
					this.refreshDerivedValues();
				},

				/**
				 * After the first save the record is no longer "new", so the scoring button may appear.
				 * TODO(verify): onSaved is called after a successful save in this version — Q-016.
				 */
				onSaved: function() {
					this.callParent(arguments);
					this.refreshDerivedValues();
				},

				/** Status code of the current record, e.g. "NEW"; null if the status is empty. */
				getStatusCode: function() {
					var status = this.get("UsrStatus");
					return status ? status.UsrCode : null;
				},

				/** Recomputes the virtual attributes from the current column values. */
				refreshDerivedValues: function() {
					var isNewStatus = !this.getStatusCode() || this.getStatusCode() === Status.NEW;
					this.set("IsFinancialEditable", isNewStatus);
					// isNewMode(): the record has not been saved to the database yet.
					this.set("CanSendToScoring", isNewStatus && !this.isNewMode() && !this.get("IsScoringInProgress"));

					var purpose = this.get("UsrPurpose");
					var rate = ScoringUtils.getBaseRate(purpose ? purpose.UsrCode : null);
					this.set("EstimatedPayment",
						ScoringUtils.calcAnnuity(this.get("UsrAmount"), rate, this.get("UsrTermMonths")));
				},

				// ---- Validation (contract 2.3, same ranges as the server) ----

				setValidationConfig: function() {
					this.callParent(arguments);
					this.addColumnValidator("UsrAmount", this.amountValidator);
					this.addColumnValidator("UsrTermMonths", this.termValidator);
					this.addColumnValidator("UsrMonthlyIncome", this.incomeValidator);
				},

				/** A validator returns {invalidMessage}; an empty message means the value is valid. */
				amountValidator: function(value) {
					var invalid = value !== null && value !== undefined &&
						(value < Limits.MIN_AMOUNT || value > Limits.MAX_AMOUNT);
					return {invalidMessage: invalid ? this.get("Resources.Strings.AmountRangeMessage") : ""};
				},

				termValidator: function(value) {
					var invalid = value !== null && value !== undefined &&
						(value < Limits.MIN_TERM || value > Limits.MAX_TERM);
					return {invalidMessage: invalid ? this.get("Resources.Strings.TermRangeMessage") : ""};
				},

				incomeValidator: function(value) {
					var invalid = value !== null && value !== undefined &&
						(value <= 0 || value > Limits.MAX_INCOME);
					return {invalidMessage: invalid ? this.get("Resources.Strings.IncomeRangeMessage") : ""};
				},

				// ---- Scoring button ----

				onSendToScoringClick: function() {
					if (this.get("IsScoringInProgress")) {
						return;
					}
					this.setScoringInProgress(true);
					// Unsaved edits must reach the database first: the server scores the stored record.
					// TODO(verify): isChanged() and save({isSilent, callback, scope}) in this version — Q-016.
					if (this.isChanged()) {
						this.save({
							isSilent: true,
							callback: this.callScoringService,
							scope: this
						});
					} else {
						this.callScoringService();
					}
				},

				callScoringService: function() {
					this.showBodyMask();
					ServiceHelper.callService("UsrLoanScoringService", "Score", this.onScoringResponse,
						{applicationId: this.get("Id")}, this);
				},

				/**
				 * BodyStyle = Wrapped on the server, so the payload is response.ScoreResult (contract 3).
				 * A transport failure (no session, 500) gives no ScoreResult and is shown as a generic error.
				 */
				onScoringResponse: function(response) {
					this.hideBodyMask();
					this.setScoringInProgress(false);
					var result = response && response.ScoreResult;
					// The server changed the record (status, results) in any case except APP_NOT_FOUND,
					// so the page reloads to show the stored values.
					// TODO(verify): reloadEntity() re-reads the record and refreshes details — Q-016.
					this.reloadEntity();
					if (result && result.success) {
						this.showInformationDialog(this.formatScoringResult(result));
					} else {
						this.showInformationDialog(ScoringUtils.getErrorMessage(result ? result.errorCode : null));
					}
				},

				setScoringInProgress: function(value) {
					this.set("IsScoringInProgress", value);
					this.refreshDerivedValues();
				},

				/** "Decision: Approved. Monthly payment: 25,423.48 ₽ at 19.90%." in the user's culture. */
				formatScoringResult: function(result) {
					var message = Ext.String.format(this.get("Resources.Strings.ScoringDecisionMessage"),
						ScoringUtils.getDecisionCaption(result.decision), result.score);
					if (result.monthlyPayment !== null && result.monthlyPayment !== undefined) {
						message += " " + Ext.String.format(this.get("Resources.Strings.ScoringPaymentMessage"),
							this.formatMoney(result.monthlyPayment), this.formatMoney(result.rate));
					}
					return message;
				},

				formatMoney: function(value) {
					return Number(value).toLocaleString(undefined, {minimumFractionDigits: 2, maximumFractionDigits: 2});
				}
			},

			diff: /**SCHEMA_DIFF*/[
				// ---- Header: number and status, both read-only ----
				{
					"operation": "insert",
					"name": "UsrNumber",
					"parentName": "Header",
					"propertyName": "items",
					"values": {
						"bindTo": "UsrNumber",
						"enabled": false,
						"layout": {"column": 0, "row": 0, "colSpan": 12}
					}
				},
				{
					"operation": "insert",
					"name": "UsrStatus",
					"parentName": "Header",
					"propertyName": "items",
					"values": {
						"bindTo": "UsrStatus",
						// Status changes only through scoring and the approval process.
						"enabled": false,
						"layout": {"column": 12, "row": 0, "colSpan": 12}
					}
				},

				// ---- Green button next to Save / Close ----
				// TODO(verify): "LeftContainer" holds the page action buttons in this version;
				// in combined (section + card) mode the container may be "CombinedModeActionButtonsCardLeftContainer" — Q-017.
				{
					"operation": "insert",
					"name": "SendToScoringButton",
					"parentName": "LeftContainer",
					"propertyName": "items",
					"values": {
						"itemType": Terrasoft.ViewItemType.BUTTON,
						"caption": {"bindTo": "Resources.Strings.SendToScoringButtonCaption"},
						"click": {"bindTo": "onSendToScoringClick"},
						"style": Terrasoft.controls.ButtonEnums.style.GREEN,
						"visible": {"bindTo": "CanSendToScoring"}
					}
				},

				// ---- Main tab with two groups ----
				{
					"operation": "insert",
					"name": "LoanTab",
					"parentName": "Tabs",
					"propertyName": "tabs",
					"index": 0,
					"values": {
						"caption": {"bindTo": "Resources.Strings.LoanTabCaption"},
						"items": []
					}
				},

				// Group "Loan parameters"
				{
					"operation": "insert",
					"name": "LoanParametersGroup",
					"parentName": "LoanTab",
					"propertyName": "items",
					"values": {
						"itemType": Terrasoft.ViewItemType.CONTROL_GROUP,
						"caption": {"bindTo": "Resources.Strings.LoanParametersGroupCaption"},
						"controlConfig": {"collapsed": false},
						"items": []
					}
				},
				{
					"operation": "insert",
					"name": "LoanParametersGridLayout",
					"parentName": "LoanParametersGroup",
					"propertyName": "items",
					"values": {
						"itemType": Terrasoft.ViewItemType.GRID_LAYOUT,
						"items": []
					}
				},
				{
					"operation": "insert",
					"name": "UsrContact",
					"parentName": "LoanParametersGridLayout",
					"propertyName": "items",
					"values": {
						"bindTo": "UsrContact",
						"enabled": {"bindTo": "IsFinancialEditable"},
						"layout": {"column": 0, "row": 0, "colSpan": 12}
					}
				},
				{
					"operation": "insert",
					"name": "UsrPurpose",
					"parentName": "LoanParametersGridLayout",
					"propertyName": "items",
					"values": {
						"bindTo": "UsrPurpose",
						"enabled": {"bindTo": "IsFinancialEditable"},
						"contentType": Terrasoft.ContentType.ENUM,
						"layout": {"column": 12, "row": 0, "colSpan": 12}
					}
				},
				{
					"operation": "insert",
					"name": "UsrAmount",
					"parentName": "LoanParametersGridLayout",
					"propertyName": "items",
					"values": {
						"bindTo": "UsrAmount",
						"enabled": {"bindTo": "IsFinancialEditable"},
						"layout": {"column": 0, "row": 1, "colSpan": 12}
					}
				},
				{
					"operation": "insert",
					"name": "UsrTermMonths",
					"parentName": "LoanParametersGridLayout",
					"propertyName": "items",
					"values": {
						"bindTo": "UsrTermMonths",
						"enabled": {"bindTo": "IsFinancialEditable"},
						"layout": {"column": 12, "row": 1, "colSpan": 12}
					}
				},
				{
					"operation": "insert",
					"name": "UsrMonthlyIncome",
					"parentName": "LoanParametersGridLayout",
					"propertyName": "items",
					"values": {
						"bindTo": "UsrMonthlyIncome",
						"enabled": {"bindTo": "IsFinancialEditable"},
						"layout": {"column": 0, "row": 2, "colSpan": 12}
					}
				},
				{
					"operation": "insert",
					"name": "EstimatedPayment",
					"parentName": "LoanParametersGridLayout",
					"propertyName": "items",
					"values": {
						"bindTo": "EstimatedPayment",
						"caption": {"bindTo": "Resources.Strings.EstimatedPaymentCaption"},
						"tip": {"content": {"bindTo": "Resources.Strings.EstimatedPaymentTip"}},
						"enabled": false,
						"layout": {"column": 12, "row": 2, "colSpan": 12}
					}
				},

				// Group "Scoring result" — everything is filled by the server, read-only.
				{
					"operation": "insert",
					"name": "ScoringResultGroup",
					"parentName": "LoanTab",
					"propertyName": "items",
					"values": {
						"itemType": Terrasoft.ViewItemType.CONTROL_GROUP,
						"caption": {"bindTo": "Resources.Strings.ScoringResultGroupCaption"},
						"controlConfig": {"collapsed": false},
						"items": []
					}
				},
				{
					"operation": "insert",
					"name": "ScoringResultGridLayout",
					"parentName": "ScoringResultGroup",
					"propertyName": "items",
					"values": {
						"itemType": Terrasoft.ViewItemType.GRID_LAYOUT,
						"items": []
					}
				},
				{
					"operation": "insert",
					"name": "UsrScore",
					"parentName": "ScoringResultGridLayout",
					"propertyName": "items",
					"values": {"bindTo": "UsrScore", "enabled": false, "layout": {"column": 0, "row": 0, "colSpan": 12}}
				},
				{
					"operation": "insert",
					"name": "UsrDecision",
					"parentName": "ScoringResultGridLayout",
					"propertyName": "items",
					"values": {"bindTo": "UsrDecision", "enabled": false, "layout": {"column": 12, "row": 0, "colSpan": 12}}
				},
				{
					"operation": "insert",
					"name": "UsrRate",
					"parentName": "ScoringResultGridLayout",
					"propertyName": "items",
					"values": {"bindTo": "UsrRate", "enabled": false, "layout": {"column": 0, "row": 1, "colSpan": 12}}
				},
				{
					"operation": "insert",
					"name": "UsrMonthlyPayment",
					"parentName": "ScoringResultGridLayout",
					"propertyName": "items",
					"values": {"bindTo": "UsrMonthlyPayment", "enabled": false, "layout": {"column": 12, "row": 1, "colSpan": 12}}
				},
				{
					"operation": "insert",
					"name": "UsrMaxApprovedAmount",
					"parentName": "ScoringResultGridLayout",
					"propertyName": "items",
					"values": {"bindTo": "UsrMaxApprovedAmount", "enabled": false, "layout": {"column": 0, "row": 2, "colSpan": 12}}
				},
				{
					"operation": "insert",
					"name": "UsrScoredOn",
					"parentName": "ScoringResultGridLayout",
					"propertyName": "items",
					"values": {"bindTo": "UsrScoredOn", "enabled": false, "layout": {"column": 12, "row": 2, "colSpan": 12}}
				},
				{
					"operation": "insert",
					"name": "UsrScoreReasons",
					"parentName": "ScoringResultGridLayout",
					"propertyName": "items",
					"values": {"bindTo": "UsrScoreReasons", "enabled": false, "layout": {"column": 0, "row": 3, "colSpan": 24}}
				},
				{
					"operation": "insert",
					"name": "UsrDecisionComment",
					"parentName": "ScoringResultGridLayout",
					"propertyName": "items",
					"values": {
						"bindTo": "UsrDecisionComment",
						// Written by the approval business process, not by the user on this page.
						"enabled": false,
						"contentType": Terrasoft.ContentType.LONG_TEXT,
						"layout": {"column": 0, "row": 4, "colSpan": 24}
					}
				},

				// ---- "History" tab with the decision history detail ----
				{
					"operation": "insert",
					"name": "HistoryTab",
					"parentName": "Tabs",
					"propertyName": "tabs",
					"index": 1,
					"values": {
						"caption": {"bindTo": "Resources.Strings.HistoryTabCaption"},
						"items": []
					}
				},
				{
					"operation": "insert",
					"name": "UsrLoanDecisionHistoryDetail",
					"parentName": "HistoryTab",
					"propertyName": "items",
					"values": {
						"itemType": Terrasoft.ViewItemType.DETAIL,
						"markerValue": "added-detail"
					}
				}
			]/**SCHEMA_DIFF*/
		};
	});
