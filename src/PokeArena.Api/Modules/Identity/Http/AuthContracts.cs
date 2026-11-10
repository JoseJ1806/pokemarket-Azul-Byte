using PokeArena.Api.Modules.Identity.Domain;

namespace PokeArena.Api.Modules.Identity.Http;

// CONTRATOS JSON DE LOS ENDPOINTS DE IDENTIDAD.
//
// Son los objetos que viajan por HTTP. Se separan de las clases del dominio a propósito:
// así se puede cambiar la forma interna de PlayerAccount sin romper a los clientes, y
// se controla exactamente qué datos salen de la API (por ejemplo, ProviderSubject NO se
// devuelve nunca).
//
// ASP.NET los convierte a JSON con nombres en camelCase: IdToken se envía como "idToken".

// Cuerpo de POST /api/auth/google.
// IdToken es el "credential" que entrega el botón de Google en el navegador.
public sealed record SignInWithGoogleRequest(string? IdToken);

// Respuesta 200 de POST /api/auth/google.
// AccessToken:  credencial de PokéArena. El cliente la manda en Authorization: Bearer.
// ExpiresAt:    cuándo vence (15 minutos después de emitida).
// Player:       datos públicos de la cuenta.
// IsNewAccount: true en el primer ingreso, para mostrar la bienvenida.
// InitialCollection: qué pasó con la colección inicial en este ingreso. Viaja como TEXTO
//                 ("Granted", "AlreadyGranted" o "NotAvailable") y no como número, para
//                 que el frontend sea legible y no dependa del orden del enum.
public sealed record SignInResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    PlayerResponse Player,
    bool IsNewAccount,
    string InitialCollection);

// Datos de la cuenta que se pueden mostrar al propio jugador.
// La usan POST /api/auth/google y GET /api/me.
public sealed record PlayerResponse(
    Guid Id,
    string Email,
    string Role,
    int Elo,
    DateTimeOffset CreatedAt)
{
    // Convierte la entidad del dominio en la respuesta HTTP.
    public static PlayerResponse From(PlayerAccount account) =>
        new(account.Id, account.Email, account.Role, account.Elo, account.CreatedAt);
}
