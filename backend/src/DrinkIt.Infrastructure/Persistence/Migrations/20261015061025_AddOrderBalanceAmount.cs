using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkIt.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderBalanceAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BalanceAmount",
                table: "Orders",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BalanceAmount",
                table: "Orders");
        }
    }
}
