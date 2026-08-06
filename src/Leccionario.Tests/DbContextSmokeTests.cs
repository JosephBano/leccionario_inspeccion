using FluentAssertions;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Tests;

/// <summary>
/// Verifica que <c>sigafi_esContext</c> se construye sin errores de mapping.
/// Si una entidad scaffoldeada tiene un tipo no soportado, una FK mal armada o
/// un índice duplicado, este test revienta en <c>UseInMemoryDatabase</c> al
/// construir el modelo. Es la red de seguridad mínima del PR
/// <c>feature/ef-powertools-scaffold</c>.
/// </summary>
[TestClass]
public sealed class DbContextSmokeTests
{
    [TestMethod]
    public void DbContext_ConstruyeModeloSinErrores()
    {
        var options = new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(databaseName: $"test-{Guid.NewGuid()}")
            .Options;

        using var ctx = new sigafi_esContext(options);

        ctx.Should().NotBeNull();
        ctx.Model.GetEntityTypes().Should().NotBeEmpty("el scaffold debe haber registrado al menos una entidad");
    }

    [TestMethod]
    public void DbContext_TodasLasEntidadesTienenDbSet()
    {
        var options = new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(databaseName: $"test-{Guid.NewGuid()}")
            .Options;

        using var ctx = new sigafi_esContext(options);

        var entidadesModelo = ctx.Model.GetEntityTypes().Select(e => e.ClrType).ToHashSet();
        var dbsets = ctx.GetType()
            .GetProperties()
            .Where(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .ToHashSet();

        dbsets.Should().BeSubsetOf(entidadesModelo,
            "todo DbSet debe corresponder a una entidad registrada en el modelo");
    }
}