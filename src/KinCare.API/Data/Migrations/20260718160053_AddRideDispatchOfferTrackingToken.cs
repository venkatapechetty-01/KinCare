using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KinCare.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRideDispatchOfferTrackingToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TrackingToken",
                table: "ride_dispatch_offers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Migrate any pre-existing "Pending|token:xxx" packed values (the old hack this
            // migration replaces) into the new dedicated column before shrinking Status back
            // down — a straight ALTER COLUMN to varchar(20) would otherwise fail/truncate.
            migrationBuilder.Sql(
                """
                UPDATE ride_dispatch_offers
                SET "TrackingToken" = split_part("Status", '|token:', 2)
                WHERE "Status" LIKE 'Pending|token:%';

                UPDATE ride_dispatch_offers
                SET "Status" = 'Pending'
                WHERE "Status" LIKE 'Pending|token:%';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ride_dispatch_offers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.CreateIndex(
                name: "IX_ride_dispatch_offers_TrackingToken",
                table: "ride_dispatch_offers",
                column: "TrackingToken",
                filter: "\"TrackingToken\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ride_dispatch_offers_TrackingToken",
                table: "ride_dispatch_offers");

            migrationBuilder.DropColumn(
                name: "TrackingToken",
                table: "ride_dispatch_offers");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ride_dispatch_offers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }
    }
}
