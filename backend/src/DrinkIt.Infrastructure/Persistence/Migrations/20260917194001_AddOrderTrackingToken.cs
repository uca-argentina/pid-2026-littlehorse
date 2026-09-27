using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkIt.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderTrackingToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TrackingToken",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Orders that already existed would be left with an empty token,
            // which is not a token at all: reading one back throws, and every
            // query that materialises the aggregate would break on it. They get
            // a real one — thirty-two hex digits, which is a GUID without its
            // hyphens — so nothing in the table is unreadable. Nobody holds
            // these links, and that is the point: those orders stay unreachable
            // while staying readable.
            migrationBuilder.Sql(
                """
                UPDATE [Orders]
                SET [TrackingToken] = LOWER(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''))
                WHERE [TrackingToken] = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrackingToken",
                table: "Orders");
        }
    }
}
