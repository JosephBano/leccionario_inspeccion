#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class rbac_rol
{
    public int idRol { get; set; }

    public string Nombre { get; set; } = null!;

    public string codigo_rol { get; set; } = null!;

    public sbyte? esActivo { get; set; }

    public virtual ICollection<rbac_rol_modulo_operacion> rbac_rol_modulo_operacion { get; set; } = new List<rbac_rol_modulo_operacion>();

    public virtual ICollection<rbac_usuario_rol> rbac_usuario_rol { get; set; } = new List<rbac_usuario_rol>();
}
