using PokeArena.Api.Modules.Matchmaking.Domain;

namespace PokeArena.Api.Modules.Matchmaking.Application;

// PUERTOS DEL MÓDULO EMPAREJAMIENTO (capa de aplicación).
//
// Los casos de uso solo conocen estas interfaces. Los adaptadores de Infrastructure las
// implementan con PostgreSQL y con los módulos Colección, Identidad y Partida. Así el
// emparejamiento no depende de cómo guardan sus datos los demás módulos.

// ---------------------------------------------------------------- otros módulos ----

// Un deck del jugador, con lo necesario para validarlo (RN-14).
public sealed record SearchDeck(Guid DeckId, string Name, IReadOnlyList<int> CardIds, int OwnedCardCount);

// Módulo Colección: el deck del jugador SOLO si es suyo (si es de otro, null).
public interface ISearchDeckLookup
{
    Task<SearchDeck?> FindAsync(Guid playerId, Guid deckId, CancellationToken cancellationToken);
}

// Módulo Colección: saldo actual de cubos (0 si el jugador no tiene saldo guardado).
public interface ICubeBalanceLookup
{
    Task<int> GetBalanceAsync(Guid playerId, CancellationToken cancellationToken);
}

// Módulo Identidad: Elo del jugador, o null si no existe una cuenta de jugador con ese id.
public interface IPlayerEloLookup
{
    Task<int?> FindEloAsync(Guid playerId, CancellationToken cancellationToken);
}

// Módulo Partida: la partida activa del jugador, o null si no tiene (RN-16).
public interface IActiveMatchLookup
{
    Task<Guid?> FindActiveMatchIdAsync(Guid playerId, CancellationToken cancellationToken);
}

// Lo que se pide al módulo Partida al emparejar: los dos jugadores con el deck que indicó
// cada uno. PairingId forma la llave de idempotencia, así que pedir lo mismo dos veces
// (un reintento) nunca crea dos partidas.
public sealed record PairedMatchRequest(
    Guid PairingId,
    Guid ChallengerId,
    IReadOnlyList<int> ChallengerCardIds,
    Guid OpponentId,
    IReadOnlyList<int> OpponentCardIds);

// Resultado de crear la partida: el id de la partida, o el motivo por el que no se creó.
public sealed record PairedMatchOutcome(Guid? MatchId, string? FailureReason)
{
    public static PairedMatchOutcome Started(Guid matchId) => new(matchId, null);

    public static PairedMatchOutcome Failed(string reason) => new(null, reason);
}

// Módulo Partida: crea la partida de dos jugadores emparejados.
public interface IPairedMatchStarter
{
    Task<PairedMatchOutcome> StartAsync(PairedMatchRequest request, CancellationToken cancellationToken);
}

// ------------------------------------------------------------- fila de búsqueda ----

// PUERTO: la fila de búsquedas en PostgreSQL.
public interface IMatchSearchRepository
{
    // La búsqueda más reciente del jugador (abierta o no), o null si nunca buscó.
    Task<MatchSearch?> FindLatestAsync(Guid playerId, CancellationToken cancellationToken);

    Task<MatchSearch?> FindAsync(Guid searchId, CancellationToken cancellationToken);

    // Abre una transacción y toma el candado de ESE jugador. Lo usan buscar y salir, para que
    // dos solicitudes del mismo jugador (doble clic, dos pestañas) se atiendan de una en una.
    Task<IPlayerSearchScope> BeginPlayerScopeAsync(Guid playerId, CancellationToken cancellationToken);

    // Abre la transacción del emparejamiento y lee la búsqueda (todavía sin bloquearla).
    Task<IPairingScope> BeginPairingScopeAsync(Guid searchId, CancellationToken cancellationToken);

    // La partida se creó: las dos búsquedas del emparejamiento pasan a MatchFound.
    Task CompletePairingAsync(Guid pairingId, Guid matchId, DateTimeOffset now, CancellationToken cancellationToken);

    // La partida no se creó: las dos búsquedas vuelven a Waiting con el motivo.
    Task ReturnPairingToWaitingAsync(Guid pairingId, string reason, CancellationToken cancellationToken);
}

// Alcance de buscar o salir: transacción con el candado del jugador.
public interface IPlayerSearchScope : IAsyncDisposable
{
    // La búsqueda abierta del jugador (Waiting o Pairing), bloqueada, o null.
    Task<MatchSearch?> FindOpenAsync(CancellationToken cancellationToken);

    void Add(MatchSearch search);

    // Guarda los cambios dentro de la transacción, sin confirmarla todavía.
    Task SaveAsync(CancellationToken cancellationToken);

    // Guarda y confirma. Si el índice único de búsquedas abiertas rechaza la fila, lanza
    // OpenSearchAlreadyExistsException.
    Task CommitAsync(CancellationToken cancellationToken);
}

// Alcance del emparejamiento: una transacción de base de datos.
public interface IPairingScope : IAsyncDisposable
{
    // La búsqueda tal como estaba al abrir el alcance, SIN bloquear, o null si no existe.
    // Sirve para elegir rival; las decisiones se toman con lo que devuelve LockAsync.
    MatchSearch? Snapshot { get; }

    // Un posible rival (sin bloquearlo): en espera, del mismo contexto, que consultó hace poco y
    // con Elo dentro del rango del que lleva más tiempo esperando.
    Task<MatchSearch?> FindCandidateAsync(MatchSearch search, DateTimeOffset now, CancellationToken cancellationToken);

    // Bloquea esas búsquedas (SELECT ... FOR UPDATE) SIEMPRE en orden de Id y las devuelve con
    // sus valores actuales. Por el orden fijo, dos emparejamientos simultáneos nunca se bloquean
    // entre sí: el segundo espera al primero y luego ve el estado nuevo.
    Task<IReadOnlyList<MatchSearch>> LockAsync(IReadOnlyCollection<Guid> searchIds, CancellationToken cancellationToken);

    // La otra búsqueda del mismo emparejamiento (sin bloquearla).
    Task<MatchSearch?> FindPartnerAsync(MatchSearch search, CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);
}

// El índice único de búsquedas abiertas rechazó una segunda búsqueda del jugador. Es la última
// barrera: el candado por jugador ya debería haberlo evitado.
public sealed class OpenSearchAlreadyExistsException(Guid playerId, Exception innerException)
    : Exception($"El jugador {playerId} ya tiene una búsqueda abierta.", innerException)
{
    public Guid PlayerId { get; } = playerId;
}
