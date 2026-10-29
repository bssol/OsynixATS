using System.ComponentModel.DataAnnotations;

namespace Osynix.Ats.Domain;

public class AdminPaymentDetails : Record
{
    [MaxLength(120)] public string BankName { get; set; } = "";
    [MaxLength(160)] public string AccountTitle { get; set; } = "";
    [MaxLength(80)] public string AccountNumber { get; set; } = "";
    [MaxLength(80)] public string Iban { get; set; } = "";
    [MaxLength(160)] public string Branch { get; set; } = "";
    [MaxLength(2000)] public string PaymentInstructions { get; set; } = "";
}
