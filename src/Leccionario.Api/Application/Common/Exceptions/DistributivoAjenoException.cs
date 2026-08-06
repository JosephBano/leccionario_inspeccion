namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>
/// 403 DISTRIBUTIVO_AJENO — el docente autenticado no es dueño de la
/// <c>idAsignacion</c> solicitada. Ver docs/03 §5.2 y docs/05 §"Autorización".
/// </summary>
/// <remarks>
/// <para>Se lanza desde un <em>service</em> (no desde el controller) después de
/// que el <c>DistributivoGuard</c> confirma que la asignación no pertenece al
/// <c>idProfesor</c> del claim <c>sub</c>.</para>
/// <para>El HTTP status es 403, no 404: el recurso existe, simplemente no es del
/// docente que pregunta. Devolver 404 filtraría menos el padrón, pero rompe el
/// contrato explícito de la API.</para>
/// </remarks>
public sealed class DistributivoAjenoException : ProhibidoException
{
    public DistributivoAjenoException()
        : base("DISTRIBUTIVO_AJENO", "La asignación solicitada no pertenece a su distributivo.") { }
}
