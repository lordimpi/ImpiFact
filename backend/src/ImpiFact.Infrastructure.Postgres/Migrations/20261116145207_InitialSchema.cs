using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ImpiFact.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_type = table.Column<short>(type: "smallint", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    changes = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "countries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_countries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_note_discrepancy_responses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_note_discrepancy_responses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_note_discrepancy_responses_sd",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_note_discrepancy_responses_sd", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "debit_note_discrepancy_responses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debit_note_discrepancy_responses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discounts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "incoterms",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incoterms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "languages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_languages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locked_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_forms",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_forms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_methods",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_methods", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    period = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    observations = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prepaid_payment_types",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prepaid_payment_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reference_prices",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reference_prices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "resellers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identification_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dv = table.Column<string>(type: "character(1)", fixedLength: true, maxLength: 1, nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    plan_label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    observation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resellers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "taxes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_taxes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "type_currencies",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_type_currencies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "type_discounts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_type_discounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "type_document_identifications",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    rips_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_type_document_identifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "type_documents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    cufe_algorithm = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_type_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "type_environments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_type_environments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "type_generation_transmitions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_type_generation_transmitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "type_item_identifications",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code_agency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_type_item_identifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "type_liabilities",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_type_liabilities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "type_operations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_type_operations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "type_organizations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_type_organizations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "type_regimes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_type_regimes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "type_rejections",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_type_rejections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "type_spds",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_type_spds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unit_measures",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_measures", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "departments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    country_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_departments", x => x.id);
                    table.ForeignKey(
                        name: "fk_departments_countries",
                        column: x => x.country_id,
                        principalTable: "countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_quotas",
                columns: table => new
                {
                    plan_id = table.Column<int>(type: "integer", nullable: false),
                    document_kind = table.Column<short>(type: "smallint", nullable: false),
                    quantity = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_quotas", x => new { x.plan_id, x.document_kind });
                    table.ForeignKey(
                        name: "fk_plan_quotas_plans",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    reseller_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false),
                    security_stamp = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_resellers",
                        column: x => x.reseller_id,
                        principalTable: "resellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "municipalities",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    department_id = table.Column<int>(type: "integer", nullable: false),
                    codefacturador = table.Column<long>(type: "bigint", nullable: true),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_municipalities", x => x.id);
                    table.ForeignKey(
                        name: "fk_municipalities_departments",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_refresh_tokens",
                        column: x => x.replaced_by,
                        principalTable: "refresh_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_roles",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_roles_users",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reseller_id = table.Column<Guid>(type: "uuid", nullable: true),
                    identification_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dv = table.Column<string>(type: "character(1)", fixedLength: true, maxLength: 1, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    merchant_registration = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    language_id = table.Column<int>(type: "integer", nullable: false),
                    country_id = table.Column<int>(type: "integer", nullable: false),
                    municipality_id = table.Column<int>(type: "integer", nullable: false),
                    tax_id = table.Column<int>(type: "integer", nullable: false),
                    type_currency_id = table.Column<int>(type: "integer", nullable: false),
                    type_operation_id = table.Column<int>(type: "integer", nullable: false),
                    type_document_identification_id = table.Column<int>(type: "integer", nullable: false),
                    type_organization_id = table.Column<int>(type: "integer", nullable: false),
                    type_regime_id = table.Column<int>(type: "integer", nullable: false),
                    storage_mode = table.Column<short>(type: "smallint", nullable: true),
                    graphic_representation_template = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    allow_seller_login = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.id);
                    table.ForeignKey(
                        name: "fk_companies_countries",
                        column: x => x.country_id,
                        principalTable: "countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_companies_languages",
                        column: x => x.language_id,
                        principalTable: "languages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_companies_municipalities",
                        column: x => x.municipality_id,
                        principalTable: "municipalities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_companies_resellers",
                        column: x => x.reseller_id,
                        principalTable: "resellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_companies_taxes",
                        column: x => x.tax_id,
                        principalTable: "taxes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_companies_type_currencies",
                        column: x => x.type_currency_id,
                        principalTable: "type_currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_companies_type_document_identifications",
                        column: x => x.type_document_identification_id,
                        principalTable: "type_document_identifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_companies_type_operations",
                        column: x => x.type_operation_id,
                        principalTable: "type_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_companies_type_organizations",
                        column: x => x.type_organization_id,
                        principalTable: "type_organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_companies_type_regimes",
                        column: x => x.type_regime_id,
                        principalTable: "type_regimes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    key_prefix = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    key_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    scopes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_api_keys_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_keys_users",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "certificates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    secret_ref = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    thumbprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    issuer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    not_before = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    not_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificates", x => x.id);
                    table.ForeignKey(
                        name: "fk_certificates_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_certificates_users",
                        column: x => x.uploaded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "company_environments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_kind = table.Column<short>(type: "smallint", nullable: false),
                    type_environment_id = table.Column<int>(type: "integer", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_environments", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_environments_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_company_environments_type_environments",
                        column: x => x.type_environment_id,
                        principalTable: "type_environments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "company_liabilities",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_liability_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_liabilities", x => new { x.company_id, x.type_liability_id });
                    table.ForeignKey(
                        name: "fk_company_liabilities_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_company_liabilities_type_liabilities",
                        column: x => x.type_liability_id,
                        principalTable: "type_liabilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "company_logos",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_logos", x => x.company_id);
                    table.ForeignKey(
                        name: "fk_company_logos_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "company_mail_settings",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    use_platform_default = table.Column<bool>(type: "boolean", nullable: false),
                    host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    port = table.Column<int>(type: "integer", nullable: true),
                    username = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    password_cipher = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    encryption = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    from_address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    from_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_mail_settings", x => x.company_id);
                    table.ForeignKey(
                        name: "fk_company_mail_settings_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "company_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<int>(type: "integer", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_plans", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_plans_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_company_plans_plans",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "company_software",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_kind = table.Column<short>(type: "smallint", nullable: false),
                    type_environment_id = table.Column<int>(type: "integer", nullable: false),
                    software_identifier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pin_cipher = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    dian_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    test_set_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_software", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_software_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_company_software_type_environments",
                        column: x => x.type_environment_id,
                        principalTable: "type_environments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identification_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dv = table.Column<string>(type: "character(1)", fixedLength: true, maxLength: 1, nullable: true),
                    type_document_identification_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    municipality_id = table.Column<int>(type: "integer", nullable: true),
                    type_organization_id = table.Column<int>(type: "integer", nullable: true),
                    type_regime_id = table.Column<int>(type: "integer", nullable: true),
                    type_liability_id = table.Column<int>(type: "integer", nullable: true),
                    merchant_registration = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                    table.UniqueConstraint("ux_customers_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "fk_customers_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customers_municipalities",
                        column: x => x.municipality_id,
                        principalTable: "municipalities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customers_type_document_identifications",
                        column: x => x.type_document_identification_id,
                        principalTable: "type_document_identifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customers_type_liabilities",
                        column: x => x.type_liability_id,
                        principalTable: "type_liabilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customers_type_organizations",
                        column: x => x.type_organization_id,
                        principalTable: "type_organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customers_type_regimes",
                        column: x => x.type_regime_id,
                        principalTable: "type_regimes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    response_status = table.Column<int>(type: "integer", nullable: false),
                    response_body = table.Column<string>(type: "jsonb", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_idempotency_keys_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    unit_measure_id = table.Column<int>(type: "integer", nullable: false),
                    type_item_identification_id = table.Column<int>(type: "integer", nullable: false),
                    sale_unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    tax_id = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_items_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_items_taxes",
                        column: x => x.tax_id,
                        principalTable: "taxes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_items_type_item_identifications",
                        column: x => x.type_item_identification_id,
                        principalTable: "type_item_identifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_items_unit_measures",
                        column: x => x.unit_measure_id,
                        principalTable: "unit_measures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notifications_users",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "numbering_sequences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    next_number = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_numbering_sequences", x => x.id);
                    table.ForeignKey(
                        name: "fk_numbering_sequences_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "received_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_identification_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    supplier_dv = table.Column<string>(type: "character(1)", fixedLength: true, maxLength: 1, nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    document_type_id = table.Column<int>(type: "integer", nullable: false),
                    prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: ""),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cufe = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    type_environment_id = table.Column<int>(type: "integer", nullable: false),
                    line_extension_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_exclusive_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    allowance_total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payable_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    xml_file_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    pdf_file_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_received_documents", x => x.id);
                    table.UniqueConstraint("ux_received_documents_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "fk_received_documents_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_received_documents_type_documents",
                        column: x => x.document_type_id,
                        principalTable: "type_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_received_documents_type_environments",
                        column: x => x.type_environment_id,
                        principalTable: "type_environments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "resolutions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type_id = table.Column<int>(type: "integer", nullable: false),
                    type_environment_id = table.Column<int>(type: "integer", nullable: false),
                    prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    resolution_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    resolution_date = table.Column<DateOnly>(type: "date", nullable: true),
                    technical_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    range_from = table.Column<long>(type: "bigint", nullable: false),
                    range_to = table.Column<long>(type: "bigint", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resolutions", x => x.id);
                    table.UniqueConstraint("ux_resolutions_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_resolutions_range", "range_from <= range_to");
                    table.ForeignKey(
                        name: "fk_resolutions_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_resolutions_type_documents",
                        column: x => x.document_type_id,
                        principalTable: "type_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_resolutions_type_environments",
                        column: x => x.type_environment_id,
                        principalTable: "type_environments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_usage",
                columns: table => new
                {
                    company_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_kind = table.Column<short>(type: "smallint", nullable: false),
                    used = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_usage", x => new { x.company_plan_id, x.document_kind });
                    table.ForeignKey(
                        name: "fk_plan_usage_company_plans",
                        column: x => x.company_plan_id,
                        principalTable: "company_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_companies", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_companies_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_companies_customers",
                        columns: x => new { x.company_id, x.customer_id },
                        principalTable: "customers",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_companies_roles",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_companies_users",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type_id = table.Column<int>(type: "integer", nullable: false),
                    type_environment_id = table.Column<int>(type: "integer", nullable: false),
                    resolution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: ""),
                    number = table.Column<long>(type: "bigint", nullable: false),
                    full_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    cufe = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    uuid_algorithm = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    issue_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    currency_id = table.Column<int>(type: "integer", nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    type_operation_id = table.Column<int>(type: "integer", nullable: true),
                    line_extension_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_exclusive_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_inclusive_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    allowance_total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    charge_total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    prepaid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    payable_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reference_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    discrepancy_code = table.Column<int>(type: "integer", nullable: true),
                    discrepancy_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    head_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    foot_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    track_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    dian_status_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    dian_status_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    email_status = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    email_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pdf_template_id = table.Column<int>(type: "integer", nullable: true),
                    extensions = table.Column<string>(type: "jsonb", nullable: true),
                    created_by_type = table.Column<short>(type: "smallint", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                    table.UniqueConstraint("ux_documents_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "fk_documents_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documents_customers",
                        columns: x => new { x.company_id, x.customer_id },
                        principalTable: "customers",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documents_documents",
                        columns: x => new { x.company_id, x.reference_document_id },
                        principalTable: "documents",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documents_resolutions",
                        columns: x => new { x.company_id, x.resolution_id },
                        principalTable: "resolutions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documents_type_currencies",
                        column: x => x.currency_id,
                        principalTable: "type_currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documents_type_documents",
                        column: x => x.document_type_id,
                        principalTable: "type_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documents_type_environments",
                        column: x => x.type_environment_id,
                        principalTable: "type_environments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documents_type_operations",
                        column: x => x.type_operation_id,
                        principalTable: "type_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    storage_provider = table.Column<short>(type: "smallint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_files", x => x.id);
                    table.UniqueConstraint("ux_document_files_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "fk_document_files_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_files_documents",
                        columns: x => new { x.company_id, x.document_id },
                        principalTable: "documents",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    type_item_identification_id = table.Column<int>(type: "integer", nullable: false),
                    unit_measure_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    base_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    line_extension_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    free_of_charge = table.Column<bool>(type: "boolean", nullable: false),
                    reference_price_id = table.Column<int>(type: "integer", nullable: true),
                    extensions = table.Column<string>(type: "jsonb", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_lines", x => x.id);
                    table.UniqueConstraint("ux_document_lines_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "fk_document_lines_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_lines_documents",
                        columns: x => new { x.company_id, x.document_id },
                        principalTable: "documents",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_lines_reference_prices",
                        column: x => x.reference_price_id,
                        principalTable: "reference_prices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_lines_type_item_identifications",
                        column: x => x.type_item_identification_id,
                        principalTable: "type_item_identifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_lines_unit_measures",
                        column: x => x.unit_measure_id,
                        principalTable: "unit_measures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_form_id = table.Column<int>(type: "integer", nullable: false),
                    payment_method_id = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    duration_measure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_payments_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_payments_documents",
                        columns: x => new { x.company_id, x.document_id },
                        principalTable: "documents",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_payments_payment_forms",
                        column: x => x.payment_form_id,
                        principalTable: "payment_forms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_payments_payment_methods",
                        column: x => x.payment_method_id,
                        principalTable: "payment_methods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_references",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    uuid = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_references", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_references_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_references_documents",
                        columns: x => new { x.company_id, x.document_id },
                        principalTable: "documents",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_taxes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_id = table.Column<int>(type: "integer", nullable: false),
                    percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    taxable_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_taxes", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_taxes_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_taxes_documents",
                        columns: x => new { x.company_id, x.document_id },
                        principalTable: "documents",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_taxes_taxes",
                        column: x => x.tax_id,
                        principalTable: "taxes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dian_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    operation = table.Column<short>(type: "smallint", nullable: false),
                    type_environment_id = table.Column<int>(type: "integer", nullable: false),
                    track_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    zip_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    dian_status_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    dian_status_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_valid = table.Column<bool>(type: "boolean", nullable: true),
                    error_messages = table.Column<string>(type: "jsonb", nullable: true),
                    request_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    response_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dian_submissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_dian_submissions_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dian_submissions_document_files_company_id_request_file_id",
                        columns: x => new { x.company_id, x.request_file_id },
                        principalTable: "document_files",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dian_submissions_document_files_company_id_response_file_id",
                        columns: x => new { x.company_id, x.response_file_id },
                        principalTable: "document_files",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dian_submissions_documents",
                        columns: x => new { x.company_id, x.document_id },
                        principalTable: "documents",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dian_submissions_type_environments",
                        column: x => x.type_environment_id,
                        principalTable: "type_environments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_id = table.Column<int>(type: "integer", nullable: false),
                    cude = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    track_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    dian_status_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    payload_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_events", x => x.id);
                    table.CheckConstraint("ck_document_events_target", "(document_id IS NOT NULL AND received_document_id IS NULL) OR (document_id IS NULL AND received_document_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_document_events_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_events_document_files",
                        columns: x => new { x.company_id, x.payload_file_id },
                        principalTable: "document_files",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_events_documents",
                        columns: x => new { x.company_id, x.document_id },
                        principalTable: "documents",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_events_events",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_events_received_documents",
                        columns: x => new { x.company_id, x.received_document_id },
                        principalTable: "received_documents",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_allowance_charges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    charge_indicator = table.Column<bool>(type: "boolean", nullable: false),
                    discount_id = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    base_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    multiplier_factor = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_allowance_charges", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_allowance_charges_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_allowance_charges_discounts",
                        column: x => x.discount_id,
                        principalTable: "discounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_allowance_charges_document_lines",
                        columns: x => new { x.company_id, x.line_id },
                        principalTable: "document_lines",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_allowance_charges_documents",
                        columns: x => new { x.company_id, x.document_id },
                        principalTable: "documents",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_line_taxes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_id = table.Column<int>(type: "integer", nullable: false),
                    percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    taxable_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    per_unit_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    unit_measure_id = table.Column<int>(type: "integer", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_line_taxes", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_line_taxes_companies",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_line_taxes_document_lines",
                        columns: x => new { x.company_id, x.line_id },
                        principalTable: "document_lines",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_line_taxes_documents",
                        columns: x => new { x.company_id, x.document_id },
                        principalTable: "documents",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_line_taxes_taxes",
                        column: x => x.tax_id,
                        principalTable: "taxes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_line_taxes_unit_measures",
                        column: x => x.unit_measure_id,
                        principalTable: "unit_measures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "name" },
                values: new object[,]
                {
                    { new Guid("0193a000-0000-7000-8000-000000000001"), "platform_admin" },
                    { new Guid("0193a000-0000-7000-8000-000000000002"), "reseller" },
                    { new Guid("0193a000-0000-7000-8000-000000000003"), "owner" },
                    { new Guid("0193a000-0000-7000-8000-000000000004"), "seller" },
                    { new Guid("0193a000-0000-7000-8000-000000000005"), "customer" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_company_id_revoked_at",
                table: "api_keys",
                columns: new[] { "company_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_created_by",
                table: "api_keys",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ux_api_keys_key_hash",
                table: "api_keys",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_company_id_created_at",
                table: "audit_logs",
                columns: new[] { "company_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_type_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_certificates_not_after",
                table: "certificates",
                column: "not_after");

            migrationBuilder.CreateIndex(
                name: "ix_certificates_uploaded_by",
                table: "certificates",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ux_certificates_active",
                table: "certificates",
                column: "company_id",
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_companies_country_id",
                table: "companies",
                column: "country_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_language_id",
                table: "companies",
                column: "language_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_municipality_id",
                table: "companies",
                column: "municipality_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_reseller_id",
                table: "companies",
                column: "reseller_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_tax_id",
                table: "companies",
                column: "tax_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_type_currency_id",
                table: "companies",
                column: "type_currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_type_document_identification_id",
                table: "companies",
                column: "type_document_identification_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_type_operation_id",
                table: "companies",
                column: "type_operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_type_organization_id",
                table: "companies",
                column: "type_organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_type_regime_id",
                table: "companies",
                column: "type_regime_id");

            migrationBuilder.CreateIndex(
                name: "ux_companies_identification_number",
                table: "companies",
                column: "identification_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_environments_type_environment_id",
                table: "company_environments",
                column: "type_environment_id");

            migrationBuilder.CreateIndex(
                name: "ux_company_environments_company_id_document_kind",
                table: "company_environments",
                columns: new[] { "company_id", "document_kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_liabilities_type_liability_id",
                table: "company_liabilities",
                column: "type_liability_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_plans_company_id_is_active",
                table: "company_plans",
                columns: new[] { "company_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_company_plans_plan_id",
                table: "company_plans",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_software_type_environment_id",
                table: "company_software",
                column: "type_environment_id");

            migrationBuilder.CreateIndex(
                name: "ux_company_software_company_id_document_kind_type_envi_c57d91e7",
                table: "company_software",
                columns: new[] { "company_id", "document_kind", "type_environment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_countries_code",
                table: "countries",
                column: "code");

            migrationBuilder.CreateIndex(
                name: "ux_credit_note_discrepancy_responses_code",
                table: "credit_note_discrepancy_responses",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_credit_note_discrepancy_responses_sd_code",
                table: "credit_note_discrepancy_responses_sd",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_municipality_id",
                table: "customers",
                column: "municipality_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_type_document_identification_id",
                table: "customers",
                column: "type_document_identification_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_type_liability_id",
                table: "customers",
                column: "type_liability_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_type_organization_id",
                table: "customers",
                column: "type_organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_type_regime_id",
                table: "customers",
                column: "type_regime_id");

            migrationBuilder.CreateIndex(
                name: "ux_customers_company_id_type_document_identification_i_29d267d2",
                table: "customers",
                columns: new[] { "company_id", "type_document_identification_id", "identification_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_debit_note_discrepancy_responses_code",
                table: "debit_note_discrepancy_responses",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_departments_country_id",
                table: "departments",
                column: "country_id");

            migrationBuilder.CreateIndex(
                name: "ux_departments_code",
                table: "departments",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dian_submissions_company_id_document_id",
                table: "dian_submissions",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dian_submissions_company_id_request_file_id",
                table: "dian_submissions",
                columns: new[] { "company_id", "request_file_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dian_submissions_company_id_response_file_id",
                table: "dian_submissions",
                columns: new[] { "company_id", "response_file_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dian_submissions_track_id",
                table: "dian_submissions",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_dian_submissions_type_environment_id",
                table: "dian_submissions",
                column: "type_environment_id");

            migrationBuilder.CreateIndex(
                name: "ux_dian_submissions_document_id_attempt",
                table: "dian_submissions",
                columns: new[] { "document_id", "attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_discounts_code",
                table: "discounts",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_allowance_charges_company_id_document_id",
                table: "document_allowance_charges",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_allowance_charges_company_id_line_id",
                table: "document_allowance_charges",
                columns: new[] { "company_id", "line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_allowance_charges_discount_id",
                table: "document_allowance_charges",
                column: "discount_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_events_company_id_document_id",
                table: "document_events",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_events_company_id_payload_file_id",
                table: "document_events",
                columns: new[] { "company_id", "payload_file_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_events_company_id_received_document_id",
                table: "document_events",
                columns: new[] { "company_id", "received_document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_events_document_id",
                table: "document_events",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_events_event_id",
                table: "document_events",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_events_received_document_id",
                table: "document_events",
                column: "received_document_id");

            migrationBuilder.CreateIndex(
                name: "ux_document_events_company_id_cude",
                table: "document_events",
                columns: new[] { "company_id", "cude" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_files_company_id_document_id",
                table: "document_files",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ux_document_files_document_id_kind_revision",
                table: "document_files",
                columns: new[] { "document_id", "kind", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_line_taxes_company_id_document_id",
                table: "document_line_taxes",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_line_taxes_company_id_line_id",
                table: "document_line_taxes",
                columns: new[] { "company_id", "line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_line_taxes_line_id",
                table: "document_line_taxes",
                column: "line_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_line_taxes_tax_id",
                table: "document_line_taxes",
                column: "tax_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_line_taxes_unit_measure_id",
                table: "document_line_taxes",
                column: "unit_measure_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_lines_company_id_document_id",
                table: "document_lines",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_lines_reference_price_id",
                table: "document_lines",
                column: "reference_price_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_lines_type_item_identification_id",
                table: "document_lines",
                column: "type_item_identification_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_lines_unit_measure_id",
                table: "document_lines",
                column: "unit_measure_id");

            migrationBuilder.CreateIndex(
                name: "ux_document_lines_document_id_line_number",
                table: "document_lines",
                columns: new[] { "document_id", "line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_payments_company_id_document_id",
                table: "document_payments",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_payments_payment_form_id",
                table: "document_payments",
                column: "payment_form_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_payments_payment_method_id",
                table: "document_payments",
                column: "payment_method_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_references_company_id_document_id",
                table: "document_references",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_taxes_company_id_document_id",
                table: "document_taxes",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_taxes_document_id",
                table: "document_taxes",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_taxes_tax_id",
                table: "document_taxes",
                column: "tax_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_company_id_reference_document_id",
                table: "documents",
                columns: new[] { "company_id", "reference_document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_company_id_resolution_id",
                table: "documents",
                columns: new[] { "company_id", "resolution_id" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_company_issued",
                table: "documents",
                columns: new[] { "company_id", "issued_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_documents_company_status",
                table: "documents",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_currency_id",
                table: "documents",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_customer",
                table: "documents",
                columns: new[] { "company_id", "customer_id", "issued_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_documents_document_type_id",
                table: "documents",
                column: "document_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_track",
                table: "documents",
                column: "track_id",
                filter: "track_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_documents_type_environment_id",
                table: "documents",
                column: "type_environment_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_type_operation_id",
                table: "documents",
                column: "type_operation_id");

            migrationBuilder.CreateIndex(
                name: "ux_documents_cufe",
                table: "documents",
                columns: new[] { "company_id", "cufe" },
                unique: true,
                filter: "cufe IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_documents_number",
                table: "documents",
                columns: new[] { "company_id", "document_type_id", "type_environment_id", "prefix", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_events_code",
                table: "events",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_expires_at",
                table: "idempotency_keys",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_idempotency_keys_company_id_key",
                table: "idempotency_keys",
                columns: new[] { "company_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_incoterms_code",
                table: "incoterms",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_tax_id",
                table: "items",
                column: "tax_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_type_item_identification_id",
                table: "items",
                column: "type_item_identification_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_unit_measure_id",
                table: "items",
                column: "unit_measure_id");

            migrationBuilder.CreateIndex(
                name: "ux_items_company_id_code",
                table: "items",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_languages_code",
                table: "languages",
                column: "code");

            migrationBuilder.CreateIndex(
                name: "ux_municipalities_department_id_code",
                table: "municipalities",
                columns: new[] { "department_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_company_id_user_id_is_read_created_at",
                table: "notifications",
                columns: new[] { "company_id", "user_id", "is_read", "created_at" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id",
                table: "notifications",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_numbering_sequences_company_id_scope_key",
                table: "numbering_sequences",
                columns: new[] { "company_id", "scope_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_status_next_attempt_at",
                table: "outbox_messages",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ux_payment_forms_code",
                table: "payment_forms",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_methods_code",
                table: "payment_methods",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_plans_name",
                table: "plans",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_prepaid_payment_types_code",
                table: "prepaid_payment_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_received_documents_company_id_issue_date",
                table: "received_documents",
                columns: new[] { "company_id", "issue_date" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_received_documents_company_id_supplier_identification_number",
                table: "received_documents",
                columns: new[] { "company_id", "supplier_identification_number" });

            migrationBuilder.CreateIndex(
                name: "ix_received_documents_document_type_id",
                table: "received_documents",
                column: "document_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_received_documents_type_environment_id",
                table: "received_documents",
                column: "type_environment_id");

            migrationBuilder.CreateIndex(
                name: "ux_received_documents_company_id_cufe",
                table: "received_documents",
                columns: new[] { "company_id", "cufe" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_reference_prices_code",
                table: "reference_prices",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_replaced_by",
                table: "refresh_tokens",
                column: "replaced_by");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_resellers_email",
                table: "resellers",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_resellers_identification_number",
                table: "resellers",
                column: "identification_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_resolutions_company_id_document_type_id_type_enviro_eb5d2c4b",
                table: "resolutions",
                columns: new[] { "company_id", "document_type_id", "type_environment_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_resolutions_document_type_id",
                table: "resolutions",
                column: "document_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_resolutions_type_environment_id",
                table: "resolutions",
                column: "type_environment_id");

            migrationBuilder.CreateIndex(
                name: "ux_roles_name",
                table: "roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_taxes_code",
                table: "taxes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_type_currencies_code",
                table: "type_currencies",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_type_discounts_code",
                table: "type_discounts",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_type_document_identifications_code",
                table: "type_document_identifications",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_type_documents_code",
                table: "type_documents",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_type_environments_code",
                table: "type_environments",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_type_generation_transmitions_code",
                table: "type_generation_transmitions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_type_item_identifications_code",
                table: "type_item_identifications",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_type_liabilities_code",
                table: "type_liabilities",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_type_operations_code",
                table: "type_operations",
                column: "code");

            migrationBuilder.CreateIndex(
                name: "ux_type_organizations_code",
                table: "type_organizations",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_type_regimes_code",
                table: "type_regimes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_type_rejections_code",
                table: "type_rejections",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_type_spds_code",
                table: "type_spds",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unit_measures_code",
                table: "unit_measures",
                column: "code");

            migrationBuilder.CreateIndex(
                name: "ix_user_companies_company_id_customer_id",
                table: "user_companies",
                columns: new[] { "company_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_companies_role_id",
                table: "user_companies",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ux_user_companies_user_id_company_id_role_id",
                table: "user_companies",
                columns: new[] { "user_id", "company_id", "role_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_reseller_id",
                table: "users",
                column: "reseller_id");

            migrationBuilder.CreateIndex(
                name: "ux_users_email",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "certificates");

            migrationBuilder.DropTable(
                name: "company_environments");

            migrationBuilder.DropTable(
                name: "company_liabilities");

            migrationBuilder.DropTable(
                name: "company_logos");

            migrationBuilder.DropTable(
                name: "company_mail_settings");

            migrationBuilder.DropTable(
                name: "company_software");

            migrationBuilder.DropTable(
                name: "credit_note_discrepancy_responses");

            migrationBuilder.DropTable(
                name: "credit_note_discrepancy_responses_sd");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "debit_note_discrepancy_responses");

            migrationBuilder.DropTable(
                name: "dian_submissions");

            migrationBuilder.DropTable(
                name: "document_allowance_charges");

            migrationBuilder.DropTable(
                name: "document_events");

            migrationBuilder.DropTable(
                name: "document_line_taxes");

            migrationBuilder.DropTable(
                name: "document_payments");

            migrationBuilder.DropTable(
                name: "document_references");

            migrationBuilder.DropTable(
                name: "document_taxes");

            migrationBuilder.DropTable(
                name: "idempotency_keys");

            migrationBuilder.DropTable(
                name: "incoterms");

            migrationBuilder.DropTable(
                name: "items");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "numbering_sequences");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "plan_quotas");

            migrationBuilder.DropTable(
                name: "plan_usage");

            migrationBuilder.DropTable(
                name: "prepaid_payment_types");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "type_discounts");

            migrationBuilder.DropTable(
                name: "type_generation_transmitions");

            migrationBuilder.DropTable(
                name: "type_rejections");

            migrationBuilder.DropTable(
                name: "type_spds");

            migrationBuilder.DropTable(
                name: "user_companies");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "discounts");

            migrationBuilder.DropTable(
                name: "document_files");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "received_documents");

            migrationBuilder.DropTable(
                name: "document_lines");

            migrationBuilder.DropTable(
                name: "payment_forms");

            migrationBuilder.DropTable(
                name: "payment_methods");

            migrationBuilder.DropTable(
                name: "company_plans");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "reference_prices");

            migrationBuilder.DropTable(
                name: "type_item_identifications");

            migrationBuilder.DropTable(
                name: "unit_measures");

            migrationBuilder.DropTable(
                name: "plans");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "resolutions");

            migrationBuilder.DropTable(
                name: "type_liabilities");

            migrationBuilder.DropTable(
                name: "companies");

            migrationBuilder.DropTable(
                name: "type_documents");

            migrationBuilder.DropTable(
                name: "type_environments");

            migrationBuilder.DropTable(
                name: "languages");

            migrationBuilder.DropTable(
                name: "municipalities");

            migrationBuilder.DropTable(
                name: "resellers");

            migrationBuilder.DropTable(
                name: "taxes");

            migrationBuilder.DropTable(
                name: "type_currencies");

            migrationBuilder.DropTable(
                name: "type_document_identifications");

            migrationBuilder.DropTable(
                name: "type_operations");

            migrationBuilder.DropTable(
                name: "type_organizations");

            migrationBuilder.DropTable(
                name: "type_regimes");

            migrationBuilder.DropTable(
                name: "departments");

            migrationBuilder.DropTable(
                name: "countries");
        }
    }
}
