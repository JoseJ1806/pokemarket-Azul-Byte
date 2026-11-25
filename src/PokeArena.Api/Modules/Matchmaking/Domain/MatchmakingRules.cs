namespace PokeArena.Api.Modules.Matchmaking.Domain;

// REGLAS DEL EMPAREJAMIENTO (módulo Emparejamiento, capa de dominio).
//
// Todos los números están en un solo lugar para que los casos de uso, la consulta SQL del
// emparejamiento, las pruebas y el ADR-007 usen los mismos valores.
public static class MatchmakingRules
{
    // Contexto de las búsquedas clasificatorias. Los torneos (#147) usarán otro contexto en
    // la misma tabla; la regla de una sola búsqueda abierta vale para todos (RN-16).
    public const string RankedContext = "Ranked";

    // RN-15: saldo mínimo para buscar partida clasificatoria. Es la apuesta máxima posible.
    public const int MinimumCubes = 8;

    // RN-14: un deck válido tiene exactamente 12 cartas distintas de la colección del jugador.
    // Es el mismo valor que DeckRules.Size del módulo Colección.
    public const int DeckSize = 12;

    // Cada cuánto consulta la pantalla el estado de la búsqueda.
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    // #128: "un jugador que deja de consultar su búsqueda durante el tiempo definido por el
    // equipo queda fuera de la búsqueda". Decisión del equipo (ADR-007): 20 segundos, que
    // equivalen a 10 consultas seguidas sin llegar.
    public static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(20);

    // RN-17: la oferta de jugar contra el bot aparece a los 60 segundos sin rival.
    public static readonly TimeSpan BotOfferAfter = TimeSpan.FromSeconds(60);

    // Si una creación de partida quedó a medias (por ejemplo, se cayó la instancia que la
    // hacía), otra consulta la reintenta pasado este tiempo. El reintento es seguro porque la
    // creación usa una llave de idempotencia por emparejamiento.
    public static readonly TimeSpan PairingRetryAfter = TimeSpan.FromSeconds(10);

    // RF-EMP-02: diferencia de Elo aceptada. Empieza en 6 puntos (dos victorias, RN-30) y
    // suma 3 puntos cada 5 segundos de espera, hasta un máximo de 30 puntos (diez victorias),
    // que se alcanza a los 40 segundos. Decisión del equipo documentada en el ADR-007.
    public const int InitialEloRange = 6;
    public const int EloRangeIncrement = 3;
    public static readonly TimeSpan EloRangeStep = TimeSpan.FromSeconds(5);
    public const int MaxEloRange = 30;

    // Diferencia de Elo que acepta una búsqueda que lleva 'waited' esperando.
    // La consulta SQL de EfMatchSearchRepository calcula lo mismo para el rival.
    public static int EloRangeFor(TimeSpan waited)
    {
        var seconds = Math.Max(0, waited.TotalSeconds);
        var steps = (int)Math.Floor(seconds / EloRangeStep.TotalSeconds);
        return Math.Min(MaxEloRange, InitialEloRange + steps * EloRangeIncrement);
    }

    // Motivo por el que un deck no sirve para buscar partida, o null si sirve.
    // cardCount: cartas del deck; distinctCount: cartas distintas; ownedCount: cuántas de
    // las distintas siguen en la colección del jugador.
    public static string? DeckProblem(int cardCount, int distinctCount, int ownedCount)
    {
        if (cardCount != DeckSize)
            return $"El deck debe tener exactamente {DeckSize} cartas y tiene {cardCount}.";
        if (distinctCount != cardCount)
            return "El deck tiene cartas repetidas.";
        if (ownedCount < distinctCount)
            return "El deck tiene cartas que ya no están en tu colección.";
        return null;
    }
}
