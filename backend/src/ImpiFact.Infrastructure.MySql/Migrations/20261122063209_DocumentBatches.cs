using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImpiFact.Infrastructure.MySql.Migrations
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
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "document_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb4_0900_as_cs")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    document_count = table.Column<int>(type: "int", nullable: false),
                    type_environment_id = table.Column<int>(type: "int", nullable: true),
                    zip_file_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_0900_as_cs")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    zip_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_0900_as_cs")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entries = table.Column<string>(type: "json", nullable: true),
                    dian_status_code = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true, collation: "utf8mb4_0900_as_cs")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    dian_status_description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_0900_as_cs")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    error_messages = table.Column<string>(type: "json", nullable: true),
                    zip_file_key = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_0900_as_cs")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    response_file_key = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_0900_as_cs")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    queued_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    company_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
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
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_0900_as_cs");

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
