using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrinkIt.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderAuditColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A rename and not a drop on purpose: an order that is ready last
            // changed when it became ready, so the value carries over as is and
            // the Listos column keeps its clocks. Every other order had no
            // ReadyAt and starts with no LastModifiedAt, which the board reads
            // as "since it was paid".
            migrationBuilder.RenameColumn(
                name: "ReadyAt",
                table: "Orders",
                newName: "LastModifiedAt");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Orders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastModifiedBy",
                table: "Orders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "LastModifiedBy",
                table: "Orders");

            migrationBuilder.RenameColumn(
                name: "LastModifiedAt",
                table: "Orders",
                newName: "ReadyAt");
        }
    }
}
