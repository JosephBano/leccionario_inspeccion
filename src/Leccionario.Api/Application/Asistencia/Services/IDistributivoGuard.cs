namespace Leccionario.Api.Application.Asistencia.Services;

/// <summary>
/// Verifica que un docente (cédula del claim <c>sub</c>) tiene la
/// <c>idAsignacion</c> solicitada. Pieza central de la autorización de
/// cplec: ver docs/03 §5.2, docs/05 §"Autorización" y ADR-005 decisión (b).
/// </summary>
/// <remarks>
/// <para>La implementación real requiere <c>sigafi_esContext</c> y la entidad
/// <c>AsignacionesProfesores</c> — ambas llegan con el PR
/// <c>feature/ef-powertools-scaffold</c> (#2). En el scaffold esta interfaz
/// se declara para que los services la puedan inyectar y los tests la
/// mockeen.</para>
/// <para>Cuando el docente no es dueño: el service debe lanzar
/// <c>DistributivoAjenoException</c> (403). El inspector <strong>no</strong>
/// pasa por este guard (su rol ya se validó por <c>[Authorize]</c>).</para>
/// </remarks>
public interface IDistributivoGuard
{
    /// <summary>
    /// Devuelve <c>true</c> si existe una asignación con
    /// <c>idAsignacion == idAsignacion</c>, <c>idProfesor == idProfesor</c>,
    /// <c>activo = 1</c> y <c>esActivaAsignacion = 1</c>.
    /// </summary>
    Task<bool> DocenteTieneAsignacionAsync(string idProfesor, int idAsignacion, CancellationToken ct = default);
}
