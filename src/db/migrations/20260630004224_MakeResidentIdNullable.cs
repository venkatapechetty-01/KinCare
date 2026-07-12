using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KinCare.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeResidentIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_rides_residents_ResidentId",
                table: "rides");

            migrationBuilder.AlterColumn<Guid>(
                name: "ResidentId",
                table: "rides",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_rides_residents_ResidentId",
                table: "rides",
                column: "ResidentId",
                principalTable: "residents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_rides_residents_ResidentId",
                table: "rides");

            migrationBuilder.AlterColumn<Guid>(
                name: "ResidentId",
                table: "rides",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_rides_residents_ResidentId",
                table: "rides",
                column: "ResidentId",
                principalTable: "residents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
