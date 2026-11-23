using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PokeArena.Api.Modules.Identity.Application;
using PokeArena.Api.Modules.Identity.Http;
using PokeArena.Api.Modules.Identity.Infrastructure;

namespace PokeArena.Api.Modules.Identity;

// REGISTRO DEL MÓDULO IDENTIDAD Y ACCESO.
//
// Igual que CatalogModule y LocationsModule: es el único lugar donde se decide qué
// clase concreta (adaptador) atiende cada interfaz (puerto). Program.cs solo llama a
// AddIdentityModule y a MapIdentityEndpoints, y no conoce los detalles.
//
// Cambio del Sprint 2: la validación de JWT que antes estaba en Program.cs se movió
// aquí, porque autenticar es responsabilidad de este módulo. Ahora además se exigen
// emisor y audiencia, para que la API solo acepte credenciales emitidas por ella misma.
public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 1. LEER Y REVISAR LA CONFIGURACIÓN DE JWT.
        // Si falta algo, la API no arranca y dice qué falta. Es mejor fallar al iniciar
        // que aceptar tokens sin poder validarlos bien.
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(jwt.SigningKey))
            throw new InvalidOperationException("Jwt:SigningKey debe configurarse mediante secretos o variables de entorno.");
        if (string.IsNullOrWhiteSpace(jwt.Issuer) || string.IsNullOrWhiteSpace(jwt.Audience))
            throw new InvalidOperationException("Jwt:Issuer y Jwt:Audience deben configurarse (ver appsettings.json).");

        // Las opciones también quedan disponibles por inyección (IOptions<JwtOptions>)
        // para JwtAccessTokenIssuer y GoogleIdentityProvider.
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<GoogleAuthOptions>(configuration.GetSection(GoogleAuthOptions.SectionName));

        // 2. AUTENTICACIÓN: cómo se valida el token que llega en cada solicitud.
        // Es la misma configuración del Sprint 1, ahora con emisor y audiencia obligatorios.
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Firma con nuestra clave: un token de Google (firmado con las claves de
                    // Google) falla aquí.
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    // Emisor y audiencia propios. Antes eran opcionales, ahora son obligatorios.
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    // Los tokens vencidos se rechazan, con un minuto de tolerancia de reloj.
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        // 3. AUTORIZACIÓN: la política AdminOnly del Sprint 1 no cambia.
        services.AddAdminAuthorization();

        // 4. PUERTOS Y ADAPTADORES.
        // Singleton: una sola instancia para toda la aplicación. Se usa en clases que no
        //            guardan datos de una solicitud o que mantienen una caché.
        // Scoped:    una instancia por solicitud HTTP. Se usa en lo que depende del
        //            DbContext, que también es por solicitud.

        // Hora del sistema, inyectable. Se usa al emitir tokens.
        services.AddSingleton(TimeProvider.System);

        // Descarga y cachea las claves públicas de Google (ver GoogleIdentityProvider).
        // Es Singleton para que la caché sobreviva entre solicitudes.
        services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(_ =>
            new ConfigurationManager<OpenIdConnectConfiguration>(
                GoogleIdentityProvider.DiscoveryDocumentUrl,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = true }));

        // Puerto IExternalIdentityProvider → adaptador de Google.
        // Para un segundo proveedor se registraría otro adaptador (patrón Estrategia).
        services.AddSingleton<IExternalIdentityProvider, GoogleIdentityProvider>();

        // Puerto IAccessTokenIssuer → JWT propio.
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        // Puerto IPlayerAccountRepository → PostgreSQL con Entity Framework.
        services.AddScoped<IPlayerAccountRepository, EfPlayerAccountRepository>();

        // Puerto IInitialCollectionGranter: lo registra el módulo Colección (AddCollectionModule),
        // que es quien sabe entregar la colección inicial. Identidad solo conoce la interfaz.
        // Antes, aquí se registraba NoOpInitialCollectionGranter, una versión provisional
        // que no entregaba nada y que ya se eliminó.

        // Casos de uso del módulo.
        services.AddScoped<SignInWithProviderUseCase>();
        services.AddScoped<GetCurrentPlayerUseCase>();

        return services;
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapSignInWithGoogle();
        app.MapGetCurrentPlayer();
        return app;
    }
}
