using System.ComponentModel.DataAnnotations;

namespace ScoringApi.Options;

/// <summary>Oracle connection settings, bound from ConnectionStrings:Oracle (env: ConnectionStrings__Oracle).</summary>
public sealed class OracleOptions
{
    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; set; } = string.Empty;
}
