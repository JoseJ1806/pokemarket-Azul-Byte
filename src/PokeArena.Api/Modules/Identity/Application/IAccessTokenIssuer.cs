using PokeArena.Api.Modules.Identity.Domain;

namespace PokeArena.Api.Modules.Identity.Application;

// PUERTO: EMISOR DE CREDENCIALES PROPIAS (módulo Identidad y Acceso, capa de aplicación).
//
// Qué es: el contrato para fabricar la credencial de PokéArena después de que el
// proveedor externo confirmó quién es la persona.
//
// Por qué existe (RNF-SEG-01): "La API confía solo en credenciales emitidas por ella
// misma. La identidad del usuario se valida con el proveedor una vez, al iniciar sesión."
// Es decir, el token de Google solo se usa una vez, en POST /api/auth/google. Desde ahí
// en adelante el cliente usa la credencial que sale de este puerto. Esto es el patrón
// de INTERCAMBIO DE TOKENS: se entrega un token externo y se recibe uno propio.
//
// Quién lo implementa: JwtAccessTokenIssuer (Infrastructure), que firma un JWT.
public interface IAccessTokenIssuer
{
    // Emite la credencial para una cuenta. En el diagrama aparece como "Issue(account)".
    AccessToken Issue(PlayerAccount account);
}

// CREDENCIAL DE ACCESO (objeto de valor).
// Value:     el texto del token que el cliente manda en "Authorization: Bearer <Value>".
// ExpiresAt: cuándo deja de servir. Se devuelve al cliente para que sepa cuándo vence
//            sin tener que abrir el token.
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
