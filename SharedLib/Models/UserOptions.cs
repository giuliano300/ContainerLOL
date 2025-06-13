using System;
using System.Collections.Generic;

namespace SharedLib.Models;

public partial class UserOptions
{
    public int Id { get; set; }

    public int OptionId { get; set; }

    public int UserId { get; set; }

    public bool Enabled { get; set; }

    public virtual Users User { get; set; } = null!;
}
