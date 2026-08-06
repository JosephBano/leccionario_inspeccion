#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class rbac_usuario_rol
{
    public int idUsuarioRol { get; set; }

    public int idUsuario { get; set; }

    public int idRol { get; set; }

    public DateOnly? fecha_creacion { get; set; }

    public DateOnly? fecha_modificacion { get; set; }

    public sbyte? esActivo { get; set; }

    public virtual rbac_rol idRolNavigation { get; set; } = null!;

    public virtual usuarios idUsuarioNavigation { get; set; } = null!;
}
