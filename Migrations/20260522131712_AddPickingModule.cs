using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Syntera.WMS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPickingModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PickingHeader",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PickingNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssignedTo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingHeader", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PickingDetail",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PickingHeaderId = table.Column<int>(type: "int", nullable: false),
                    SKUId = table.Column<int>(type: "int", nullable: false),
                    InventoryStockId = table.Column<int>(type: "int", nullable: false),
                    RequestedQty = table.Column<int>(type: "int", nullable: false),
                    PickedQty = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingDetail", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PickingDetail_InventoryStock_InventoryStockId",
                        column: x => x.InventoryStockId,
                        principalTable: "InventoryStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingDetail_MasterSKU_SKUId",
                        column: x => x.SKUId,
                        principalTable: "MasterSKU",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickingDetail_PickingHeader_PickingHeaderId",
                        column: x => x.PickingHeaderId,
                        principalTable: "PickingHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PickingDetail_InventoryStockId",
                table: "PickingDetail",
                column: "InventoryStockId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingDetail_PickingHeaderId",
                table: "PickingDetail",
                column: "PickingHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_PickingDetail_SKUId",
                table: "PickingDetail",
                column: "SKUId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PickingDetail");

            migrationBuilder.DropTable(
                name: "PickingHeader");
        }
    }
}
