#nullable enable
#pragma warning disable CS8981
﻿using System;
using System.Collections.Generic;

namespace Leccionario.Api.Domain.Entities;

public partial class rbac_sistema
{
    public int idSistema { get; set; }

    public string codigo { get; set; } = null!;

    public string detalle { get; set; } = null!;

    public string? url { get; set; }

    public string? icono { get; set; }

    public virtual ICollection<rbac_modulos> rbac_modulos { get; set; } = new List<rbac_modulos>();
}
