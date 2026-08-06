using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Leccionario.Tests;

/// <summary>Implementación mínima de <see cref="IHostEnvironment"/> para tests.</summary>
internal sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Production";
    public string ApplicationName { get; set; } = "Leccionario.Api.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
