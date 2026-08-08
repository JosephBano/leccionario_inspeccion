using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Application.Horarios.Services;

/// <summary>
/// Catálogo de franjas horarias de cplec (<c>horas_clases</c> con
/// <c>tipo = 'Z'</c>). Ver <c>ADR-008</c> decisión 3.
/// </summary>
public interface IFranjaService
{
    /// <summary>Franjas Z activas, ordenadas por hora de inicio.</summary>
    Task<IReadOnlyList<FranjaDto>> ListarAsync(CancellationToken ct = default);

    Task<FranjaDto> CrearAsync(CrearFranjaDto req, CancellationToken ct = default);
    Task<FranjaDto> ActualizarAsync(int idhora, CrearFranjaDto req, CancellationToken ct = default);

    /// <summary>Borrado lógico. Rechaza si la franja tiene horario activo colgando.</summary>
    Task DesactivarAsync(int idhora, CancellationToken ct = default);
}

/// <inheritdoc cref="IFranjaService"/>
/// <remarks>
/// <para><c>minutos</c> se deriva del rango: es un dato calculable y dejar que el
/// usuario lo teclee solo produce inconsistencias.</para>
/// <para><c>idCarrera</c> e <c>idSeccion</c> quedan NULL. La carrera 6 tiene
/// franjas <c>'C'</c> segmentadas por jornada, pero son de otro sistema; el
/// catálogo Z es plano y el inspector elige en el grid.</para>
/// <para>No se permiten dos franjas Z solapadas: romperían el cálculo de bloques
/// contiguos y ofrecerían horarios imposibles en el grid.</para>
/// </remarks>
public sealed class FranjaService : IFranjaService
{
    private readonly sigafi_esContext _db;
    private readonly IFranjaZGuard _guard;

    public FranjaService(sigafi_esContext db, IFranjaZGuard guard)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    public async Task<IReadOnlyList<FranjaDto>> ListarAsync(CancellationToken ct = default)
    {
        var filas = await _db.horas_clases
            .AsNoTracking()
            .Where(h => h.tipo == FranjaZGuard.TipoCplec && h.activo == 1)
            .ToListAsync(ct);

        return filas
            .Select(Proyectar)
            .Where(f => f is not null)
            .Select(f => f!)
            .OrderBy(f => f.HoraInicio, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<FranjaDto> CrearAsync(CrearFranjaDto req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        var (ini, fin) = ParsearRango(req);
        await EnsureNoSolapaAsync(ini, fin, excluir: null, ct);

        var fila = new horas_clases
        {
            tipo = FranjaZGuard.TipoCplec,
            idCarrera = null,
            idSeccion = null,
            hora_inicio = ini.ToString("HH\\:mm"),
            hora_fin = fin.ToString("HH\\:mm"),
            minutos = (int)(fin - ini).TotalMinutes,
            numero_hora = req.NumeroHora,
            activo = 1
        };

        _db.horas_clases.Add(fila);
        await _db.SaveChangesAsync(ct);

        return Proyectar(fila)!;
    }

    public async Task<FranjaDto> ActualizarAsync(int idhora, CrearFranjaDto req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        await _guard.EnsureEsFranjaZAsync(idhora, ct);

        var (ini, fin) = ParsearRango(req);
        await EnsureNoSolapaAsync(ini, fin, excluir: idhora, ct);

        var fila = await _db.horas_clases.FirstAsync(h => h.idhora == idhora, ct);
        fila.hora_inicio = ini.ToString("HH\\:mm");
        fila.hora_fin = fin.ToString("HH\\:mm");
        fila.minutos = (int)(fin - ini).TotalMinutes;
        fila.numero_hora = req.NumeroHora;
        await _db.SaveChangesAsync(ct);

        return Proyectar(fila)!;
    }

    public async Task DesactivarAsync(int idhora, CancellationToken ct = default)
    {
        await _guard.EnsureEsFranjaZAsync(idhora, ct);

        var enUso = await _db.horario_detalle
            .AsNoTracking()
            .AnyAsync(h => h.idhora == idhora && h.activo == 1, ct);

        if (enUso)
            throw new ConflictoException("FRANJA_EN_USO",
                "La franja tiene horarios activos asignados. Elimínalos antes de desactivarla.");

        var fila = await _db.horas_clases.FirstAsync(h => h.idhora == idhora, ct);
        fila.activo = 0;
        await _db.SaveChangesAsync(ct);
    }

    private static (TimeOnly Inicio, TimeOnly Fin) ParsearRango(CrearFranjaDto req)
    {
        if (!TimeOnly.TryParse(req.HoraInicio, out var ini) || !TimeOnly.TryParse(req.HoraFin, out var fin))
            throw new ValidacionException("Las horas deben tener formato HH:mm.");
        if (ini >= fin)
            throw new ValidacionException("La hora de inicio debe ser anterior a la de fin.");
        return (ini, fin);
    }

    private async Task EnsureNoSolapaAsync(TimeOnly ini, TimeOnly fin, int? excluir, CancellationToken ct)
    {
        var existentes = await _db.horas_clases
            .AsNoTracking()
            .Where(h => h.tipo == FranjaZGuard.TipoCplec && h.activo == 1
                        && (excluir == null || h.idhora != excluir))
            .Select(h => new { h.idhora, h.hora_inicio, h.hora_fin })
            .ToListAsync(ct);

        foreach (var e in existentes)
        {
            if (!TimeOnly.TryParse(e.hora_inicio, out var eIni) ||
                !TimeOnly.TryParse(e.hora_fin, out var eFin))
                continue;

            // Bordes que se tocan no solapan: 07:00–08:00 y 08:00–09:00 conviven.
            if (ini < eFin && eIni < fin)
                throw new ConflictoException("CONFLICTO_HORARIO",
                    $"La franja se solapa con la existente {e.hora_inicio}–{e.hora_fin}.");
        }
    }

    private static FranjaDto? Proyectar(horas_clases h)
    {
        if (!TimeOnly.TryParse(h.hora_inicio, out var ini) || !TimeOnly.TryParse(h.hora_fin, out var fin))
            return null;

        return new FranjaDto(
            h.idhora,
            ini.ToString("HH\\:mm"),
            fin.ToString("HH\\:mm"),
            h.minutos ?? (int)(fin - ini).TotalMinutes,
            h.numero_hora);
    }
}
