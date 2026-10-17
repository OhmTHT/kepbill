using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReceiptReader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniqueProductName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                table: "products",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            // Same rule as ProductNames: NFC, trimmed, whitespace collapsed, lower-cased.
            migrationBuilder.Sql("""
                UPDATE products SET name = regexp_replace(btrim(normalize(name, NFC)), '\s+', ' ', 'g');
                UPDATE products SET normalized_name = lower(name);
                """);
            // Names repeated before this rule existed: every one after the first gets its code appended.
            migrationBuilder.Sql("""
                UPDATE products p SET name = p.name || ' (' || p.code || ')', normalized_name = lower(p.name || ' (' || p.code || ')')
                FROM (SELECT id, row_number() OVER (PARTITION BY company_id, normalized_name ORDER BY created_at) AS n FROM products) d
                WHERE d.id = p.id AND d.n > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_products_company_id_normalized_name",
                table: "products",
                columns: new[] { "company_id", "normalized_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_products_company_id_normalized_name",
                table: "products");

            migrationBuilder.DropColumn(
                name: "normalized_name",
                table: "products");
        }
    }
}
