using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkIt.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBarStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Adjustments from before bars kept a stock of their own name no
            // bar, and a KdsId made up for them would break the foreign key
            // below. They only ever existed in local databases of this branch:
            // AddNightStockAdjustments never reached main.
            migrationBuilder.Sql("DELETE FROM [NightStockAdjustments];");

            migrationBuilder.DropIndex(
                name: "IX_NightStockAdjustments_VenueId_NightId_ProductId",
                table: "NightStockAdjustments");

            migrationBuilder.AddColumn<Guid>(
                name: "KdsId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "KdsId",
                table: "NightStockAdjustments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "BarStocks",
                columns: table => new
                {
                    KdsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Units = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BarStocks", x => new { x.KdsId, x.ProductId });
                    table.CheckConstraint("CK_BarStocks_Units", "[Units] >= 0");
                    table.ForeignKey(
                        name: "FK_BarStocks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BarStocks_StaffUsers_KdsId",
                        column: x => x.KdsId,
                        principalTable: "StaffUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BarStocks_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_VenueId_KdsId_Status",
                table: "Orders",
                columns: new[] { "VenueId", "KdsId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_NightStockAdjustments_KdsId",
                table: "NightStockAdjustments",
                column: "KdsId");

            migrationBuilder.CreateIndex(
                name: "IX_NightStockAdjustments_VenueId_NightId_KdsId_ProductId",
                table: "NightStockAdjustments",
                columns: new[] { "VenueId", "NightId", "KdsId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_BarStocks_ProductId",
                table: "BarStocks",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_BarStocks_VenueId",
                table: "BarStocks",
                column: "VenueId");

            migrationBuilder.AddForeignKey(
                name: "FK_NightStockAdjustments_StaffUsers_KdsId",
                table: "NightStockAdjustments",
                column: "KdsId",
                principalTable: "StaffUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NightStockAdjustments_StaffUsers_KdsId",
                table: "NightStockAdjustments");

            migrationBuilder.DropTable(
                name: "BarStocks");

            migrationBuilder.DropIndex(
                name: "IX_Orders_VenueId_KdsId_Status",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_NightStockAdjustments_KdsId",
                table: "NightStockAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_NightStockAdjustments_VenueId_NightId_KdsId_ProductId",
                table: "NightStockAdjustments");

            migrationBuilder.DropColumn(
                name: "KdsId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "KdsId",
                table: "NightStockAdjustments");

            migrationBuilder.CreateIndex(
                name: "IX_NightStockAdjustments_VenueId_NightId_ProductId",
                table: "NightStockAdjustments",
                columns: new[] { "VenueId", "NightId", "ProductId" });
        }
    }
}
