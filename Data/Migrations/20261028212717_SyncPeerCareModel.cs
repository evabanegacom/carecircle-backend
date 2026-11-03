using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerCare.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncPeerCareModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // UserId1 was introduced by the initial model as a nullable shadow FK.
            // Remove its unused constraint and index, but retain the column so any
            // values written before this model correction are preserved.
            migrationBuilder.DropForeignKey(
                name: "FK_professional_verifications_users_UserId1",
                table: "professional_verifications");

            migrationBuilder.DropIndex(
                name: "IX_professional_verifications_UserId1",
                table: "professional_verifications");

            migrationBuilder.AddColumn<string>(
                name: "Bio",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileVisibility",
                table: "users",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DiagnosisStatus",
                table: "posts",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastVerifiedAt",
                table: "facilities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "facilities",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "communities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Guidelines = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ConditionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_communities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_communities_conditions_ConditionId",
                        column: x => x.ConditionId,
                        principalTable: "conditions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "moderation_appeals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    DecisionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moderation_appeals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moderation_appeals_moderation_actions_ActionId",
                        column: x => x.ActionId,
                        principalTable: "moderation_actions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_moderation_appeals_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provider_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: true),
                    ReviewText = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    VisitDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_provider_reviews_facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_provider_reviews_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_blocks",
                columns: table => new
                {
                    BlockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockedId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_blocks", x => new { x.BlockerId, x.BlockedId, x.Kind });
                    table.ForeignKey(
                        name: "FK_user_blocks_users_BlockedId",
                        column: x => x.BlockedId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_blocks_users_BlockerId",
                        column: x => x.BlockerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "community_memberships",
                columns: table => new
                {
                    CommunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_memberships", x => new { x.CommunityId, x.UserId });
                    table.ForeignKey(
                        name: "FK_community_memberships_communities_CommunityId",
                        column: x => x.CommunityId,
                        principalTable: "communities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_community_memberships_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_communities_ConditionId",
                table: "communities",
                column: "ConditionId");

            migrationBuilder.CreateIndex(
                name: "IX_communities_Slug",
                table: "communities",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_communities_Status_Name",
                table: "communities",
                columns: new[] { "Status", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_community_memberships_UserId_Status",
                table: "community_memberships",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_moderation_appeals_ActionId",
                table: "moderation_appeals",
                column: "ActionId");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_appeals_Status_CreatedAt",
                table: "moderation_appeals",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_moderation_appeals_UserId_ActionId",
                table: "moderation_appeals",
                columns: new[] { "UserId", "ActionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provider_reviews_FacilityId_Status_CreatedAt",
                table: "provider_reviews",
                columns: new[] { "FacilityId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_provider_reviews_UserId_FacilityId",
                table: "provider_reviews",
                columns: new[] { "UserId", "FacilityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_blocks_BlockedId",
                table: "user_blocks",
                column: "BlockedId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "community_memberships");

            migrationBuilder.DropTable(
                name: "moderation_appeals");

            migrationBuilder.DropTable(
                name: "provider_reviews");

            migrationBuilder.DropTable(
                name: "user_blocks");

            migrationBuilder.DropTable(
                name: "communities");

            migrationBuilder.DropColumn(
                name: "Bio",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ProfileVisibility",
                table: "users");

            migrationBuilder.DropColumn(
                name: "DiagnosisStatus",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "LastVerifiedAt",
                table: "facilities");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "facilities");

            migrationBuilder.CreateIndex(
                name: "IX_professional_verifications_UserId1",
                table: "professional_verifications",
                column: "UserId1");

        }
    }
}
