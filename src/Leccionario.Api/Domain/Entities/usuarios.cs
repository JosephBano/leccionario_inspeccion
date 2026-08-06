#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class usuarios
{
    public int idUsuario { get; set; }

    /// <summary>
    /// este es idSifafi\n
    /// </summary>
    public string idSigafi { get; set; } = null!;

    public string tablaSigafi { get; set; } = null!;

    public string? nombre { get; set; }

    public string contrasenia { get; set; } = null!;

    public sbyte activo { get; set; }

    public sbyte administrador { get; set; }

    public string? emailInstitucional { get; set; }

    public sbyte emailValidado { get; set; }

    public string? hashEmailToken { get; set; }

    public DateTime? fechaEmailValidacion { get; set; }

    public virtual ICollection<rbac_refresh_tokens> rbac_refresh_tokens { get; set; } = new List<rbac_refresh_tokens>();

    public virtual ICollection<rbac_usuario_rol> rbac_usuario_rol { get; set; } = new List<rbac_usuario_rol>();
}
