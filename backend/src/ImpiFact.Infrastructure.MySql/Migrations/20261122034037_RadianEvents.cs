using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImpiFact.Infrastructure.MySql.Migrations
{
    /// <inheritdoc />
    public partial class RadianEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "supplier_email",
                table: "received_documents",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true,
                collation: "utf8mb4_0900_as_cs")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "supplier_organization_type_code",
                table: "received_documents",
                type: "varchar(2)",
                maxLength: 2,
                nullable: true,
                collation: "utf8mb4_0900_as_cs")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "supplier_regime_code",
                table: "received_documents",
                type: "varchar(2)",
                maxLength: 2,
                nullable: true,
                collation: "utf8mb4_0900_as_cs")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "dian_status_description",
                table: "document_events",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true,
                collation: "utf8mb4_0900_as_cs")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "error_messages",
                table: "document_events",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "number",
                table: "document_events",
                type: "varchar(120)",
                maxLength: 120,
                nullable: true,
                collation: "utf8mb4_0900_as_cs")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "rejection_code",
                table: "document_events",
                type: "varchar(2)",
                maxLength: 2,
                nullable: true,
                collation: "utf8mb4_0900_as_cs")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "response_file_key",
                table: "document_events",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true,
                collation: "utf8mb4_0900_as_cs")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "xml_file_key",
                table: "document_events",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true,
                collation: "utf8mb4_0900_as_cs")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "supplier_email",
                table: "received_documents");

            migrationBuilder.DropColumn(
                name: "supplier_organization_type_code",
                table: "received_documents");

            migrationBuilder.DropColumn(
                name: "supplier_regime_code",
                table: "received_documents");

            migrationBuilder.DropColumn(
                name: "dian_status_description",
                table: "document_events");

            migrationBuilder.DropColumn(
                name: "error_messages",
                table: "document_events");

            migrationBuilder.DropColumn(
                name: "number",
                table: "document_events");

            migrationBuilder.DropColumn(
                name: "rejection_code",
                table: "document_events");

            migrationBuilder.DropColumn(
                name: "response_file_key",
                table: "document_events");

            migrationBuilder.DropColumn(
                name: "xml_file_key",
                table: "document_events");
        }
    }
}
