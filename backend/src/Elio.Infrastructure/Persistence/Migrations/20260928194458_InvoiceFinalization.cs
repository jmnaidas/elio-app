using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceFinalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FinalizedAtUtc",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalizedSubtotal",
                table: "Invoices",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalizedTotal",
                table: "Invoices",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "Invoices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IssuedBillingAddress",
                table: "Invoices",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IssuedClientEmail",
                table: "Invoices",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IssuedClientIsActive",
                table: "Invoices",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IssuedClientName",
                table: "Invoices",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IssuedClientPhone",
                table: "Invoices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerName",
                table: "Invoices",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerTimeZone",
                table: "Invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SequenceValue",
                table: "Invoices",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvoiceSequences",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastValue = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceSequences", x => x.OrganizationId);
                    table.CheckConstraint("CK_InvoiceSequences_Value", "\"LastValue\" > 0");
                    table.ForeignKey(
                        name: "FK_InvoiceSequences_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OrganizationId_InvoiceNumber",
                table: "Invoices",
                columns: new[] { "OrganizationId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OrganizationId_SequenceValue",
                table: "Invoices",
                columns: new[] { "OrganizationId", "SequenceValue" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_Issuance",
                table: "Invoices",
                sql: "(\"Lifecycle\" = 'Draft' AND \"SequenceValue\" IS NULL AND \"InvoiceNumber\" IS NULL AND \"FinalizedAtUtc\" IS NULL\n AND \"SellerName\" IS NULL AND \"SellerTimeZone\" IS NULL AND \"IssuedClientName\" IS NULL AND \"IssuedClientEmail\" IS NULL\n AND \"IssuedClientPhone\" IS NULL AND \"IssuedBillingAddress\" IS NULL AND \"IssuedClientIsActive\" IS NULL\n AND \"FinalizedSubtotal\" IS NULL AND \"FinalizedTotal\" IS NULL)\nOR (\"Lifecycle\" IN ('Finalized', 'Void') AND \"SequenceValue\" IS NOT NULL AND \"SequenceValue\" > 0\n AND \"InvoiceNumber\" IS NOT NULL AND \"InvoiceNumber\" = 'INV-' || lpad(\"SequenceValue\"::text, greatest(6, length(\"SequenceValue\"::text)), '0')\n AND \"FinalizedAtUtc\" IS NOT NULL AND \"SellerName\" IS NOT NULL AND \"SellerTimeZone\" IS NOT NULL\n AND \"IssuedClientName\" IS NOT NULL AND \"IssuedClientEmail\" IS NOT NULL AND \"IssuedClientIsActive\" IS NOT NULL\n AND \"FinalizedSubtotal\" IS NOT NULL AND \"FinalizedTotal\" IS NOT NULL\n AND \"FinalizedSubtotal\" >= 0 AND \"FinalizedTotal\" = \"FinalizedSubtotal\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceSequences");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_OrganizationId_InvoiceNumber",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_OrganizationId_SequenceValue",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Issuance",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FinalizedAtUtc",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FinalizedSubtotal",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FinalizedTotal",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IssuedBillingAddress",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IssuedClientEmail",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IssuedClientIsActive",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IssuedClientName",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IssuedClientPhone",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SellerName",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SellerTimeZone",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SequenceValue",
                table: "Invoices");
        }
    }
}
