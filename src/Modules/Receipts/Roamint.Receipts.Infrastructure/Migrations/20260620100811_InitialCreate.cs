using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roamint.Receipts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "receipts");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "receipts",
                schema: "receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    draft = table.Column<string>(type: "jsonb", nullable: true),
                    linked_expense_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    parsed_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_unprocessed",
                schema: "receipts",
                table: "outbox_messages",
                column: "processed_on_utc",
                filter: "processed_on_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "receipts");

            migrationBuilder.DropTable(
                name: "receipts",
                schema: "receipts");
        }
    }
}
