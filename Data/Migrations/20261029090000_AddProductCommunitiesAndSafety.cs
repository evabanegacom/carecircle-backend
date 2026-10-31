using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerCare.Data.Migrations;

/// <summary>
/// Reconciles the schema already introduced by SyncPeerCareModel with the current model.
/// The earlier migration created the community and safety tables and columns; this
/// migration adds only the missing reviewer relationship and records the intended defaults.
/// </summary>
public partial class AddProductCommunitiesAndSafety : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "ProfileVisibility",
            table: "users",
            type: "character varying(24)",
            maxLength: 24,
            nullable: false,
            defaultValue: "public",
            oldClrType: typeof(string),
            oldType: "character varying(24)",
            oldMaxLength: 24);

        migrationBuilder.AlterColumn<string>(
            name: "DiagnosisStatus",
            table: "posts",
            type: "character varying(24)",
            maxLength: 24,
            nullable: false,
            defaultValue: "not_stated",
            oldClrType: typeof(string),
            oldType: "character varying(24)",
            oldMaxLength: 24);

        migrationBuilder.CreateIndex(
            name: "IX_moderation_appeals_ReviewedBy",
            table: "moderation_appeals",
            column: "ReviewedBy");

        migrationBuilder.AddForeignKey(
            name: "FK_moderation_appeals_users_ReviewedBy",
            table: "moderation_appeals",
            column: "ReviewedBy",
            principalTable: "users",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_moderation_appeals_users_ReviewedBy",
            table: "moderation_appeals");

        migrationBuilder.DropIndex(
            name: "IX_moderation_appeals_ReviewedBy",
            table: "moderation_appeals");

        migrationBuilder.AlterColumn<string>(
            name: "ProfileVisibility",
            table: "users",
            type: "character varying(24)",
            maxLength: 24,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(24)",
            oldMaxLength: 24,
            oldDefaultValue: "public");

        migrationBuilder.AlterColumn<string>(
            name: "DiagnosisStatus",
            table: "posts",
            type: "character varying(24)",
            maxLength: 24,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(24)",
            oldMaxLength: 24,
            oldDefaultValue: "not_stated");
    }
}
