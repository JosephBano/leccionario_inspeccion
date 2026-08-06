#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class rbac_modulos
{
    public int idModulos { get; set; }

    public int id_sistema { get; set; }

    public string? Nombre { get; set; }

    public sbyte? esActivo { get; set; }

    public virtual rbac_sistema id_sistemaNavigation { get; set; } = null!;

    public virtual ICollection<rbac_modulos_operaciones> rbac_modulos_operaciones { get; set; } = new List<rbac_modulos_operaciones>();
}
