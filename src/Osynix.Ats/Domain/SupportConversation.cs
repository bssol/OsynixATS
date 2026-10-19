using System.ComponentModel.DataAnnotations;

namespace Osynix.Ats.Domain;

public class SupportConversation : Record
{
    [Required, MaxLength(450)] public string UserId { get; set; } = "";
    [Required, MaxLength(200)] public string Subject { get; set; } = "Osynix Support";
    public Guid? WorkspaceId { get; set; }
    public Guid? EmployerAccessRequestId { get; set; }
    [MaxLength(60)] public string IssueType { get; set; } = "General Support";
    public DateTime? ClosedUtc { get; set; }
    [MaxLength(30)] public string Status { get; set; } = "Open";
    public DateTime LastMessageUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UserReadUtc { get; set; }
    public DateTime? AdminReadUtc { get; set; }
}

public class SupportMessage : Record
{
    public Guid ConversationId { get; set; }
    [Required, MaxLength(450)] public string SenderId { get; set; } = "";
    [MaxLength(10000)] public string Body { get; set; } = "";
    [MaxLength(200)] public string AttachmentName { get; set; } = "";
    [MaxLength(100)] public string AttachmentType { get; set; } = "";
    public byte[]? AttachmentContent { get; set; }
}
