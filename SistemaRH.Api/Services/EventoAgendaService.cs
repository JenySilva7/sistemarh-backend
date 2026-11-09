using Microsoft.EntityFrameworkCore;
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

        var eventos = await query.OrderBy(e => e.DataEvento).ToListAsync();
        return eventos.Select(ParaDto).ToList();
    }

    public async Task<EventoAgendaDto> CriarAsync(EventoAgendaCreateDto dto)
    {
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
            Titulo = dto.Titulo.Trim(),
            Descricao = dto.Descricao.Trim(),
            DataEvento = dto.DataEvento,
            Tipo = dto.Tipo,
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
