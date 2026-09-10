using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsResourcePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResourcePool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmployeeType",
                table: "People");

            migrationBuilder.AddColumn<int>(
                name: "ResourcePoolId",
                table: "ResourcePlanLines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResourcePoolId",
                table: "People",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ResourcePools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProgramId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    CostCenter = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AverageRate = table.Column<decimal>(type: "decimal(9,2)", nullable: false),
                    Vendor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourcePools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourcePools_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourcePlanLines_ResourcePoolId",
                table: "ResourcePlanLines",
                column: "ResourcePoolId");

            migrationBuilder.CreateIndex(
                name: "IX_People_ResourcePoolId",
                table: "People",
                column: "ResourcePoolId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourcePools_ProgramId",
                table: "ResourcePools",
                column: "ProgramId");

            migrationBuilder.AddForeignKey(
                name: "FK_People_ResourcePools_ResourcePoolId",
                table: "People",
                column: "ResourcePoolId",
                principalTable: "ResourcePools",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ResourcePlanLines_ResourcePools_ResourcePoolId",
                table: "ResourcePlanLines",
                column: "ResourcePoolId",
                principalTable: "ResourcePools",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_People_ResourcePools_ResourcePoolId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_ResourcePlanLines_ResourcePools_ResourcePoolId",
                table: "ResourcePlanLines");

            migrationBuilder.DropTable(
                name: "ResourcePools");

            migrationBuilder.DropIndex(
                name: "IX_ResourcePlanLines_ResourcePoolId",
                table: "ResourcePlanLines");

            migrationBuilder.DropIndex(
                name: "IX_People_ResourcePoolId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "ResourcePoolId",
                table: "ResourcePlanLines");

            migrationBuilder.DropColumn(
                name: "ResourcePoolId",
                table: "People");

            migrationBuilder.AddColumn<string>(
                name: "EmployeeType",
                table: "People",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
