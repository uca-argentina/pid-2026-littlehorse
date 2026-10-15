using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkIt.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNightToBarStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rows from before each night kept its own bars name no night, and
            // one made up for them would break the foreign key below. Their
            // units are still in Products.Stock, so dropping the rows puts them
            // back in the storeroom, which is where a bar's leftovers go now.
            // Only local databases of this branch ever had any.
            migrationBuilder.Sql("DELETE FROM [BarStocks];");

            migrationBuilder.DropPrimaryKey(
                name: "PK_BarStocks",
                table: "BarStocks");

            migrationBuilder.AddColumn<Guid>(
                name: "NightId",
                table: "BarStocks",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "PK_BarStocks",
                table: "BarStocks",
                columns: new[] { "NightId", "KdsId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_BarStocks_KdsId",
                table: "BarStocks",
                column: "KdsId");

            migrationBuilder.AddForeignKey(
                name: "FK_BarStocks_Nights_NightId",
                table: "BarStocks",
                column: "NightId",
                principalTable: "Nights",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BarStocks_Nights_NightId",
                table: "BarStocks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_BarStocks",
                table: "BarStocks");

            migrationBuilder.DropIndex(
                name: "IX_BarStocks_KdsId",
                table: "BarStocks");

            migrationBuilder.DropColumn(
                name: "NightId",
                table: "BarStocks");

            migrationBuilder.AddPrimaryKey(
                name: "PK_BarStocks",
                table: "BarStocks",
                columns: new[] { "KdsId", "ProductId" });
        }
    }
}
