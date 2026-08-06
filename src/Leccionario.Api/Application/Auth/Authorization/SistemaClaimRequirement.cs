using Microsoft.AspNetCore.Authorization;

namespace Leccionario.Api.Application.Auth.Authorization;

/// <summary>
/// Requirement para el filtro global que valida que el JWT pertenezca al
/// sistema <c>cplec</c>. Satisfecho por
/// <c>SistemaClaimAuthorizationHandler</c>. Ver ADR-006.
/// </summary>
public sealed class SistemaClaimRequirement : IAuthorizationRequirement
{
    /// <summary>Código del sistema esperado en el claim <c>codigo_sistema</c>.</summary>
    public string CodigoSistemaEsperado { get; }

    public SistemaClaimRequirement(string codigoSistemaEsperado = "cplec")
    {
        CodigoSistemaEsperado = codigoSistemaEsperado;
    }
}
