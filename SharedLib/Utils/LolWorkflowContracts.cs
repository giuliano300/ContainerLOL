namespace SharedLib.Utils;

/// <summary>
/// Centralizes the operational contract for each LOL workflow step.
/// </summary>
public static class LolWorkflowContracts
{
    /// <summary>
    /// Returns the Poste-call mode for a workflow step.
    /// </summary>
    public static LolWorkflowMode GetMode(LolWorkflowStep step)
    {
        return step switch
        {
            LolWorkflowStep.Invio => LolWorkflowMode.SingleCall,
            LolWorkflowStep.Valorizza => LolWorkflowMode.Polling,
            LolWorkflowStep.Conferma => LolWorkflowMode.SingleCall,
            LolWorkflowStep.RecuperaDocumentoFinale => LolWorkflowMode.Retryable,
            _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Step LOL non gestito.")
        };
    }

    /// <summary>
    /// Returns the audit status used when a recipient is inserted into a step queue.
    /// </summary>
    public static WorkStatus GetQueuedStatus(LolWorkflowStep step)
    {
        return step switch
        {
            LolWorkflowStep.Invio => WorkStatus.InCodaInvio,
            LolWorkflowStep.Valorizza => WorkStatus.InCodaValorizza,
            LolWorkflowStep.Conferma => WorkStatus.InCodaConferma,
            LolWorkflowStep.RecuperaDocumentoFinale => WorkStatus.InCodaRecuperaDocumentoFinale,
            _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Step LOL non gestito.")
        };
    }

    /// <summary>
    /// Returns the audit status used when a processor completes a step attempt.
    /// </summary>
    public static WorkStatus GetProcessedStatus(LolWorkflowStep step)
    {
        return step switch
        {
            LolWorkflowStep.Invio => WorkStatus.InviatoPoste,
            LolWorkflowStep.Valorizza => WorkStatus.InviatoValorizza,
            LolWorkflowStep.Conferma => WorkStatus.InviatoConferma,
            LolWorkflowStep.RecuperaDocumentoFinale => WorkStatus.InviatoRecuperaDocumentoFinale,
            _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Step LOL non gestito.")
        };
    }

    /// <summary>
    /// Returns the standard audit message used when a recipient is queued for a step.
    /// </summary>
    public static string GetQueuedMessage(LolWorkflowStep step)
    {
        return step switch
        {
            LolWorkflowStep.Invio => "Inserito in coda invio",
            LolWorkflowStep.Valorizza => "Inserito in coda valorizza",
            LolWorkflowStep.Conferma => "Inserito in coda conferma",
            LolWorkflowStep.RecuperaDocumentoFinale => "Inserito in coda recupera documento finale",
            _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Step LOL non gestito.")
        };
    }
}
