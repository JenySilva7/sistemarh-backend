using Microsoft.EntityFrameworkCore;
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
    private readonly AppDbContext _db;

    public CargoService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<CargoDto>> ListarAsync()
    {
        var cargos = await _db.Cargos.OrderBy(c => c.Nome).ToListAsync();
        return cargos.Select(ParaDto).ToList();
    }

    public async Task<CargoDto> CriarAsync(CargoCreateDto dto)
    {
        await GarantirNomeDisponivelAsync(dto.Nome, idAtual: null);

        var cargo = new Cargo
        {
            Nome = dto.Nome.Trim(),
            SalarioBase = dto.SalarioBase,
            Descricao = dto.Descricao.Trim(),
        };

        _db.Cargos.Add(cargo);
        await _db.SaveChangesAsync();

        return ParaDto(cargo);
    }

    public async Task<CargoDto> AtualizarAsync(int id, CargoCreateDto dto)
    {
        var cargo = await _db.Cargos.FindAsync(id);
        if (cargo is null)
        {
            throw new NotFoundException("Cargo não encontrado.");
        }

        await GarantirNomeDisponivelAsync(dto.Nome, idAtual: id);

        cargo.Nome = dto.Nome.Trim();
        cargo.SalarioBase = dto.SalarioBase;
        cargo.Descricao = dto.Descricao.Trim();
        await _db.SaveChangesAsync();

        return ParaDto(cargo);
    }

    private async Task GarantirNomeDisponivelAsync(string nome, int? idAtual)
    {
        var nomeNormalizado = nome.Trim();
        var jaExiste = await _db.Cargos
            .AnyAsync(c => c.Nome == nomeNormalizado && c.Id != idAtual);

        if (jaExiste)
        {
            throw new ConflictException("Já existe um cargo com esse nome.");
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
