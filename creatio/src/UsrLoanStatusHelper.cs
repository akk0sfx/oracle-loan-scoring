namespace Terrasoft.Configuration.UsrLoanScoring
{
    using System;
    using System.Collections.Generic;
    using Terrasoft.Core;
    using Terrasoft.Core.Entities;

    /// <summary>
    /// Resolves UsrLoanStatus / UsrLoanPurpose records by UsrCode and back.
    ///
    /// Why codes and not hard-coded Ids: lookup record Ids differ between environments unless
    /// the data is shipped with fixed Ids, while UsrCode is part of the contract (2.1, 2.2).
    ///
    /// Why the cache lives in an instance: one helper is created per web-service call or per
    /// listener event, so every lookup is read once per request. A static cache would be shared
    /// between users and would keep stale values after someone edits the lookup.
    /// </summary>
    public class UsrLoanStatusHelper
    {
        private readonly UserConnection _userConnection;
        private Dictionary<string, Guid> _statusIdsByCode;
        private Dictionary<Guid, string> _statusCodesById;
        private Dictionary<Guid, string> _purposeCodesById;

        public UsrLoanStatusHelper(UserConnection userConnection) {
            if (userConnection == null) {
                throw new ArgumentNullException("userConnection");
            }
            _userConnection = userConnection;
        }

        /// <summary>Id of the UsrLoanStatus record with the given UsrCode.</summary>
        public Guid GetStatusId(string code) {
            EnsureStatusesLoaded();
            Guid id;
            if (!_statusIdsByCode.TryGetValue(code, out id)) {
                throw new InvalidOperationException(string.Format(
                    "Loan status with code '{0}' is missing in lookup {1}.", code, UsrSchemaNames.LoanStatus));
            }
            return id;
        }

        /// <summary>UsrCode of the status, or null for an empty or unknown Id.</summary>
        public string GetStatusCode(Guid id) {
            if (id == Guid.Empty) {
                return null;
            }
            EnsureStatusesLoaded();
            string code;
            return _statusCodesById.TryGetValue(id, out code) ? code : null;
        }

        /// <summary>UsrCode of the loan purpose, or null for an empty or unknown Id.</summary>
        public string GetPurposeCode(Guid id) {
            if (id == Guid.Empty) {
                return null;
            }
            if (_purposeCodesById == null) {
                _purposeCodesById = new Dictionary<Guid, string>();
                foreach (var pair in LoadCodes(UsrSchemaNames.LoanPurpose)) {
                    _purposeCodesById[pair.Key] = pair.Value;
                }
            }
            string code;
            return _purposeCodesById.TryGetValue(id, out code) ? code : null;
        }

        private void EnsureStatusesLoaded() {
            if (_statusIdsByCode != null) {
                return;
            }
            _statusIdsByCode = new Dictionary<string, Guid>(StringComparer.Ordinal);
            _statusCodesById = new Dictionary<Guid, string>();
            foreach (var pair in LoadCodes(UsrSchemaNames.LoanStatus)) {
                _statusIdsByCode[pair.Value] = pair.Key;
                _statusCodesById[pair.Key] = pair.Value;
            }
        }

        /// <summary>
        /// Reads (Id, UsrCode) of a small lookup in one query. The lookups have five rows, so
        /// loading them whole is cheaper than one query per code.
        /// </summary>
        private IEnumerable<KeyValuePair<Guid, string>> LoadCodes(string schemaName) {
            var esq = new EntitySchemaQuery(_userConnection.EntitySchemaManager, schemaName);
            // Lookups are reference data every user may read; the query must not depend on
            // record-level rights of the current user, otherwise a status could "disappear".
            esq.UseAdminRights = false;
            var idColumn = esq.AddColumn(UsrColumnNames.Id);
            var codeColumn = esq.AddColumn(UsrColumnNames.UsrCode);
            var result = new List<KeyValuePair<Guid, string>>();
            foreach (var entity in esq.GetEntityCollection(_userConnection)) {
                result.Add(new KeyValuePair<Guid, string>(
                    entity.GetTypedColumnValue<Guid>(idColumn.Name),
                    entity.GetTypedColumnValue<string>(codeColumn.Name)));
            }
            return result;
        }
    }
}
