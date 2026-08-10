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

    /// <summary>
    /// <c>horario_detalle.idhora</c> es la FK real hacia <c>horas_clases.idhora</c>
    /// (verificado contra <c>sigafi_es</c>: <c>KEY fk_horario_detalle_horas_clases1_idx (idhora)</c>).
    /// Sin esta declaración explícita, EF Core 8 descubre la relación por convención
    /// (vía la nav de colección <c>horas_clases.horario_detalle</c>) y crea una
    /// shadow property <c>horas_clasesidhora</c> que NO existe en la tabla, reventando
    /// cualquier <c>SELECT</c> contra MySQL con
    /// <c>Unknown column 'h.horas_clasesidhora' in 'field list'</c>.
    /// Ver <see cref="HorarioService"/> (replicar / editar celda) y
    /// <see cref="ConflictoHorarioService"/>.
    /// </summary>
    [TestMethod]
    public void HorarioDetalle_FkAHorasClases_EstaDeclaradaEnIdhora()
    {
        var options = new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(databaseName: nameof(HorarioDetalle_FkAHorasClases_EstaDeclaradaEnIdhora))
            .Options;
        using var db = new sigafi_esContext(options);

        var entityType = db.Model.FindEntityType(typeof(horario_detalle))!;
        var fkHorasClases = entityType.GetForeignKeys()
            .SingleOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(horas_clases));

        fkHorasClases.Should().NotBeNull(
            "horario_detalle debe tener una FK declarada hacia horas_clases; " +
            "sin esta declaración, EF Core 8 crea una shadow property inexistente en MySQL");

        fkHorasClases!.Properties
            .Select(p => p.Name)
            .Should()
            .BeEquivalentTo(new[] { "idhora" },
                "la FK debe usar la columna real idhora; " +
                "el nombre 'horas_clasesidhora' lo genera la convención y NO existe en la tabla");
    }

    /// <summary>
    /// Verifica que <c>horario_detalle</c> NO tiene shadow properties generadas por
    /// convención. Si aparece alguna, su columna no existe en MySQL y la consulta falla.
    /// Esta es la red de seguridad que el scaffold original no tenía: el smoke test
    /// <c>DbContext_ConstruyeModeloSinErrores</c> acepta shadow properties sin quejarse.
    /// </summary>
    [TestMethod]
    public void HorarioDetalle_NoTieneShadowProperties()
    {
        var options = new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(databaseName: nameof(HorarioDetalle_NoTieneShadowProperties))
            .Options;
        using var db = new sigafi_esContext(options);

        var entityType = db.Model.FindEntityType(typeof(horario_detalle))!;
        var shadowProps = entityType.GetProperties()
            .Where(p => p.IsShadowProperty())
            .Select(p => p.Name)
            .ToArray();

        shadowProps.Should().BeEmpty(
            "horario_detalle no debe tener shadow properties: cada columna debe corresponder " +
            "a una propiedad real de la entidad. Shadow property típica detectada: 'horas_clasesidhora'.");
    }
}