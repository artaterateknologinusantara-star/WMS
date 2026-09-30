using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Syntera.WMS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddRequiresFEFOToCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresFEFO",
                table: "Categories",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresFEFO",
                table: "Categories");
        }
    }
}
