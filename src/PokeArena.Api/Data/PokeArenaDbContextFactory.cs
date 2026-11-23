using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace PokeArena.Api.Data;

// FACTORÍA DE DISEÑO DE ENTITY FRAMEWORK.
// dotnet ef usa esta clase para crear PokeArenaDbContext al generar o aplicar
// migraciones, sin tener que iniciar el servidor ni ejecutar los endpoints.
public sealed class PokeArenaDbContextFactory : IDesignTimeDbContextFactory<PokeArenaDbContext>
{
    public PokeArenaDbContext CreateDbContext(string[] args)
    {
        // Lee la misma cadena de conexión que usa Program.cs, sin iniciar la API.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Devuelve un contexto configurado con el proveedor PostgreSQL del proyecto.
        var options = new DbContextOptionsBuilder<PokeArenaDbContext>()
            .UseNpgsql(configuration.GetConnectionString("DefaultConnection"))
            .Options;

        return new PokeArenaDbContext(options);
    }
}