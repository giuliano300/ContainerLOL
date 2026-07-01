using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using SharedLib.Models;

namespace SharedLib.Db;

public partial class AppDbContext : DbContext
{
    /// <summary>
    /// Creates an unconfigured database context for tooling and design-time scenarios.
    /// </summary>
    public AppDbContext()
    {
    }

    /// <summary>
    /// Creates a database context with the configured runtime options.
    /// </summary>
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Bulletins> Bulletins { get; set; }

    public virtual DbSet<HistoricRecipientStatus> HistoricRecipientStatus { get; set; }

    public virtual DbSet<Notifications> Notifications { get; set; }

    public virtual DbSet<Operations> Operations { get; set; }

    public virtual DbSet<PasswordRecovery> PasswordRecovery { get; set; }

    public virtual DbSet<Recipients> Recipients { get; set; }

    public virtual DbSet<Senders> Senders { get; set; }

    public virtual DbSet<UserLogos> UserLogos { get; set; }

    public virtual DbSet<UserOptions> UserOptions { get; set; }

    public virtual DbSet<UserProducts> UserProducts { get; set; }

    public virtual DbSet<UserRecipients> UserRecipients { get; set; }

    public virtual DbSet<UserSenders> UserSenders { get; set; }

    public virtual DbSet<Users> Users { get; set; }
    public virtual DbSet<RecipientWorks> RecipientWorks { get; set; }
    public virtual DbSet<PosteCallClaims> PosteCallClaims { get; set; }

