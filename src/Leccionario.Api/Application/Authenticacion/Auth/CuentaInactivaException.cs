namespace Leccionario.Api.Application.Authenticacion.Auth;

/// <summary>
/// 401 con código <c>CUENTA_INACTIVA</c> — la credencial es correcta pero
/// <c>usuarios.activo = 0</c>. Port verbatim de
/// <c>BienestarInstitucional.Api/Application/Authenticacion/Auth/CuentaInactivaException.cs</c>.
/// </summary>
/// <remarks>
/// Crítico: esta excepción <strong>solo</strong> se lanza DESPUÉS de validar la
/// credencial. Si se validara <c>activo</c> antes, una cuenta inactiva caería en
/// el camino de auto-registro y chocar contra el UNIQUE (1062) → 500. Además
/// permitiría enumerar el padrón de cuentas desactivadas. Ver docs/03 §2.
/// </remarks>
public sealed class CuentaInactivaException : Exception
{
    public CuentaInactivaException()
        : base("Cuenta inactiva. Contacte al administrador.") { }
}
