using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunLeague.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<StravaOptions>().BindConfiguration("Strava")
    .Validate(o => Uri.TryCreate(o.CallbackUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.AbsolutePath == "/api/strava/callback", "Strava callback must use HTTPS and /api/strava/callback.")
    .Validate(o => StravaOptions.ParseScopes(o.Scopes).All(s => s is "read" or "activity:read" or "activity:read_all")
        && StravaOptions.ParseScopes(o.Scopes).Any(s => s is "activity:read" or "activity:read_all"),
        "Request only read scopes, including activity:read or activity:read_all.")
    .ValidateOnStart();
builder.Services.AddDbContext<RunLeagueDb>(o => o.UseSqlite(builder.Configuration.GetConnectionString("RunLeague")));
builder.Services.AddDataProtection().SetApplicationName("RunLeague");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<StravaGate>();
builder.Services.AddScoped<OAuthState>();
builder.Services.AddScoped<StravaService>();
builder.Services.AddHttpClient("Strava", client => {
    client.BaseAddress = new Uri("https://www.strava.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
}).RemoveAllLoggers();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => {
    o.Cookie.Name = "__Host-runleague-session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.ExpireTimeSpan = TimeSpan.FromDays(7);
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o => {
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = "__Host-runleague-csrf";
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
builder.Services.AddProblemDetails();
var app = builder.Build();
// Never return exception details, upstream payloads or OAuth query strings.
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.Use(async (context, next) => {
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/session", (HttpContext context, IAntiforgery csrf, IOptions<StravaOptions> options) =>
    Results.Ok(new { configured = options.Value.IsConfigured, csrfToken = csrf.GetAndStoreTokens(context).RequestToken }));

app.MapGet("/api/strava/connect", async (HttpContext context, OAuthState state, IOptions<StravaOptions> configuration, CancellationToken ct) => {
    var options = configuration.Value;
    if (!options.IsConfigured) return Results.Redirect("/?error=not_configured");
    var nonce = await state.Create(ct);
    context.Response.Cookies.Append(OAuthState.CookieName, nonce, new CookieOptions {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = TimeSpan.FromMinutes(10)
    });
    return Results.Redirect(QueryHelpers.AddQueryString("https://www.strava.com/oauth/authorize", new Dictionary<string,string?> {
        ["client_id"] = options.ClientId, ["redirect_uri"] = options.CallbackUrl,
        ["response_type"] = "code", ["approval_prompt"] = "auto", ["scope"] = options.Scopes, ["state"] = nonce
    }));
});

app.MapGet("/api/strava/callback", async (HttpContext context, OAuthState state, StravaService service,
    IOptions<StravaOptions> configuration, CancellationToken ct) => {
    var query = context.Request.Query;
    var cookie = context.Request.Cookies[OAuthState.CookieName];
    context.Response.Cookies.Delete(OAuthState.CookieName, new CookieOptions { Secure = true, Path = "/" });
    if (!await state.Consume(query["state"].ToString(), cookie, ct)) return Results.Redirect("/?error=invalid_state");
    if (!configuration.Value.IsConfigured) return Results.Redirect("/?error=not_configured");
    if (query.ContainsKey("error")) return Results.Redirect("/?error=access_denied");
    if (string.IsNullOrWhiteSpace(query["code"])) return Results.Redirect("/?error=missing_code");
    var granted = StravaOptions.ParseScopes(query["scope"].ToString());
    if (!StravaOptions.ParseScopes(configuration.Value.Scopes).All(granted.Contains))
        return Results.Redirect("/?error=missing_scope");
    try {
        var id = await service.Connect(query["code"].ToString(), string.Join(",", granted), ct);
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Redirect("/?connected=1");
    }
    catch (StravaFailure ex) { return Results.Redirect("/?error=" + ex.Code); }
    catch (HttpRequestException) { return Results.Redirect("/?error=strava_unavailable"); }
    catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Results.Redirect("/?error=strava_unavailable"); }
});

app.MapGet("/api/athlete", async (HttpContext context, RunLeagueDb db, CancellationToken ct) => {
    var id = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var result = await db.Connections.AsNoTracking().Where(x => x.AthleteId == id)
        .Select(x => new { x.Athlete.StravaAthleteId, x.Athlete.FirstName, x.Athlete.LastName,
            x.Athlete.ProfileImageUrl, x.ConnectedAt, x.AccessTokenExpiresAt, x.Scopes, x.RequiresReconnect })
        .SingleOrDefaultAsync(ct);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
}).RequireAuthorization();

app.MapPost("/api/strava/check", async (HttpContext context, IAntiforgery csrf, StravaService service, CancellationToken ct) => {
    try { await csrf.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(new { error = "invalid_csrf" }); }
    try {
        using var profile = await service.GetAthlete(Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!), ct);
        return Results.Ok(new { status = "connected" });
    }
    catch (StravaFailure ex) { return Results.Json(new { error = ex.Code }, statusCode: ex.Code == "reconnect_required" ? 409 : 502); }
    catch (HttpRequestException) { return Results.Json(new { error = "strava_unavailable" }, statusCode: 502); }
    catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Results.Json(new { error = "strava_unavailable" }, statusCode: 504); }
}).RequireAuthorization();

app.MapPost("/api/session/logout", async (HttpContext context, IAntiforgery csrf) => {
    try { await csrf.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    await context.SignOutAsync();
    return Results.NoContent();
}).RequireAuthorization();
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

using (var scope = app.Services.CreateScope()) {
    Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
    await scope.ServiceProvider.GetRequiredService<RunLeagueDb>().Database.MigrateAsync();
}
app.Run();
public partial class Program { }
