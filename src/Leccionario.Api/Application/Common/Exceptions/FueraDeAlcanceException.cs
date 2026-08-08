namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>
/// 403 FUERA_DE_ALCANCE — la asignación no pertenece a la carrera 6 (Escuela de
/// Conducción), así que cplec no puede escribir su horario.
/// </summary>
/// <remarks>
/// Frontera de datos, no de rol: aplica también al inspector. Ver
/// <c>docs/adr/ADR-008-horarios-en-tabla-compartida.md</c> decisión 2.
/// </remarks>
public sealed class FueraDeAlcanceException : ProhibidoException
{
    public FueraDeAlcanceException()
        : base("FUERA_DE_ALCANCE",
               "La asignación no pertenece a la Escuela de Conducción.") { }
}
