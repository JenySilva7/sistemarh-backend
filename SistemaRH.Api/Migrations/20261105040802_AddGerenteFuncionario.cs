using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaRH.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGerenteFuncionario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GerenteId",
                table: "Funcionarios",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Funcionarios_GerenteId",
                table: "Funcionarios",
                column: "GerenteId");

            migrationBuilder.AddForeignKey(
                name: "FK_Funcionarios_Funcionarios_GerenteId",
                table: "Funcionarios",
                column: "GerenteId",
                principalTable: "Funcionarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Funcionarios_Funcionarios_GerenteId",
                table: "Funcionarios");

            migrationBuilder.DropIndex(
                name: "IX_Funcionarios_GerenteId",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "GerenteId",
                table: "Funcionarios");
        }
    }
}
