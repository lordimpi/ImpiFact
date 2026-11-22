using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImpiFact.Infrastructure.Postgres.Migrations
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
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "document_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    document_count = table.Column<int>(type: "integer", nullable: false),
                    type_environment_id = table.Column<int>(type: "integer", nullable: true),
                    zip_file_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    zip_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    entries = table.Column<string>(type: "jsonb", nullable: true),
                    dian_status_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    dian_status_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    error_messages = table.Column<string>(type: "jsonb", nullable: true),
                    zip_file_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    response_file_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_batches", x => x.id);
                    table.UniqueConstraint("ux_document_batches_company_id_id", x => new { x.company_id, x.id });
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
