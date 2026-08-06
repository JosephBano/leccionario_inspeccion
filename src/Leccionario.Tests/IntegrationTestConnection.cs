using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Leccionario.Api.Application.Authenticacion.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Leccionario.Tests;

/// <summary>
/// Helper para saltar tests de integración cuando no hay MySQL configurado.
/// Portado de BienestarInstitucional.Tests/IntegrationTestConnection.cs.
/// </summary>
public static class IntegrationTestConnection
{
    /// <summary>
    /// Devuelve <c>true</c> si se puede obtener una cadena de conexión a
    /// <c>sigafi_es</c> desde <c>SIGAFI_INTEGRATION_CONNECTION</c> o desde
    /// <c>appsettings.IntegrationTests.json</c>.
    /// </summary>
    public static bool TryGetSigafiConnectionString(out string? connectionString)
    {
        connectionString = Environment.GetEnvironmentVariable("SIGAFI_INTEGRATION_CONNECTION");
        if (!string.IsNullOrEmpty(connectionString)) return true;

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.IntegrationTests.json", optional: true)
            .Build();

        connectionString = config["ConnectionStrings:SigafiDb"];
        return !string.IsNullOrEmpty(connectionString);
    }
}
