namespace Leccionario.Api.Application.Common.Exceptions;

/// <summary>
/// 403 SIN_ACCESO_SISTEMA — el usuario autenticó correctamente pero no tiene
/// ningún rol activo con permisos sobre el sistema <c>cplec</c>. Ver docs/03 §2
/// paso 3 y docs/04 §"Formato de error".
/// </summary>
public sealed class SinAccesoSistemaException : ProhibidoException
{
    public SinAccesoSistemaException()
        : base("SIN_ACCESO_SISTEMA", "No tienes acceso a esta aplicación.") { }
}
