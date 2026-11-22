using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImpiFact.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class DocumentBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "batch_id",
                table: "documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "document_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, collation: "Latin1_General_100_CS_AS_SC"),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    document_count = table.Column<int>(type: "int", nullable: false),
                    type_environment_id = table.Column<int>(type: "int", nullable: true),
                    zip_file_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true, collation: "Latin1_General_100_CS_AS_SC"),
                    zip_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_CS_AS_SC"),
                    entries = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    dian_status_code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true, collation: "Latin1_General_100_CS_AS_SC"),
                    dian_status_description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true, collation: "Latin1_General_100_CS_AS_SC"),
                    error_messages = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    zip_file_key = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true, collation: "Latin1_General_100_CS_AS_SC"),
                    response_file_key = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true, collation: "Latin1_General_100_CS_AS_SC"),
                    queued_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_batches", x => x.id);
                    table.UniqueConstraint("ux_document_batches_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_document_batches_entries_json", "ISJSON([entries]) = 1");
                    table.CheckConstraint("ck_document_batches_error_messages_json", "ISJSON([error_messages]) = 1");
                    table.ForeignKey(
                        name: "fk_document_batches_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_batches_type_environments",
                        column: x => x.type_environment_id,
                        principalTable: "type_environments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documents_batch",
                table: "documents",
                columns: new[] { "company_id", "batch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_batches_company_id_created_at",
                table: "document_batches",
                columns: new[] { "company_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_document_batches_type_environment_id",
                table: "document_batches",
                column: "type_environment_id");

            migrationBuilder.CreateIndex(
                name: "ux_document_batches_company_id_name",
                table: "document_batches",
                columns: new[] { "company_id", "name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_documents_document_batches",
                table: "documents",
                columns: new[] { "company_id", "batch_id" },
                principalTable: "document_batches",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_documents_document_batches",
                table: "documents");

            migrationBuilder.DropTable(
                name: "document_batches");

            migrationBuilder.DropIndex(
                name: "ix_documents_batch",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "batch_id",
                table: "documents");
        }
    }
}
