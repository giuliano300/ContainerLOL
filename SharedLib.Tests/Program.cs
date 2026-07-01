using Microsoft.EntityFrameworkCore;
using SharedLib.Db;
using SharedLib.Models;
using SharedLib.Utils;
using SharedLib.WsdlModels;
using System.Security.Cryptography;

var tests = new (string Name, Action Test)[]
{
    ("workflow contracts expose the expected step modes", WorkflowContractsExposeExpectedModes),
    ("workflow contracts expose the expected queued audit data", WorkflowContractsExposeExpectedQueuedAuditData),
    ("workflow contracts expose the expected processed audit data", WorkflowContractsExposeExpectedProcessedAuditData),
    ("recipient workflow helper marks queued recipients consistently", RecipientWorkflowHelperMarksQueuedRecipientsConsistently),
    ("recipient workflow helper releases the correct step flag", RecipientWorkflowHelperReleasesTheCorrectStepFlag),
    ("recipient workflow helper adds processed audit rows consistently", RecipientWorkflowHelperAddsProcessedAuditRowsConsistently),
    ("poste call claim entity initializes local defaults", PosteCallClaimEntityInitializesLocalDefaults),
    ("poste call claim mapping has the idempotency unique index", PosteCallClaimMappingHasTheIdempotencyUniqueIndex),
    ("watcher recipient indexes are configured in the model", WatcherRecipientIndexesAreConfiguredInTheModel),
    ("LOL document helper preserves PDF bytes and MD5", DocumentHelperPreservesPdfBytesAndMd5),
    ("wsdl queue items initialize nullable strings safely", WsdlQueueItemsInitializeNullableStringsSafely)
};

var passed = 0;

foreach (var (name, test) in tests)
{
    try
    {
        test();
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL {name}");
        Console.WriteLine(ex.Message);
        Environment.ExitCode = 1;
    }
}

Console.WriteLine($"{passed}/{tests.Length} tests passed");

static void WorkflowContractsExposeExpectedModes()
{
    AssertEqual(LolWorkflowMode.SingleCall, LolWorkflowContracts.GetMode(LolWorkflowStep.Invio), "Invio mode");
    AssertEqual(LolWorkflowMode.Polling, LolWorkflowContracts.GetMode(LolWorkflowStep.Valorizza), "Valorizza mode");
    AssertEqual(LolWorkflowMode.SingleCall, LolWorkflowContracts.GetMode(LolWorkflowStep.Conferma), "Conferma mode");
    AssertEqual(LolWorkflowMode.Retryable, LolWorkflowContracts.GetMode(LolWorkflowStep.RecuperaDocumentoFinale), "RecuperaDocumentoFinale mode");
}

static void WorkflowContractsExposeExpectedQueuedAuditData()
{
    AssertEqual(WorkStatus.InCodaInvio, LolWorkflowContracts.GetQueuedStatus(LolWorkflowStep.Invio), "Invio queued status");
    AssertEqual(WorkStatus.InCodaValorizza, LolWorkflowContracts.GetQueuedStatus(LolWorkflowStep.Valorizza), "Valorizza queued status");
    AssertEqual(WorkStatus.InCodaConferma, LolWorkflowContracts.GetQueuedStatus(LolWorkflowStep.Conferma), "Conferma queued status");
    AssertEqual(WorkStatus.InCodaRecuperaDocumentoFinale, LolWorkflowContracts.GetQueuedStatus(LolWorkflowStep.RecuperaDocumentoFinale), "RecuperaDocumentoFinale queued status");

    AssertEqual("Inserito in coda invio", LolWorkflowContracts.GetQueuedMessage(LolWorkflowStep.Invio), "Invio queued message");
    AssertEqual("Inserito in coda valorizza", LolWorkflowContracts.GetQueuedMessage(LolWorkflowStep.Valorizza), "Valorizza queued message");
    AssertEqual("Inserito in coda conferma", LolWorkflowContracts.GetQueuedMessage(LolWorkflowStep.Conferma), "Conferma queued message");
    AssertEqual("Inserito in coda recupera documento finale", LolWorkflowContracts.GetQueuedMessage(LolWorkflowStep.RecuperaDocumentoFinale), "RecuperaDocumentoFinale queued message");
}

