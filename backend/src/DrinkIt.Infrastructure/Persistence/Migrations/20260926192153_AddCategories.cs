using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkIt.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// US-14: each venue splits its menu into categories of its own. Written by
    /// hand around what EF scaffolded: the scaffold adds CategoryId as a non-null
    /// column defaulting to an empty guid, which no category row matches, so the
    /// foreign key fails on any database that already has products. Here every
    /// venue gets the three the platform starts with, every existing product
    /// lands in Tragos (US-14, criterion 4), and only then is the column required.
    /// </summary>
    /// <inheritdoc />
    public partial class AddCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_VenueId_Name",
                table: "Categories",
                columns: new[] { "VenueId", "Name" },
                unique: true);

            // In the order the customer's tabs are drawn: a millisecond apart, so
            // CreatedAt keeps it.
            migrationBuilder.Sql(
                """
                INSERT INTO Categories (Id, VenueId, Name, CreatedAt)
                SELECT NEWID(), venue.Id, tab.Name, DATEADD(MILLISECOND, tab.Position, SYSDATETIMEOFFSET())
                FROM Venues AS venue
                CROSS JOIN (VALUES (1, N'Tragos'), (2, N'Cervezas'), (3, N'Sin alcohol')) AS tab (Position, Name)
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE product
                SET CategoryId = category.Id
                FROM Products AS product
                JOIN Categories AS category
                    ON category.VenueId = product.VenueId
                    AND category.Name = N'Tragos'
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoryId",
                table: "Products",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId",
                table: "Products",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Categories_CategoryId",
                table: "Products",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Categories_CategoryId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_CategoryId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}
