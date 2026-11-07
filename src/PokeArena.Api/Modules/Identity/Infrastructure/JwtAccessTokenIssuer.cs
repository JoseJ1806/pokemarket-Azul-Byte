using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PokeArena.Api.Modules.Identity.Application;
using PokeArena.Api.Modules.Identity.Domain;

namespace PokeArena.Api.Modules.Identity.Infrastructure;

// ADAPTADOR DE SALIDA: EMISOR DE JWT PROPIOS (módulo Identidad y Acceso).
//
// Implementa el puerto IAccessTokenIssuer. Fabrica la credencial de PokéArena que el
// cliente usará en todas las solicitudes después de iniciar sesión.
//
// Cómo se conecta con la validación: el JWT se firma con la MISMA clave (Jwt:SigningKey),
// el MISMO emisor (Jwt:Issuer) y la MISMA audiencia (Jwt:Audience) que IdentityModule
// configura para validar los tokens que llegan. Por eso un token emitido aquí es aceptado
// por la API, y uno emitido por Google o por cualquier otro no lo es (RNF-SEG-01).
//
// Contenido del token (claims):
// sub:   Id de la cuenta (Guid). GET /api/me lo usa para saber quién pregunta.
// role:  Player o Admin. La política AdminOnly lo lee para dar o negar permiso.
// email: correo de la cuenta, para que el cliente pueda mostrarlo.
// jti:   identificador único del token. Servirá para revocar sesiones en la US de
//        Renovación y cierre de sesión.
// iss, aud, iat, nbf, exp: emisor, audiencia, emitido en, válido desde y expira en.
//
// Se registra como Singleton porque no guarda nada de una solicitud a otra.
public sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock) : IAccessTokenIssuer
{
    private static readonly JsonWebTokenHandler TokenHandler = new();

    public AccessToken Issue(PlayerAccount account)
    {
        var settings = options.Value;

        // TimeProvider en lugar de DateTime.UtcNow: es la forma de .NET de pedir la hora
        // actual que se puede reemplazar en pruebas si algún día hace falta.
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(settings.AccessTokenMinutes);

        // HMAC SHA256 con clave simétrica: el mismo algoritmo que ya usaba el Sprint 1.
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = account.Id.ToString(),
                // Se escribe "role" (igual que el token de desarrollo del Sprint 1).
                // Al validar, ASP.NET lo convierte al rol que entiende RequireRole.
                ["role"] = account.Role,
                [JwtRegisteredClaimNames.Email] = account.Email,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString()
            }
        };

        return new AccessToken(TokenHandler.CreateToken(descriptor), expiresAt);
    }
}
