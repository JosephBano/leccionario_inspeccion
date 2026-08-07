using FluentAssertions;
using Leccionario.Api.Application.Authenticacion.Auth;

namespace Leccionario.Tests;

/// <summary>
/// Tests de <see cref="PasswordService"/>. Portados de
/// <c>BienestarInstitucional.Tests/PasswordServiceGenerarHashCentinelaTests.cs</c>
/// y <c>PasswordServiceTests.cs</c>.
/// </summary>
[TestClass]
public sealed class PasswordServiceTests
{
    private const string Contrasena = "ClaveSegura!2026";

    [TestMethod]
    public void Hash_ProduceHashBcryptValido()
    {
        var hash = PasswordService.Hash(Contrasena);

        hash.Should().NotBeNullOrEmpty();
        hash.Should().StartWith("$2");   // $2a, $2b, $2y
        hash.Length.Should().BeGreaterThanOrEqualTo(50);
    }

    [TestMethod]
    public void Hash_DosLlamadas_ProducenHashesDistintos()
    {
        var h1 = PasswordService.Hash(Contrasena);
        var h2 = PasswordService.Hash(Contrasena);

        h1.Should().NotBe(h2, "bcrypt usa sal aleatoria");
    }

    [TestMethod]
    public void Verify_ContrasenaCorrecta_DevuelveTrue()
    {
        var hash = PasswordService.Hash(Contrasena);
        PasswordService.Verify(Contrasena, hash).Should().BeTrue();
    }

    [TestMethod]
    public void Verify_ContrasenaIncorrecta_DevuelveFalse()
    {
        var hash = PasswordService.Hash(Contrasena);
        PasswordService.Verify("otra-clave", hash).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void Hash_EntradaVacia_Lanza(string? entrada)
    {
        Action act = () => PasswordService.Hash(entrada!);
        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void IsHashed_HashBcrypt_Reconocido()
    {
        var hash = PasswordService.Hash(Contrasena);
        PasswordService.IsHashed(hash).Should().BeTrue();
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("123")]
    [DataRow("admin")]
    public void IsHashed_TextoPlanoONuloOVacio_Rechazado(string? valor)
    {
        PasswordService.IsHashed(valor).Should().BeFalse();
    }

    [TestMethod]
    public void GenerarHashCentinela_DosLlamadas_DistintosHashes()
    {
        var c1 = PasswordService.GenerarHashCentinela();
        var c2 = PasswordService.GenerarHashCentinela();

        c1.Should().NotBe(c2, "el centinela es aleatorio por diseño (ver docs/03 sección 2)");
        c1.Should().StartWith("$2");
        c2.Should().StartWith("$2");
    }

    [TestMethod]
    [DataRow("123456")]
    [DataRow("admin")]
    [DataRow("password")]
    [DataRow("clave")]
    public void GenerarHashCentinela_NoVerificaContraContrasenasProbables(string probable)
    {
        var centinela = PasswordService.GenerarHashCentinela();
        PasswordService.Verify(probable, centinela).Should().BeFalse();
    }
}
