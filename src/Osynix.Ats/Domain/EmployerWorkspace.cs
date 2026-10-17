using System.ComponentModel.DataAnnotations;
namespace Osynix.Ats.Domain;

public class EmployerWorkspace : Record
{
    public string Kind {get;set;} = "Employer";
    public bool AgencyEnabled {get;set;}
    public bool EmailTemplatesEnabled {get;set;}
    public bool MasterTalentPoolEnabled {get;set;}
    [MaxLength(450)] public string OwnerId { get; set; } = "";
    [MaxLength(30)] public string OperatingType { get; set; } = "Company Owner";
    [MaxLength(200)] public string CompanyName { get; set; } = "";
    [MaxLength(5000)] public string AboutUs { get; set; } = "";
    [MaxLength(500)] public string Website { get; set; } = "";
    [MaxLength(320)] public string BusinessEmail { get; set; } = "";
    [MaxLength(50)] public string ContactNumber { get; set; } = "";
    [MaxLength(500)] public string Address { get; set; } = "";
    [MaxLength(100)] public string Country { get; set; } = "";
    [MaxLength(100)] public string TimeZone { get; set; } = "Asia/Karachi";
    public byte[]? LogoContent { get; set; }
    public string LogoContentType { get; set; } = "";
    public string CareersColorScheme { get; set; } = "Osynix";
    public string CareersCoverMode { get; set; } = "Stock";
    public int CareersStockCover { get; set; } = 1;
    public byte[]? CareersCoverContent { get; set; }
    public string CareersCoverContentType { get; set; } = "";
    public bool ShowBusinessContacts { get; set; }
    public bool CareersPublished { get; set; }
    public int FreeRecruiterSeats { get; set; } = 1;
    public int AdditionalRecruiterSeats { get; set; }
    public int FreePositionSlots { get; set; } = 3;
    public int AdditionalPositionSlots { get; set; }
    public bool TeamTalentPoolEnabled { get; set; }
}

public class EmployerAccessRequest : Record
{
    public Guid WorkspaceId { get; set; }
    [MaxLength(50)] public string Feature { get; set; } = "Position Slots";
    public int Quantity { get; set; } = 1;
    [MaxLength(2000)] public string Remarks { get; set; } = "";
    [MaxLength(30)] public string Status { get; set; } = "Submitted";
    [MaxLength(2000)] public string AdminResponse { get; set; } = "";
    public DateTime? RespondedUtc { get; set; }
}
