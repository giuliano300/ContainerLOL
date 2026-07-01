using SharedLib.Db;
using SharedLib.Models;

namespace SharedLib.Utils;

/// <summary>
/// Applies common recipient state, flag and audit changes for LOL workflow steps.
/// </summary>
public static class RecipientWorkflowHelper
{
    /// <summary>
    /// Marks a recipient as queued for the requested workflow step.
    /// </summary>
    public static void MarkQueued(AppDbContext db, Recipients recipient, LolWorkflowStep step)
    {
        SetStepFlag(recipient, step, true);
        recipient.worked = false;
        AddQueuedWork(db, recipient.Id, step);
    }

    /// <summary>
    /// Releases the in-process flag for a workflow step.
    /// </summary>
    public static void ReleaseStep(Recipients recipient, LolWorkflowStep step)
    {
        SetStepFlag(recipient, step, false);
        recipient.worked = true;
    }

    /// <summary>
    /// Adds a queue-insertion audit row for a workflow step.
    /// </summary>
    public static void AddQueuedWork(AppDbContext db, int recipientId, LolWorkflowStep step)
    {
        AddWork(
            db,
            recipientId,
            LolWorkflowContracts.GetQueuedStatus(step),
            LolWorkflowContracts.GetQueuedMessage(step));
    }

    /// <summary>
    /// Adds a processor-completion audit row for a workflow step.
    /// </summary>
    public static void AddProcessedWork(AppDbContext db, int recipientId, LolWorkflowStep step, string? message)
    {
        AddWork(db, recipientId, LolWorkflowContracts.GetProcessedStatus(step), message);
    }

    /// <summary>
    /// Marks a recipient as failed for a step and records the processor audit row.
    /// </summary>
    public static async Task MarkAsFailedAsync(
        AppDbContext db,
        Recipients recipient,
        LolWorkflowStep step,
        string? message,
        CancellationToken cancellationToken,
        bool setGenericErrorState = true,
        bool invalidateRecipient = true)
    {
        ReleaseStep(recipient, step);
        recipient.Message = message;

        if (setGenericErrorState)
        {
            recipient.CurrentState = (int)CurrentState.ErroreGenerico;
        }

        if (invalidateRecipient)
        {
            recipient.Valid = false;
        }

        AddProcessedWork(db, recipient.Id, step, message);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Adds a recipient work audit row.
    /// </summary>
    private static void AddWork(AppDbContext db, int recipientId, WorkStatus status, string? message)
    {
        db.RecipientWorks.Add(new RecipientWorks
        {
            Message = message,
            RecipientId = recipientId,
            WorkDate = DateTime.UtcNow,
            WorkStatus = (int)status
        });
    }

    /// <summary>
    /// Sets the concrete in-process flag for a workflow step.
    /// </summary>
    private static void SetStepFlag(Recipients recipient, LolWorkflowStep step, bool value)
    {
        switch (step)
        {
            case LolWorkflowStep.Invio:
                recipient.InProcessStep1 = value;
                break;
            case LolWorkflowStep.Valorizza:
                recipient.InProcessStep2 = value;
                break;
            case LolWorkflowStep.Conferma:
                recipient.InProcessStep3 = value;
                break;
            case LolWorkflowStep.RecuperaDocumentoFinale:
                recipient.InProcessStep4 = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step), step, "Step LOL non gestito.");
        }
    }
}
