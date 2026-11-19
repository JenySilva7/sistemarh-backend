using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Common;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IEventoAgendaService
{
    Task<List<EventoAgendaDto>> ListarAsync(DateTime? de, DateTime? ate);
    Task<EventoAgendaDto> CriarAsync(EventoAgendaCreateDto dto);
    Task RemoverAsync(int id);
}

/// <summary>Compromissos da agenda do RH (reuniões, entrevistas, avaliações...).</summary>
public class EventoAgendaService : IEventoAgendaService
{
    private readonly AppDbContext _db;

    public EventoAgendaService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<EventoAgendaDto>> ListarAsync(DateTime? de, DateTime? ate)
    {
        var query = _db.EventosAgenda.AsQueryable();
        if (de is not null)
        {
            query = query.Where(e => e.DataEvento >= de);
        }
        if (ate is not null)
        {
            query = query.Where(e => e.DataEvento <= ate);
        }

        var eventos = await query.OrderBy(e => e.DataEvento).ThenBy(e => e.Id).ToListAsync();
        return eventos.Select(ParaDto).ToList();
    }

    public async Task<EventoAgendaDto> CriarAsync(EventoAgendaCreateDto dto)
    {
        var titulo = Textos.Validar(dto.Titulo, Textos.TituloEvento);
        var descricao = Textos.ValidarOpcional(dto.Descricao, Textos.DescricaoLonga);
        var tipo = Enums.Validar<TipoEventoAgenda>(dto.Tipo ?? TipoEventoAgenda.Outro, string.Empty, "Tipo de evento inválido.");

        // Data e hora do evento (não é só um dia): confere a faixa pelo dia,
        // de 1 ano atrás até 2 anos à frente. Sem data, antes virava 01/01/0001.
        if (dto.DataEvento is not DateTime informada)
        {
            throw new BusinessRuleException("Informe a data/hora do evento.");
        }
        // Sempre grava a hora de Brasília, sem fuso (o JSON já chega assim
        // pelo DataHoraBrasiliaJsonConverter; isto cobre quem chama o serviço direto).
        var dataEvento = Datas.ParaHorarioDeBrasilia(informada);
        var hoje = Datas.Hoje();
        Datas.ValidarFaixa(dataEvento, string.Empty, "A data do evento", hoje.AddYears(-1), hoje.AddYears(2));

        if (dto.FuncionarioId is not null)
        {
            var funcionarioExiste = await _db.Funcionarios.AnyAsync(f => f.Id == dto.FuncionarioId);
            if (!funcionarioExiste)
            {
                throw new NotFoundException("Funcionário informado não existe.");
            }
        }

        var evento = new EventoAgenda
        {
            Titulo = titulo,
            Descricao = descricao,
            DataEvento = dataEvento,
            Tipo = tipo,
            FuncionarioId = dto.FuncionarioId,
        };

        _db.EventosAgenda.Add(evento);
        await _db.SaveChangesAsync();

        return ParaDto(evento);
    }

    public async Task RemoverAsync(int id)
    {
        var evento = await _db.EventosAgenda.FindAsync(id);
        if (evento is null)
        {
            throw new NotFoundException("Evento não encontrado.");
        }

        _db.EventosAgenda.Remove(evento);
        await _db.SaveChangesAsync();
    }

    private static EventoAgendaDto ParaDto(EventoAgenda evento) => new()
    {
        Id = evento.Id,
        Titulo = evento.Titulo,
        Descricao = evento.Descricao,
        DataEvento = evento.DataEvento,
        Tipo = evento.Tipo,
        FuncionarioId = evento.FuncionarioId,
    };
}
