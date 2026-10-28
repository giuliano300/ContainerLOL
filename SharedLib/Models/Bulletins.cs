using System;
using System.Collections.Generic;

namespace SharedLib.Models;

public partial class Bulletins
{
    public int Id { get; set; }

    public int RecipientId { get; set; }

    public int ProductType { get; set; }

    public string? NumeroContoCorrente { get; set; }

    public string? IBAN { get; set; }
    public string? IntestatoA { get; set; }

    public string? ImportoEuro { get; set; }

    public string? EseguitoDaNominativo { get; set; }

    public string? EseguitoDaIndirizzo { get; set; }

    public string? EseguitoDaLocalita { get; set; }

    public string? AnnoDiRiferimento { get; set; }

    public string? EseguitoDaCap { get; set; }

    public string? CodiceCliente { get; set; }

    public string? Causale { get; set; }

    public DateOnly? PaymentDate { get; set; }

    public bool? Paid { get; set; }

    public virtual Recipients Recipient { get; set; } = null!;
}
