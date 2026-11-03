using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerCare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniquePublicUsernames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedDisplayName",
                table: "users",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.Sql("""
                DO $migration$
                DECLARE
                    duplicate_record record;
                    candidate text;
                    suffix integer;
                BEGIN
                    FOR duplicate_record IN
                        SELECT "Id", "DisplayName",
                               row_number() OVER (
                                   PARTITION BY upper(btrim("DisplayName"))
                                   ORDER BY "CreatedAt", "Id"
                               ) AS duplicate_rank
                        FROM "users"
                        WHERE btrim("DisplayName") <> ''
                    LOOP
                        IF duplicate_record.duplicate_rank > 1 THEN
                            suffix := 2;
                            LOOP
                                candidate := left(
                                    btrim(duplicate_record."DisplayName"),
                                    80 - char_length(' (' || suffix::text || ')')
                                ) || ' (' || suffix::text || ')';
                                EXIT WHEN NOT EXISTS (
                                    SELECT 1 FROM "users" AS existing
                                    WHERE existing."Id" <> duplicate_record."Id"
                                      AND upper(btrim(existing."DisplayName")) = upper(btrim(candidate))
                                );
                                suffix := suffix + 1;
                            END LOOP;
                            UPDATE "users"
                            SET "DisplayName" = candidate
                            WHERE "Id" = duplicate_record."Id";
                        END IF;
                    END LOOP;
                END $migration$;

                UPDATE "users"
                SET "NormalizedDisplayName" = upper(btrim("DisplayName"))
                WHERE btrim("DisplayName") <> '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_users_NormalizedDisplayName",
                table: "users",
                column: "NormalizedDisplayName",
                unique: true,
                filter: "\"NormalizedDisplayName\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_NormalizedDisplayName",
                table: "users");

            migrationBuilder.DropColumn(
                name: "NormalizedDisplayName",
                table: "users");
        }
    }
}
