using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Leccionario.Api.Infrastructure.DbContexts;

/// <summary>
/// Factory usado por <c>dotnet ef</c> y EF Core Power Tools al momento de
/// scaffoldear. <strong>NO</strong> se usa en runtime — el runtime usa el
/// <c>sigafi_esContext</c> registrado en DI
/// (<see cref="Leccionario.Api.Extensions.DependencyInjectionExtensions.AddInfrastructureLayer"/>).
/// Lee la cadena de conexión de <c>appsettings.json</c> o
/// <c>appsettings.Development.json</c> (este último está en
/// <c>.gitignore</c>). En producción se inyecta por variable de entorno.
/// </summary>
public sealed class sigafi_esContextFactory : IDesignTimeDbContextFactory<sigafi_esContext>
{
    public sigafi_esContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("SigafiDb")
            ?? throw new InvalidOperationException(
                "Falta ConnectionStrings:SigafiDb. Configurá appsettings.Development.json o la variable de entorno ConnectionStrings__SigafiDb antes de scaffoldear.");

        var options = new DbContextOptionsBuilder<sigafi_esContext>()
            .UseMySql(connectionString, ServerVersion.Create(new Version(5, 7, 21), Pomelo.EntityFrameworkCore.MySql.Infrastructure.ServerType.MySql))
            .Options;

        return new sigafi_esContext(options);
    }
}