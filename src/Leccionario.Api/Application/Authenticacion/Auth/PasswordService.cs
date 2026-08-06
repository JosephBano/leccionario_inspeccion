using System.Security.Cryptography;
using BCrypt.Net;

namespace Leccionario.Api.Application.Authenticacion.Auth;

/// <summary>
/// Servicio de hashing de contraseñas. Port verbatim de
/// <c>BienestarInstitucional.Api/Application/Authenticacion/Auth/PasswordService.cs</c>
/// (cambio de namespace y archivo).
/// </summary>
/// <remarks>
/// <para>Algoritmo: <strong>BCrypt con work factor 12</strong> (no 11 como dice
/// el SECURITY_GUIDE de Bienestar — el código real usa 12; ver docs/03 §2).</para>
/// <para>El <c>GenerarHashCentinela()</c> es 32 bytes aleatorios hasheados con
/// bcrypt — NUNCA constante. Si fuera constante, una filtración del centinela
/// permitiría entrar a cualquier cuenta recién creada. Ver docs/03 §2.</para>
/// </remarks>
public static class PasswordService
{
    /// <summary>Work factor de BCrypt. Coincide con Bienestar y con docs/03.</summary>
    public const int WorkFactor = 12;

    /// <summary>Hashea una contraseña en claro. Devuelve un string con formato <c>$2…</c>.</summary>
    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    /// <summary>Verifica una contraseña contra un hash. Lanza si el hash no tiene formato válido.</summary>
    public static bool Verify(string password, string hash)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(hash);
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }

    /// <summary>Detecta si un valor parece un hash bcrypt (<c>$2a/$2b/$2y</c> + longitud ≥ 50).</summary>
    public static bool IsHashed(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (value.Length < 50) return false;
        // BCrypt empieza con $2a, $2b o $2y.
        return value.StartsWith("$2a$", StringComparison.Ordinal)
            || value.StartsWith("$2b$", StringComparison.Ordinal)
            || value.StartsWith("$2y$", StringComparison.Ordinal);
    }

    /// <summary>
    /// Genera un hash centinela aleatorio. <strong>Cada llamada produce un
    /// valor distinto</strong>; nunca debe ser constante.
    /// </summary>
    public static string GenerarHashCentinela()
    {
        var randomBytes = new byte[32];
        RandomNumberGenerator.Fill(randomBytes);
        // El centinela es la contraseña aleatoria, no el hash.
        // Guardar el hash, no la contraseña, en BD. La contraseña se "desperdicia".
        var randomText = Convert.ToBase64String(randomBytes);
        return Hash(randomText);
    }
}
