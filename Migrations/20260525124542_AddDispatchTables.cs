using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Syntera.WMS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DispatchHeader",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DispatchNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DriverName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VehicleNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchHeader", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DispatchDetail",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DispatchHeaderId = table.Column<int>(type: "int", nullable: false),
                    PickingDetailId = table.Column<int>(type: "int", nullable: false),
                    SKUId = table.Column<int>(type: "int", nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    StagingBinCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PalletId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchDetail", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DispatchDetail_DispatchHeader_DispatchHeaderId",
                        column: x => x.DispatchHeaderId,
                        principalTable: "DispatchHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DispatchDetail_MasterSKU_SKUId",
                        column: x => x.SKUId,
                        principalTable: "MasterSKU",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DispatchDetail_PickingDetail_PickingDetailId",
                        column: x => x.PickingDetailId,
                        principalTable: "PickingDetail",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DispatchDetail_DispatchHeaderId",
                table: "DispatchDetail",
                column: "DispatchHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchDetail_PickingDetailId",
                table: "DispatchDetail",
                column: "PickingDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchDetail_SKUId",
                table: "DispatchDetail",
                column: "SKUId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DispatchDetail");

            migrationBuilder.DropTable(
                name: "DispatchHeader");
        }
    }
}
