using System;
using System.Collections.Generic;

namespace SharedLib.Models;

public partial class UserLogos
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string Logo { get; set; } = null!;

    public int UserId { get; set; }

    public int? ParentUserId { get; set; }

    public virtual Users User { get; set; } = null!;
}
