using System;
using System.Collections.Generic;

namespace SharedLib.Models;

public partial class Notifications
{
    public int Id { get; set; }

    public string Title { get; set; } = null!;

    public string Description { get; set; } = null!;

    public bool Enabled { get; set; }

    public int NotificationType { get; set; }
}
