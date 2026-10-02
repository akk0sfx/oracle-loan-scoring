namespace Terrasoft.Configuration.UsrLoanScoring
{
    using System;
    using System.Collections.Generic;
    using Terrasoft.Core;
    using Terrasoft.Core.Configuration;
    using Terrasoft.Core.Entities;
    using Terrasoft.Core.Entities.Events;

    /// <summary>
    /// Thrown to cancel a save with a message the user can understand. Creatio shows the
    /// exception message in the UI, so it must be a sentence, not a technical code.
    /// </summary>
    public class UsrLoanApplicationValidationException : Exception
    {
        public UsrLoanApplicationValidationException(string message) : base(message) { }
    }

    /// <summary>
    /// Server-side rules of UsrLoanApplication (contract 2.1, 2.3, 2.4).
    ///
    /// Why a listener and not only page validation: the page can be bypassed (DataService,
    /// business processes, imports, the scoring service itself). Everything that saves an
    /// application through the ORM passes through these handlers.
    ///
    /// Event order (Creatio docs): insert = OnSaving, OnInserting, OnInserted, OnSaved;
    /// update = OnSaving, OnUpdating, OnUpdated, OnSaved. Rules are split by event so that
    /// "is this an insert?" never has to be guessed (ADR-021):
    ///   OnSaving    — range validation (insert and update);
    ///   OnInserting — default status NEW, UsrNumber generation;
    ///   OnUpdating  — allowed status transitions, financial fields lock, UsrNumber immutability;
    ///   OnSaved     — UsrLoanDecisionHistory record on status change.
    ///
    /// The listener holds no fields: one instance may serve many saves, so all state is local.
    /// </summary>
    [EntityEventListener(SchemaName = UsrSchemaNames.LoanApplication)]
    public class UsrLoanApplicationEventListener : BaseEntityEventListener
    {
        private static readonly string[] FinancialColumns = {
            UsrColumnNames.ContactId, UsrColumnNames.PurposeId, UsrColumnNames.Amount,
            UsrColumnNames.TermMonths, UsrColumnNames.MonthlyIncome
        };

        public override void OnSaving(object sender, EntityBeforeEventArgs e) {
            base.OnSaving(sender, e);
            var entity = (Entity)sender;

            var errors = UsrLoanApplicationRules.ValidateRanges(
                entity.GetTypedColumnValue<decimal>(UsrColumnNames.Amount),
                entity.GetTypedColumnValue<int>(UsrColumnNames.TermMonths),
                entity.GetTypedColumnValue<decimal>(UsrColumnNames.MonthlyIncome));
            if (errors.Count > 0) {
                throw new UsrLoanApplicationValidationException(string.Join(" ", errors));
            }
        }

        public override void OnInserting(object sender, EntityBeforeEventArgs e) {
            base.OnInserting(sender, e);
            var entity = (Entity)sender;
            var statuses = new UsrLoanStatusHelper(entity.UserConnection);

            // A new application always starts in NEW; any other initial status would skip scoring.
            var newStatusId = statuses.GetStatusId(UsrLoanStatusCodes.New);
            var statusId = entity.GetTypedColumnValue<Guid>(UsrColumnNames.StatusId);
            if (statusId == Guid.Empty) {
                entity.SetColumnValue(UsrColumnNames.StatusId, newStatusId);
            } else if (statusId != newStatusId) {
                throw new UsrLoanApplicationValidationException(
                    "A new loan application must start in status " + UsrLoanStatusCodes.New + ".");
            }

            // The number is always generated: a value typed by the user is ignored (contract 2.3).
            entity.SetColumnValue(UsrColumnNames.Number, NextNumber(entity.UserConnection));
        }

        public override void OnUpdating(object sender, EntityBeforeEventArgs e) {
            base.OnUpdating(sender, e);
            var entity = (Entity)sender;
            var statuses = new UsrLoanStatusHelper(entity.UserConnection);

            var oldStatusCode = statuses.GetStatusCode(entity.GetTypedOldColumnValue<Guid>(UsrColumnNames.StatusId));
            var newStatusCode = statuses.GetStatusCode(entity.GetTypedColumnValue<Guid>(UsrColumnNames.StatusId));

            // 1. Status transition must be in the table of contract 2.1.
            if (oldStatusCode != newStatusCode && !UsrLoanApplicationRules.IsTransitionAllowed(oldStatusCode, newStatusCode)) {
                throw new UsrLoanApplicationValidationException(string.Format(
                    "Status cannot change from {0} to {1}.", oldStatusCode ?? "empty", newStatusCode ?? "empty"));
            }

            // 2. Financial fields are frozen once the application left NEW: the score and the
            //    decision were computed from them.
            if (oldStatusCode != UsrLoanStatusCodes.New) {
                var changed = ChangedColumns(entity, FinancialColumns);
                if (changed.Count > 0) {
                    throw new UsrLoanApplicationValidationException(string.Format(
                        "Financial fields cannot be changed after the application left status {0}: {1}.",
                        UsrLoanStatusCodes.New, string.Join(", ", changed)));
                }
            }

            // 3. UsrNumber is assigned once on insert.
            if (ChangedColumns(entity, new[] { UsrColumnNames.Number }).Count > 0) {
                throw new UsrLoanApplicationValidationException("The application number cannot be changed.");
            }
        }

        public override void OnSaved(object sender, EntityAfterEventArgs e) {
            base.OnSaved(sender, e);
            var entity = (Entity)sender;

            // On insert the old value is empty, so the first record is "empty -> NEW" (contract 2.4).
            // TODO(verify): old column values are still available in OnSaved — docs/OPEN_QUESTIONS.md, Q-012.
            var oldStatusId = entity.GetTypedOldColumnValue<Guid>(UsrColumnNames.StatusId);
            var newStatusId = entity.GetTypedColumnValue<Guid>(UsrColumnNames.StatusId);
            if (oldStatusId == newStatusId) {
                return;
            }

            var userConnection = entity.UserConnection;
            var history = userConnection.EntitySchemaManager
                .GetInstanceByName(UsrSchemaNames.DecisionHistory)
                .CreateEntity(userConnection);
            // History is written only by this listener (contract 2.4), on behalf of the system:
            // a user who may change the application must not fail because they lack insert
            // rights on the history object.
            history.UseAdminRights = false;
            history.SetDefColumnValues();
            history.SetColumnValue(UsrColumnNames.HistoryApplicationId, entity.PrimaryColumnValue);
            history.SetColumnValue(UsrColumnNames.HistoryStatusFromId, oldStatusId == Guid.Empty ? (object)null : oldStatusId);
            history.SetColumnValue(UsrColumnNames.HistoryStatusToId, newStatusId);
            history.SetColumnValue(UsrColumnNames.HistoryScore, entity.GetTypedColumnValue<int>(UsrColumnNames.Score));
            var comment = entity.GetTypedColumnValue<string>(UsrColumnNames.DecisionComment);
            if (!string.IsNullOrWhiteSpace(comment)) {
                history.SetColumnValue(UsrColumnNames.HistoryComment, comment);
            }
            history.Save();
        }

        /// <summary>
        /// Columns whose new value differs from the old one. Compares typed values instead of
        /// relying on "was SetColumnValue called", because setting the same value is not a change.
        /// </summary>
        private static IList<string> ChangedColumns(Entity entity, IEnumerable<string> columnNames) {
            var changed = new List<string>();
            foreach (var name in columnNames) {
                if (!Equals(entity.GetColumnOldValue(name), entity.GetColumnValue(name))) {
                    changed.Add(name);
                }
            }
            return changed;
        }

        /// <summary>
        /// LA-000001, LA-000002, ... from the system setting UsrLoanApplicationLastNumber.
        ///
        /// Known race: two parallel inserts can read the same value and get the same number
        /// (read-increment-write is not atomic). Acceptable for a demo; production options are
        /// listed in ADR-022 (DB sequence, locked counter row, unique index + retry).
        /// </summary>
        private static string NextNumber(UserConnection userConnection) {
            var next = SysSettings.GetValue(userConnection, UsrSysSettingCodes.LastNumber, 0) + 1;
            // TODO(verify): SetDefValue stores the value for all users (not per user) — Q-009.
            SysSettings.SetDefValue(userConnection, UsrSysSettingCodes.LastNumber, next);
            return UsrLoanApplicationRules.NumberPrefix + next.ToString(UsrLoanApplicationRules.NumberFormat);
        }
    }
}
