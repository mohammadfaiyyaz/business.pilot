using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessPilot.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkInventoryToInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProductId",
                table: "InvoiceItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductSkuSnapshot",
                table: "InvoiceItems",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InvoiceId",
                table: "InventoryMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_ProductId",
                table: "InvoiceItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_InvoiceId",
                table: "InventoryMovements",
                column: "InvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Invoices_InvoiceId",
                table: "InventoryMovements",
                column: "InvoiceId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceItems_Products_ProductId",
                table: "InvoiceItems",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_Invoices_InvoiceId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceItems_Products_ProductId",
                table: "InvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceItems_ProductId",
                table: "InvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_InvoiceId",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "InvoiceItems");

            migrationBuilder.DropColumn(
                name: "ProductSkuSnapshot",
                table: "InvoiceItems");

            migrationBuilder.DropColumn(
                name: "InvoiceId",
                table: "InventoryMovements");
        }
    }
}
