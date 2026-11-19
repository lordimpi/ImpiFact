using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImpiFact.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ItemTaxRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "tax_per_unit_amount",
                table: "items",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_percent",
                table: "items",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tax_per_unit_amount",
                table: "items");

            migrationBuilder.DropColumn(
                name: "tax_percent",
                table: "items");
        }
    }
}
