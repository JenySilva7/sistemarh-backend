using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Common;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IRecrutamentoService
{
    Task<List<RecrutamentoDto>> ListarAsync(StatusRecrutamento? status);
    Task<RecrutamentoDto> CriarAsync(RecrutamentoCreateDto dto);
    Task<RecrutamentoDto> AtualizarStatusAsync(int id, AtualizarStatusRecrutamentoDto dto);
}

/// <summary>Acompanhamento de candidatos num processo seletivo.</summary>
public class RecrutamentoService : IRecrutamentoService
{
    private const string MensagemStatusInvalido = MensagensValidacao.MensagemStatusInvalido;

    private readonly AppDbContext _db;

    public RecrutamentoService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<RecrutamentoDto>> ListarAsync(StatusRecrutamento? status)
    {
        Enums.ValidarFiltro(status, MensagemStatusInvalido);

        var query = _db.Recrutamentos.AsQueryable();
        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        var candidatos = await query.OrderByDescending(r => r.DataCandidatura).ThenByDescending(r => r.Id).ToListAsync();
        return candidatos.Select(ParaDto).ToList();
    }

    public async Task<RecrutamentoDto> CriarAsync(RecrutamentoCreateDto dto)
    {
        var hoje = Datas.Hoje();
        var recrutamento = new Recrutamento
        {
            NomeCandidato = Textos.ValidarNomePessoa(dto.NomeCandidato, Textos.NomeCandidato),
            Email = Emails.NormalizarOpcional(dto.Email),
            Telefone = Telefones.NormalizarOpcional(dto.Telefone),
            Vaga = Textos.Validar(dto.Vaga, Textos.Vaga),
            DataCandidatura = Datas.ValidarFaixa(dto.DataCandidatura, "Informe a data da candidatura.",
                "A data da candidatura", hoje.AddYears(-10), hoje),
            Status = StatusRecrutamento.EmProcesso,
            Observacoes = Textos.ValidarOpcional(dto.Observacoes, Textos.Observacoes),
            CriadoEm = DateTime.UtcNow,
        };

        _db.Recrutamentos.Add(recrutamento);
        await _db.SaveChangesAsync();

        return ParaDto(recrutamento);
    }

    public async Task<RecrutamentoDto> AtualizarStatusAsync(int id, AtualizarStatusRecrutamentoDto dto)
    {
        var recrutamento = await _db.Recrutamentos.FindAsync(id);
        if (recrutamento is null)
        {
            throw new NotFoundException("Candidato não encontrado.");
        }

        recrutamento.Status = Enums.Validar(dto.Status, MensagemStatusInvalido, MensagemStatusInvalido);
        if (dto.Observacoes is not null)
        {
            recrutamento.Observacoes = Textos.ValidarOpcional(dto.Observacoes, Textos.Observacoes);
        }

        await _db.SaveChangesAsync();
        return ParaDto(recrutamento);
    }

    private static RecrutamentoDto ParaDto(Recrutamento recrutamento) => new()
    {
        Id = recrutamento.Id,
        NomeCandidato = recrutamento.NomeCandidato,
        Email = recrutamento.Email,
        Telefone = recrutamento.Telefone,
        Vaga = recrutamento.Vaga,
        DataCandidatura = recrutamento.DataCandidatura,
        Status = recrutamento.Status,
        Observacoes = recrutamento.Observacoes,
    };
}
