# Guida sviluppatore ContainerLOL

Questa guida spiega come modificare il progetto senza rompere idempotenza, code RabbitMQ, stati del recipient e performance delle query.

## Scopo del progetto

Il container LOL gestisce il flusso automatico dei recipient LOL A4 attraverso quattro step:

1. `Invio`
2. `Valorizza`
3. `Conferma`
4. `RecuperaDocumentoFinale`

Ogni step ha due componenti principali:

- `Watcher`: cerca recipient eleggibili nel database e li inserisce in coda RabbitMQ.
- `Processor`: consuma il messaggio RabbitMQ, chiama Poste quando previsto e aggiorna lo stato del recipient.

## Regola piu importante

Non tutti gli step hanno la stessa semantica.

| Step | Contratto | Claim Poste | Motivo |
| --- | --- | --- | --- |
| `Invio` | `SingleCall` | Si | Non deve mai chiamare Poste due volte per lo stesso recipient/step. |
| `Conferma` | `SingleCall` | Si | Non deve mai confermare due volte lo stesso recipient. |
| `Valorizza` | `Polling` | No | Puo interrogare Poste piu volte finche la valorizzazione non e disponibile. |
| `RecuperaDocumentoFinale` | `Retryable` | No | Puo riprovare il recupero documento finale. |

Questi contratti sono definiti in:

- `SharedLib/Utils/LolWorkflowStep.cs`
- `SharedLib/Utils/LolWorkflowMode.cs`
- `SharedLib/Utils/LolWorkflowContracts.cs`

Prima di cambiare uno step, controllare sempre il suo contratto.

## Idempotenza verso Poste

Gli step `SingleCall` usano la tabella `PosteCallClaims`.

La regola e:

1. creare il claim;
2. salvare il claim nel DB;
3. solo dopo chiamare Poste;
4. se il claim esiste gia, non chiamare Poste e consumare il messaggio Rabbit.

Il codice e centralizzato in:

- `SharedLib/Models/PosteCallClaims.cs`
- `SharedLib/Utils/PosteCallStep.cs`
- `SharedLib/Utils/PosteCallClaimHelper.cs`

Il vincolo unique fondamentale e:

```text
RecipientId + Step
```

Questo vincolo e la vera protezione contro la doppia chiamata automatica verso Poste.

## Stati e flag del recipient

Il flusso usa `CurrentState`, `Valid`, `worked` e `InProcessStepX`.

Mappatura step:

| Step | Flag |
| --- | --- |
| `Invio` | `InProcessStep1` |
| `Valorizza` | `InProcessStep2` |
| `Conferma` | `InProcessStep3` |
| `RecuperaDocumentoFinale` | `InProcessStep4` |

Non modificare direttamente questi flag nei processor o watcher se non strettamente necessario.

Usare:

- `SharedLib/Utils/RecipientWorkflowHelper.cs`

Metodi principali:

- `MarkQueued`
- `ReleaseStep`
- `AddProcessedWork`
- `MarkAsFailedAsync`

Questo evita errori tipo usare `InProcessStep4` dentro `Valorizza` o scrivere un `WorkStatus` sbagliato.

## Audit `RecipientWorks`

Le righe audit devono essere create tramite `RecipientWorkflowHelper`.

Non duplicare blocchi del tipo:

```csharp
db.RecipientWorks.Add(new RecipientWorks { ... });
```

L'audit corretto per ogni step e definito in `LolWorkflowContracts`.

## RabbitMQ

Il consumo RabbitMQ e centralizzato in:

- `SharedLib/Messaging/RabbitConsumerHelper.cs`

Questo helper gestisce:

- dichiarazione coda;
- `BasicQos`;
- consumer asincrono;
- semaforo locale;
- deserializzazione;
- `BasicAck`;
- `BasicNack`;
- log dei messaggi invalidi.

Nei processor non riscrivere manualmente `BasicAck`, `BasicNack`, `BasicConsume` o `AsyncEventingBasicConsumer`.

Un processor deve limitarsi a:

```csharp
return RabbitConsumerHelper.StartSingleMessageConsumerAsync(
    _channel,
    _semaphore,
    QueueName,
    _logger,
    JsonConvert.DeserializeObject<InvioItem>,
    ProcessItemAsync,
    stoppingToken);
```

Per `System.Text.Json`, usare una lambda esplicita:

```csharp
json => JsonSerializer.Deserialize<ConfermaItem>(json)
```

## Watcher

I watcher devono essere veloci.

Regole:

- usare `AsNoTracking`;
- selezionare solo le colonne necessarie con `Select`;
- usare `Take`;
- usare `Attach` su una stub entity;
- marcare modificate solo le property cambiate;
- salvare sul DB prima di pubblicare su RabbitMQ.

Pattern corretto:

