using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Syntera.WMS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddQCGateToReceivingDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "QCCheckedAt",
                table: "ReceivingDetail",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QCCheckedBy",
                table: "ReceivingDetail",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QCRemarks",
                table: "ReceivingDetail",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QCStatus",
                table: "ReceivingDetail",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Pending");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QCCheckedAt",
                table: "ReceivingDetail");

            migrationBuilder.DropColumn(
                name: "QCCheckedBy",
                table: "ReceivingDetail");

            migrationBuilder.DropColumn(
                name: "QCRemarks",
                table: "ReceivingDetail");

            migrationBuilder.DropColumn(
                name: "QCStatus",
                table: "ReceivingDetail");
        }
    }
}
