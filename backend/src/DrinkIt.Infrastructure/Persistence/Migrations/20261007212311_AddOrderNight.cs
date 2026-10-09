using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkIt.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderNight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NightId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_VenueId_NightId",
                table: "Orders",
                columns: new[] { "VenueId", "NightId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_VenueId_NightId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "NightId",
                table: "Orders");
        }
    }
}
