using FluentAssertions;
using Leccionario.Api.Application.Horarios.Services;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Tests.Horarios;

[TestClass]
public sealed class EscrituraSerializableTests
{
    private sigafi_esContext _db = null!;

    [TestInitialize]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new sigafi_esContext(options);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
    }

    [TestMethod]
    public async Task EjecutarAsync_EjecutaOperacionConEstrategiaDeEjecucion()
    {
        var sut = new EscrituraSerializable(_db);
        var ejecutado = false;

        var resultado = await sut.EjecutarAsync(ct =>
        {
            ejecutado = true;
            return Task.FromResult(123);
        });

        resultado.Should().Be(123);
        ejecutado.Should().BeTrue();
    }
}
