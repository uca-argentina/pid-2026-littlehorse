using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkIt.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNightStocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NightStocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Loaded = table.Column<int>(type: "int", nullable: false),
                    Remaining = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NightStocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NightStocks_Nights_NightId",
                        column: x => x.NightId,
                        principalTable: "Nights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NightStocks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NightStocks_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NightStocks_NightId",
                table: "NightStocks",
                column: "NightId");

            migrationBuilder.CreateIndex(
                name: "IX_NightStocks_ProductId",
                table: "NightStocks",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_NightStocks_VenueId_NightId_ProductId",
                table: "NightStocks",
                columns: new[] { "VenueId", "NightId", "ProductId" },
                unique: true);

            migrationBuilder.Sql(NightStockBackfill.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NightStocks");
        }
    }
}
