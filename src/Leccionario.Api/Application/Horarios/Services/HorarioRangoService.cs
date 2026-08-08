using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>
/// Replica, edita o elimina todas las ocurrencias de
/// <c>(idAsignacion, díaSemana, idhora)</c> en un rango de fechas.
/// Ver <c>ADR-008</c> decisión 11 y spec sección 5.6.
/// </summary>
public interface IHorarioRangoService
{
    Task<ResultadoRangoDto> ReplicarAsync(
        OperacionRangoDto req, int? maxFilas = null, CancellationToken ct = default);

    Task<ResultadoRangoDto> ActualizarAsync(
        OperacionRangoDto req, int? maxFilas = null, CancellationToken ct = default);

    Task<ResultadoRangoDto> EliminarAsync(
        OperacionRangoDto req, int? maxFilas = null, CancellationToken ct = default);
}

/// <inheritdoc cref="IHorarioRangoService"/>
/// <remarks>
/// <para><b>El rango siempre es explícito.</b> No existe "replicar todo el
/// período": los módulos de la carrera 6 llegan a 408 días y replicar los siete
/// paralelos vigentes generaría ~6 940 filas contra las ~1 781 que tiene
/// <c>horario_detalle</c> entera, con datos de otras cuatro carreras adentro.</para>
/// <para>Los topes son constantes, no configuración. El parámetro
/// <c>maxFilas</c> existe únicamente para poder ejercitar el tope en tests sin
/// sembrar 500 fechas; el controller nunca lo pasa.</para>
/// <para>Superadas las validaciones, <b>el lote no falla entero</b>: cada fecha
/// se reporta por separado. Una fecha ausente del calendario o un conflicto no
/// deben tirar abajo las otras quince semanas de trabajo del inspector.</para>
/// </remarks>
public sealed class HorarioRangoService : IHorarioRangoService
{
    /// <summary>Semanas máximas por operación (inclusivo).</summary>
    public const int MaxSemanas = 16;

    /// <summary>Filas máximas que una sola operación puede crear.</summary>
    public const int MaxFilasPorLote = 500;

    private static readonly string[] DiasSemana =
        ["Lunes", "Martes", "Miercoles", "Jueves", "Viernes", "Sabado", "Domingo"];

    private readonly sigafi_esContext _db;
    private readonly IHorarioService _horarios;
    private readonly IHorarioCarreraGuard _carrera;
    private readonly IFranjaZGuard _franjaZ;

    private enum ModoRango
    {
        Replicar,
        Actualizar,
        Eliminar
    }

