using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReceiptReader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FourRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Two roles became four. Admins ran everything: super admins now. Members could approve: approvers now.
            migrationBuilder.Sql("UPDATE users SET role = 'SuperAdmin' WHERE role = 'Admin';");
            migrationBuilder.Sql("UPDATE users SET role = 'Approver' WHERE role = 'Member';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE users SET role = 'Admin' WHERE role IN ('SuperAdmin', 'Admin');");
            migrationBuilder.Sql("UPDATE users SET role = 'Member' WHERE role IN ('Approver', 'User');");
        }
    }
}
