using FluentAssertions;
using Leccionario.Api.Application.Horarios;
using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Leccionario.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Tests.Horarios;

/// <summary>
/// Detección de conflictos por solapamiento de rangos. Ver ADR-008 decisión 5.
/// Escenario base: fecha 500, docente DUENIO con la asignación 100 (paralelo A),
/// docente DUENIO también con la asignación 101 (mismo paralelo).
/// </summary>
[TestClass]
public sealed class ConflictoHorarioServiceTests
{
    private const int IdFecha = 500;
    private const int Asig100 = 100;
    private const int Asig101 = 101;
    private const int AsigOtroDocente = 200;
    private const string Duenio = "0000000001";
    private const string Otro = "0000000002";
    private const int NivelC6 = 35;
    private const int NivelOtraCarrera = 77;

    // Franjas Z de cplec
    private const int Z_0700_0900 = 901;
    private const int Z_0800_0845 = 902;
    private const int Z_0900_1000 = 903;
    private const int Z_1000_1100 = 904;
    // Franja X del instituto, solapada con Z_0700_0900
    private const int X_0800_0900 = 12;

    private static sigafi_esContext CrearContexto(string nombreDb) =>
        new(new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(nombreDb).Options);

    /// <summary>Siembra franjas, cursos y asignaciones comunes a todos los tests.</summary>
    private static async Task SembrarCatalogoAsync(sigafi_esContext db)
    {
        db.horas_clases.AddRange(
            new FranjaBuilder().ConId(Z_0700_0900).DeTipo("Z").DeRango("07:00", "09:00").Build(),
            new FranjaBuilder().ConId(Z_0800_0845).DeTipo("Z").DeRango("08:00", "08:45").Build(),
            new FranjaBuilder().ConId(Z_0900_1000).DeTipo("Z").DeRango("09:00", "10:00").Build(),
            new FranjaBuilder().ConId(Z_1000_1100).DeTipo("Z").DeRango("10:00", "11:00").Build(),
            new FranjaBuilder().ConId(X_0800_0900).DeTipo("X").DeRango("08:00", "09:00").Build());

        db.carreras.Add(new carreras { idCarrera = 6, Carrera = "ESCUELA DE CONDUCCION" });
        db.carreras.Add(new carreras { idCarrera = 19, Carrera = "GASTRONOMIA" });
        db.cursos.Add(new cursos { idNivel = NivelC6, idCarrera = 6, Nivel = "TIPO \"C\"" });
        db.cursos.Add(new cursos { idNivel = NivelOtraCarrera, idCarrera = 19, Nivel = "PRIMERO" });

        db.asignaciones_profesores.AddRange(
            new AsignacionBuilder().ConId(Asig100).DelProfesor(Duenio).ConNivel(NivelC6).ConParalelo("A").Build(),
            new AsignacionBuilder().ConId(Asig101).DelProfesor(Duenio).ConNivel(NivelC6).ConParalelo("A").Build(),
            new AsignacionBuilder().ConId(AsigOtroDocente).DelProfesor(Otro).ConNivel(NivelC6).ConParalelo("B").Build());

        db.fechas_horarios.Add(new fechas_horarios
        {
            idFecha = IdFecha, fecha = new DateOnly(2026, 8, 3), dia = "Lunes"
        });

        await db.SaveChangesAsync();
    }

    private static IConflictoHorarioService Crear(sigafi_esContext db) =>
        new ConflictoHorarioService(db);

