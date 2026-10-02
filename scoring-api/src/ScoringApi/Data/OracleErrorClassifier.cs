namespace ScoringApi.Data;

public enum OracleErrorKind
{
    /// <summary>RAISE_APPLICATION_ERROR -20001..-20005 of PKG_LOAN_SCORING.</summary>
    PackageValidation,

    /// <summary>The database cannot be reached or used right now.</summary>
    Connectivity,

    Other,
}

/// <summary>
/// Classifies ORA error numbers (OracleException.Number, a positive value).
/// Works on the number rather than OracleException, which has no public constructor,
/// so the rules are unit-testable.
/// </summary>
public static class OracleErrorClassifier
{
    private const int FirstPackageValidationError = 20001;
    private const int LastPackageValidationError = 20005;

    // TODO(verify): the full list for ODP.NET Managed — docs/OPEN_QUESTIONS.md, Q-005.
    private static readonly HashSet<int> ConnectivityErrors =
    [
        1017,   // invalid username/password; logon denied
        1033,   // ORACLE initialization or shutdown in progress
        1034,   // ORACLE not available
        1089,   // immediate shutdown in progress
        1109,   // database not open
        3113,   // end-of-file on communication channel
        3114,   // not connected to ORACLE
        3135,   // connection lost contact
        12154,  // could not resolve the connect identifier
        12170,  // connect timeout occurred
        12514,  // listener does not currently know of service requested
        12516,  // listener could not find available handler
        12520,  // listener could not find available handler for requested type of server
        12528,  // listener: all appropriate instances are blocking new connections
        12537,  // connection closed
        12541,  // no listener
        12543,  // destination host unreachable
        12545,  // connect failed because target host or object does not exist
        12560,  // protocol adapter error
        12571,  // packet writer failure
        28000,  // the account is locked
        // ODP.NET Managed wraps network failures: the outer OracleException carries 50201 and the
        // TNS error (e.g. ORA-12537) is only in the inner NetworkException. Observed with Oracle stopped.
        50201,  // Oracle Communication: failed to connect to server
        // TODO(verify): connection pool timeout ("Connection request timed out") — Q-005.
        50000,
    ];

    public static OracleErrorKind Classify(int oraNumber) => oraNumber switch
    {
        >= FirstPackageValidationError and <= LastPackageValidationError => OracleErrorKind.PackageValidation,
        _ when ConnectivityErrors.Contains(oraNumber) => OracleErrorKind.Connectivity,
        _ => OracleErrorKind.Other,
    };

    /// <summary>"ORA-20001: Loan amount must be ...\nORA-06512: at ..." -> "Loan amount must be ...".</summary>
    public static string FirstMessageLine(string message)
    {
        var firstLine = message.Split('\n', 2)[0].Trim();
        var prefixEnd = firstLine.IndexOf(": ", StringComparison.Ordinal);
        return firstLine.StartsWith("ORA-", StringComparison.Ordinal) && prefixEnd > 0
            ? firstLine[(prefixEnd + 2)..]
            : firstLine;
    }
}
