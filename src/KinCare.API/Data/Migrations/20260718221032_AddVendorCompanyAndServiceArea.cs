using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KinCare.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorCompanyAndServiceArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Company",
                table: "vendors",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceArea",
                table: "vendors",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Company",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "ServiceArea",
                table: "vendors");
        }
    }
}
