using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReceiptReader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CategoryOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                table: "product_categories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing categories keep the order they were shown in until now: by name.
            migrationBuilder.Sql("""
                UPDATE product_categories c SET sort_order = d.n
                FROM (SELECT id, row_number() OVER (PARTITION BY company_id ORDER BY name) - 1 AS n FROM product_categories) d
                WHERE d.id = c.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sort_order",
                table: "product_categories");
        }
    }
}
