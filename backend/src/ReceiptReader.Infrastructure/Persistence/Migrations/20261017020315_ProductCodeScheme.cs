using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReceiptReader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductCodeScheme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_code_schemes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    product_types = table.Column<List<string>>(type: "text[]", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    category_name_hint = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    product_name_hint = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    code_hint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    segments = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_code_schemes", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_code_schemes_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_code_terms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    segment = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    term = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_term = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_code_terms", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_code_terms_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_code_schemes_company_id",
                table: "product_code_schemes",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_code_terms_company_id_segment_code",
                table: "product_code_terms",
                columns: new[] { "company_id", "segment", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_code_terms_company_id_segment_normalized_term",
                table: "product_code_terms",
                columns: new[] { "company_id", "segment", "normalized_term" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_code_schemes");

            migrationBuilder.DropTable(
                name: "product_code_terms");
        }
    }
}