    /// <summary>
    /// Configures database mappings, indexes and relationships for the LOL automation models.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Bulletins>(entity =>
        {
            entity.Property(e => e.AnnoDiRiferimento).HasMaxLength(50);
            entity.Property(e => e.Causale).HasMaxLength(450);
            entity.Property(e => e.CodiceCliente).HasMaxLength(50);
            entity.Property(e => e.EseguitoDaCap).HasMaxLength(50);
            entity.Property(e => e.EseguitoDaIndirizzo).HasMaxLength(350);
            entity.Property(e => e.EseguitoDaLocalita).HasMaxLength(250);
            entity.Property(e => e.EseguitoDaNominativo).HasMaxLength(250);
            entity.Property(e => e.ImportoEuro).HasMaxLength(50);
            entity.Property(e => e.IntestatoA).HasMaxLength(250);
            entity.Property(e => e.NumeroContoCorrente).HasMaxLength(120);

            entity.HasOne(d => d.Recipient).WithMany(p => p.Bulletins)
                .HasForeignKey(d => d.RecipientId)
                .HasConstraintName("FK_Bulletins_Recipients");
        });

        modelBuilder.Entity<RecipientWorks>(entity =>
        {
            entity.HasIndex(e => e.WorkDate, "IX_RecipientWorks_3");
            entity.HasIndex(e => e.WorkStatus, "IX_RecipientWorks_2");
            entity.HasIndex(e => e.RecipientId, "IX_RecipientWorks_1");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.WorkDate)
                .HasColumnType("datetime")
                .HasColumnName("WorkDate");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.RecipientId).HasColumnName("recipientId");

            entity.HasOne(d => d.Recipient).WithMany(p => p.RecipientWorks)
                .HasForeignKey(d => d.RecipientId)
                .HasConstraintName("FK_RecipientWorks_Recipients");
        });

        modelBuilder.Entity<PosteCallClaims>(entity =>
        {
            // PosteCallClaims is the persistent idempotency table for single-call Poste operations.
            entity.ToTable("PosteCallClaims");

            // The unique index prevents more than one automatic call for the same recipient and step.
            entity.HasIndex(e => new { e.RecipientId, e.Step }, "UQ_PosteCallClaims_RecipientId_Step")
                .IsUnique();

            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.RecipientId).HasColumnName("RecipientId");
            entity.Property(e => e.Step).HasColumnName("Step");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime2(3)")
                .HasColumnName("CreatedAt");
            entity.Property(e => e.Message)
                .HasMaxLength(500)
                .HasColumnName("Message");

            entity.HasOne(d => d.Recipient).WithMany()
                .HasForeignKey(d => d.RecipientId)
                .HasConstraintName("FK_PosteCallClaims_Recipients");
        });


        modelBuilder.Entity<HistoricRecipientStatus>(entity =>
        {
            entity.HasIndex(e => e.InsertDate, "IX_HistoricRecipientStatus");

            entity.HasIndex(e => e.RecipientId, "IX_HistoricRecipientStatus_1");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.InsertDate)
                .HasColumnType("datetime")
                .HasColumnName("insertDate");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.RecipientId).HasColumnName("recipientId");

            entity.HasOne(d => d.Recipient).WithMany(p => p.HistoricRecipientStatus)
                .HasForeignKey(d => d.RecipientId)
                .HasConstraintName("FK_HistoricRecipientStatus_Recipients");
        });

        modelBuilder.Entity<Notifications>(entity =>
        {
            entity.HasIndex(e => e.Enabled, "IX_Notifications");

            entity.HasIndex(e => e.NotificationType, "IX_Notifications_1");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Enabled).HasColumnName("enabled");
            entity.Property(e => e.NotificationType).HasColumnName("notificationType");
            entity.Property(e => e.Title)
                .HasMaxLength(550)
                .HasColumnName("title");
        });

        modelBuilder.Entity<Operations>(entity =>
        {
            entity.HasIndex(e => e.AreaTestOperation, "IX_Operations");

            entity.HasIndex(e => e.Complete, "IX_Operations_1");

            entity.HasIndex(e => e.Error, "IX_Operations_2");

            entity.HasIndex(e => e.InsertDate, "IX_Operations_3");

            entity.HasIndex(e => e.Name, "IX_Operations_4");

            entity.HasIndex(e => e.UserId, "IX_Operations_5");

            entity.HasIndex(e => e.UserParentId, "IX_Operations_6");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AreaTestOperation).HasColumnName("areaTestOperation");
            entity.Property(e => e.Complete).HasColumnName("complete");
            entity.Property(e => e.CsvFileName).HasColumnName("csvFileName");
            entity.Property(e => e.Error).HasColumnName("error");
            entity.Property(e => e.ErrorMessage).HasColumnName("errorMessage");
            entity.Property(e => e.InsertDate)
                .HasColumnType("datetime")
                .HasColumnName("insertDate");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NumberOfRecipient).HasColumnName("numberOfRecipient");
            entity.Property(e => e.OperationType).HasColumnName("operationType");
            entity.Property(e => e.UserId).HasColumnName("userId");
            entity.Property(e => e.UserParentId).HasColumnName("userParentId");
        });

        modelBuilder.Entity<PasswordRecovery>(entity =>
        {
            entity.HasIndex(e => e.Email, "IX_PasswordRecovery");

            entity.HasIndex(e => e.ExpirationDate, "IX_PasswordRecovery_1");

            entity.HasIndex(e => e.Token, "IX_PasswordRecovery_2");

            entity.HasIndex(e => e.Used, "IX_PasswordRecovery_3");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Email)
                .HasMaxLength(350)
                .HasColumnName("email");
            entity.Property(e => e.ExpirationDate)
                .HasColumnType("datetime")
                .HasColumnName("expirationDate");
            entity.Property(e => e.Token)
                .HasMaxLength(255)
                .HasColumnName("token");
            entity.Property(e => e.Used).HasColumnName("used");
        });

        modelBuilder.Entity<Recipients>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_Names");

            entity.HasIndex(e => e.Address, "IX_RecipientROL");

            entity.HasIndex(e => e.City, "IX_RecipientROL_1");

            entity.HasIndex(e => e.FromApi, "IX_RecipientROL_10");

            entity.HasIndex(e => e.FrontBack, "IX_RecipientROL_11");

            entity.HasIndex(e => e.InsertDate, "IX_RecipientROL_12");

            entity.HasIndex(e => e.LogoId, "IX_RecipientROL_13");

            entity.HasIndex(e => e.Notified, "IX_RecipientROL_14");

            entity.HasIndex(e => e.NumberOfPages, "IX_RecipientROL_15");

            entity.HasIndex(e => e.OperationId, "IX_RecipientROL_16");

            entity.HasIndex(e => e.Price, "IX_RecipientROL_17");

            entity.HasIndex(e => e.PrintType, "IX_RecipientROL_18");

            entity.HasIndex(e => e.ProductType, "IX_RecipientROL_19");

            entity.HasIndex(e => e.Code, "IX_RecipientROL_2");

            entity.HasIndex(e => e.Province, "IX_RecipientROL_20");

            entity.HasIndex(e => e.ReturnReceipt, "IX_RecipientROL_21");

            entity.HasIndex(e => e.State, "IX_RecipientROL_24");

            entity.HasIndex(e => e.Tag1, "IX_RecipientROL_25");

            entity.HasIndex(e => e.Tag2, "IX_RecipientROL_26");

            entity.HasIndex(e => e.Tag3, "IX_RecipientROL_27");

            entity.HasIndex(e => e.Tag4, "IX_RecipientROL_28");

            entity.HasIndex(e => e.Tag5, "IX_RecipientROL_29");

            entity.HasIndex(e => e.Code, "IX_RecipientROL_3");

            entity.HasIndex(e => e.TotalPrice, "IX_RecipientROL_30");

            entity.HasIndex(e => e.Valid, "IX_RecipientROL_31");

            entity.HasIndex(e => e.VatPrice, "IX_RecipientROL_32");

            entity.HasIndex(e => e.ZipCode, "IX_RecipientROL_33");

            entity.HasIndex(e => e.PosteType, "IX_RecipientROL_34");

            entity.HasIndex(e => e.CodiceAgolAr, "IX_RecipientROL_4");

            entity.HasIndex(e => e.ComplementAddress, "IX_RecipientROL_5");

            entity.HasIndex(e => e.ComplementName, "IX_RecipientROL_6");

            entity.HasIndex(e => e.CurrentState, "IX_RecipientROL_7");

            entity.HasIndex(e => e.DigitalReturnReceipt, "IX_RecipientROL_8");

            entity.HasIndex(e => e.Format, "IX_RecipientROL_9");

            // Composite indexes mirror the hot watcher filters: state, validity, LOL product, A4 format and step flag.
            entity.HasIndex(e => new { e.CurrentState, e.Valid, e.ProductType, e.Format, e.InProcessStep1, e.Id }, "IX_Recipients_LOL_InvioWatcher");
            entity.HasIndex(e => new { e.CurrentState, e.Valid, e.ProductType, e.Format, e.InProcessStep2, e.Id }, "IX_Recipients_LOL_ValorizzaWatcher");
            entity.HasIndex(e => new { e.CurrentState, e.Valid, e.ProductType, e.Format, e.InProcessStep3, e.Id }, "IX_Recipients_LOL_ConfermaWatcher");
            entity.HasIndex(e => new { e.CurrentState, e.Valid, e.ProductType, e.Format, e.InProcessStep4, e.Code, e.Id }, "IX_Recipients_LOL_RecuperaDocumentoWatcher");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address).HasColumnName("address");
            entity.Property(e => e.AttachedFile).HasColumnName("attachedFile");
            entity.Property(e => e.AttachedFileRa).HasColumnName("attachedFileRA");
            entity.Property(e => e.AttachedFileRr).HasColumnName("attachedFileRR");
            entity.Property(e => e.BusinessName)
                .HasMaxLength(550)
                .HasColumnName("businessName");
            entity.Property(e => e.Cciaa)
                .HasMaxLength(50)
                .HasColumnName("cciaa");
            entity.Property(e => e.City)
                .HasMaxLength(150)
                .HasColumnName("city");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CodiceAgolAr)
                .HasMaxLength(50)
                .HasColumnName("codiceAgolAr");
            entity.Property(e => e.ComplementAddress)
                .HasMaxLength(350)
                .HasColumnName("complementAddress");
            entity.Property(e => e.ComplementName)
                .HasMaxLength(250)
                .HasColumnName("complementName");
            entity.Property(e => e.CurrentState).HasColumnName("currentState");
            entity.Property(e => e.DigitalReturnReceipt).HasColumnName("digitalReturnReceipt");
            entity.Property(e => e.FileName).HasColumnName("fileName");
            entity.Property(e => e.FiscalCode).HasMaxLength(150);
            entity.Property(e => e.Format).HasColumnName("format");
            entity.Property(e => e.FromApi).HasColumnName("fromApi");
            entity.Property(e => e.FrontBack).HasColumnName("frontBack");
            entity.Property(e => e.InsertDate)
                .HasColumnType("datetime")
                .HasColumnName("insertDate");
            entity.Property(e => e.LogoId).HasColumnName("logoId");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.Mobile).HasMaxLength(50);
            entity.Property(e => e.Notified).HasColumnName("notified");
            entity.Property(e => e.NumberOfPages).HasColumnName("numberOfPages");
            entity.Property(e => e.OperationId).HasColumnName("operationId");
            entity.Property(e => e.PathGedurl).HasColumnName("pathGEDUrl");
            entity.Property(e => e.PathRecoveryFile).HasColumnName("pathRecoveryFile");
            entity.Property(e => e.Pec)
                .HasMaxLength(350)
                .HasColumnName("pec");
            entity.Property(e => e.PosteType)
                .HasMaxLength(50)
                .HasColumnName("posteType");
            entity.Property(e => e.Price)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("price");
            entity.Property(e => e.PrintType).HasColumnName("printType");
            entity.Property(e => e.ProductType).HasColumnName("productType");
            entity.Property(e => e.Province)
                .HasMaxLength(2)
                .HasColumnName("province");
            entity.Property(e => e.ReaNumber)
                .HasMaxLength(50)
                .HasColumnName("reaNumber");
            entity.Property(e => e.ReturnReceipt).HasColumnName("returnReceipt");
            entity.Property(e => e.State)
                .HasMaxLength(50)
                .HasColumnName("state");
            entity.Property(e => e.Tag1)
                .HasMaxLength(10)
                .IsFixedLength()
                .HasColumnName("tag1");
            entity.Property(e => e.Tag2)
                .HasMaxLength(10)
                .IsFixedLength()
                .HasColumnName("tag2");
            entity.Property(e => e.Tag3)
                .HasMaxLength(10)
                .IsFixedLength()
                .HasColumnName("tag3");
            entity.Property(e => e.Tag4)
                .HasMaxLength(10)
                .IsFixedLength()
                .HasColumnName("tag4");
            entity.Property(e => e.Tag5)
                .HasMaxLength(10)
                .IsFixedLength()
                .HasColumnName("tag5");
            entity.Property(e => e.Tag6)
                .HasMaxLength(10)
                .IsFixedLength()
                .HasColumnName("tag6");
            entity.Property(e => e.TelegramText).HasColumnName("telegramText");
            entity.Property(e => e.TipologiaNotificante).HasColumnName("tipologiaNotificante");
            entity.Property(e => e.TotalPrice)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("totalPrice");
            entity.Property(e => e.TypeVisura).HasColumnName("typeVisura");
            entity.Property(e => e.Valid).HasColumnName("valid");
            entity.Property(e => e.ValoreNotificante)
                .HasMaxLength(350)
                .HasColumnName("valoreNotificante");
            entity.Property(e => e.Vat)
                .HasMaxLength(50)
                .HasColumnName("vat");
            entity.Property(e => e.VatPrice)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("vatPrice");
            entity.Property(e => e.ZipCode)
                .HasMaxLength(50)
                .HasColumnName("zipCode");

            entity.HasOne(d => d.Operations).WithMany(p => p.Recipients)
                .HasForeignKey(d => d.OperationId)
                .HasConstraintName("FK_RecipientROL_Operations");
        });

        modelBuilder.Entity<Senders>(entity =>
        {
            entity.HasIndex(e => e.Address, "IX_Senders");

            entity.HasIndex(e => e.City, "IX_Senders_1");

            entity.HasIndex(e => e.Email, "IX_Senders_2");

            entity.HasIndex(e => e.Mobile, "IX_Senders_3");

            entity.HasIndex(e => e.Province, "IX_Senders_4");

            entity.HasIndex(e => e.State, "IX_Senders_5");

            entity.HasIndex(e => e.ZipCode, "IX_Senders_6");

            entity.HasIndex(e => e.OperationId, "IX_Senders_7");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address)
                .HasMaxLength(50)
                .HasColumnName("address");
            entity.Property(e => e.Ar).HasColumnName("AR");
            entity.Property(e => e.BusinessName)
                .HasMaxLength(550)
                .HasColumnName("businessName");
            entity.Property(e => e.City)
                .HasMaxLength(250)
                .HasColumnName("city");
            entity.Property(e => e.ComplementAddress).HasColumnName("complementAddress");
            entity.Property(e => e.ComplementNames).HasColumnName("complementNames");
            entity.Property(e => e.Email)
                .HasMaxLength(250)
                .HasColumnName("email");
            entity.Property(e => e.Mobile)
                .HasMaxLength(10)
                .HasColumnName("mobile");
            entity.Property(e => e.OperationId).HasColumnName("operationId");
            entity.Property(e => e.Province)
                .HasMaxLength(2)
                .HasColumnName("province");
            entity.Property(e => e.State)
                .HasMaxLength(50)
                .HasColumnName("state");
            entity.Property(e => e.ZipCode)
                .HasMaxLength(5)
                .HasColumnName("zipCode");

            entity.HasOne(d => d.Operation).WithMany(p => p.Senders)
                .HasForeignKey(d => d.OperationId)
                .HasConstraintName("FK_Senders_Operations");
        });

        modelBuilder.Entity<UserLogos>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_Logos");

            entity.HasIndex(e => e.Name, "IX_UserLogos");

            entity.HasIndex(e => e.ParentUserId, "IX_UserLogos_1");

            entity.HasIndex(e => e.UserId, "IX_UserLogos_2");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Logo).HasColumnName("logo");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ParentUserId).HasColumnName("parentUserId");
            entity.Property(e => e.UserId).HasColumnName("userId");

            entity.HasOne(d => d.User).WithMany(p => p.UserLogos)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_UserLogos_Users");
        });

        modelBuilder.Entity<UserOptions>(entity =>
        {
            entity.HasIndex(e => e.Enabled, "IX_UserOptions");

            entity.HasIndex(e => e.OptionId, "IX_UserOptions_1");

            entity.HasIndex(e => e.UserId, "IX_UserOptions_2");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Enabled).HasColumnName("enabled");
            entity.Property(e => e.OptionId).HasColumnName("optionId");
            entity.Property(e => e.UserId).HasColumnName("userId");

            entity.HasOne(d => d.User).WithMany(p => p.UserOptions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_UserOptions_Users");
        });

        modelBuilder.Entity<UserProducts>(entity =>
        {
            entity.HasIndex(e => e.Code, "IX_UserProducts");

            entity.HasIndex(e => e.Type, "IX_UserProducts_1");

            entity.HasIndex(e => e.UserId, "IX_UserProducts_2");

            entity.HasIndex(e => e.Enabled, "IX_UserProducts_3");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.Enabled).HasColumnName("enabled");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.UserId).HasColumnName("userId");

            entity.HasOne(d => d.User).WithMany(p => p.UserProducts)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_UserProducts_Users");
        });

        modelBuilder.Entity<UserRecipients>(entity =>
        {
            entity.HasIndex(e => e.Address, "IX_UserRecipients");

            entity.HasIndex(e => e.City, "IX_UserRecipients_1");

            entity.HasIndex(e => e.Email, "IX_UserRecipients_2");

            entity.HasIndex(e => e.Mobile, "IX_UserRecipients_3");

            entity.HasIndex(e => e.Province, "IX_UserRecipients_4");

            entity.HasIndex(e => e.State, "IX_UserRecipients_5");

            entity.HasIndex(e => e.UserId, "IX_UserRecipients_6");

            entity.HasIndex(e => e.UserParentId, "IX_UserRecipients_7");

            entity.HasIndex(e => e.ZipCode, "IX_UserRecipients_8");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address)
                .HasMaxLength(50)
                .HasColumnName("address");
            entity.Property(e => e.BusinessName)
                .HasMaxLength(550)
                .HasColumnName("businessName");
            entity.Property(e => e.City)
                .HasMaxLength(250)
                .HasColumnName("city");
            entity.Property(e => e.ComplementAddress).HasColumnName("complementAddress");
            entity.Property(e => e.ComplementNames).HasColumnName("complementNames");
            entity.Property(e => e.Email)
                .HasMaxLength(250)
                .HasColumnName("email");
            entity.Property(e => e.FiscalCode)
                .HasMaxLength(50)
                .HasColumnName("fiscalCode");
            entity.Property(e => e.Mobile)
                .HasMaxLength(10)
                .HasColumnName("mobile");
            entity.Property(e => e.Province)
                .HasMaxLength(2)
                .HasColumnName("province");
            entity.Property(e => e.State)
                .HasMaxLength(50)
                .HasColumnName("state");
            entity.Property(e => e.UserId).HasColumnName("userId");
            entity.Property(e => e.UserParentId).HasColumnName("userParentId");
            entity.Property(e => e.ZipCode)
                .HasMaxLength(5)
                .HasColumnName("zipCode");

            entity.HasOne(d => d.User).WithMany(p => p.UserRecipients)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_UserRecipients_UserRecipients1");
        });

        modelBuilder.Entity<UserSenders>(entity =>
        {
            entity.HasIndex(e => e.Address, "IX_UserSenders");

            entity.HasIndex(e => e.ZipCode, "IX_UserSenders_1");

            entity.HasIndex(e => e.City, "IX_UserSenders_2");

            entity.HasIndex(e => e.Mobile, "IX_UserSenders_3");

            entity.HasIndex(e => e.Province, "IX_UserSenders_4");

            entity.HasIndex(e => e.State, "IX_UserSenders_5");

            entity.HasIndex(e => e.UserId, "IX_UserSenders_6");

            entity.HasIndex(e => e.UserParentId, "IX_UserSenders_7");

            entity.HasIndex(e => e.Email, "IX_UserSenders_8");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address)
                .HasMaxLength(50)
                .HasColumnName("address");
            entity.Property(e => e.BusinessName)
                .HasMaxLength(550)
                .HasColumnName("businessName");
            entity.Property(e => e.City)
                .HasMaxLength(250)
                .HasColumnName("city");
            entity.Property(e => e.ComplementAddress).HasColumnName("complementAddress");
            entity.Property(e => e.ComplementNames).HasColumnName("complementNames");
            entity.Property(e => e.Email)
                .HasMaxLength(250)
                .HasColumnName("email");
            entity.Property(e => e.Mobile)
                .HasMaxLength(10)
                .HasColumnName("mobile");
            entity.Property(e => e.Province)
                .HasMaxLength(2)
                .HasColumnName("province");
            entity.Property(e => e.State)
                .HasMaxLength(50)
                .HasColumnName("state");
            entity.Property(e => e.UserId).HasColumnName("userId");
            entity.Property(e => e.UserParentId).HasColumnName("userParentId");
            entity.Property(e => e.ZipCode)
                .HasMaxLength(5)
                .HasColumnName("zipCode");

            entity.HasOne(d => d.User).WithMany(p => p.UserSenders)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_UserSenders_Users");
        });

        modelBuilder.Entity<Users>(entity =>
        {
            entity.HasIndex(e => e.Email, "IX_Users");

            entity.HasIndex(e => e.Enabled, "IX_Users_1");

            entity.HasIndex(e => e.Password, "IX_Users_2");

            entity.HasIndex(e => e.VatNumber, "IX_Users_3");

            entity.HasIndex(e => e.Deleted, "IX_Users_4");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address)
                .HasMaxLength(450)
                .HasColumnName("address");
            entity.Property(e => e.ArraySenderId).HasColumnName("arraySenderId");
            entity.Property(e => e.BusinessName)
                .HasMaxLength(550)
                .HasColumnName("businessName");
            entity.Property(e => e.City)
                .HasMaxLength(150)
                .HasColumnName("city");
            entity.Property(e => e.Deleted).HasColumnName("deleted");
            entity.Property(e => e.Email)
                .HasMaxLength(250)
                .HasColumnName("email");
            entity.Property(e => e.Enabled).HasColumnName("enabled");
            entity.Property(e => e.Guid)
                .HasDefaultValueSql("(newid())")
                .HasColumnName("guid");
            entity.Property(e => e.Mobile)
                .HasMaxLength(10)
                .HasColumnName("mobile");
            entity.Property(e => e.ParentId).HasColumnName("parentId");
            entity.Property(e => e.Password)
                .HasMaxLength(255)
                .HasColumnName("password");
            entity.Property(e => e.PasswordPoste)
                .HasMaxLength(350)
                .HasColumnName("passwordPoste");
            entity.Property(e => e.Pec)
                .HasMaxLength(350)
                .HasColumnName("pec");
            entity.Property(e => e.Province)
                .HasMaxLength(2)
                .HasColumnName("province");
            entity.Property(e => e.UserTypes).HasColumnName("userTypes");
            entity.Property(e => e.UsernamePoste)
                .HasMaxLength(350)
                .HasColumnName("usernamePoste");
            entity.Property(e => e.VatNumber)
                .HasMaxLength(50)
                .HasColumnName("vatNumber");
            entity.Property(e => e.ZipCode)
                .HasMaxLength(5)
                .HasColumnName("zipCode");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
