using System.Diagnostics;

namespace SistemaRH.Api.Services;

/// <summary>
/// Limite de tentativas de login, com duas contagens:
///  - por <b>e-mail + IP</b>: 5 falhas em 15 minutos bloqueiam aquele e-mail,
///    só a partir daquele IP, por 15 minutos. Antes o bloqueio era só por
///    e-mail, e qualquer anônimo deixava o Admin de fora pra sempre mandando
///    5 senhas erradas a cada 15 minutos;
///  - por <b>IP</b>: 20 falhas em 15 minutos, somando todos os e-mails,
///    bloqueiam o IP por 15 minutos — freia quem testa uma senha comum em
///    muitos e-mails (password spraying).
///
/// Cada tentativa é <b>reservada</b> antes de conferir a senha
/// (<see cref="ReservarAsync"/>), e quem não cabe ESPERA A VEZ em vez de
/// levar 429:
///  - o mesmo e-mail, do mesmo IP, confere uma senha por vez. 20 senhas
///    erradas em paralelo continuam parando em 5 (a 6ª já encontra o
///    bloqueio), e 8 logins CERTOS em paralelo entram todos — antes, as
///    tentativas em andamento contavam como falha e 3 dos 8 recebiam
///    "tente em 15 minutos" sem bloqueio nenhum;
///  - do mesmo IP, as tentativas em andamento mais as falhas nunca passam
///    de 20: a 21ª espera uma terminar (pessoas diferentes atrás do mesmo
///    NAT entram, só um pouco depois).
/// O 429 só sai com bloqueio de verdade (Retry-After = o que falta dele) ou,
/// se a fila não andar em <see cref="LimiteTentativasLogin.EsperaMaximaPadrao"/>,
/// com uma pausa curta de alguns segundos.
///
/// Fica em memória (a API roda numa instância só), com teto de entradas.
/// Uma entrada só é apagada quando não guarda nada (sem falha na janela,
/// sem bloqueio, sem tentativa em andamento) — apagar uma dessas é igual a
/// ela nunca ter existido. Com o teto cheio de entradas ATIVAS, uma chave
/// NOVA recebe 429 curto (falha fechada): encher a memória nunca apaga um
/// bloqueio nem zera uma contagem. Antes, com o MemoryCache cheio, a
/// contagem de uma chave nova deixava de ser guardada e as mais antigas
/// (inclusive bloqueios) podiam ser descartadas.
/// </summary>
public interface ILimiteTentativasLogin
{
    /// <summary>
    /// Espera a vez e reserva uma tentativa. Devolve null se pode conferir a
    /// senha, ou quanto tempo esperar (o que falta do bloqueio, ou uma pausa
    /// curta se a fila não andou). Toda reserva aceita precisa ser concluída
    /// com <see cref="RegistrarFalha"/>, <see cref="RegistrarSucesso"/> ou <see cref="Cancelar"/>.
    /// </summary>
    Task<TimeSpan?> ReservarAsync(string email, string ip, CancellationToken cancelamento = default);

    void RegistrarFalha(string email, string ip);

    void RegistrarSucesso(string email, string ip);

    /// <summary>Devolve a reserva sem contar como falha (erro inesperado no meio do login).</summary>
    void Cancelar(string email, string ip);
}

public class LimiteTentativasLogin : ILimiteTentativasLogin
{
    public const int MaximoFalhasPorConta = 5;
    public const int MaximoFalhasPorIp = 20;
    public static readonly TimeSpan Janela = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DuracaoBloqueio = TimeSpan.FromMinutes(15);

    /// <summary>Quanto uma tentativa espera a vez antes de desistir.</summary>
    public static readonly TimeSpan EsperaMaximaPadrao = TimeSpan.FromSeconds(10);

    /// <summary>Pausa (Retry-After) de quem desistiu da fila: não há bloqueio, é só tentar de novo.</summary>
    public static readonly TimeSpan PausaFilaParada = TimeSpan.FromSeconds(2);