static void WorkflowContractsExposeExpectedProcessedAuditData()
{
    AssertEqual(WorkStatus.InviatoPoste, LolWorkflowContracts.GetProcessedStatus(LolWorkflowStep.Invio), "Invio processed status");
    AssertEqual(WorkStatus.InviatoValorizza, LolWorkflowContracts.GetProcessedStatus(LolWorkflowStep.Valorizza), "Valorizza processed status");
    AssertEqual(WorkStatus.InviatoConferma, LolWorkflowContracts.GetProcessedStatus(LolWorkflowStep.Conferma), "Conferma processed status");
    AssertEqual(WorkStatus.InviatoRecuperaDocumentoFinale, LolWorkflowContracts.GetProcessedStatus(LolWorkflowStep.RecuperaDocumentoFinale), "RecuperaDocumentoFinale processed status");
}

static void RecipientWorkflowHelperMarksQueuedRecipientsConsistently()
{
    foreach (var step in Enum.GetValues<LolWorkflowStep>())
    {
        using var db = CreateDbContext();
        var recipient = new Recipients { Id = (int)step };

        RecipientWorkflowHelper.MarkQueued(db, recipient, step);

        AssertEqual(false, recipient.worked, $"{step} worked flag");
        AssertStepFlag(recipient, step, true);

        var audit = db.ChangeTracker.Entries<RecipientWorks>().Single().Entity;
        AssertEqual(recipient.Id, audit.RecipientId, $"{step} audit recipient");
        AssertEqual((int)LolWorkflowContracts.GetQueuedStatus(step), audit.WorkStatus, $"{step} audit status");
        AssertEqual(LolWorkflowContracts.GetQueuedMessage(step), audit.Message, $"{step} audit message");
    }
}

static void RecipientWorkflowHelperReleasesTheCorrectStepFlag()
{
    foreach (var step in Enum.GetValues<LolWorkflowStep>())
    {
        var recipient = new Recipients();
        SetAllStepFlags(recipient, true);

        RecipientWorkflowHelper.ReleaseStep(recipient, step);

        AssertEqual(true, recipient.worked, $"{step} worked flag");
        AssertStepFlag(recipient, step, false);
    }
}

static void RecipientWorkflowHelperAddsProcessedAuditRowsConsistently()
{
    foreach (var step in Enum.GetValues<LolWorkflowStep>())
    {
        using var db = CreateDbContext();
        var message = $"processed {step}";

        RecipientWorkflowHelper.AddProcessedWork(db, (int)step, step, message);

        var audit = db.ChangeTracker.Entries<RecipientWorks>().Single().Entity;
        AssertEqual((int)step, audit.RecipientId, $"{step} processed recipient");
        AssertEqual((int)LolWorkflowContracts.GetProcessedStatus(step), audit.WorkStatus, $"{step} processed status");
        AssertEqual(message, audit.Message, $"{step} processed message");
    }
}

static void PosteCallClaimEntityInitializesLocalDefaults()
{
    var before = DateTime.UtcNow.AddSeconds(-1);
    var claim = new PosteCallClaims();
    var after = DateTime.UtcNow.AddSeconds(1);

    AssertNotEqual(Guid.Empty, claim.Id, "claim id");
    AssertTrue(claim.CreatedAt >= before && claim.CreatedAt <= after, "claim CreatedAt should be initialized to now");
}

static void PosteCallClaimMappingHasTheIdempotencyUniqueIndex()
{
    using var db = CreateDbContext();
    var entityType = db.Model.FindEntityType(typeof(PosteCallClaims))
        ?? throw new InvalidOperationException("PosteCallClaims entity is not mapped.");

    var index = entityType.GetIndexes()
        .SingleOrDefault(i => i.GetDatabaseName() == "UQ_PosteCallClaims_RecipientId_Step");

    AssertTrue(index is not null, "idempotency unique index should exist");
    AssertEqual(true, index!.IsUnique, "idempotency index uniqueness");
    AssertSequenceEqual(new[] { nameof(PosteCallClaims.RecipientId), nameof(PosteCallClaims.Step) }, index.Properties.Select(p => p.Name), "idempotency index columns");
}

