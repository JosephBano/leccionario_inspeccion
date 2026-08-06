#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class gest_audit_registros
{
    public long idAuditRegistros { get; set; }

    public DateTime fechaHora { get; set; }

    public string codigoSistema { get; set; } = null!;

    public string idUsuario { get; set; } = null!;

    public string? rol { get; set; }

    public string idModulo { get; set; } = null!;

    public string accion { get; set; } = null!;

    public int? idEntidad { get; set; }

    public string? tablaAfectada { get; set; }

    public string? descripcion { get; set; }

    public string? datosAnteriores { get; set; }

    public string? datosNuevos { get; set; }

    public string? ipOrigen { get; set; }

    public string? userAgent { get; set; }

    public string? jti { get; set; }

    public string? requestMethod { get; set; }

    public string? requestPath { get; set; }

    public int? statusCode { get; set; }

    public string? mensajeError { get; set; }

    public int? duracionMs { get; set; }
}