    public HorarioRangoService(
        sigafi_esContext db, IHorarioService horarios,
        IHorarioCarreraGuard carrera, IFranjaZGuard franjaZ)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _horarios = horarios ?? throw new ArgumentNullException(nameof(horarios));
        _carrera = carrera ?? throw new ArgumentNullException(nameof(carrera));
        _franjaZ = franjaZ ?? throw new ArgumentNullException(nameof(franjaZ));
    }

    public Task<ResultadoRangoDto> ReplicarAsync(
        OperacionRangoDto req, int? maxFilas = null, CancellationToken ct = default) =>
        EjecutarAsync(req, maxFilas, ModoRango.Replicar, ct);

    public Task<ResultadoRangoDto> ActualizarAsync(
        OperacionRangoDto req, int? maxFilas = null, CancellationToken ct = default) =>
        EjecutarAsync(req, maxFilas, ModoRango.Actualizar, ct);

    public Task<ResultadoRangoDto> EliminarAsync(
        OperacionRangoDto req, int? maxFilas = null, CancellationToken ct = default) =>
        EjecutarAsync(req, maxFilas, ModoRango.Eliminar, ct);

    private async Task<ResultadoRangoDto> EjecutarAsync(
        OperacionRangoDto req, int? maxFilas, ModoRango modo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);

        var indiceDia = Array.FindIndex(DiasSemana,
            d => string.Equals(d, req.Dia, StringComparison.OrdinalIgnoreCase));
        if (indiceDia < 0)
            throw new ValidacionException(
                $"Día inválido: '{req.Dia}'. Valores válidos: {string.Join(", ", DiasSemana)}.");

        await _carrera.EnsureAsignacionEsDeCarrera6Async(req.IdAsignacion, ct);
        await _franjaZ.EnsureEsFranjaZAsync(req.Idhora, ct);

        var advertencia = await ValidarRangoAsync(req, ct);

        // Fechas del rango que caen en el día de semana pedido.
        var objetivo = (DayOfWeek)((indiceDia + 1) % 7);
        var fechas = new List<DateOnly>();
        for (var f = req.Desde; f <= req.Hasta; f = f.AddDays(1))
            if (f.DayOfWeek == objetivo)
                fechas.Add(f);

        // Conteo previo: se rechaza ANTES de escribir o abrir transacción alguna.
        var tope = maxFilas ?? MaxFilasPorLote;
        if (fechas.Count > tope)
            throw new ConflictoLoteException("LOTE_EXCEDE_TOPE",
                $"La operación generaría {fechas.Count} filas y el máximo es {tope}. " +
                "Reduce el rango y repite la operación por tramos.");

        var calendario = await _db.fechas_horarios
            .AsNoTracking()
            .Where(f => f.fecha != null && f.fecha >= req.Desde && f.fecha <= req.Hasta)
            .ToDictionaryAsync(f => f.fecha!.Value, f => f.idFecha, ct);

        var detalles = new List<DetalleOperacionDto>(fechas.Count);

        foreach (var fecha in fechas)
        {
            if (!calendario.TryGetValue(fecha, out var idFecha))
            {
                detalles.Add(new DetalleOperacionDto(fecha, false,
                    "La fecha no existe en el calendario institucional.", null));
                continue;
            }

            try
            {
                switch (modo)
                {
                    case ModoRango.Eliminar:
                    {
                        var fila = await _db.horario_detalle.FirstOrDefaultAsync(h =>
                            h.idAsignacion == req.IdAsignacion && h.idFecha == idFecha
                            && h.idhora == req.Idhora && h.activo == 1, ct);

                        if (fila is null)
                        {
                            detalles.Add(new DetalleOperacionDto(fecha, false, "No había horario ese día.", null));
                            break;
                        }

                        await _horarios.DesactivarAsync(fila.idHorario, ct);
                        detalles.Add(new DetalleOperacionDto(fecha, true, null, fila.idHorario));
                        break;
                    }
                    case ModoRango.Actualizar:
                    {
                        var fila = await _db.horario_detalle.FirstOrDefaultAsync(h =>
                            h.idAsignacion == req.IdAsignacion && h.idFecha == idFecha
                            && h.idhora == req.Idhora && h.activo == 1, ct);

                        if (fila is null)
                        {
                            detalles.Add(new DetalleOperacionDto(fecha, false, "No había horario ese día.", null));
                            break;
                        }

                        // Solo tipoBloque: mover día u hora es reasignación, fuera de alcance de M4b.
                        fila.tipoBloque = req.TipoBloque;
                        await _db.SaveChangesAsync(ct);
                        detalles.Add(new DetalleOperacionDto(fecha, true, null, fila.idHorario));
                        break;
                    }
                    case ModoRango.Replicar:
                    default:
                    {
                        var r = await _horarios.CrearAsync(new CrearCeldaDto(
                            req.IdAsignacion, idFecha, req.Idhora, req.TipoBloque,
                            req.ConfirmarAdvertencias), ct);
                        detalles.Add(new DetalleOperacionDto(fecha, true, null, r.IdHorario));
                        break;
                    }
                }
            }
            catch (AppException ex)
            {
                // Un conflicto o una advertencia no confirmada en un día concreto
                // se reporta y no aborta las demás semanas.
                detalles.Add(new DetalleOperacionDto(fecha, false, ex.Message, null));
            }
        }

        return new ResultadoRangoDto(
            detalles.Count,
            detalles.Count(d => d.Exitoso),
            detalles.Count(d => !d.Exitoso),
            detalles,
            advertencia);
    }

    /// <returns>La advertencia a propagar, o null.</returns>
    private async Task<string?> ValidarRangoAsync(OperacionRangoDto req, CancellationToken ct)
    {
        if (req.Desde > req.Hasta)
            throw new ConflictoLoteException("RANGO_INVALIDO",
                "La fecha inicial debe ser anterior o igual a la final.");

        var dias = req.Hasta.DayNumber - req.Desde.DayNumber + 1;
        if (dias > MaxSemanas * 7)
            throw new ConflictoLoteException("RANGO_EXCEDE_TOPE",
                $"El rango abarca {dias} días y el máximo por operación es {MaxSemanas} semanas. " +
                "Divide la carga en tramos.");

        var ap = await _db.asignaciones_profesores
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.idAsignacion == req.IdAsignacion, ct)
            ?? throw new NoEncontradoException("La asignación solicitada no existe.");

        if (ap.fecha_inicial is null || ap.fecha_fin is null)
            return "ASIGNACION_SIN_VENTANA";

        if (req.Desde < ap.fecha_inicial.Value || req.Hasta > ap.fecha_fin.Value)
            throw new FueraDeVentanaException(ap.fecha_inicial, ap.fecha_fin);

        return null;
    }
}

/// <summary>422 — la operación por rango no cumple sus topes o su rango.</summary>
public sealed class ConflictoLoteException : AppException
{
    public ConflictoLoteException(string codigo, string mensaje)
        : base(codigo, 422, mensaje) { }
}
