using System.ComponentModel.DataAnnotations;

namespace Tickets.Api.Models;

public static class EventTypes
{
    public const string TicketCreated = "TicketCreated";
    public const string StatusChanged = "StatusChanged";

    public static readonly string[] All = [TicketCreated, StatusChanged];
}

public static class TicketStatuses
{
    public const string Open = "Open";
    public const string InProgress = "InProgress";
    public const string Resolved = "Resolved";

    /// <summary>Statuses a StatusChanged event may move a ticket to.</summary>
    public static readonly string[] StatusChangedTargets = [InProgress, Resolved];
}

public class TicketEventRequest : IValidatableObject
{
    [Required(AllowEmptyStrings = false), MaxLength(100)]
    public string? EventId { get; set; }

    [Required(AllowEmptyStrings = false), MaxLength(100)]
    public string? TicketId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string? Type { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string? Status { get; set; }

    [Required]
    public DateTimeOffset? OccurredAt { get; set; }

    /// <summary>Required when Type is TicketCreated.</summary>
    [MaxLength(200)]
    public string? CustomerId { get; set; }

    /// <summary>Required when Type is TicketCreated.</summary>
    [MaxLength(500)]
    public string? Subject { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Type == EventTypes.TicketCreated)
        {
            if (string.IsNullOrWhiteSpace(CustomerId))
                yield return new ValidationResult("CustomerId is required for TicketCreated events.", [nameof(CustomerId)]);
            if (string.IsNullOrWhiteSpace(Subject))
                yield return new ValidationResult("Subject is required for TicketCreated events.", [nameof(Subject)]);
            if (Status is not null && Status != TicketStatuses.Open)
                yield return new ValidationResult(
                    $"Status must be {TicketStatuses.Open} for TicketCreated events.", [nameof(Status)]);
        }
        else if (Type == EventTypes.StatusChanged)
        {
            if (Status is not null && !TicketStatuses.StatusChangedTargets.Contains(Status))
                yield return new ValidationResult(
                    $"Status must be one of: {string.Join(", ", TicketStatuses.StatusChangedTargets)} for StatusChanged events.",
                    [nameof(Status)]);
        }
        else if (Type is not null)
        {
            yield return new ValidationResult(
                $"Type must be one of: {string.Join(", ", EventTypes.All)}.", [nameof(Type)]);
        }
    }
}
