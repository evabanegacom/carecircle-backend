using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerCare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunityCheckIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "community_check_ins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekOf = table.Column<DateOnly>(type: "date", nullable: false),
                    Mood = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SupportNeed = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    IsAnonymous = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_check_ins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_check_ins_communities_CommunityId",
                        column: x => x.CommunityId,
                        principalTable: "communities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_community_check_ins_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_community_check_ins_CommunityId_UserId_WeekOf",
                table: "community_check_ins",
                columns: new[] { "CommunityId", "UserId", "WeekOf" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_community_check_ins_CommunityId_WeekOf_CreatedAt",
                table: "community_check_ins",
                columns: new[] { "CommunityId", "WeekOf", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_community_check_ins_UserId",
                table: "community_check_ins",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "community_check_ins");
        }
    }
}
