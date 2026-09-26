using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkIt.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// US-14 grows from three fixed categories into a table each venue fills.
    /// Written by hand around what EF scaffolded: the scaffold drops the old
    /// column first, which would have thrown away which category every product
    /// was in. Here every venue gets the three the platform started with, every
    /// product is moved into the row that matches what it had, and only then
    /// does the old column go.
    /// </summary>
    /// <inheritdoc />
    public partial class ReplaceProductCategoryWithCategories : Migration
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

            // The three that existed as an enum, in the order the customer's
            // tabs were drawn: a millisecond apart, so CreatedAt keeps it.
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

            // 1 Drink, 2 Beer, 3 NonAlcoholic. Anything else was never valid and
            // lands in Tragos, which is where AddProductCategory put it too.
            migrationBuilder.Sql(
                """
                UPDATE product
                SET CategoryId = category.Id
                FROM Products AS product
                JOIN Categories AS category
                    ON category.VenueId = product.VenueId
                    AND category.Name = CASE product.Category
                        WHEN 2 THEN N'Cervezas'
                        WHEN 3 THEN N'Sin alcohol'
                        ELSE N'Tragos'
                    END
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoryId",
                table: "Products",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Products");

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

            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 1);

            // Back into the three the enum can hold. A category the venue made
            // itself has no number to go back to, so its products land in Tragos.
            migrationBuilder.Sql(
                """
                UPDATE product
                SET Category = CASE category.Name WHEN N'Cervezas' THEN 2 WHEN N'Sin alcohol' THEN 3 ELSE 1 END
                FROM Products AS product
                JOIN Categories AS category ON category.Id = product.CategoryId
                """);

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}
