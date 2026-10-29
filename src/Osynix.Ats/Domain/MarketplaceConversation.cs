namespace Osynix.Ats.Domain;

public class MarketplaceConversation : Record
{
    public Guid WorkspaceId { get; set; }
    public string FreelancerId { get; set; } = "";
    public bool Agency { get; set; }
    public DateTime? EmployerReadUtc { get; set; }
    public DateTime? FreelancerReadUtc { get; set; }
    public DateTime LastMessageUtc { get; set; } = DateTime.UtcNow;
}

public class MarketplaceMessage : Record
{
    public Guid ConversationId { get; set; }
    public string SenderId { get; set; } = "";
    public string Body { get; set; } = "";
    public Guid? InvitationId { get; set; }
    public string AttachmentName { get; set; } = "";
    public string AttachmentType { get; set; } = "";
    public byte[]? AttachmentContent { get; set; }
}

public class ProjectInvitation : Record
{
    public Guid ConversationId { get; set; }
    public Guid? PositionId { get; set; }
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Start { get; set; } = "To be agreed";
    public string Duration { get; set; } = "To be discussed";
    public string Status { get; set; } = "Pending";
    public string EmailDelivery { get; set; } = "Not requested";
    public DateTime? RespondedUtc { get; set; }
}
