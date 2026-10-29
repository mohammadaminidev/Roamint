using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roamint.Expenses.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaidBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "paid_by",
                schema: "expenses",
                table: "expenses",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "paid_by",
                schema: "expenses",
                table: "expenses");
        }
    }
}
