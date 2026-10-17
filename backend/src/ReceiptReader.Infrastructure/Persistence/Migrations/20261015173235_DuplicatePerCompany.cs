using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReceiptReader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DuplicatePerCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_receipts_sha256",
                table: "receipts");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_company_id_sha256",
                table: "receipts",
                columns: new[] { "company_id", "sha256" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_receipts_company_id_sha256",
                table: "receipts");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_sha256",
                table: "receipts",
                column: "sha256",
                unique: true);
        }
    }
}
