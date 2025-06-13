using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SharedLib.Models;

public partial class Operations
{
    public int Id { get; set; }

    public DateTime InsertDate { get; set; }

    public int UserId { get; set; }

    public int UserParentId { get; set; }

    public int OperationType { get; set; }

    public int NumberOfRecipient { get; set; }

    public string Name { get; set; } = null!;

    public bool Complete { get; set; }

    public bool AreaTestOperation { get; set; }

    public bool Error { get; set; }

    public string? ErrorMessage { get; set; }

    public string? CsvFileName { get; set; }

    public virtual ICollection<Recipients> Recipients { get; set; } = new List<Recipients>();

    public virtual ICollection<Senders> Senders { get; set; } = new List<Senders>();

    [ForeignKey("UserId")]
    public virtual Users Users { get; set; } = null!;
}
