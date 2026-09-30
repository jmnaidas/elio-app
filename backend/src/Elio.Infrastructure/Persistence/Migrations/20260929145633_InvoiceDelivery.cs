using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Invoices_OrganizationId_Id",
                table: "Invoices",
                columns: new[] { "OrganizationId", "Id" });

            migrationBuilder.CreateTable(
                name: "InvoiceDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AttemptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceDeliveries", x => x.Id);
                    table.CheckConstraint("CK_InvoiceDeliveries_State", "(\"Status\" = 'Pending' AND \"SentAtUtc\" IS NULL AND \"FailureCode\" IS NULL)\nOR (\"Status\" = 'Sent' AND \"SentAtUtc\" IS NOT NULL AND \"SentAtUtc\" >= \"AttemptedAtUtc\" AND \"FailureCode\" IS NULL)\nOR (\"Status\" = 'Failed' AND \"SentAtUtc\" IS NULL AND \"FailureCode\" IN ('provider_unavailable', 'delivery_failed', 'pdf_failed') AND \"FailureCode\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_InvoiceDeliveries_Invoices_OrganizationId_InvoiceId",
                        columns: x => new { x.OrganizationId, x.InvoiceId },
                        principalTable: "Invoices",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceDeliveries_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDeliveries_OrganizationId_InvoiceId",
                table: "InvoiceDeliveries",
                columns: new[] { "OrganizationId", "InvoiceId" },
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDeliveries_OrganizationId_InvoiceId_AttemptedAtUtc",
                table: "InvoiceDeliveries",
                columns: new[] { "OrganizationId", "InvoiceId", "AttemptedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceDeliveries");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Invoices_OrganizationId_Id",
                table: "Invoices");
        }
    }
}
