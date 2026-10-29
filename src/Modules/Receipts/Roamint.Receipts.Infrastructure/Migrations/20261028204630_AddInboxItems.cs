using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roamint.Receipts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_items",
                schema: "receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sender_external_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    text = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    sent_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    draft = table.Column<string>(type: "jsonb", nullable: true),
                    linked_expense_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_inbox_items_source_external_id",
                schema: "receipts",
                table: "inbox_items",
                columns: new[] { "source", "external_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_items",
                schema: "receipts");
        }
    }
}
