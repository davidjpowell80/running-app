using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RunLeague.Api.Migrations;

[DbContext(typeof(RunLeagueDb))]
[Migration("202609070001_InitialIdentity")]
public sealed class InitialIdentity : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => InitialModel.Build(modelBuilder);
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("Athletes", columns: table => new {
            Id = table.Column<Guid>(nullable: false),
            StravaAthleteId = table.Column<long>(nullable: false),
            FirstName = table.Column<string>(nullable: false),
            LastName = table.Column<string>(nullable: false),
            ProfileImageUrl = table.Column<string>(nullable: true),
            CreatedAt = table.Column<DateTime>(nullable: false),
            UpdatedAt = table.Column<DateTime>(nullable: false)
        }, constraints: table => table.PrimaryKey("PK_Athletes", x => x.Id));
        migrationBuilder.CreateTable("OAuthAttempts", columns: table => new {
            StateHash = table.Column<string>(nullable: false),
            ExpiresAt = table.Column<DateTime>(nullable: false)
        }, constraints: table => table.PrimaryKey("PK_OAuthAttempts", x => x.StateHash));
        migrationBuilder.CreateTable("Connections", columns: table => new {
            Id = table.Column<Guid>(nullable: false),
            AthleteId = table.Column<Guid>(nullable: false),
            AccessToken = table.Column<string>(nullable: false),
            RefreshToken = table.Column<string>(nullable: false),
            AccessTokenExpiresAt = table.Column<DateTime>(nullable: false),
            Scopes = table.Column<string>(nullable: false),
            ConnectedAt = table.Column<DateTime>(nullable: false),
            UpdatedAt = table.Column<DateTime>(nullable: false),
            RequiresReconnect = table.Column<bool>(nullable: false)
        }, constraints: table => {
            table.PrimaryKey("PK_Connections", x => x.Id);
            table.ForeignKey("FK_Connections_Athletes_AthleteId", x => x.AthleteId, "Athletes", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_Athletes_StravaAthleteId", "Athletes", "StravaAthleteId", unique: true);
        migrationBuilder.CreateIndex("IX_Connections_AthleteId", "Connections", "AthleteId", unique: true);
        migrationBuilder.CreateIndex("IX_OAuthAttempts_ExpiresAt", "OAuthAttempts", "ExpiresAt");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("Connections");
        migrationBuilder.DropTable("OAuthAttempts");
        migrationBuilder.DropTable("Athletes");
    }
}
