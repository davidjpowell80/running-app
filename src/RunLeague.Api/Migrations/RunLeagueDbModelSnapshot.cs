using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace RunLeague.Api.Migrations;

[DbContext(typeof(RunLeagueDb))]
public sealed class RunLeagueDbModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => InitialModel.Build(modelBuilder);
}
// Frozen representation of migration 1. Do not edit when changing the domain model.
internal static class InitialModel
{
    internal static void Build(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.6");
        modelBuilder.Entity("RunLeague.Api.Athlete", b => {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<long>("StravaAthleteId").HasColumnType("INTEGER");
            b.Property<string>("FirstName").IsRequired().HasColumnType("TEXT");
            b.Property<string>("LastName").IsRequired().HasColumnType("TEXT");
            b.Property<string>("ProfileImageUrl").HasColumnType("TEXT");
            b.Property<DateTime>("CreatedAt").HasColumnType("TEXT");
            b.Property<DateTime>("UpdatedAt").HasColumnType("TEXT");
            b.HasKey("Id"); b.HasIndex("StravaAthleteId").IsUnique(); b.ToTable("Athletes");
        });
        modelBuilder.Entity("RunLeague.Api.StravaConnection", b => {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            b.Property<Guid>("AthleteId").HasColumnType("TEXT");
            b.Property<string>("AccessToken").IsRequired().HasColumnType("TEXT");
            b.Property<string>("RefreshToken").IsRequired().HasColumnType("TEXT");
            b.Property<string>("Scopes").IsRequired().HasColumnType("TEXT");
            b.Property<DateTime>("AccessTokenExpiresAt").HasColumnType("TEXT");
            b.Property<DateTime>("ConnectedAt").HasColumnType("TEXT");
            b.Property<DateTime>("UpdatedAt").HasColumnType("TEXT");
            b.Property<bool>("RequiresReconnect").HasColumnType("INTEGER");
            b.HasKey("Id"); b.HasIndex("AthleteId").IsUnique(); b.ToTable("Connections");
        });
        modelBuilder.Entity("RunLeague.Api.OAuthAttempt", b => {
            b.Property<string>("StateHash").HasColumnType("TEXT");
            b.Property<DateTime>("ExpiresAt").HasColumnType("TEXT");
            b.HasKey("StateHash"); b.HasIndex("ExpiresAt"); b.ToTable("OAuthAttempts");
        });
        modelBuilder.Entity("RunLeague.Api.StravaConnection", b => {
            b.HasOne("RunLeague.Api.Athlete", "Athlete").WithOne()
                .HasForeignKey("RunLeague.Api.StravaConnection", "AthleteId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.Navigation("Athlete");
        });
    }
}
