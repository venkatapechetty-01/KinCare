using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KinCare.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class VendorPhotoUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhotoUrl",
                table: "vendors",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhotoUrl",
                table: "vendors");
        }
    }
}