static void WatcherRecipientIndexesAreConfiguredInTheModel()
{
    using var db = CreateDbContext();
    var entityType = db.Model.FindEntityType(typeof(Recipients))
        ?? throw new InvalidOperationException("Recipients entity is not mapped.");

    var indexNames = entityType.GetIndexes()
        .Select(i => i.GetDatabaseName())
        .ToHashSet(StringComparer.Ordinal);

    AssertTrue(indexNames.Contains("IX_Recipients_LOL_InvioWatcher"), "Invio watcher index");
    AssertTrue(indexNames.Contains("IX_Recipients_LOL_ValorizzaWatcher"), "Valorizza watcher index");
    AssertTrue(indexNames.Contains("IX_Recipients_LOL_ConfermaWatcher"), "Conferma watcher index");
    AssertTrue(indexNames.Contains("IX_Recipients_LOL_RecuperaDocumentoWatcher"), "RecuperaDocumentoFinale watcher index");
}

static void WsdlQueueItemsInitializeNullableStringsSafely()
{
    var valorizza = new ValorizzaItem();
    var conferma = new ConfermaItem();
    var controllaStato = new ControllaStatoItem();

    AssertEqual(string.Empty, valorizza.RequesId, "ValorizzaItem RequesId");
    AssertEqual(string.Empty, conferma.RequesId, "ConfermaItem RequesId");
    AssertEqual(string.Empty, controllaStato.RequesId, "ControllaStatoItem RequesId");
    AssertEqual(string.Empty, controllaStato.StatoMarker, "ControllaStatoItem StatoMarker");
}

static void DocumentHelperPreservesPdfBytesAndMd5()
{
    var pdfBytes = "%PDF-1.7 EasySender LOL"u8.ToArray();
    var document = LOLServiceHelper.GetDoc(pdfBytes).Single();
    var expectedMd5 = Convert.ToHexString(MD5.HashData(pdfBytes));

    AssertTrue(document.Immagine.SequenceEqual(pdfBytes), "LOL document image must match attachedFile bytes");
    AssertEqual(expectedMd5, document.MD5, "LOL document MD5");
    AssertEqual("pdf", document.TipoDocumento, "LOL document type");
}

static void SetAllStepFlags(Recipients recipient, bool value)
{
    recipient.InProcessStep1 = value;
    recipient.InProcessStep2 = value;
    recipient.InProcessStep3 = value;
    recipient.InProcessStep4 = value;
}

static AppDbContext CreateDbContext()
{
    var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ContainerLolTests;Trusted_Connection=True;TrustServerCertificate=True")
        .Options;

    return new AppDbContext(options);
}

static void AssertStepFlag(Recipients recipient, LolWorkflowStep step, bool expected)
{
    var actual = step switch
    {
        LolWorkflowStep.Invio => recipient.InProcessStep1,
        LolWorkflowStep.Valorizza => recipient.InProcessStep2,
        LolWorkflowStep.Conferma => recipient.InProcessStep3,
        LolWorkflowStep.RecuperaDocumentoFinale => recipient.InProcessStep4,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Step LOL non gestito.")
    };

    AssertEqual(expected, actual, $"{step} flag");
}

static void AssertEqual<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{label}: expected '{expected}', actual '{actual}'.");
    }
}

static void AssertNotEqual<T>(T unexpected, T actual, string label)
{
    if (EqualityComparer<T>.Default.Equals(unexpected, actual))
    {
        throw new InvalidOperationException($"{label}: value should not be '{unexpected}'.");
    }
}

static void AssertTrue(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidOperationException($"{label}: condition is false.");
    }
}

static void AssertSequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string label)
{
    var expectedArray = expected.ToArray();
    var actualArray = actual.ToArray();

    if (!expectedArray.SequenceEqual(actualArray))
    {
        throw new InvalidOperationException($"{label}: expected '{string.Join(", ", expectedArray)}', actual '{string.Join(", ", actualArray)}'.");
    }
}
