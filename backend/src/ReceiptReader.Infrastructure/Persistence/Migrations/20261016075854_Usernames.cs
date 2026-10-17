using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReceiptReader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Usernames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Existing users get the part of their email before "@" (only allowed characters, at least 3, unique by
            // numbering repeats); admins can change it afterwards.
            migrationBuilder.Sql(
                """
                WITH cleaned AS (
                    SELECT id, created_at,
                           left(trim(both '._-' from regexp_replace(lower(split_part(email, '@', 1)), '[^a-z0-9._-]', '', 'g')), 28) AS name
                    FROM users),
                base AS (
                    SELECT id, created_at, CASE WHEN length(name) < 3 THEN rpad(name, 3, '0') ELSE name END AS name
                    FROM cleaned),
                numbered AS (
                    SELECT id, name, row_number() OVER (PARTITION BY name ORDER BY created_at) AS n FROM base)
                UPDATE users u
                SET username = CASE WHEN numbered.n = 1 THEN numbered.name ELSE numbered.name || numbered.n END
                FROM numbered
                WHERE u.id = numbered.id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                table: "users",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_username",
                table: "users");

            migrationBuilder.DropColumn(
                name: "username",
                table: "users");
        }
    }
}
