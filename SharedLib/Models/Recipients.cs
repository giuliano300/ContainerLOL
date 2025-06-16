using System;
using System.Collections.Generic;

namespace SharedLib.Models;

public partial class Recipients
{
    public int Id { get; set; }

    public DateTime InsertDate { get; set; }

    public int OperationId { get; set; }

    public int? LogoId { get; set; }

    public int ProductType { get; set; }

    public string BusinessName { get; set; } = null!;

    public string? ComplementName { get; set; }

    public string Address { get; set; } = null!;

    public string? ComplementAddress { get; set; }

    public string ZipCode { get; set; } = null!;

    public string City { get; set; } = null!;

    public string Province { get; set; } = null!;

    public string State { get; set; } = null!;

    public int CurrentState { get; set; }

    public bool Valid { get; set; }

    public int Format { get; set; }

    public int PrintType { get; set; }

    public int FrontBack { get; set; }

    public string? PosteType { get; set; }

    public bool ReturnReceipt { get; set; }

    public string? Code { get; set; }

    public string? CodiceAgolAr { get; set; }

    public int? NumberOfPages { get; set; }

    public decimal? Price { get; set; }

    public decimal? VatPrice { get; set; }

    public decimal? TotalPrice { get; set; }

    public byte[]? AttachedFile { get; set; }

    public string? FileName { get; set; }

    public string? PathRecoveryFile { get; set; }

    public string? PathGedurl { get; set; }

    public bool? DigitalReturnReceipt { get; set; }

    public string? Message { get; set; }

    public string? Tag1 { get; set; }

    public string? Tag2 { get; set; }

    public string? Tag3 { get; set; }

    public string? Tag4 { get; set; }

    public string? Tag5 { get; set; }

    public string? Tag6 { get; set; }

    public bool? Notified { get; set; }
    public bool? InProcess { get; set; }

    public bool FromApi { get; set; }

    public int? TipologiaNotificante { get; set; }

    public string? ValoreNotificante { get; set; }

    public string? Pec { get; set; }

    public string? TelegramText { get; set; }

    public string? Vat { get; set; }

    public string? Cciaa { get; set; }

    public string? ReaNumber { get; set; }

    public int? TypeVisura { get; set; }

    public byte[]? AttachedFileRr { get; set; }

    public byte[]? AttachedFileRa { get; set; }

    public string? FiscalCode { get; set; }

	public string? Mobile { get; set; }
    public string? RequestId { get; set; }
    public string? StatoMarker { get; set; }

    public bool? worked { get; set; }
    public bool? finalState { get; set; }

    public int? TentativiValorizzazione { get; set; }
    public virtual ICollection<Bulletins> Bulletins { get; set; } = new List<Bulletins>();

    public virtual ICollection<HistoricRecipientStatus> HistoricRecipientStatus { get; set; } = new List<HistoricRecipientStatus>();
    public virtual ICollection<RecipientWorks> RecipientWorks { get; set; } = new List<RecipientWorks>();

    public virtual Operations Operations { get; set; } = null!;
}
