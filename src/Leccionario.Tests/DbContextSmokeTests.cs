using FluentAssertions;
using Leccionario.Api.Domain.Entities;
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

    [TestMethod]
    public void DbContext_ExponeLasTablasDeHorarios()
    {
        var opciones = new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nameof(DbContext_ExponeLasTablasDeHorarios))
            .Options;
        using var db = new sigafi_esContext(opciones);

        db.horario_detalle.Should().NotBeNull();
        db.horas_clases.Should().NotBeNull();
        db.espacios.Should().NotBeNull();
    }

    [TestMethod]
    public void HorasClases_UsaLosNombresDeColumnaRealesEnSnakeCase()
    {
        // hora_inicio / hora_fin / numero_hora, NO horaInicio / horaFin / numeroHora.
        // gacad usa camelCase en sus DTOs; la columna real es snake_case.
        var props = typeof(horas_clases).GetProperties().Select(p => p.Name).ToArray();

        props.Should().Contain(new[] { "hora_inicio", "hora_fin", "numero_hora", "tipo" });
        props.Should().NotContain("horaInicio");
    }

    [TestMethod]
    public void HorarioDetalle_TieneLasColumnasQueEscribimos()
    {
        var props = typeof(horario_detalle).GetProperties().Select(p => p.Name).ToArray();

        props.Should().Contain(new[]
        {
            "idHorario", "idAsignacion", "idFecha", "idhora", "idEspacio", "tipoBloque", "activo"
        });
    }
}