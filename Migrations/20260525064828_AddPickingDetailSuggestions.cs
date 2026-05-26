using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Syntera.WMS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPickingDetailSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SuggestedPalletId",
                table: "PickingDetail",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SuggestedRackId",
                table: "PickingDetail",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickingDetail_SuggestedRackId",
                table: "PickingDetail",
                column: "SuggestedRackId");

            migrationBuilder.AddForeignKey(
                name: "FK_PickingDetail_BinLocations_SuggestedRackId",
                table: "PickingDetail",
                column: "SuggestedRackId",
                principalTable: "BinLocations",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PickingDetail_BinLocations_SuggestedRackId",
                table: "PickingDetail");

            migrationBuilder.DropIndex(
                name: "IX_PickingDetail_SuggestedRackId",
                table: "PickingDetail");

            migrationBuilder.DropColumn(
                name: "SuggestedPalletId",
                table: "PickingDetail");

            migrationBuilder.DropColumn(
                name: "SuggestedRackId",
                table: "PickingDetail");
        }
    }
}
