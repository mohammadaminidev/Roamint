using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Roamint.Expenses.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "item_mappings",
                schema: "expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    canonical_description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_used_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1024)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_mappings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_item_mappings_source_text",
                schema: "expenses",
                table: "item_mappings",
                column: "source_text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_mappings",
                schema: "expenses");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
