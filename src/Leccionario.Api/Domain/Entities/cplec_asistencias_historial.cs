#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

/// <summary>
/// cplec — Auditoria de cambios de asistencia
/// </summary>
public partial class cplec_asistencias_historial
{
    public ulong idHistorial { get; set; }

    public int idAsistencia { get; set; }

    /// <summary>
    /// Desnormalizado: sobrevive al borrado en cascada
    /// </summary>
    public int idSesion { get; set; }

    /// <summary>
    /// Desnormalizado por el mismo motivo
    /// </summary>
    public int idMatricula { get; set; }

    /// <summary>
    /// NULL = alta inicial
    /// </summary>
    public string? estadoAnterior { get; set; }

    public string estadoNuevo { get; set; } = null!;

    public string? motivo { get; set; }

    /// <summary>
    /// usuarios.idSigafi tomado del JWT
    /// </summary>
    public string usuario { get; set; } = null!;

    /// <summary>
    /// cplec_docente | cplec_inspector
    /// </summary>
    public string? rol { get; set; }

    public string? ipAddress { get; set; }

    public DateTime fecha { get; set; }
}
