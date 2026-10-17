using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReceiptReader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompaniesAndAccounting : Migration
    {
        private const string LegacyCompanyId = "7b0c3f4e-2a1d-4c5b-9e8f-000000000001";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_category",
                table: "vendors");

            // Categories are replaced by the chart of accounts; keep the old text in the notes.
            migrationBuilder.Sql("""
                UPDATE receipts
                SET notes = concat_ws(' · ', nullif(notes, ''), 'หมวดเดิม: ' || category)
                WHERE category IS NOT NULL AND category <> '';
                """);

            migrationBuilder.DropColumn(
                name: "category",
                table: "receipts");

            migrationBuilder.AddColumn<string>(
                name: "reference_document_number",
                table: "receipts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "pinned",
                table: "vendor_examples",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "account_id",
                table: "receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                table: "receipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "direction",
                table: "receipts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Purchase");

            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                table: "receipts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "own_tax_id_on_document",
                table: "receipts",
                type: "character varying(13)",
                maxLength: 13,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "period",
                table: "receipts",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reference_receipt_id",
                table: "receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "discount",
                table: "receipt_line_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    tax_id = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    branch = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    vat_registered = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_accounts_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "closed_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_closed_periods", x => x.id);
                    table.ForeignKey(
                        name: "fk_closed_periods_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_auto = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payments_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_receipts_account_id",
                table: "receipts",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_buyer_tax_id",
                table: "receipts",
                column: "buyer_tax_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_company_id_direction_period",
                table: "receipts",
                columns: new[] { "company_id", "direction", "period" });

            migrationBuilder.CreateIndex(
                name: "ix_receipts_reference_receipt_id",
                table: "receipts",
                column: "reference_receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_company_id_code",
                table: "accounts",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_closed_periods_company_id_period",
                table: "closed_periods",
                columns: new[] { "company_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_account_id",
                table: "payments",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_date",
                table: "payments",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_payments_receipt_id",
                table: "payments",
                column: "receipt_id");

            // Documents from before companies existed go to a placeholder company (purchases), which the user
            // renames and fills in. Its chart of accounts is seeded at startup (CompanyService.EnsureChartsAsync).
            migrationBuilder.Sql($"""
                INSERT INTO companies (id, name, tax_id, branch, address, vat_registered, created_at, updated_at)
                SELECT '{LegacyCompanyId}', 'บริษัทของฉัน (แก้ไขโปรไฟล์)', '', '00000', NULL, TRUE, now(), now()
                WHERE EXISTS (SELECT 1 FROM receipts);

                UPDATE receipts SET company_id = '{LegacyCompanyId}'
                WHERE company_id = '00000000-0000-0000-0000-000000000000';

                UPDATE receipts SET period = to_char(issue_date, 'YYYY-MM')
                WHERE issue_date IS NOT NULL AND period IS NULL;
                """);

            migrationBuilder.AddForeignKey(
                name: "fk_receipts_accounts_account_id",
                table: "receipts",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_receipts_companies_company_id",
                table: "receipts",
                column: "company_id",
                principalTable: "companies",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_receipts_receipts_reference_receipt_id",
                table: "receipts",
                column: "reference_receipt_id",
                principalTable: "receipts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_receipts_accounts_account_id",
                table: "receipts");

            migrationBuilder.DropForeignKey(
                name: "fk_receipts_companies_company_id",
                table: "receipts");

            migrationBuilder.DropForeignKey(
                name: "fk_receipts_receipts_reference_receipt_id",
                table: "receipts");

            migrationBuilder.DropTable(
                name: "closed_periods");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "companies");

            migrationBuilder.DropIndex(
                name: "ix_receipts_account_id",
                table: "receipts");

            migrationBuilder.DropIndex(
                name: "ix_receipts_buyer_tax_id",
                table: "receipts");

            migrationBuilder.DropIndex(
                name: "ix_receipts_company_id_direction_period",
                table: "receipts");

            migrationBuilder.DropIndex(
                name: "ix_receipts_reference_receipt_id",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "pinned",
                table: "vendor_examples");

            migrationBuilder.DropColumn(
                name: "account_id",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "company_id",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "direction",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "due_date",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "own_tax_id_on_document",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "period",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "reference_receipt_id",
                table: "receipts");

            migrationBuilder.DropColumn(
                name: "discount",
                table: "receipt_line_items");

            migrationBuilder.DropColumn(
                name: "reference_document_number",
                table: "receipts");

            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "receipts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "default_category",
                table: "vendors",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