    /// <summary>Pausa de uma chave nova quando o teto está cheio de entradas ativas.</summary>
    public static readonly TimeSpan PausaMemoriaCheia = TimeSpan.FromMinutes(1);

    // Teto de memória: cada entrada é um e-mail+IP ou um IP.
    public const int LimiteDeEntradasPadrao = 100_000;

    // De quanto em quanto tempo as entradas que não guardam nada são apagadas.
    // Com o teto cheio, a varredura é antecipada, mas no máximo uma por
    // segundo: uma enxurrada de chaves novas não faz varrer 100 mil entradas
    // a cada requisição.
    private static readonly TimeSpan IntervaloDeLimpeza = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan IntervaloDeLimpezaComTetoCheio = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _relogio;
    private readonly TimeSpan _esperaMaxima;
    private readonly int _limiteDeEntradas;
    private readonly Dictionary<string, Contador> _contadores = new(StringComparer.Ordinal);
    private readonly object _trava = new();
    private DateTimeOffset _ultimaLimpeza = DateTimeOffset.MinValue;

    public LimiteTentativasLogin(
        TimeProvider? relogio = null,
        TimeSpan? esperaMaxima = null,
        int limiteDeEntradas = LimiteDeEntradasPadrao)
    {
        _relogio = relogio ?? TimeProvider.System;
        _esperaMaxima = esperaMaxima ?? EsperaMaximaPadrao;
        _limiteDeEntradas = limiteDeEntradas;
    }

    private sealed class Contador
    {
        public readonly Queue<DateTimeOffset> Falhas = new();
        public int EmAndamento;
        public DateTimeOffset? BloqueadoAte;

        // Só no contador do IP: completada quando uma tentativa daquele IP
        // termina, pra quem está esperando a vez conferir de novo.
        public TaskCompletionSource? Liberacao;

        public bool Ociosa => Falhas.Count == 0 && BloqueadoAte is null && EmAndamento == 0 && Liberacao is null;

        public void Atualizar(DateTimeOffset agora)
        {
            while (Falhas.Count > 0 && agora - Falhas.Peek() > Janela)
            {
                Falhas.Dequeue();
            }
            if (BloqueadoAte is DateTimeOffset ate && ate <= agora)
            {
                BloqueadoAte = null;
            }
        }

        public void ContarFalha(DateTimeOffset agora, int maximo)
        {
            EmAndamento = Math.Max(0, EmAndamento - 1);
            Falhas.Enqueue(agora);
            if (BloqueadoAte is null && Falhas.Count >= maximo)
            {
                BloqueadoAte = agora + DuracaoBloqueio;
                Falhas.Clear();
            }
        }
    }

    public async Task<TimeSpan?> ReservarAsync(string email, string ip, CancellationToken cancelamento = default)
    {
        var cronometro = Stopwatch.StartNew();
        while (true)
        {
            Task vez;
            lock (_trava)
            {
                var agora = _relogio.GetUtcNow();
                // A limpeza vem antes de pegar as entradas desta tentativa:
                // uma entrada recém-criada ainda está "ociosa" e não pode
                // sumir entre o Obter e a reserva.
                ApagarOciosas(agora, tetoCheio: _contadores.Count + 2 > _limiteDeEntradas);

                // O IP primeiro: com ele bloqueado, a chave e-mail+IP nem é
                // criada (antes, cada tentativa recusada ainda ocupava memória).
                var porIp = Obter(ChaveIp(ip), agora, respeitarTeto: true);
                if (porIp is null)
                {
                    return PausaMemoriaCheia;
                }
                if (porIp.BloqueadoAte is DateTimeOffset ipBloqueadoAte)
                {
                    return ipBloqueadoAte - agora;
                }

                var porConta = Obter(ChaveConta(email, ip), agora, respeitarTeto: true);
                if (porConta is null)
                {
                    return PausaMemoriaCheia;
                }
                if (porConta.BloqueadoAte is DateTimeOffset contaBloqueadaAte)
                {
                    return contaBloqueadaAte - agora;
                }

                if (porConta.EmAndamento == 0 && porIp.Falhas.Count + porIp.EmAndamento < MaximoFalhasPorIp)
                {
                    porIp.EmAndamento++;
                    porConta.EmAndamento++;
                    return null;
                }

                // Tem tentativa desse e-mail em andamento (ou o IP está no
                // limite contando as em andamento): espera uma terminar.
                porIp.Liberacao ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                vez = porIp.Liberacao.Task;
            }

            var falta = _esperaMaxima - cronometro.Elapsed;
            if (falta <= TimeSpan.Zero)
            {
                return PausaFilaParada;
            }
            try
            {
                await vez.WaitAsync(falta, cancelamento);
            }
            catch (TimeoutException)
            {
                return PausaFilaParada;
            }
        }
    }

