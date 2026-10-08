using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DepEdDTRSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolHeadDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SchoolHeadName",
                table: "Schools",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchoolHeadPosition",
                table: "Schools",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SchoolHeadName",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "SchoolHeadPosition",
                table: "Schools");
        }
    }
}
