using System.Text.Json;

namespace PokeArena.Api.Modules.Matchmaking.Domain;

// BÚSQUEDA DE PARTIDA de un jugador (módulo Emparejamiento, capa de dominio).
//
// Qué es: una fila de la fila de espera. Vive en PostgreSQL (tabla match_searches) y no en la
// memoria de una instancia, así que todas las instancias de la API ven la misma fila (D-01,
// ADR-007).
//
// Sin llaves foráneas hacia otros módulos (misma decisión que Colección): PlayerId, DeckId y
// MatchId son solo identificadores.
public sealed class MatchSearch
{
    public Guid Id { get; private set; }

    // Jugador que busca. Siempre sale del token, nunca del cuerpo de la solicitud.
    public Guid PlayerId { get; private set; }

    // "Ranked" para la partida clasificatoria. El torneo usará otro valor (#147).
    public string Context { get; private set; } = MatchmakingRules.RankedContext;

    // Deck que indicó el jugador y sus 12 cartas, copiadas al buscar. Así el deck no puede
    // cambiar mientras la búsqueda está abierta ni durante la partida (#127).
    public Guid DeckId { get; private set; }

    public string DeckCardIds { get; private set; } = "[]";

    // Elo del jugador al empezar a buscar.
    public int Elo { get; private set; }

    public MatchSearchStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // Última vez que el jugador consultó la búsqueda. Si pasa más de
    // MatchmakingRules.InactivityTimeout sin consultar, la búsqueda deja de emparejarse.
    public DateTimeOffset LastSeenAt { get; private set; }

    // Emparejamiento en curso: las dos búsquedas emparejadas comparten este identificador.
    // También forma la llave de idempotencia con la que se crea la partida.
    public Guid? PairingId { get; private set; }

    public DateTimeOffset? PairedAt { get; private set; }

    // Partida asignada cuando el estado es MatchFound.
    public Guid? MatchId { get; private set; }

    // Cuándo terminó la búsqueda (partida encontrada, cancelada o expirada).
    public DateTimeOffset? ClosedAt { get; private set; }

    // Aviso para el jugador, por ejemplo por qué volvió a la búsqueda.
    public string? Notice { get; private set; }

    // Constructor vacío y privado: lo necesita Entity Framework para leer de la base.
    private MatchSearch()
    {
    }

    // MÉTODO DE FÁBRICA. Quien lo llama ya validó el deck, el saldo y la regla de una sola
    // búsqueda abierta (lo hace SearchMatchUseCase).
    public static MatchSearch Open(
        Guid playerId,
        string context,
        Guid deckId,
        IReadOnlyCollection<int> deckCardIds,
        int elo,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            PlayerId = playerId,
            Context = context,
            DeckId = deckId,
            DeckCardIds = JsonSerializer.Serialize(deckCardIds.Order().ToList()),
            Elo = elo,
            Status = MatchSearchStatus.Waiting,
            CreatedAt = now,
            LastSeenAt = now,
        };

    public bool IsOpen => Status is MatchSearchStatus.Waiting or MatchSearchStatus.Pairing;

    // Una búsqueda en espera que el jugador dejó de consultar.
    public bool IsInactive(DateTimeOffset now) =>
        Status == MatchSearchStatus.Waiting && now - LastSeenAt > MatchmakingRules.InactivityTimeout;

    // Tiempo de espera: hasta ahora si sigue abierta, o hasta que terminó.
    public TimeSpan WaitedAt(DateTimeOffset now)
    {
        var end = ClosedAt ?? now;
        return end > CreatedAt ? end - CreatedAt : TimeSpan.Zero;
    }

    public IReadOnlyList<int> ReadDeckCardIds() =>
        JsonSerializer.Deserialize<List<int>>(DeckCardIds) ?? [];

    // El jugador consultó la búsqueda.
    public void Touch(DateTimeOffset now)
    {
        if (now > LastSeenAt)
            LastSeenAt = now;
    }

    public void Expire(DateTimeOffset now)
    {
        RequireStatus(MatchSearchStatus.Waiting, "expirar");
        Status = MatchSearchStatus.Expired;
        ClosedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        RequireStatus(MatchSearchStatus.Waiting, "cancelar");
        Status = MatchSearchStatus.Cancelled;
        ClosedAt = now;
    }

    // Se eligió rival: empieza la creación de la partida. También se usa para renovar un
    // reintento del mismo emparejamiento.
    public void BeginPairing(Guid pairingId, DateTimeOffset now)
    {
        var retry = Status == MatchSearchStatus.Pairing && PairingId == pairingId;
        if (Status != MatchSearchStatus.Waiting && !retry)
            throw new InvalidOperationException($"No se puede emparejar una búsqueda en estado {Status}.");

        Status = MatchSearchStatus.Pairing;
        PairingId = pairingId;
        PairedAt = now;
        Notice = null;
    }

    // La partida no se pudo crear: la búsqueda vuelve a la espera con un aviso.
    public void ReturnToWaiting(string notice)
    {
        RequireStatus(MatchSearchStatus.Pairing, "devolver a la espera");
        Status = MatchSearchStatus.Waiting;
        PairingId = null;
        PairedAt = null;
        Notice = notice;
    }

    private void RequireStatus(MatchSearchStatus expected, string action)
    {
        if (Status != expected)
            throw new InvalidOperationException($"No se puede {action} una búsqueda en estado {Status}.");
    }
}
