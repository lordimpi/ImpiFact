using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImpiFact.Infrastructure.Postgres.Migrations
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
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supplier_organization_type_code",
                table: "received_documents",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supplier_regime_code",
                table: "received_documents",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dian_status_description",
                table: "document_events",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "error_messages",
                table: "document_events",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "number",
                table: "document_events",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_code",
                table: "document_events",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "response_file_key",
                table: "document_events",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "xml_file_key",
                table: "document_events",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
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