```csharp
var newRecipients = await db.Recipients
    .AsNoTracking()
    .Where(...)
    .OrderBy(r => r.Id)
    .Select(r => new { r.Id, r.RequestId })
    .Take(20)
    .ToListAsync(stoppingToken);

foreach (var r in newRecipients)
{
    if (!_tracker.TryTrack(r.Id))
        continue;

    var recipient = new Recipients { Id = r.Id };
    db.Recipients.Attach(recipient);

    RecipientWorkflowHelper.MarkQueued(db, recipient, LolWorkflowStep.Valorizza);

    db.Entry(recipient).Property(x => x.InProcessStep2).IsModified = true;
    db.Entry(recipient).Property(x => x.worked).IsModified = true;
}

await db.SaveChangesAsync(stoppingToken);
```

## Processor

Ogni processor deve:

1. caricare solo i dati necessari;
2. verificare lo stato corretto;
3. verificare il contratto workflow;
4. usare claim solo se lo step e `SingleCall`;
5. chiamare Poste;
6. aggiornare il recipient;
7. rilasciare lo step;
8. scrivere audit;
9. fare `Untrack` in `finally` quando usa tracker.

## Regole specifiche per step

### Invio

- Stato iniziale richiesto: `CurrentState.inAttesa`.
- Contratto: `SingleCall`.
- Deve usare `PosteCallStep.Invio`.
- Il claim va creato prima di:
  - recupero request id;
  - invio Poste.
- Usare `AsSplitQuery` quando si caricano collections come `Senders` e `Bulletins`.

### Valorizza

- Stato iniziale richiesto: `CurrentState.accettatoOnline`.
- Contratto: `Polling`.
- Non deve usare claim.
- Deve usare `InProcessStep2`.
- Deve usare `WorkStatus.InviatoValorizza`.
- Puo chiamare Poste piu volte.

### Conferma

- Stato iniziale richiesto: `CurrentState.documentoValidato`.
- Contratto: `SingleCall`.
- Deve usare `PosteCallStep.Conferma`.
- Il claim va creato prima di `PreConfermaAsync`.
- Il recupero documento finale chiamato dopo conferma resta fuori dal claim.

### RecuperaDocumentoFinale

- Stato iniziale richiesto: `CurrentState.presaInCarico`.
- Contratto: `Retryable`.
- Non deve usare claim.
- Deve rilasciare sempre `InProcessStep4` in caso di errore.
- Non deve invalidare automaticamente il recipient solo per un errore retryable, salvo scelta esplicita.

## Indici e performance DB

In `AppDbContext` sono mappati gli indici per:

- claim idempotenza;
- query calde dei watcher.

Attenzione: la mappatura EF non crea automaticamente gli indici nel DB reale se non si usano migration/applicazione SQL.

Indici importanti:

- `UQ_PosteCallClaims_RecipientId_Step`
- `IX_Recipients_LOL_InvioWatcher`
- `IX_Recipients_LOL_ValorizzaWatcher`
- `IX_Recipients_LOL_ConfermaWatcher`
- `IX_Recipients_LOL_RecuperaDocumentoWatcher`

Prima di andare in produzione, verificare che esistano nel database.

## Test

Il progetto test e:

- `SharedLib.Tests`

I test coprono:

- contratti workflow;
- status audit;
- helper workflow;
- default claim;
- unique index idempotenza;
- indici watcher;
- DTO WSDL inizializzati.

Eseguire:

```powershell
dotnet run --no-restore --project SharedLib.Tests\SharedLib.Tests.csproj
```

Risultato atteso:

```text
10/10 tests passed
```

## Build

Eseguire:

```powershell
dotnet build ContainerLOL.sln --no-restore
```

Risultato atteso:

```text
Avvisi: 0
Errori: 0
```

## Checklist prima di modificare

- Lo step e `SingleCall`, `Polling` o `Retryable`?
- Serve davvero un claim?
- Il claim viene creato prima della chiamata Poste?
- Il messaggio Rabbit viene ackato senza richiamare Poste se il claim esiste gia?
- Il watcher usa `AsNoTracking` e `Select` minimo?
- Lo step flag viene rilasciato in tutti i percorsi?
- L'audit passa da `RecipientWorkflowHelper`?
- Il consumer Rabbit passa da `RabbitConsumerHelper`?
- I test sono verdi?
- La build e a zero warning?

## Cose da non fare

- Non aggiungere claim a `Valorizza` senza cambiare esplicitamente il contratto.
- Non aggiungere claim a `RecuperaDocumentoFinale` senza cambiare esplicitamente il contratto.
- Non chiamare Poste prima di creare il claim negli step `SingleCall`.
- Non duplicare `BasicAck` / `BasicNack` nei processor.
- Non duplicare `RecipientWorks.Add` nei processor o watcher.
- Non usare `inlavorazione` come stato tecnico di lock per questi container.
- Non fare query watcher caricando tutto il recipient se servono solo `Id` e `RequestId`.

## Quando aggiungere un nuovo step

1. Aggiungere il valore in `LolWorkflowStep`.
2. Decidere il `LolWorkflowMode`.
3. Aggiornare `LolWorkflowContracts`.
4. Aggiungere eventuale `PosteCallStep` solo se e `SingleCall`.
5. Aggiornare `RecipientWorkflowHelper` per il nuovo flag.
6. Aggiungere audit queued/processed.
7. Aggiungere test in `SharedLib.Tests`.
8. Verificare build e test.
