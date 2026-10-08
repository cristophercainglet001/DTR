using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DepEdDTRSystem.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeMustChangePassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "EmployeeAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "EmployeeAccounts");
        }
    }
}