    public void RegistrarFalha(string email, string ip) => Concluir(email, ip, (porIp, porConta, agora) =>
    {
        porIp.ContarFalha(agora, MaximoFalhasPorIp);
        porConta.ContarFalha(agora, MaximoFalhasPorConta);
    });

    public void RegistrarSucesso(string email, string ip) => Concluir(email, ip, (porIp, porConta, _) =>
    {
        porIp.EmAndamento = Math.Max(0, porIp.EmAndamento - 1);

        // O acerto zera as falhas daquele e-mail, mas NUNCA um bloqueio que
        // já está valendo (antes, a senha certa no meio de uma rajada apagava
        // o bloqueio criado pelas erradas). As falhas do IP não são zeradas:
        // quem testa senhas em muitos e-mails pode ter uma conta válida.
        porConta.EmAndamento = Math.Max(0, porConta.EmAndamento - 1);
        if (porConta.BloqueadoAte is null)
        {
            porConta.Falhas.Clear();
        }
    });

    public void Cancelar(string email, string ip) => Concluir(email, ip, (porIp, porConta, _) =>
    {
        porIp.EmAndamento = Math.Max(0, porIp.EmAndamento - 1);
        porConta.EmAndamento = Math.Max(0, porConta.EmAndamento - 1);
    });

    private void Concluir(string email, string ip, Action<Contador, Contador, DateTimeOffset> registrar)
    {
        TaskCompletionSource? liberacao;
        lock (_trava)
        {
            var agora = _relogio.GetUtcNow();
            // Quem tinha reserva tem as duas entradas (entrada com tentativa
            // em andamento nunca é apagada); o teto não vale aqui.
            var porIp = Obter(ChaveIp(ip), agora, respeitarTeto: false)!;
            var porConta = Obter(ChaveConta(email, ip), agora, respeitarTeto: false)!;
            registrar(porIp, porConta, agora);
            liberacao = porIp.Liberacao;
            porIp.Liberacao = null;
        }
        // Fora da trava: quem estava esperando a vez confere de novo.
        liberacao?.TrySetResult();
    }

    private static string ChaveIp(string ip) => "ip|" + ip;

    private static string ChaveConta(string email, string ip) => "conta|" + ip + "|" + email;

    private Contador? Obter(string chave, DateTimeOffset agora, bool respeitarTeto)
    {
        if (_contadores.TryGetValue(chave, out var existente))
        {
            existente.Atualizar(agora);
            return existente;
        }

        if (respeitarTeto && _contadores.Count >= _limiteDeEntradas)
        {
            return null;
        }

        var contador = new Contador();
        _contadores[chave] = contador;
        return contador;
    }

    private void ApagarOciosas(DateTimeOffset agora, bool tetoCheio)
    {
        var intervalo = tetoCheio ? IntervaloDeLimpezaComTetoCheio : IntervaloDeLimpeza;
        if (agora - _ultimaLimpeza < intervalo)
        {
            return;
        }
        _ultimaLimpeza = agora;

        // Desde o .NET Core 3.0, Remove durante o foreach do Dictionary é permitido.
        foreach (var (chave, contador) in _contadores)
        {
            contador.Atualizar(agora);
            if (contador.Ociosa)
            {
                _contadores.Remove(chave);
            }
        }
    }
}
