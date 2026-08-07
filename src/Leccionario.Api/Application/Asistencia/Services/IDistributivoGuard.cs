namespace Leccionario.Api.Application.Asistencia.Services;

/// <summary>
/// Verifica que un docente (cédula del claim <c>sub</c>) tiene la
/// <c>idAsignacion</c> solicitada. Pieza central de la autorización de
/// cplec: ver <c>docs/03 sección 5.2</c>, <c>docs/05 sección "Autorización"</c> y
/// <c>ADR-007</c>.
/// </summary>
/// <remarks>
/// <para>La <c>idProfesor</c> <strong>siempre</strong> proviene del claim
/// <c>sub</c> del JWT; nunca del body ni del query string. El guard no
/// inspecciona el <c>HttpContext</c>: recibe el id ya resuelto por el
/// controller (mismo argumento que <c>AuthService</c>: la capa
/// <c>Application</c> queda libre de <c>Microsoft.AspNetCore.Http</c>).</para>
/// <para>Bypass de inspector: el flag <c>esInspector</c> lo calcula el
/// controller con <c>User.IsInRole("cplec_inspector")</c>. Si es
/// <c>true</c>, el guard <strong>no consulta la BD</strong>: la asignación
/// puede no existir y aun así no lanza. La auditoría de accesos del
/// inspector la escribe <c>AuditMiddleware</c> aguas arriba.</para>
/// <para>Sin caché: la revocación de un distributivo debe ser inmediata.
/// <c>ADR-007</c> lo prohíbe explícitamente.</para>
/// </remarks>
public interface IDistributivoGuard
{
    /// <summary>
    /// Verifica si la <c>idAsignacion</c> pertenece al <c>idProfesor</c>
    /// y está vigente: <c>activo</c> y <c>esActivaAsignacion</c> ambos
    /// <c>1</c> o <c>NULL</c> (tolerar nulos legacy). No lanza.
    /// </summary>
    /// <returns>
    /// <c>true</c> si la asignación existe, pertenece al docente y está
    /// vigente; <c>false</c> en cualquier otro caso (incluida asignación
    /// inexistente).
    /// </returns>
    Task<bool> DocenteTieneAsignacionAsync(
        string idProfesor,
        int idAsignacion,
        CancellationToken ct = default);

    /// <summary>
    /// Verifica la asignación y, si falla para un docente, lanza
    /// <see cref="Leccionario.Api.Application.Common.Exceptions.DistributivoAjenoException"/>
    /// (403 <c>DISTRIBUTIVO_AJENO</c>).
    /// </summary>
    /// <param name="idProfesor">Cédula del docente, tomada del claim <c>sub</c>.</param>
    /// <param name="idAsignacion">Identificador de la asignación a validar.</param>
    /// <param name="esInspector">
    /// <c>true</c> solo si la capa <c>[Authorize(Roles = "cplec_inspector")]</c>
    /// pasó y el controller lo calculó con <c>User.IsInRole</c>. Bypass
    /// explícito: el guard no consulta la BD.
    /// </param>
    /// <param name="ct">Token de cancelación.</param>
    /// <exception cref="Leccionario.Api.Application.Common.Exceptions.DistributivoAjenoException">
    /// Cuando <paramref name="esInspector"/> es <c>false</c> y la asignación
    /// no pertenece al <paramref name="idProfesor"/> o no está vigente.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// Si <paramref name="idProfesor"/> es nulo o vacío (no se puede
    /// determinar el alcance del distributivo).
    /// </exception>
    Task EnsureDocenteTieneAsignacionAsync(
        string idProfesor,
        int idAsignacion,
        bool esInspector,
        CancellationToken ct = default);
}