    // -------------------------------------------------------------------------
    // Geometría del solapamiento
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task BordesQueSeTocan_NoChocan()
    {
        // 09:00–10:00 y 10:00–11:00 son consecutivas, no simultáneas.
        // Es el caso más importante: si esto falla, no se puede armar un bloque.
        using var db = CrearContexto(nameof(BordesQueSeTocan_NoChocan));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig100).EnFecha(IdFecha).EnFranja(Z_0900_1000).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_1000_1100));

        r.HayBloqueantes.Should().BeFalse();
        r.Advertencias.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SolapeParcial_PorLaIzquierda_Choca()
    {
        // Existente 08:00–08:45 dentro de la nueva 07:00–09:00.
        using var db = CrearContexto(nameof(SolapeParcial_PorLaIzquierda_Choca));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig101).EnFecha(IdFecha).EnFranja(Z_0800_0845).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0700_0900));

        r.HayBloqueantes.Should().BeTrue();
    }

    [TestMethod]
    public async Task Contencion_FranjaLargaSobreCorta_Choca()
    {
        // Inversa del anterior: la existente es la larga.
        using var db = CrearContexto(nameof(Contencion_FranjaLargaSobreCorta_Choca));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig101).EnFecha(IdFecha).EnFranja(Z_0700_0900).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0800_0845));

        r.HayBloqueantes.Should().BeTrue();
    }

    [TestMethod]
    public async Task OtraFecha_NoChoca()
    {
        using var db = CrearContexto(nameof(OtraFecha_NoChoca));
        await SembrarCatalogoAsync(db);
        db.fechas_horarios.Add(new fechas_horarios
        {
            idFecha = 501, fecha = new DateOnly(2026, 8, 4), dia = "Martes"
        });
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig101).EnFecha(501).EnFranja(Z_0700_0900).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0700_0900));

        r.HayBloqueantes.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Las cuatro reglas
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task MismaAsignacionSolapada_AsignacionDuplicada_Bloqueante()
    {
        using var db = CrearContexto(nameof(MismaAsignacionSolapada_AsignacionDuplicada_Bloqueante));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig100).EnFecha(IdFecha).EnFranja(Z_0700_0900).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0700_0900));

        r.Bloqueantes.Should().ContainSingle()
            .Which.Tipo.Should().Be("ASIGNACION_DUPLICADA");
    }

    [TestMethod]
    public async Task MismoDocenteEnFranjaZ_DocenteOcupado_Bloqueante()
    {
        // El docente ya está en la asignación 101 a esa hora, en carrera 6.
        // Se puede arreglar desde acá, así que bloquea.
        using var db = CrearContexto(nameof(MismoDocenteEnFranjaZ_DocenteOcupado_Bloqueante));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig101).EnFecha(IdFecha).EnFranja(Z_0700_0900).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0800_0845));

        r.Bloqueantes.Should().Contain(c => c.Tipo == "DOCENTE_OCUPADO");
        r.Bloqueantes.First(c => c.Tipo == "DOCENTE_OCUPADO").IdProfesor.Should().Be(Duenio);
    }

    [TestMethod]
    public async Task MismoParaleloDistintaAsignacion_ParaleloOcupado_Bloqueante()
    {
        // Asig 100 y 101 comparten la 5-tupla del paralelo pero tienen docentes
        // distintos: el grupo de alumnos no puede estar en dos clases a la vez.
        using var db = CrearContexto(nameof(MismoParaleloDistintaAsignacion_ParaleloOcupado_Bloqueante));
        await SembrarCatalogoAsync(db);
        // Reasignamos 101 a otro docente para aislar la regla de paralelo.
        var a101 = await db.asignaciones_profesores.FirstAsync(a => a.idAsignacion == Asig101);
        db.asignaciones_profesores.Remove(a101);
        db.asignaciones_profesores.Add(new AsignacionBuilder()
            .ConId(Asig101).DelProfesor(Otro).ConNivel(NivelC6).ConParalelo("A").Build());
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig101).EnFecha(IdFecha).EnFranja(Z_0700_0900).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0800_0845));

        r.Bloqueantes.Should().Contain(c => c.Tipo == "PARALELO_OCUPADO");
    }

    [TestMethod]
    public async Task MismoDocenteEnFranjaAjena_EsAdvertenciaYNoBloquea()
    {
        // El docente está ocupado en Gastronomía con una franja X. Es un choque
        // real, pero cplec no puede editar el horario ajeno: avisa, no bloquea.
        using var db = CrearContexto(nameof(MismoDocenteEnFranjaAjena_EsAdvertenciaYNoBloquea));
        await SembrarCatalogoAsync(db);
        db.asignaciones_profesores.Add(new AsignacionBuilder()
            .ConId(300).DelProfesor(Duenio).ConNivel(NivelOtraCarrera).ConParalelo("A").Build());
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(300).EnFecha(IdFecha).EnFranja(X_0800_0900).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0700_0900));

        r.HayBloqueantes.Should().BeFalse();
        var adv = r.Advertencias.Should().ContainSingle().Which;
        adv.Tipo.Should().Be("DOCENTE_OCUPADO");
        adv.Severidad.Should().Be(SeveridadConflicto.Advertencia);
        adv.Carrera.Should().Be("GASTRONOMIA");
        adv.FranjaAjena.Should().Be("08:00–09:00");
    }

    [TestMethod]
    public async Task OtroDocenteYOtroParalelo_NoChoca()
    {
        using var db = CrearContexto(nameof(OtroDocenteYOtroParalelo_NoChoca));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(AsigOtroDocente).EnFecha(IdFecha).EnFranja(Z_0700_0900).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0800_0845));

        r.HayBloqueantes.Should().BeFalse();
        r.Advertencias.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Filtros
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task FilaConActivoCero_SeIgnora()
    {
        using var db = CrearContexto(nameof(FilaConActivoCero_SeIgnora));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig100).EnFecha(IdFecha).EnFranja(Z_0700_0900).Activo(0).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0700_0900));

        r.HayBloqueantes.Should().BeFalse();
    }

    [TestMethod]
    public async Task FilaConActivoSucio_NoCuentaComoActiva()
    {
        // `activo` es tinyint(4) y hay basura en la base (existe un 11).
        // Solo `= 1` cuenta como activo (spec H10).
        using var db = CrearContexto(nameof(FilaConActivoSucio_NoCuentaComoActiva));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig100).EnFecha(IdFecha).EnFranja(Z_0700_0900).Activo(11).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0700_0900));

        r.HayBloqueantes.Should().BeFalse();
    }

    [TestMethod]
    public async Task IdHorarioExcluir_NoSeCuentaContraSiMismo()
    {
        // Al editar una celda, su propia fila no es un conflicto.
        using var db = CrearContexto(nameof(IdHorarioExcluir_NoSeCuentaContraSiMismo));
        await SembrarCatalogoAsync(db);
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(7)
            .DeAsignacion(Asig100).EnFecha(IdFecha).EnFranja(Z_0700_0900).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(
            new SolicitudConflictoDto(Asig100, IdFecha, Z_0700_0900, IdHorarioExcluir: 7));

        r.HayBloqueantes.Should().BeFalse();
    }

    [TestMethod]
    public async Task FranjaExistenteConHoraNula_SeDescarta()
    {
        // hora_inicio/hora_fin son DEFAULT NULL. Una franja sin horas no se puede
        // comparar: se descarta en vez de tratarla como 00:00 (spec H3).
        using var db = CrearContexto(nameof(FranjaExistenteConHoraNula_SeDescarta));
        await SembrarCatalogoAsync(db);
        db.horas_clases.Add(new FranjaBuilder().ConId(999).DeTipo("Z").DeRango(null, null).Build());
        db.horario_detalle.Add(new HorarioDetalleBuilder().ConId(1)
            .DeAsignacion(Asig101).EnFecha(IdFecha).EnFranja(999).Build());
        await db.SaveChangesAsync();

        var r = await Crear(db).ValidarAsync(new SolicitudConflictoDto(Asig100, IdFecha, Z_0700_0900));

        r.HayBloqueantes.Should().BeFalse();
    }
}
