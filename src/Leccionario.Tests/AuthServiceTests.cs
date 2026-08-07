using FluentAssertions;
using Leccionario.Api.Application.Authenticacion.Auth;
using Leccionario.Api.Application.Common.Exceptions;
using Leccionario.Api.Application.Distributivo;
using Leccionario.Api.Domain.Entities;
using Leccionario.Api.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Leccionario.Tests;

[TestClass]
public sealed class AuthServiceTests
{
    private const string JwtSecret = "this-is-a-test-secret-32-bytes-min!";
    private const string Contrasena = "clave-correcta-123";

    private static sigafi_esContext NewDb() => new(
        new DbContextOptionsBuilder<sigafi_esContext>()
            .UseInMemoryDatabase(databaseName: $"authservicetest-{Guid.NewGuid()}")
            .Options);

    private static IConfiguration NewConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SistemaCodigo"] = "cplec" })
            .Build();

    private static Mock<IRefreshTokenService> NewMockRefreshTokens()
    {
        var mock = new Mock<IRefreshTokenService>();
        mock.Setup(r => r.IssueAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("token-de-refresh-de-prueba", DateTime.UtcNow.AddDays(7)));
        return mock;
    }

    private static AuthService NewService(sigafi_esContext db, Mock<IRefreshTokenService>? refreshTokens = null, Mock<IMisParalelosService>? misParalelos = null)
    {
        var paralelosMock = misParalelos ?? new Mock<IMisParalelosService>();
        paralelosMock.Setup(s => s.ResolverAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Array.Empty<MiParaleloDto>());
        return new(db,
            new JwtTokenService(JwtSecret),
            (refreshTokens ?? NewMockRefreshTokens()).Object,
            paralelosMock.Object,
            NewConfig(),
            NullLogger<AuthService>.Instance);
    }

    /// <summary>Siembra sistema/modulo/operacion/rol-modulo-operacion para que un rol quede "con grants sobre cplec". Devuelve idRol.</summary>
    private static async Task<int> CrearRolCplecAsync(sigafi_esContext db, string codigoRol)
    {
        var sistema = new rbac_sistema { codigo = "cplec", detalle = "Leccionario" };
        db.rbac_sistema.Add(sistema);
        await db.SaveChangesAsync();

        var modulo = new rbac_modulos { id_sistema = sistema.idSistema, Nombre = "asistencia", esActivo = 1 };
        db.rbac_modulos.Add(modulo);
        await db.SaveChangesAsync();

        var operacion = new rbac_operaciones { NombreOperacion = "ver" };
        db.rbac_operaciones.Add(operacion);
        await db.SaveChangesAsync();

        var moduloOperacion = new rbac_modulos_operaciones { idModulos = modulo.idModulos, idOperaciones = operacion.idOperaciones, esActivo = 1 };
        db.rbac_modulos_operaciones.Add(moduloOperacion);
        await db.SaveChangesAsync();

        var rol = new rbac_rol { Nombre = codigoRol, codigo_rol = codigoRol, esActivo = 1 };
        db.rbac_rol.Add(rol);
        await db.SaveChangesAsync();

        db.rbac_rol_modulo_operacion.Add(new rbac_rol_modulo_operacion
        {
            idRol = rol.idRol,
            idModulosOperaciones = moduloOperacion.idModulosOperaciones,
            esActivo = 1
        });
        await db.SaveChangesAsync();

        return rol.idRol;
    }

    private static async Task AsignarRolAsync(sigafi_esContext db, int idUsuario, int idRol)
    {
        db.rbac_usuario_rol.Add(new rbac_usuario_rol { idUsuario = idUsuario, idRol = idRol, esActivo = 1 });
        await db.SaveChangesAsync();
    }

    private static async Task<usuarios> CrearUsuarioActivoAsync(sigafi_esContext db, string idSigafi = "1804567890", string tablaSigafi = "profesor", string? passwordPlano = null)
    {
        var usuario = new usuarios
        {
            idSigafi = idSigafi,
            tablaSigafi = tablaSigafi,
            nombre = "PEREZ, JUAN",
            contrasenia = passwordPlano is null ? PasswordService.Hash(Contrasena) : passwordPlano,
            activo = 1,
            administrador = 0
        };
        db.usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }

