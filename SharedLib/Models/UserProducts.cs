using System;
using System.Collections.Generic;

namespace SharedLib.Models;

public partial class UserProducts
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int Type { get; set; }

    public string Code { get; set; } = null!;

    public bool Enabled { get; set; }

    public virtual Users User { get; set; } = null!;
}
