namespace RunLeague.Api;

public sealed class StravaOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string CallbackUrl { get; set; } = "https://localhost:7043/api/strava/callback";
    public string Scopes { get; set; } = "read,activity:read";
    public long AllowedAthleteId { get; set; }
    public bool IsConfigured => long.TryParse(ClientId, out var id) && id > 0
        && !string.IsNullOrWhiteSpace(ClientSecret) && AllowedAthleteId > 0;
    public static string[] ParseScopes(string value) =>
        value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
