using System.ComponentModel.DataAnnotations;

namespace ScoringApi.Options;

/// <summary>Section "Scoring" of the configuration.</summary>
public sealed class ScoringOptions
{
    public const string SectionName = "Scoring";

    /// <summary>Expected value of the X-Api-Key header. Supplied via environment (Scoring__ApiKey), never appsettings.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;
}