    [TestMethod]
    public async Task LoginAsync_UsuarioInexistenteYSinProfesorMatching_MensajeGenerico()
    {
        using var db = NewDb();
        var svc = NewService(db);

        var act = async () => await svc.LoginAsync("9999999999", Contrasena, null, null);

        var ex = await act.Should().ThrowAsync<UnauthorizedAccessException>();
        ex.Which.Message.Should().Be("Credenciales inválidas.");
    }

    [TestMethod]
    public async Task LoginAsync_PasswordIncorrecta_MismoMensajeQueUsuarioInexistente()
    {
        using var db = NewDb();
        await CrearUsuarioActivoAsync(db);
        var svc = NewService(db);

        var act = async () => await svc.LoginAsync("1804567890", "clave-equivocada", null, null);

        var ex = await act.Should().ThrowAsync<UnauthorizedAccessException>();
        ex.Which.Message.Should().Be("Credenciales inválidas.");
    }

    [TestMethod]
    public async Task LoginAsync_CuentaInactivaConCredencialCorrecta_LanzaYNoMigraPassword()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);
        usuario.activo = 0;
        await db.SaveChangesAsync();
        var hashOriginal = usuario.contrasenia;
        var svc = NewService(db);

        var act = async () => await svc.LoginAsync("1804567890", Contrasena, null, null);

        await act.Should().ThrowAsync<CuentaInactivaException>();
        var recargado = await db.usuarios.AsNoTracking().SingleAsync(u => u.idSigafi == "1804567890");
        recargado.contrasenia.Should().Be(hashOriginal);
    }

    [TestMethod]
    public async Task LoginAsync_ProfesorNoEsReal_NoAutoRegistra()
    {
        using var db = NewDb();
        db.profesores.Add(new profesores { idProfesor = "0102030405", clave = Contrasena, esReal = 0, tipoSangre = "O+" });
        await db.SaveChangesAsync();
        var svc = NewService(db);

        var act = async () => await svc.LoginAsync("0102030405", Contrasena, null, null);

        var ex = await act.Should().ThrowAsync<UnauthorizedAccessException>();
        ex.Which.Message.Should().Be("Credenciales inválidas.");
        (await db.usuarios.CountAsync()).Should().Be(0);
    }

    [TestMethod]
    public async Task LoginAsync_UsuarioSinNingunRolCplec_LanzaSinAccesoSistema()
    {
        using var db = NewDb();
        await CrearUsuarioActivoAsync(db);
        var svc = NewService(db);

        var act = async () => await svc.LoginAsync("1804567890", Contrasena, null, null);

        await act.Should().ThrowAsync<SinAccesoSistemaException>();
    }

    [TestMethod]
    public async Task LoginAsync_ConRolCplecDocente_EmiteAccessTokenYRefreshToken()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);
        var idRol = await CrearRolCplecAsync(db, "cplec_docente");
        await AsignarRolAsync(db, usuario.idUsuario, idRol);
        var svc = NewService(db);

        var respuesta = await svc.LoginAsync("1804567890", Contrasena, "Chrome/Win", "10.0.0.5");

        respuesta.AccessToken.Should().NotBeNullOrEmpty();
        respuesta.RefreshToken.Should().Be("token-de-refresh-de-prueba");
        respuesta.ExpiresIn.Should().Be(8 * 3600);
        respuesta.Usuario.IdSigafi.Should().Be("1804567890");
        respuesta.Usuario.TipoUsuario.Should().Be("profesor");
        respuesta.Usuario.Roles.Should().ContainSingle().Which.Should().Be("cplec_docente");
    }

    [TestMethod]
    public async Task LoginAsync_CredencialPlanaEnUsuarios_ValidaSinMigrarHash()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db, passwordPlano: Contrasena); // sin hashear
        var idRol = await CrearRolCplecAsync(db, "cplec_docente");
        await AsignarRolAsync(db, usuario.idUsuario, idRol);
        var svc = NewService(db);

        await svc.LoginAsync("1804567890", Contrasena, null, null);

        var recargado = await db.usuarios.AsNoTracking().SingleAsync(u => u.idSigafi == "1804567890");
        recargado.contrasenia.Should().Be(Contrasena); // docs/03: solo migra el caso "hash centinela"
    }

    [TestMethod]
    public async Task LoginAsync_HashCentinelaConCredencialLegacyProfesor_MigraABcrypt()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db, passwordPlano: PasswordService.GenerarHashCentinela());
        db.profesores.Add(new profesores { idProfesor = "1804567890", clave = Contrasena, esReal = 1, tipoSangre = "O+" });
        var idRol = await CrearRolCplecAsync(db, "cplec_docente");
        await AsignarRolAsync(db, usuario.idUsuario, idRol);
        await db.SaveChangesAsync();
        var svc = NewService(db);

        await svc.LoginAsync("1804567890", Contrasena, null, null);

        var recargado = await db.usuarios.AsNoTracking().SingleAsync(u => u.idSigafi == "1804567890");
        PasswordService.IsHashed(recargado.contrasenia).Should().BeTrue();
        PasswordService.Verify(Contrasena, recargado.contrasenia).Should().BeTrue();
    }

    [TestMethod]
    public async Task LoginAsync_ProfesorNuevoSinRolPreasignado_AutoRegistraPeroRechazaPorSinAcceso()
    {
        using var db = NewDb();
        db.profesores.Add(new profesores { idProfesor = "0708091011", clave = Contrasena, esReal = 1, apellidos = "TORRES", nombres = "MARIA", tipoSangre = "O+" });
        await db.SaveChangesAsync();
        var svc = NewService(db);

        var act = async () => await svc.LoginAsync("0708091011", Contrasena, null, null);

        await act.Should().ThrowAsync<SinAccesoSistemaException>();
        var creado = await db.usuarios.AsNoTracking().SingleAsync(u => u.idSigafi == "0708091011");
        creado.tablaSigafi.Should().Be("profesor");
        PasswordService.Verify(Contrasena, creado.contrasenia).Should().BeTrue();
    }

    [TestMethod]
    public async Task RefreshTokenAsync_TokenInvalido_LanzaUnauthorized()
    {
        using var db = NewDb();
        var mockRefresh = new Mock<IRefreshTokenService>();
        mockRefresh.Setup(r => r.ValidateAndRotateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenValidationResult { Status = RefreshTokenStatus.Invalid });
        var svc = NewService(db, mockRefresh);

        var act = async () => await svc.RefreshTokenAsync("token-invalido", null, null);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [TestMethod]
    public async Task RefreshTokenAsync_TokenValido_EmiteNuevoAccessTokenConRolesActuales()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);
        var idRol = await CrearRolCplecAsync(db, "cplec_inspector");
        await AsignarRolAsync(db, usuario.idUsuario, idRol);

        var mockRefresh = new Mock<IRefreshTokenService>();
        mockRefresh.Setup(r => r.ValidateAndRotateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenValidationResult
            {
                Status = RefreshTokenStatus.Ok,
                IdUsuario = usuario.idUsuario,
                NewRefreshToken = "nuevo-refresh-token",
                NewRefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7)
            });
        var svc = NewService(db, mockRefresh);

        var respuesta = await svc.RefreshTokenAsync("token-viejo", null, null);

        respuesta.AccessToken.Should().NotBeNullOrEmpty();
        respuesta.RefreshToken.Should().Be("nuevo-refresh-token");
        respuesta.ExpiresIn.Should().Be(8 * 3600);
    }

    [TestMethod]
    public async Task RefreshTokenAsync_UsuarioInactivoTrasRotacion_LanzaUnauthorized()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);
        usuario.activo = 0;
        await db.SaveChangesAsync();

        var mockRefresh = new Mock<IRefreshTokenService>();
        mockRefresh.Setup(r => r.ValidateAndRotateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenValidationResult { Status = RefreshTokenStatus.Ok, IdUsuario = usuario.idUsuario, NewRefreshToken = "x" });
        var svc = NewService(db, mockRefresh);

        var act = async () => await svc.RefreshTokenAsync("token", null, null);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [TestMethod]
    public async Task RefreshTokenAsync_UsuarioSinRolCplec_LanzaSinAccesoSistema()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);

        var mockRefresh = new Mock<IRefreshTokenService>();
        mockRefresh.Setup(r => r.ValidateAndRotateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenValidationResult { Status = RefreshTokenStatus.Ok, IdUsuario = usuario.idUsuario, NewRefreshToken = "x" });
        var svc = NewService(db, mockRefresh);

        var act = async () => await svc.RefreshTokenAsync("token", null, null);

        await act.Should().ThrowAsync<SinAccesoSistemaException>();
    }

    [TestMethod]
    public async Task LogoutAsync_DelegaEnRevokeAsyncConRazonLogout()
    {
        using var db = NewDb();
        var mockRefresh = NewMockRefreshTokens();
        var svc = NewService(db, mockRefresh);

        await svc.LogoutAsync("token-a-revocar");

        mockRefresh.Verify(r => r.RevokeAsync("token-a-revocar", RefreshTokenRevokedReason.Logout, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ObtenerMiPerfilAsync_Docente_PuedeReabrirSesionEsFalse()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);
        var idRol = await CrearRolCplecAsync(db, "cplec_docente");
        await AsignarRolAsync(db, usuario.idUsuario, idRol);
        var svc = NewService(db);

        var perfil = await svc.ObtenerMiPerfilAsync(usuario.idUsuario);

        perfil.Usuario.Roles.Should().Contain("cplec_docente");
        perfil.Permisos.PuedeEditarAsistencia.Should().BeTrue();
        perfil.Permisos.PuedeCerrarSesion.Should().BeTrue();
        perfil.Permisos.PuedeReabrirSesion.Should().BeFalse();
        perfil.Permisos.PuedeEliminarAsistencia.Should().BeFalse();
        perfil.Permisos.PuedeDescargarReportes.Should().BeTrue();
        perfil.Paralelos.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ObtenerMiPerfilAsync_Inspector_PuedeReabrirSesionEsTrue()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);
        var idRol = await CrearRolCplecAsync(db, "cplec_inspector");
        await AsignarRolAsync(db, usuario.idUsuario, idRol);
        var svc = NewService(db);

        var perfil = await svc.ObtenerMiPerfilAsync(usuario.idUsuario);

        perfil.Permisos.PuedeReabrirSesion.Should().BeTrue();
        perfil.Paralelos.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ObtenerMiPerfilAsync_UsuarioSinRolCplec_LanzaSinAccesoSistema()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);
        var svc = NewService(db);

        var act = async () => await svc.ObtenerMiPerfilAsync(usuario.idUsuario);

        await act.Should().ThrowAsync<SinAccesoSistemaException>();
    }

    [TestMethod]
    public async Task ObtenerMiPerfilAsync_Docente_DevuelveParalelosDelDistributivo()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);
        var idRol = await CrearRolCplecAsync(db, "cplec_docente");
        await AsignarRolAsync(db, usuario.idUsuario, idRol);

        var mockMisParalelos = new Mock<Leccionario.Api.Application.Distributivo.IMisParalelosService>();
        mockMisParalelos.Setup(s => s.ResolverAsync(usuario.idSigafi, It.IsAny<string?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MiParaleloDto>
            {
                new() { IdAsignacion = 100, IdPeriodo = "OCC2025", Asignatura = "GEOGRAFÍA", TipoLicencia = "TIPO \"C\"", Jornada = "NOCTURNA", Modalidad = "PRESENCIAL", Paralelo = "A", FechaInicial = new DateOnly(2025,10,6), FechaFin = new DateOnly(2025,11,5), TotalAlumnos = 29, SesionesRegistradas = 0, UltimaSesion = null }
            });

        var svc = new AuthService(
            db,
            new JwtTokenService(JwtSecret),
            NewMockRefreshTokens().Object,
            mockMisParalelos.Object,
            NewConfig(),
            NullLogger<AuthService>.Instance);

        var perfil = await svc.ObtenerMiPerfilAsync(usuario.idUsuario);

        perfil.Paralelos.Should().HaveCount(1);
        perfil.Paralelos[0].IdAsignacion.Should().Be(100);
        perfil.Paralelos[0].Asignatura.Should().Be("GEOGRAFÍA");
        perfil.Paralelos[0].TotalAlumnos.Should().Be(29);
    }

    [TestMethod]
    public async Task ObtenerMiPerfilAsync_Inspector_ListaParalelosVacia()
    {
        using var db = NewDb();
        var usuario = await CrearUsuarioActivoAsync(db);
        var idRol = await CrearRolCplecAsync(db, "cplec_inspector");
        await AsignarRolAsync(db, usuario.idUsuario, idRol);

        var svc = NewService(db);

        var perfil = await svc.ObtenerMiPerfilAsync(usuario.idUsuario);

        perfil.Paralelos.Should().BeEmpty();
    }
}
