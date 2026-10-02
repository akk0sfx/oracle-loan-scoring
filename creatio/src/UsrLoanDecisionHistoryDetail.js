/**
 * Decision history detail (list of UsrLoanDecisionHistory) — newest status change first.
 *
 * TODO(verify): the section wizard names detail schemas itself (e.g. "UsrSchema...Detail");
 * this file must go into that schema and the define() name must match it — Q-014.
 * TODO(verify): getGridDataColumns with orderPosition/orderDirection sets the default sorting of
 * a grid detail in this version — Q-018.
 */
define("UsrLoanDecisionHistoryDetail", [], function() {
	return {
		entitySchemaName: "UsrLoanDecisionHistory",
		methods: {
			/**
			 * Columns requested by the detail grid. Adding sort settings to CreatedOn makes the query
			 * ORDER BY CreatedOn DESC, so the latest transition is on top.
			 */
			getGridDataColumns: function() {
				var columns = this.callParent(arguments);
				columns.CreatedOn = columns.CreatedOn || {path: "CreatedOn"};
				columns.CreatedOn.orderPosition = 0;
				columns.CreatedOn.orderDirection = Terrasoft.OrderDirection.DESC;
				return columns;
			}
		},
		diff: /**SCHEMA_DIFF*/[]/**SCHEMA_DIFF*/
	};
});
