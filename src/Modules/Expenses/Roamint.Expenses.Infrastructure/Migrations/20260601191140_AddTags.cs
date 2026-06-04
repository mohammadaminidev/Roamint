using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roamint.Expenses.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expense_line_tags",
                schema: "expenses",
                columns: table => new
                {
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_line_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_line_tags", x => new { x.expense_line_id, x.tag_id });
                    table.ForeignKey(
                        name: "FK_expense_line_tags_expense_lines_expense_line_id",
                        column: x => x.expense_line_id,
                        principalSchema: "expenses",
                        principalTable: "expense_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                schema: "expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scope_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expense_line_tags",
                schema: "expenses");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "expenses");
        }
    }
}
