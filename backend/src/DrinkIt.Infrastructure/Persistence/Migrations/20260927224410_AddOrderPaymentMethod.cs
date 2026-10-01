using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkIt.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Method",
                table: "Orders",
                type: "int",
                nullable: true);

            // Every order paid before this column existed was settled by the
            // only strategy that has ever run — Digital — so backfilling it is
            // recording a known fact, not inventing one (unlike US-30's audit
            // columns, which stay null because nobody recorded who did what).
            // Orders still in Cart, never paid, keep Method null: they have no
            // method yet, same as PaidAt.
            migrationBuilder.Sql(
                "UPDATE Orders SET Method = 0 WHERE PaidAt IS NOT NULL AND Method IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Method",
                table: "Orders");
        }
    }
}
