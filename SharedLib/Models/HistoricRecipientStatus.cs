using System;
using System.Collections.Generic;

namespace SharedLib.Models;

public partial class HistoricRecipientStatus
{
    public int Id { get; set; }

    public int RecipientId { get; set; }

    public DateTime InsertDate { get; set; }

    public string? Message { get; set; }

    public virtual Recipients Recipient { get; set; } = null!;
}
