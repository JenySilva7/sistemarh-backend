using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Common;
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
    private const string MensagemNomeDuplicado = "Já existe um departamento com esse nome.";

    private readonly AppDbContext _db;

    public DepartamentoService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<DepartamentoDto>> ListarAsync()
    {
        var departamentos = await _db.Departamentos.OrderBy(d => d.Nome).ThenBy(d => d.Id).ToListAsync();
        return departamentos.Select(ParaDto).ToList();
    }

    public async Task<DepartamentoDto> CriarAsync(DepartamentoCreateDto dto)
    {
        var nome = Textos.Validar(dto.Nome, Textos.NomeDepartamento);
        var descricao = Textos.ValidarOpcional(dto.Descricao, Textos.DescricaoCurta);
        await GarantirNomeDisponivelAsync(nome, idAtual: null);

        var departamento = new Departamento
        {
            Nome = nome,
            Descricao = descricao,
            CriadoEm = DateTime.UtcNow,
        };

        _db.Departamentos.Add(departamento);
        await SalvarAsync();

        return ParaDto(departamento);
    }

    public async Task<DepartamentoDto> AtualizarAsync(int id, DepartamentoCreateDto dto)
    {
        var departamento = await _db.Departamentos.FindAsync(id);
        if (departamento is null)
        {
            throw new NotFoundException("Departamento não encontrado.");
        }

        var nome = Textos.Validar(dto.Nome, Textos.NomeDepartamento);
        var descricao = Textos.ValidarOpcional(dto.Descricao, Textos.DescricaoCurta);
        await GarantirNomeDisponivelAsync(nome, idAtual: id);

        departamento.Nome = nome;
        departamento.Descricao = descricao;
        await SalvarAsync();

        return ParaDto(departamento);
    }

    private async Task GarantirNomeDisponivelAsync(string nome, int? idAtual)
    {
        var jaExiste = await _db.Departamentos
            .AnyAsync(d => d.Nome == nome && d.Id != idAtual);

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

    private static DepartamentoDto ParaDto(Departamento departamento) => new()
    {
        Id = departamento.Id,
        Nome = departamento.Nome,
        Descricao = departamento.Descricao,
    };
}
