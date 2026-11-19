using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Common;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface ICargoService
{
    Task<List<CargoDto>> ListarAsync();
    Task<CargoDto> CriarAsync(CargoCreateDto dto);
    Task<CargoDto> AtualizarAsync(int id, CargoCreateDto dto);
}

/// <summary>CRUD simples dos cargos (tabela de referência).</summary>
public class CargoService : ICargoService
{
    private const string MensagemNomeDuplicado = "Já existe um cargo com esse nome.";

    private readonly AppDbContext _db;

    public CargoService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<CargoDto>> ListarAsync()
    {
        var cargos = await _db.Cargos.OrderBy(c => c.Nome).ThenBy(c => c.Id).ToListAsync();
        return cargos.Select(ParaDto).ToList();
    }

    public async Task<CargoDto> CriarAsync(CargoCreateDto dto)
    {
        var (nome, salarioBase, descricao) = Validar(dto);
        await GarantirNomeDisponivelAsync(nome, idAtual: null);

        var cargo = new Cargo
        {
            Nome = nome,
            SalarioBase = salarioBase,
            Descricao = descricao,
        };

        _db.Cargos.Add(cargo);
        await SalvarAsync();

        return ParaDto(cargo);
    }

    public async Task<CargoDto> AtualizarAsync(int id, CargoCreateDto dto)
    {
        var cargo = await _db.Cargos.FindAsync(id);
        if (cargo is null)
        {
            throw new NotFoundException("Cargo não encontrado.");
        }

        var (nome, salarioBase, descricao) = Validar(dto);
        await GarantirNomeDisponivelAsync(nome, idAtual: id);

        cargo.Nome = nome;
        cargo.SalarioBase = salarioBase;
        cargo.Descricao = descricao;
        await SalvarAsync();

        return ParaDto(cargo);
    }

    private static (string Nome, decimal SalarioBase, string Descricao) Validar(CargoCreateDto dto)
    {
        var nome = Textos.Validar(dto.Nome, Textos.NomeCargo);
        var salarioBase = Dinheiro.Validar(dto.SalarioBase ?? 0m, string.Empty, 0m, Dinheiro.SalarioMaximo,
            "O salário base precisa estar entre R$ 0,00 e R$ 1.000.000,00.", "o salário base");
        var descricao = Textos.ValidarOpcional(dto.Descricao, Textos.DescricaoCurta);
        return (nome, salarioBase, descricao);
    }

    private async Task GarantirNomeDisponivelAsync(string nome, int? idAtual)
    {
        var jaExiste = await _db.Cargos
            .AnyAsync(c => c.Nome == nome && c.Id != idAtual);

        if (jaExiste)
        {
            throw new ConflictException(MensagemNomeDuplicado);
        }
    }

    /// <summary>Dois cadastros iguais ao mesmo tempo: o índice único segura o segundo, que vira 409 (antes 500).</summary>
    private async Task SalvarAsync()
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ErrosBanco.IndiceDuplicado(ex) is not null)
        {
            throw new ConflictException(MensagemNomeDuplicado);
        }
    }

    private static CargoDto ParaDto(Cargo cargo) => new()
    {
        Id = cargo.Id,
        Nome = cargo.Nome,
        SalarioBase = cargo.SalarioBase,
        Descricao = cargo.Descricao,
    };
}
