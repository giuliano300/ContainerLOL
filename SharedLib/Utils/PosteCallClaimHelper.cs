using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SharedLib.Db;
using SharedLib.Models;

namespace SharedLib.Utils;

public static class PosteCallClaimHelper
{
    /// <summary>
    /// Creates the persistent Poste call claim for a recipient and step.
    /// </summary>
    public static async Task<bool> TryClaimAsync(
        AppDbContext db,
        int recipientId,
        PosteCallStep step,
        string message,
        CancellationToken cancellationToken)
    {
        // The unique index on RecipientId + Step is the actual idempotency guard.
        db.PosteCallClaims.Add(new PosteCallClaims
        {
            RecipientId = recipientId,
            Step = (int)step,
            Message = message
        });

        try
        {
            // A successful commit authorizes exactly one Poste call for this recipient/step.
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Duplicate claims are expected on RabbitMQ redelivery; clear the failed insert and skip Poste.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>
    /// Detects SQL Server unique-key violations raised by the claim insert.
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var sqlException = ex.GetBaseException() as SqlException;
        return sqlException?.Number is 2601 or 2627;
    }
}
