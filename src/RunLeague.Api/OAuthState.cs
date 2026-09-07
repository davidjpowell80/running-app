using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace RunLeague.Api;

// A browser-bound, expiring, single-use attempt. Atomic deletion prevents callback replay.
public sealed class OAuthState(RunLeagueDb db, TimeProvider time)
{
    public const string CookieName = "__Host-runleague-oauth";
    public async Task<string> Create(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await db.OAuthAttempts.Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(ct);
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        db.OAuthAttempts.Add(new() { StateHash = Hash(state), ExpiresAt = now.AddMinutes(10) });
        await db.SaveChangesAsync(ct);
        return state;
    }
    public async Task<bool> Consume(string? state, string? cookie, CancellationToken ct)
    {
        if (state is null || cookie is null || state.Length != 64 || cookie.Length != 64 ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(cookie)))
            return false;
        var hash = Hash(state);
        var now = time.GetUtcNow().UtcDateTime;
        return await db.OAuthAttempts.Where(x => x.StateHash == hash && x.ExpiresAt > now)
            .ExecuteDeleteAsync(ct) == 1;
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
