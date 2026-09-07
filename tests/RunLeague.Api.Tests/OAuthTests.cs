using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RunLeague.Api;
using Xunit;

namespace RunLeague.Api.Tests;

public sealed class FakeStrava : HttpMessageHandler
{
    public int Exchanges;
    public int Refreshes;
    public bool RejectRefresh;
    public string LastRefreshToken = "";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method == HttpMethod.Get) return Json(new { id = 42 });
        var values = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
        if (values["grant_type"] == "refresh_token")
        {
            Refreshes++;
            LastRefreshToken = values["refresh_token"].ToString();
            if (RejectRefresh) return new(HttpStatusCode.BadRequest);
            return Json(new { access_token = "new-access", refresh_token = "rotated-refresh", expires_at = DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeSeconds() });
        }
        Exchanges++;
        return Json(new { access_token = "initial-access", refresh_token = "initial-refresh",
            expires_at = DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeSeconds(),
            athlete = new { id = 42, firstname = "Test", lastname = "Runner" } });
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json") };
}
public sealed class AppFactory : WebApplicationFactory<Program>
{
    public readonly FakeStrava Strava = new();
    private readonly string dbFile = Path.Combine(Path.GetTempPath(), "runleague-test-" + Guid.NewGuid() + ".db");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string,string?> {
            ["ConnectionStrings:RunLeague"] = "Data Source=" + dbFile,
            ["Strava:ClientId"] = "123",
            ["Strava:ClientSecret"] = "test-only-secret",
            ["Strava:AllowedAthleteId"] = "42",
            ["Strava:Scopes"] = "read,activity:read"
        }));
        builder.ConfigureServices(services => {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddHttpClient("Strava").ConfigurePrimaryHttpMessageHandler(() => Strava);
        });
    }
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    });
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(dbFile)) File.Delete(dbFile);
    }
}
public sealed class OAuthTests
{
    private static async Task<string> Start(HttpClient browser)
    {
        var response = await browser.GetAsync("/api/strava/connect");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return QueryHelpers.ParseQuery(response.Headers.Location!.Query)["state"].ToString();
    }
    private static Task<HttpResponseMessage> Callback(HttpClient browser, string state, string extra = "code=test&scope=read,activity:read") =>
        browser.GetAsync("/api/strava/callback?state=" + state + "&" + extra);
    private static async Task Connect(HttpClient browser)
    {
        var response = await Callback(browser, await Start(browser));
        Assert.Equal("/?connected=1", response.Headers.Location!.OriginalString);
    }

    [Fact] public async Task Login_persists_encrypted_tokens_and_returns_only_safe_profile()
    {
        using var app = new AppFactory(); using var browser = app.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/athlete")).StatusCode);
        await Connect(browser);
        var json = await browser.GetStringAsync("/api/athlete");
        Assert.Contains("Test", json); Assert.DoesNotContain("initial-access", json); Assert.DoesNotContain("initial-refresh", json);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RunLeagueDb>();
        var connection = await db.Connections.SingleAsync();
        Assert.NotEqual("initial-access", connection.AccessToken);
        Assert.NotEqual("initial-refresh", connection.RefreshToken);
        Assert.Equal("read,activity:read", connection.Scopes);
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        await Connect(browser);
        Assert.Equal(1, await db.Athletes.CountAsync());
        Assert.Equal(1, await db.Connections.CountAsync());
    }

    [Fact] public async Task State_is_browser_bound_and_single_use()
    {
        using var app = new AppFactory(); using var browser = app.Browser(); using var other = app.Browser();
        var state = await Start(browser);
        Assert.Equal("/?error=invalid_state", (await Callback(other,state)).Headers.Location!.OriginalString);
        Assert.Equal("/?connected=1", (await Callback(browser,state)).Headers.Location!.OriginalString);
        Assert.Equal("/?error=invalid_state", (await Callback(browser,state)).Headers.Location!.OriginalString);
        Assert.Equal(1, app.Strava.Exchanges);
    }

