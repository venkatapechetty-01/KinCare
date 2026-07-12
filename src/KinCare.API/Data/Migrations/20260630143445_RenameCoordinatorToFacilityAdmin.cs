using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KinCare.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameCoordinatorToFacilityAdmin : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"AspNetUsers\" SET \"Role\" = 'FacilityAdmin' WHERE \"Role\" = 'Coordinator'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"AspNetUsers\" SET \"Role\" = 'Coordinator' WHERE \"Role\" = 'FacilityAdmin'");
        }
    }
}
