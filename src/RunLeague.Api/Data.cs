using Microsoft.EntityFrameworkCore;

namespace RunLeague.Api;

public sealed class Athlete
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long StravaAthleteId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? ProfileImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
public sealed class StravaConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AthleteId { get; set; }
    public Athlete Athlete { get; set; } = null!;
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime AccessTokenExpiresAt { get; set; }
    public string Scopes { get; set; } = "";
    public DateTime ConnectedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool RequiresReconnect { get; set; }
}
public sealed class OAuthAttempt
{
    public string StateHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}
public sealed class RunLeagueDb(DbContextOptions<RunLeagueDb> options) : DbContext(options)
{
    public DbSet<Athlete> Athletes => Set<Athlete>();
    public DbSet<StravaConnection> Connections => Set<StravaConnection>();
    public DbSet<OAuthAttempt> OAuthAttempts => Set<OAuthAttempt>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Athlete>().HasIndex(x => x.StravaAthleteId).IsUnique();
        model.Entity<StravaConnection>().HasIndex(x => x.AthleteId).IsUnique();
        model.Entity<StravaConnection>().HasOne(x => x.Athlete).WithOne()
            .HasForeignKey<StravaConnection>(x => x.AthleteId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<OAuthAttempt>().HasKey(x => x.StateHash);
        model.Entity<OAuthAttempt>().HasIndex(x => x.ExpiresAt);
    }
}
