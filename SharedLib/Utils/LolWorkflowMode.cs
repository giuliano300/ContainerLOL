namespace SharedLib.Utils;

/// <summary>
/// Describes how a LOL workflow step is allowed to call Poste.
/// </summary>
public enum LolWorkflowMode
{
    SingleCall = 1,
    Polling = 2,
    Retryable = 3
}
