using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedLib.Config;
using SharedLib.Db;
using SharedLib.Services;
using SharedLib.Utils;

namespace RecuperaPresaInCarico.Services;

/// <summary>
/// Polling post-PreConferma: per LOL lo stato 1 resta transitorio finche' Poste
/// non conferma la presa in carico tramite RecuperaStatoIdRichiesta.
/// </summary>
public sealed class RecuperaPresaInCaricoWorker : BackgroundService
{
    private const string AutomationKey = "LOL:Verificapresaincarico";
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecuperaPresaInCaricoWorker> _logger;
    private readonly IConfiguration _configuration;
    private readonly LolServiceOptions _options;

    public RecuperaPresaInCaricoWorker(IServiceScopeFactory scopeFactory, ILogger<RecuperaPresaInCaricoWorker> logger,
        IConfiguration configuration, IOptions<LolServiceOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delaySeconds = Math.Max(60, _configuration.GetValue<int?>("Timers:RecuperaPresaInCaricoLOLSeconds") ?? 60);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var batchSize = Math.Clamp(_configuration.GetValue<int?>("Timers:RecuperaPresaInCaricoLOLBatchSize") ?? 20, 1, 100);
                var schedule = await AutomationRuntimeSchedule.ReadAsync(db, AutomationKey, stoppingToken);
                if (schedule is not null)
                {
                    delaySeconds = (schedule.IntervalMinutes ?? Math.Max(1, delaySeconds / 60)) * 60;
                    batchSize = Math.Clamp(schedule.BatchSize ?? batchSize, 1, 500);
                    var manualRun = schedule.ManualRunRequestedAt.HasValue &&
                        await AutomationRuntimeSchedule.TryConsumeManualRunAsync(db, AutomationKey, stoppingToken);
                    if (!schedule.Enabled || (!manualRun && !schedule.IsInWindow(DateTime.Now)))
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                        continue;
                    }
                }
                var recipients = await db.Recipients
                    .Include(r => r.Operations).ThenInclude(o => o.Users)
                    .Where(r => r.ProductType == (int)ProductTypes.LOL && r.Valid && r.Operations.Complete &&
                                r.CurrentState == (int)CurrentState.presaInCarico && r.RequiresAcceptanceCheck == true &&
                                !string.IsNullOrWhiteSpace(r.RequestId))
                    .OrderBy(r => r.Id).Take(batchSize).ToListAsync(stoppingToken);

                foreach (var recipient in recipients)
                {
                    try
                    {
                        var service = LOLServiceHelper.GetNewServiceLOL(recipient.Operations.Users, _options);
                        var status = (await service.RecuperaStatoIdRichiestaAsync(recipient.RequestId!)).StatoIdRichiesta?.Trim();
                        if (HasTakenInCharge(status))
                        {
                            recipient.CurrentState = (int)CurrentState.inlavorazione;
                            recipient.RequiresAcceptanceCheck = false;
                            recipient.Message = "Presa in carico Poste";
                        }
                        else if (IsFinalError(status))
                        {
                            recipient.CurrentState = (int)CurrentState.ErroreConfirm;
                            recipient.RequiresAcceptanceCheck = false;
                            recipient.Valid = false;
                            recipient.Message = status ?? "Errore finale durante la presa in carico Poste";
                        }
                        else
                        {
                            recipient.Message = string.IsNullOrWhiteSpace(status)
                                ? "In attesa presa in carico Poste"
                                : $"In attesa presa in carico Poste: {status}";
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "LOL {RecipientId}: recupero presa in carico non riuscito.", recipient.Id);
                    }
                }

                if (recipients.Count > 0)
                    await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel ciclo RecuperaPresaInCarico LOL.");
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }
    }

    private static bool HasTakenInCharge(string? status) => !string.IsNullOrWhiteSpace(status) &&
        (status.Contains("presa in carico", StringComparison.OrdinalIgnoreCase) || status.Contains("postalizzato", StringComparison.OrdinalIgnoreCase));

    private static bool IsFinalError(string? status) => !string.IsNullOrWhiteSpace(status) &&
        new[] { "non convertito", "non valido", "pagine in eccesso", "non prezzato", "annullato", "timeout", "errore negli esiti" }
            .Any(value => status.Contains(value, StringComparison.OrdinalIgnoreCase));
}
