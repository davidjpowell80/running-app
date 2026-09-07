using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace RunLeague.Api;

public sealed class StravaFailure(string code) : Exception(code)
{
    public string Code { get; } = code;
}
public sealed class StravaTokens
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("expires_at")] public long ExpiresAt { get; set; }
    [JsonPropertyName("athlete")] public StravaAthlete? Athlete { get; set; }
}
public sealed class StravaAthlete
{
    public long Id { get; set; }
    public string? Firstname { get; set; }
    public string? Lastname { get; set; }
    public string? Profile { get; set; }
}
// The local prototype runs as one process. Serialize exchanges and refreshes to
// avoid overwriting a rotated refresh token; reload from the database under the lock.
// Replace with a distributed lease before running multiple API instances.
public sealed class StravaGate { public SemaphoreSlim Lock { get; } = new(1, 1); }

public sealed class StravaService(
    RunLeagueDb db, IHttpClientFactory clients, IDataProtectionProvider protection,
    IOptions<StravaOptions> configuration, TimeProvider time, StravaGate gate)
{
    private readonly StravaOptions options = configuration.Value;
    private readonly IDataProtector protector = protection.CreateProtector("RunLeague.StravaTokens.v1");

    public async Task<Guid> Connect(string code, string scopes, CancellationToken ct)
    {
        await gate.Lock.WaitAsync(ct);
        try
        {
            var tokens = await TokenRequest(new() { ["grant_type"] = "authorization_code", ["code"] = code }, ct);
            var profile = tokens.Athlete;
            if (profile is null || profile.Id <= 0) throw new StravaFailure("invalid_response");
            if (profile.Id != options.AllowedAthleteId) throw new StravaFailure("athlete_not_allowed");
            var now = time.GetUtcNow().UtcDateTime;
            var athlete = await db.Athletes.SingleOrDefaultAsync(x => x.StravaAthleteId == profile.Id, ct);
            if (athlete is null)
            {
                athlete = new() { StravaAthleteId = profile.Id, CreatedAt = now };
                db.Athletes.Add(athlete);
            }
            athlete.FirstName = profile.Firstname ?? "";
            athlete.LastName = profile.Lastname ?? "";
            athlete.ProfileImageUrl = profile.Profile;
            athlete.UpdatedAt = now;
            var connection = await db.Connections.SingleOrDefaultAsync(x => x.AthleteId == athlete.Id, ct);
            if (connection is null)
            {
                connection = new() { AthleteId = athlete.Id, Athlete = athlete, ConnectedAt = now };
                db.Connections.Add(connection);
            }
            connection.Scopes = scopes;
            SaveTokens(connection, tokens);
            await db.SaveChangesAsync(ct);
            return athlete.Id;
        }
        finally { gate.Lock.Release(); }
    }

    public async Task<string> GetAccessToken(Guid athleteId, CancellationToken ct)
    {
        await gate.Lock.WaitAsync(ct);
        try
        {
            var connection = await db.Connections.SingleOrDefaultAsync(x => x.AthleteId == athleteId, ct)
                ?? throw new StravaFailure("reconnect_required");
            await db.Entry(connection).ReloadAsync(ct);
            if (connection.RequiresReconnect) throw new StravaFailure("reconnect_required");
            if (connection.AccessTokenExpiresAt > time.GetUtcNow().UtcDateTime.AddMinutes(5))
                return protector.Unprotect(connection.AccessToken);
            try
            {
                var tokens = await TokenRequest(new() {
                    ["grant_type"] = "refresh_token", ["refresh_token"] = protector.Unprotect(connection.RefreshToken)
                }, ct);
                SaveTokens(connection, tokens);
                await db.SaveChangesAsync(ct);
                return tokens.AccessToken;
            }
            catch (StravaFailure ex) when (ex.Code == "authorization_failed")
            {
                connection.RequiresReconnect = true;
                connection.UpdatedAt = time.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(ct);
                throw new StravaFailure("reconnect_required");
            }
        }
        finally { gate.Lock.Release(); }
    }

    // Dedicated entry point for subsequent Strava GETs. Never returns tokens to an endpoint.
    public async Task<JsonDocument> GetAthlete(Guid athleteId, CancellationToken ct)
    {
        var token = await GetAccessToken(athleteId, ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v3/athlete");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await clients.CreateClient("Strava").SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await db.Connections.Where(x => x.AthleteId == athleteId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RequiresReconnect, true), ct);
            throw new StravaFailure("reconnect_required");
        }
        CheckStatus(response);
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); }
        catch (JsonException) { throw new StravaFailure("invalid_response"); }
    }

    private async Task<StravaTokens> TokenRequest(Dictionary<string, string> fields, CancellationToken ct)
    {
        fields["client_id"] = options.ClientId;
        fields["client_secret"] = options.ClientSecret;
        using var response = await clients.CreateClient("Strava")
            .PostAsync("oauth/token", new FormUrlEncodedContent(fields), ct);
        CheckStatus(response);
        StravaTokens? tokens;
        try { tokens = await response.Content.ReadFromJsonAsync<StravaTokens>(ct); }
        catch (JsonException) { throw new StravaFailure("invalid_response"); }
        if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken) ||
            string.IsNullOrWhiteSpace(tokens.RefreshToken) ||
            tokens.ExpiresAt <= time.GetUtcNow().ToUnixTimeSeconds() || tokens.ExpiresAt > 253402300799)
            throw new StravaFailure("invalid_response");
        return tokens;
    }
    private void SaveTokens(StravaConnection connection, StravaTokens tokens)
    {
        connection.AccessToken = protector.Protect(tokens.AccessToken);
        connection.RefreshToken = protector.Protect(tokens.RefreshToken);
        connection.AccessTokenExpiresAt = DateTimeOffset.FromUnixTimeSeconds(tokens.ExpiresAt).UtcDateTime;
        connection.UpdatedAt = time.GetUtcNow().UtcDateTime;
        connection.RequiresReconnect = false;
    }
    private static void CheckStatus(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new StravaFailure("rate_limited");
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new StravaFailure("authorization_failed");
        if (!response.IsSuccessStatusCode) throw new StravaFailure("strava_unavailable");
    }
}
