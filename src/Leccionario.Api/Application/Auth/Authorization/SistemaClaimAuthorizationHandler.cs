using Microsoft.AspNetCore.Authorization;

namespace Leccionario.Api.Application.Auth.Authorization;

/// <summary>
/// Handler de autorización que valida que el JWT presente el claim
/// <c>codigo_sistema</c> con el valor esperado (por defecto <c>"cplec"</c>).
/// </summary>
/// <remarks>
/// <para>Se registra como <em>singleton</em>: es <strong>stateless</strong> y no
/// debe acceder a servicios scoped (BD, HTTP context). Si en el futuro
/// necesita acceso a BD, pasarlo a <c>AddScoped</c>.</para>
/// <para>Si el claim falta o no coincide, el handler <strong>no</strong> llama
/// <c>context.Succeed</c>; eso es un <c>Fail</c> implícito que el pipeline de
/// autorización traduce a 403. Si la verificación misma lanza una excepción
/// (bug), el pipeline responde 500: un fallo de seguridad no es un éxito
/// silencioso. Ver ADR-006 sección "Consecuencias positivas".</para>
/// </remarks>
public sealed class SistemaClaimAuthorizationHandler
    : AuthorizationHandler<SistemaClaimRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SistemaClaimRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        // Importante: el cliente de JWT (Microsoft.AspNetCore.Authentication.JwtBearer)
        // mapea el claim "sub" a ClaimTypes.NameIdentifier, pero los demás claims
        // personalizados ("codigo_sistema", "uid", "jti", etc.) se preservan con
        // el nombre original. Por seguridad, leemos directamente el nombre.
        var claim = context.User?.FindFirst("codigo_sistema")?.Value;

        // Comparación Ordinal: el codigo_sistema es siempre lowercase por
        // convención. Tolerar mayúsculas abriría la puerta a confusiones.
        if (!string.IsNullOrEmpty(claim)
            && string.Equals(claim, requirement.CodigoSistemaEsperado, StringComparison.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