    [Theory]
    [InlineData("error=access_denied", "access_denied")]
    [InlineData("code=test&scope=read", "missing_scope")]
    [InlineData("scope=read,activity:read", "missing_code")]
    public async Task Invalid_callbacks_never_exchange_tokens(string query,string error)
    {
        using var app = new AppFactory(); using var browser = app.Browser();
        Assert.Equal("/?error="+error, (await Callback(browser,await Start(browser),query)).Headers.Location!.OriginalString);
        Assert.Equal(0, app.Strava.Exchanges);
    }

    [Fact] public async Task Expired_attempt_is_rejected()
    {
        using var app = new AppFactory(); using var browser = app.Browser();
        var state = await Start(browser);
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RunLeagueDb>().OAuthAttempts
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ExpiresAt,DateTime.UtcNow.AddMinutes(-1)));
        Assert.Equal("/?error=invalid_state",(await Callback(browser,state)).Headers.Location!.OriginalString);
        Assert.Equal(0,app.Strava.Exchanges);
    }

    [Fact] public async Task Fresh_token_is_reused_and_expired_token_is_rotated_once()
    {
        using var app = new AppFactory(); using var browser = app.Browser(); await Connect(browser);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RunLeagueDb>();
        var id = (await db.Athletes.SingleAsync()).Id;
        var service = scope.ServiceProvider.GetRequiredService<StravaService>();
        Assert.Equal("initial-access",await service.GetAccessToken(id,default));
        Assert.Equal(0,app.Strava.Refreshes);
        await db.Connections.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AccessTokenExpiresAt,DateTime.UtcNow.AddMinutes(-1)));
        // Separate request scopes exercise the singleton refresh lock.
        await Task.WhenAll(Enumerable.Range(0,3).Select(async _ => {
            using var requestScope = app.Services.CreateScope();
            Assert.Equal("new-access",await requestScope.ServiceProvider.GetRequiredService<StravaService>().GetAccessToken(id,default));
        }));
        Assert.Equal(1,app.Strava.Refreshes); Assert.Equal("initial-refresh",app.Strava.LastRefreshToken);
        db.ChangeTracker.Clear();
        var connection = await db.Connections.SingleAsync();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("RunLeague.StravaTokens.v1");
        Assert.Equal("rotated-refresh",protector.Unprotect(connection.RefreshToken));
    }

    [Fact] public async Task Revoked_refresh_marks_reconnect_and_preserves_tokens()
    {
        using var app = new AppFactory(); using var browser = app.Browser(); await Connect(browser);
        app.Strava.RejectRefresh = true;
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RunLeagueDb>();
        await db.Connections.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AccessTokenExpiresAt,DateTime.UtcNow.AddMinutes(-1)));
        var id = (await db.Athletes.SingleAsync()).Id;
        var failure = await Assert.ThrowsAsync<StravaFailure>(()=>scope.ServiceProvider.GetRequiredService<StravaService>().GetAccessToken(id,default));
        Assert.Equal("reconnect_required",failure.Code);
        Assert.True((await db.Connections.SingleAsync()).RequiresReconnect);
    }

    [Fact] public async Task Authenticated_mutations_require_antiforgery_token()
    {
        using var app = new AppFactory(); using var browser = app.Browser(); await Connect(browser);
        Assert.Equal(HttpStatusCode.BadRequest,(await browser.PostAsync("/api/strava/check",null)).StatusCode);
        using var session = JsonDocument.Parse(await browser.GetStringAsync("/api/session"));
        browser.DefaultRequestHeaders.Add("X-CSRF-TOKEN",session.RootElement.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.OK,(await browser.PostAsync("/api/strava/check",null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await browser.PostAsync("/api/session/logout",null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await browser.GetAsync("/api/athlete")).StatusCode);
    }
}
