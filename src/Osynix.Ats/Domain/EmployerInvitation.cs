using System.ComponentModel.DataAnnotations;
namespace Osynix.Ats.Domain;
public class EmployerInvitation : Record
{
    public Guid WorkspaceId {get;set;}
    [MaxLength(100)] public string FullName {get;set;}="";
    [MaxLength(320)] public string Email {get;set;}="";
    [MaxLength(320)] public string NormalizedEmail {get;set;}="";
    public string TokenHash {get;set;}="";
    public string ProtectedToken {get;set;}="";
    public DateTime ExpiresUtc {get;set;}
    public string Status {get;set;}="Pending";
    public string Delivery {get;set;}="Link Created";
    public string? AcceptedBy {get;set;}
}
