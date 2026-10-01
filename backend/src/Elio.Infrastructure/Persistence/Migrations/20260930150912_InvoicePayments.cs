using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoicePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoicePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicePayments", x => x.Id);
                    table.CheckConstraint("CK_InvoicePayments_Amount", "\"Amount\" > 0 AND \"Amount\" <= 999999999999.99");
                    table.CheckConstraint("CK_InvoicePayments_Currency", "\"Currency\" IN ('PHP', 'USD')");
                    table.CheckConstraint("CK_InvoicePayments_Method", "\"Method\" IN ('BankTransfer', 'Cash', 'Check', 'Card', 'EWallet', 'Other')");
                    table.ForeignKey(
                        name: "FK_InvoicePayments_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoicePayments_Invoices_OrganizationId_InvoiceId",
                        columns: x => new { x.OrganizationId, x.InvoiceId },
                        principalTable: "Invoices",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoicePayments_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_CreatedBy",
                table: "InvoicePayments",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_OrganizationId_InvoiceId_ReceivedAtUtc",
                table: "InvoicePayments",
                columns: new[] { "OrganizationId", "InvoiceId", "ReceivedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoicePayments");
        }
    }
}
