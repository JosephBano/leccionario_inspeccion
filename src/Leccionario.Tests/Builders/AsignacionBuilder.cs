using Leccionario.Api.Domain.Entities;

namespace Leccionario.Tests.Builders;

/// <summary>
/// Builder fluido para <see cref="asignaciones_profesores"/>. Permite declarar
/// solo los campos relevantes en cada test sin repetir código de setup.
/// Patrón de docs/07 §"Builders".
/// </summary>
/// <remarks>
/// IDs ficticios y rangos plausibles pero reconocibles (0000000001…). No usar
/// datos reales de <c>sigafi_es</c> en tests.
/// </remarks>
public sealed class AsignacionBuilder
{
    private int _idAsignacion = 100;
    private string _idProfesor = "0000000001";
    private string _idPeriodo = "TEST0001";
    private int _idNivel = 35;
    private int _idSeccion = 1;
    private int _idModalidad = 1;
    private int _idAsignatura = 1;
    private string _paralelo = "A";
    private sbyte? _activo = 1;
    private sbyte? _esActivaAsignacion = 1;
    private DateOnly? _fechaInicial = new DateOnly(2026, 8, 1);
    private DateOnly? _fechaFin = new DateOnly(2026, 12, 31);

    public AsignacionBuilder ConId(int idAsignacion)
    {
        _idAsignacion = idAsignacion;
        return this;
    }

    public AsignacionBuilder DelProfesor(string idProfesor)
    {
        _idProfesor = idProfesor;
        return this;
    }

    public AsignacionBuilder EnPeriodo(string idPeriodo)
    {
        _idPeriodo = idPeriodo;
        return this;
    }

    public AsignacionBuilder ConNivel(int idNivel)
    {
        _idNivel = idNivel;
        return this;
    }

    public AsignacionBuilder ConParalelo(string paralelo)
    {
        _paralelo = paralelo;
        return this;
    }

    public AsignacionBuilder Activo(sbyte? activo)
    {
        _activo = activo;
        return this;
    }

    public AsignacionBuilder EsActivaAsignacion(sbyte? esActiva)
    {
        _esActivaAsignacion = esActiva;
        return this;
    }

    public AsignacionBuilder ConRango(DateOnly? fechaInicial, DateOnly? fechaFin)
    {
        _fechaInicial = fechaInicial;
        _fechaFin = fechaFin;
        return this;
    }

    public asignaciones_profesores Build() => new()
    {
        idAsignacion = _idAsignacion,
        idProfesor = _idProfesor,
        idPeriodo = _idPeriodo,
        idNivel = _idNivel,
        idSeccion = _idSeccion,
        idModalidad = _idModalidad,
        idAsignatura = _idAsignatura,
        paralelo = _paralelo,
        activo = _activo,
        esActivaAsignacion = _esActivaAsignacion,
        fecha_inicial = _fechaInicial,
        fecha_fin = _fechaFin
    };
}