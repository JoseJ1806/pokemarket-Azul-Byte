namespace PokeArena.Api.Modules.Identity.Domain;

// CUENTA DEL JUGADOR (módulo Identidad y Acceso, capa de dominio).
//
// Qué es: la ENTIDAD que representa a una persona dentro de PokéArena. A diferencia
// de ExternalIdentity, sí tiene identidad propia (su Id) y vive en la base de datos,
// en la tabla player_accounts (ver PokeArenaDbContext).
//
// Cómo se crea: SOLO con el método de fábrica CreatePlayer. El constructor es privado,
// así nadie puede armar una cuenta "a mano" y olvidar asignar el rol o el Elo inicial.
// Esto es el patrón MÉTODO DE FÁBRICA: las reglas de creación viven en un único lugar.
//
// Regla clave de la US: la cuenta se identifica por Provider + ProviderSubject, nunca
// por el correo. En la base de datos hay un índice único sobre esas dos columnas, y
// ese índice es lo que impide crear dos cuentas para la misma persona aunque lleguen
// dos ingresos al mismo tiempo (ver EfPlayerAccountRepository.AddAsync).
public sealed class PlayerAccount
{
    // Elo con el que empieza todo jugador (criterio de éxito de la US).
    public const int InitialElo = 0;

    // Identificador interno de la cuenta. Es un Guid versión 7:
    // 1. No se puede adivinar ni enumerar (con un entero, el jugador 37 sabría que hay
    //    unos 37 jugadores y podría probar Ids vecinos). Esto refuerza RNF-SEG-04.
    // 2. Lo genera la aplicación antes de guardar, sin esperar a la base de datos.
    // 3. La versión 7 empieza con la fecha y hora, así que los Ids nuevos quedan
    //    ordenados y no desordenan el índice de PostgreSQL como un Guid aleatorio.
    // Este mismo Id es el que va en el claim "sub" del JWT de PokéArena y el que recibe
    // el módulo de colección en IInitialCollectionGranter.GrantIfMissingAsync.
    public Guid Id { get; private set; }

    // Proveedor que verificó a la persona, por ejemplo "Google".
    public string Provider { get; private set; } = string.Empty;

    // Identificador de la persona dentro del proveedor (claim "sub" de Google).
    public string ProviderSubject { get; private set; } = string.Empty;

    // Correo con el que se creó la cuenta. Es solo informativo.
    public string Email { get; private set; } = string.Empty;

    // Rol dentro de PokéArena. Toda cuenta creada por inicio de sesión es Player.
    public string Role { get; private set; } = string.Empty;

    // Puntaje de clasificación. Empieza en 0 y lo cambiarán las partidas en el futuro.
    public int Elo { get; private set; }

    // Momento en que se creó la cuenta (UTC).
    public DateTimeOffset CreatedAt { get; private set; }

    // Constructor vacío y privado. Lo necesita Entity Framework para reconstruir la
    // cuenta cuando la lee de la base de datos. El código de la aplicación no puede
    // usarlo porque es privado, y por eso está obligado a pasar por CreatePlayer.
    private PlayerAccount()
    {
    }

    // MÉTODO DE FÁBRICA. Crea la cuenta de un jugador nuevo a partir de la identidad
    // que el proveedor ya verificó. Aparece en el diagrama de secuencia de V5 como
    // "PlayerAccount.CreatePlayer(identity) con rol Player y Elo 0".
    //
    // Importante: este método solo arma el objeto en memoria. Guardarlo en la base de
    // datos le corresponde a IPlayerAccountRepository.AddAsync.
    public static PlayerAccount CreatePlayer(ExternalIdentity identity)
    {
        var now = DateTimeOffset.UtcNow;
        return new PlayerAccount
        {
            Id = Guid.CreateVersion7(now),
            Provider = identity.Provider,
            ProviderSubject = identity.Subject,
            Email = identity.Email,
            Role = IdentityRoles.Player,
            Elo = InitialElo,
            CreatedAt = now
        };
    }
}
