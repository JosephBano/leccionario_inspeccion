using Leccionario.Api.Domain.Entities;

namespace Leccionario.Tests.Builders;

/// <summary>
/// Builder fluido para <see cref="horario_detalle"/>. Patrón de docs/07.
/// </summary>
/// <remarks>
/// <c>idEspacio</c> queda siempre NULL: cplec no gestiona aulas (ADR-008 dec. 4).
/// </remarks>
public sealed class HorarioDetalleBuilder
{
    private int _idHorario = 1;
    private int _idAsignacion = 100;
    private int _idFecha = 500;
    private int _idhora = 900;
    private string? _tipoBloque = "teorico";
    private sbyte? _activo = 1;

    public HorarioDetalleBuilder ConId(int idHorario) { _idHorario = idHorario; return this; }
    public HorarioDetalleBuilder DeAsignacion(int id) { _idAsignacion = id; return this; }
    public HorarioDetalleBuilder EnFecha(int idFecha) { _idFecha = idFecha; return this; }
    public HorarioDetalleBuilder EnFranja(int idhora) { _idhora = idhora; return this; }
    public HorarioDetalleBuilder Activo(sbyte? activo) { _activo = activo; return this; }
    public HorarioDetalleBuilder DeTipoBloque(string? t) { _tipoBloque = t; return this; }

    public horario_detalle Build() => new()
    {
        idHorario = _idHorario,
        idAsignacion = _idAsignacion,
        idFecha = _idFecha,
        idhora = _idhora,
        idEspacio = null,
        tipoBloque = _tipoBloque,
        activo = _activo
    };
}
