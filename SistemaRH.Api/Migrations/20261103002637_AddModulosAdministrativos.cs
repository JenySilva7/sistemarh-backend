using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaRH.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddModulosAdministrativos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Departamentos",
                keyColumn: "Id",
                keyValue: 1,
                column: "CriadoEm",
                value: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "Departamentos",
                keyColumn: "Id",
                keyValue: 2,
                column: "CriadoEm",
                value: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "Departamentos",
                keyColumn: "Id",
                keyValue: 3,
                column: "CriadoEm",
                value: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "Departamentos",
                keyColumn: "Id",
                keyValue: 4,
                column: "CriadoEm",
                value: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Departamentos",
                keyColumn: "Id",
                keyValue: 1,
                column: "CriadoEm",
                value: new DateTime(2026, 11, 2, 23, 45, 53, 757, DateTimeKind.Utc).AddTicks(3739));

            migrationBuilder.UpdateData(
                table: "Departamentos",
                keyColumn: "Id",
                keyValue: 2,
                column: "CriadoEm",
                value: new DateTime(2026, 11, 2, 23, 45, 53, 757, DateTimeKind.Utc).AddTicks(3742));

            migrationBuilder.UpdateData(
                table: "Departamentos",
                keyColumn: "Id",
                keyValue: 3,
                column: "CriadoEm",
                value: new DateTime(2026, 11, 2, 23, 45, 53, 757, DateTimeKind.Utc).AddTicks(3743));

            migrationBuilder.UpdateData(
                table: "Departamentos",
                keyColumn: "Id",
                keyValue: 4,
                column: "CriadoEm",
                value: new DateTime(2026, 11, 2, 23, 45, 53, 757, DateTimeKind.Utc).AddTicks(3744));
        }
    }
}
