using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KinCare.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUberHealthEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UberHealthEnabled",
                table: "facilities");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UberHealthEnabled",
                table: "facilities",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
