using System.ComponentModel.DataAnnotations;
namespace Osynix.Ats.Domain;

public class EmailTemplate : Record
{
    public Guid? WorkspaceId { get; set; }
    [MaxLength(100)] public string Key { get; set; } = "";
    [MaxLength(160)] public string Name { get; set; } = "";
    [MaxLength(60)] public string Recipient { get; set; } = "Candidate";
    [MaxLength(500)] public string Subject { get; set; } = "";
    [MaxLength(30000)] public string BodyHtml { get; set; } = "";
    public bool Custom { get; set; }
}
