using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaRH.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSalarioValeAlimentacaoEHistorico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Salario",
                table: "Funcionarios",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ValeAlimentacao",
                table: "Funcionarios",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Backfill dos funcionários que já existiam: o salário individual
            // começa igual ao salário base do cargo, e o vale alimentação no
            // valor padrão (FuncionarioService.ValeAlimentacaoPadrao). O 600.00
            // fica escrito aqui de propósito: a migration não pode mudar de
            // comportamento se a constante mudar depois.
            migrationBuilder.Sql(@"
UPDATE `Funcionarios` AS f
INNER JOIN `Cargos` AS c ON c.`Id` = f.`CargoId`
SET f.`Salario` = c.`SalarioBase`;");

            migrationBuilder.Sql("UPDATE `Funcionarios` SET `ValeAlimentacao` = 600.00;");

            migrationBuilder.CreateTable(
                name: "AjustesRemuneracao",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    FuncionarioId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    ValorAnterior = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ValorNovo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CargoAnteriorId = table.Column<int>(type: "int", nullable: true),
                    CargoNovoId = table.Column<int>(type: "int", nullable: true),
                    DepartamentoAnteriorId = table.Column<int>(type: "int", nullable: true),
                    DepartamentoNovoId = table.Column<int>(type: "int", nullable: true),
                    DataVigencia = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Motivo = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResponsavelUsuarioId = table.Column<int>(type: "int", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AjustesRemuneracao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AjustesRemuneracao_Cargos_CargoAnteriorId",
                        column: x => x.CargoAnteriorId,
                        principalTable: "Cargos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AjustesRemuneracao_Cargos_CargoNovoId",
                        column: x => x.CargoNovoId,
                        principalTable: "Cargos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AjustesRemuneracao_Departamentos_DepartamentoAnteriorId",
                        column: x => x.DepartamentoAnteriorId,
                        principalTable: "Departamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AjustesRemuneracao_Departamentos_DepartamentoNovoId",
                        column: x => x.DepartamentoNovoId,
                        principalTable: "Departamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AjustesRemuneracao_Funcionarios_FuncionarioId",
                        column: x => x.FuncionarioId,
                        principalTable: "Funcionarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AjustesRemuneracao_Usuarios_ResponsavelUsuarioId",
                        column: x => x.ResponsavelUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AjustesRemuneracao_CargoAnteriorId",
                table: "AjustesRemuneracao",
                column: "CargoAnteriorId");

            migrationBuilder.CreateIndex(
                name: "IX_AjustesRemuneracao_CargoNovoId",
                table: "AjustesRemuneracao",
                column: "CargoNovoId");

            migrationBuilder.CreateIndex(
                name: "IX_AjustesRemuneracao_DepartamentoAnteriorId",
                table: "AjustesRemuneracao",
                column: "DepartamentoAnteriorId");

            migrationBuilder.CreateIndex(
                name: "IX_AjustesRemuneracao_DepartamentoNovoId",
                table: "AjustesRemuneracao",
                column: "DepartamentoNovoId");

            migrationBuilder.CreateIndex(
                name: "IX_AjustesRemuneracao_FuncionarioId",
                table: "AjustesRemuneracao",
                column: "FuncionarioId");

            migrationBuilder.CreateIndex(
                name: "IX_AjustesRemuneracao_ResponsavelUsuarioId",
                table: "AjustesRemuneracao",
                column: "ResponsavelUsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AjustesRemuneracao");

            migrationBuilder.DropColumn(
                name: "Salario",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "ValeAlimentacao",
                table: "Funcionarios");
        }
    }
}
