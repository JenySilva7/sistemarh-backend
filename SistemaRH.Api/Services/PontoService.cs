using Microsoft.EntityFrameworkCore;
using SistemaRH.Api.Data;
using SistemaRH.Api.Dtos;
using SistemaRH.Api.Exceptions;
using SistemaRH.Api.Models;

namespace SistemaRH.Api.Services;

public interface IPontoService
{
    Task<List<RegistroPontoDto>> ListarRegistrosDeHojeAsync(int usuarioId);
    Task<RegistrarPontoResponseDto> RegistrarProximoAsync(int usuarioId);
}

/// <summary>
/// Regras do ponto: sempre existem 4 marcações por dia, nesta ordem —
/// Entrada, Saída (almoço), Volta (almoço), Saída — igual ao
/// PontoController.dart do app. Cada chamada de "bater o ponto" registra
/// automaticamente a PRÓXIMA marcação pendente do dia; o cliente não escolhe
/// qual tipo registrar, só pede "registre o meu próximo ponto".
/// </summary>
public class PontoService : IPontoService
{
    private readonly AppDbContext _db;

    public PontoService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<RegistroPontoDto>> ListarRegistrosDeHojeAsync(int usuarioId)
    {
        var registros = await RegistrosDeHojeQuery(usuarioId).ToListAsync();
        return registros.Select(ParaDto).ToList();
    }

    public async Task<RegistrarPontoResponseDto> RegistrarProximoAsync(int usuarioId)
    {
        var registrosHoje = await RegistrosDeHojeQuery(usuarioId).ToListAsync();
        var tiposJaRegistrados = registrosHoje.Select(r => r.Tipo).ToHashSet();

        // Percorre os 4 tipos na ordem do enum e pega o primeiro que ainda
        // não foi batido hoje — esse é o "próximo" a ser registrado agora.
        var proximoTipo = Enum.GetValues<TipoRegistroPonto>()
            .Cast<TipoRegistroPonto>()
            .FirstOrDefault(tipo => !tiposJaRegistrados.Contains(tipo), (TipoRegistroPonto)(-1));

        if ((int)proximoTipo == -1)
        {
            throw new BusinessRuleException("Todas as marcações de ponto de hoje já foram registradas.");
        }

        var registro = new RegistroPonto
        {
            UsuarioId = usuarioId,
            Tipo = proximoTipo,
            RegistradoEm = DateTime.UtcNow,
        };
        _db.RegistrosPonto.Add(registro);
        await _db.SaveChangesAsync();

        var aindaFaltam = tiposJaRegistrados.Count + 1 < 4;
        TipoRegistroPonto? proximoDepoisDesse = null;
        if (aindaFaltam)
        {
            tiposJaRegistrados.Add(proximoTipo);
            proximoDepoisDesse = Enum.GetValues<TipoRegistroPonto>()
                .Cast<TipoRegistroPonto>()
                .First(tipo => !tiposJaRegistrados.Contains(tipo));
        }

        return new RegistrarPontoResponseDto
        {
            Registro = ParaDto(registro),
            Proximo = proximoDepoisDesse,
        };
    }

    private IQueryable<RegistroPonto> RegistrosDeHojeQuery(int usuarioId)
    {
        var inicioDoDia = DateTime.UtcNow.Date;
        var fimDoDia = inicioDoDia.AddDays(1);

        return _db.RegistrosPonto
            .Where(r => r.UsuarioId == usuarioId && r.RegistradoEm >= inicioDoDia && r.RegistradoEm < fimDoDia)
            .OrderBy(r => r.RegistradoEm);
    }

    private static RegistroPontoDto ParaDto(RegistroPonto registro) => new()
    {
        Id = registro.Id,
        Tipo = registro.Tipo,
        RegistradoEm = registro.RegistradoEm,
    };
}
