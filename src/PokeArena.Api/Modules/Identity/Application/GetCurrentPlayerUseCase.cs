using PokeArena.Api.Modules.Identity.Domain;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Identity.Application;

// Resultado de consultar el jugador actual. Mismo estilo que los demás resultados.
public sealed record GetCurrentPlayerResult(PlayerAccount? Account, DomainError? Error);

// CASO DE USO: OBTENER EL JUGADOR ACTUAL (módulo Identidad y Acceso).
//
// Lo usa GET /api/me. Devuelve los datos de la cuenta dueña del token que vino en la
// solicitud.
//
// Por qué la ruta NO recibe un Id (/api/me en lugar de /api/players/{id}):
// RNF-SEG-04 dice "ningún usuario puede leer ni modificar recursos de otro usuario,
// aunque conozca su identificador". Si el Id no viaja en la ruta sino que sale del
// token firmado, es imposible pedir los datos de otra persona.
public sealed class GetCurrentPlayerUseCase(IPlayerAccountRepository accounts)
{
    public const string NotAPlayerCode = "player-not-found";

    // subject es el claim "sub" del JWT de PokéArena, que JwtAccessTokenIssuer llena
    // con el Id de la cuenta.
    public async Task<GetCurrentPlayerResult> ExecuteAsync(string? subject, CancellationToken cancellationToken)
    {
        // El token de administrador de desarrollo (generar-token.mjs) tiene un "sub" como
        // "admin-dev", que no es un Guid. Ese token es válido pero no corresponde a una
        // cuenta de jugador, así que se responde 404 en lugar de fallar.
        if (!Guid.TryParse(subject, out var playerId))
            return NotAPlayer();

        var account = await accounts.FindByIdAsync(playerId, cancellationToken);
        return account is null ? NotAPlayer() : new GetCurrentPlayerResult(account, null);
    }

    private static GetCurrentPlayerResult NotAPlayer() =>
        new(null, DomainError.NotFound(NotAPlayerCode, "La sesión actual no corresponde a una cuenta de jugador."));
}
