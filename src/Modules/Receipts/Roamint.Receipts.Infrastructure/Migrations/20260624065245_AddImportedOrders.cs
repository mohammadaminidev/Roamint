using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roamint.Receipts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportedOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "imported_orders",
                schema: "receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    external_order_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    draft = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    linked_expense_id = table.Column<Guid>(type: "uuid", nullable: true),
                    imported_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_imported_orders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_imported_orders_source_external_id",
                schema: "receipts",
                table: "imported_orders",
                columns: new[] { "source", "external_order_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "imported_orders",
                schema: "receipts");
        }
    }
}
