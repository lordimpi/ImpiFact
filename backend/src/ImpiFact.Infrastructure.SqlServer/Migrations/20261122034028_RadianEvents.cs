using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImpiFact.Infrastructure.SqlServer.Migrations
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
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true,
                collation: "Latin1_General_100_CS_AS_SC");

            migrationBuilder.AddColumn<string>(
                name: "supplier_organization_type_code",
                table: "received_documents",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true,
                collation: "Latin1_General_100_CS_AS_SC");

            migrationBuilder.AddColumn<string>(
                name: "supplier_regime_code",
                table: "received_documents",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true,
                collation: "Latin1_General_100_CS_AS_SC");

            migrationBuilder.AddColumn<string>(
                name: "dian_status_description",
                table: "document_events",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                collation: "Latin1_General_100_CS_AS_SC");

            migrationBuilder.AddColumn<string>(
                name: "error_messages",
                table: "document_events",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "number",
                table: "document_events",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true,
                collation: "Latin1_General_100_CS_AS_SC");

            migrationBuilder.AddColumn<string>(
                name: "rejection_code",
                table: "document_events",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true,
                collation: "Latin1_General_100_CS_AS_SC");

            migrationBuilder.AddColumn<string>(
                name: "response_file_key",
                table: "document_events",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                collation: "Latin1_General_100_CS_AS_SC");

            migrationBuilder.AddColumn<string>(
                name: "xml_file_key",
                table: "document_events",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                collation: "Latin1_General_100_CS_AS_SC");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_events_error_messages_json",
                table: "document_events",
                sql: "ISJSON([error_messages]) = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_document_events_error_messages_json",
                table: "document_events");

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
