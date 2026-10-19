namespace Osynix.Ats.Domain;

public class ChatInboxDeletion
{
    public string UserId { get; set; } = "";
    public string Kind { get; set; } = "Support";
    public Guid ConversationId { get; set; }
    public DateTime DeletedThroughUtc { get; set; }
}
