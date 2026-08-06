#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

/// <summary>
/// cplec — Asistencia por estudiante y sesion
/// </summary>
public partial class cplec_asistencias
{
    public int idAsistencia { get; set; }

    /// <summary>
    /// FK cplec_sesiones.idSesion
    /// </summary>
    public int idSesion { get; set; }

    /// <summary>
    /// FK matriculas.idMatricula
    /// </summary>
    public int idMatricula { get; set; }

    public string estado { get; set; } = null!;

    /// <summary>
    /// Solo aplica cuando estado = atraso
    /// </summary>
    public ushort? minutosAtraso { get; set; }

    public string? observacion { get; set; }

    /// <summary>
    /// usuarios.idSigafi del autor
    /// </summary>
    public string usuarioCreacion { get; set; } = null!;

    public DateTime fechaCreacion { get; set; }

    public string? usuarioActualiza { get; set; }

    public DateTime? fechaActualizacion { get; set; }

    public virtual matriculas idMatriculaNavigation { get; set; } = null!;

    public virtual cplec_sesiones idSesionNavigation { get; set; } = null!;
}
