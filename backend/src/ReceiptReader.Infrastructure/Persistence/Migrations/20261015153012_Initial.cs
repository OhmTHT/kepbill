using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReceiptReader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vendors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_id = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    branch = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    default_category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    approved_receipt_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    stored_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    extractor_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    model_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    raw_extraction = table.Column<string>(type: "jsonb", nullable: true),
                    ocr_text = table.Column<string>(type: "text", nullable: true),
                    processing_error = table.Column<string>(type: "text", nullable: true),
                    issues = table.Column<string>(type: "jsonb", nullable: false),
                    processing_attempts = table.Column<int>(type: "integer", nullable: false),
                    document_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    document_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    seller_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    seller_tax_id = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    seller_branch = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    seller_address = table.Column<string>(type: "text", nullable: true),
                    buyer_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    buyer_tax_id = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    buyer_branch = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    buyer_address = table.Column<string>(type: "text", nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    vat_included = table.Column<bool>(type: "boolean", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_receipts_vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "vendor_examples",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_examples", x => x.id);
                    table.ForeignKey(
                        name: "fk_vendor_examples_vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receipt_line_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_line_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_receipt_line_items_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_receipt_line_items_receipt_id_line_no",
                table: "receipt_line_items",
                columns: new[] { "receipt_id", "line_no" });

            migrationBuilder.CreateIndex(
                name: "ix_receipts_issue_date",
                table: "receipts",
                column: "issue_date");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_seller_tax_id",
                table: "receipts",
                column: "seller_tax_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_sha256",
                table: "receipts",
                column: "sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_receipts_status",
                table: "receipts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_vendor_id",
                table: "receipts",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_examples_receipt_id",
                table: "vendor_examples",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_examples_vendor_id",
                table: "vendor_examples",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendors_tax_id",
                table: "vendors",
                column: "tax_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receipt_line_items");

            migrationBuilder.DropTable(
                name: "vendor_examples");

            migrationBuilder.DropTable(
                name: "receipts");

            migrationBuilder.DropTable(
                name: "vendors");
        }
    }
}
