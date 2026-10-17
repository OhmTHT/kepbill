using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReceiptReader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CategoryActiveAndStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "active",
                table: "product_categories",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Category codes become required and unique: give a blank or repeated one a generated code first.
            migrationBuilder.Sql("""
                UPDATE product_categories c SET code_prefix = 'C' || upper(substr(md5(c.id::text), 1, 5))
                FROM (SELECT id, row_number() OVER (PARTITION BY company_id, code_prefix ORDER BY created_at) AS n
                      FROM product_categories) d
                WHERE d.id = c.id AND (c.code_prefix = '' OR d.n > 1);
                """);

            // Product codes are now always PREFIX-number: one typed by hand without the prefix ("001") gets it ("BED-001").
            migrationBuilder.Sql("""
                UPDATE products p SET code = c.code_prefix || '-' || p.code
                FROM product_categories c
                WHERE c.id = p.category_id
                  AND p.code NOT LIKE c.code_prefix || '-%'
                  AND length(c.code_prefix || '-' || p.code) <= 30
                  AND NOT EXISTS (SELECT 1 FROM products o
                                  WHERE o.company_id = p.company_id AND o.code = c.code_prefix || '-' || p.code);
                """);

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_movements_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_stock_movements_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_categories_company_id_code_prefix",
                table: "product_categories",
                columns: new[] { "company_id", "code_prefix" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_company_id_product_id_date",
                table: "stock_movements",
                columns: new[] { "company_id", "product_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_product_id",
                table: "stock_movements",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_warehouse_id",
                table: "stock_movements",
                column: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropIndex(
                name: "ix_product_categories_company_id_code_prefix",
                table: "product_categories");

            migrationBuilder.DropColumn(
                name: "active",
                table: "product_categories");
        }
    }
}
