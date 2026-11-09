using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IDepartamentoService
{
    Task<List<DepartamentoDto>> ListarAsync();
    Task<DepartamentoDto> CriarAsync(DepartamentoCreateDto dto);
    Task<DepartamentoDto> AtualizarAsync(int id, DepartamentoCreateDto dto);
}

/// <summary>CRUD simples dos departamentos (tabela de referência).</summary>
public class DepartamentoService : IDepartamentoService
{
    private readonly AppDbContext _db;

    public DepartamentoService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<DepartamentoDto>> ListarAsync()
    {
        var departamentos = await _db.Departamentos.OrderBy(d => d.Nome).ToListAsync();
        return departamentos.Select(ParaDto).ToList();
    }

    public async Task<DepartamentoDto> CriarAsync(DepartamentoCreateDto dto)
    {
        await GarantirNomeDisponivelAsync(dto.Nome, idAtual: null);

        var departamento = new Departamento
        {
            Nome = dto.Nome.Trim(),
            Descricao = dto.Descricao.Trim(),
            CriadoEm = DateTime.UtcNow,
        };

        _db.Departamentos.Add(departamento);
        await _db.SaveChangesAsync();

        return ParaDto(departamento);
    }

    public async Task<DepartamentoDto> AtualizarAsync(int id, DepartamentoCreateDto dto)
    {
        var departamento = await _db.Departamentos.FindAsync(id);
        if (departamento is null)
        {
            throw new NotFoundException("Departamento não encontrado.");
        }

        await GarantirNomeDisponivelAsync(dto.Nome, idAtual: id);

        departamento.Nome = dto.Nome.Trim();
        departamento.Descricao = dto.Descricao.Trim();
        await _db.SaveChangesAsync();

        return ParaDto(departamento);
    }

    private async Task GarantirNomeDisponivelAsync(string nome, int? idAtual)
    {
        var nomeNormalizado = nome.Trim();
        var jaExiste = await _db.Departamentos
            .AnyAsync(d => d.Nome == nomeNormalizado && d.Id != idAtual);

        if (jaExiste)
        {
            throw new ConflictException("Já existe um departamento com esse nome.");
        }
    }

    private static DepartamentoDto ParaDto(Departamento departamento) => new()
    {
        Id = departamento.Id,
        Nome = departamento.Nome,
        Descricao = departamento.Descricao,
    };
}
