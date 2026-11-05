using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkIt.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStampCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StampCardId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StampsSpent",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "StampCardRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StampsForReward = table.Column<int>(type: "int", nullable: false),
                    RewardProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StampCardRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StampCardRules_Products_RewardProductId",
                        column: x => x.RewardProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StampCardRules_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StampCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Token = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    RewardsClaimed = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StampCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StampCards_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StampCardId",
                table: "Orders",
                column: "StampCardId");

            migrationBuilder.CreateIndex(
                name: "IX_StampCardRules_RewardProductId",
                table: "StampCardRules",
                column: "RewardProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StampCardRules_VenueId",
                table: "StampCardRules",
                column: "VenueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StampCards_VenueId_Token",
                table: "StampCards",
                columns: new[] { "VenueId", "Token" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_StampCards_StampCardId",
                table: "Orders",
                column: "StampCardId",
                principalTable: "StampCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_StampCards_StampCardId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "StampCardRules");

            migrationBuilder.DropTable(
                name: "StampCards");

            migrationBuilder.DropIndex(
                name: "IX_Orders_StampCardId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "StampCardId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "StampsSpent",
                table: "OrderItems");
        }
    }
}
