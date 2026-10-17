using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReceiptReader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NameSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_username",
                table: "users");

            migrationBuilder.DropColumn(
                name: "username",
                table: "users");

            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // The name now doubles as a sign-in name: tidy existing ones the way the app does (NFC, single spaces,
            // no "@"), number repeats so they are unique, then fill the lower-cased lookup key.
            migrationBuilder.Sql(
                """
                UPDATE users
                SET name = coalesce(nullif(trim(regexp_replace(replace(normalize(name, NFC), '@', ''), '\s+', ' ', 'g')), ''), 'user');

                WITH numbered AS (
                    SELECT id, row_number() OVER (PARTITION BY lower(name) ORDER BY created_at) AS n FROM users)
                UPDATE users u
                SET name = left(u.name, 94) || ' (' || numbered.n || ')'
                FROM numbered
                WHERE u.id = numbered.id AND numbered.n > 1;

                UPDATE users SET normalized_name = lower(name);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_name",
                table: "users",
                column: "normalized_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_normalized_name",
                table: "users");

            migrationBuilder.DropColumn(
                name: "normalized_name",
                table: "users");

            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                table: "users",
                column: "username",
                unique: true);
        }
    }
}
