namespace Osynix.Ats.Services;

public partial class HiringReviewService
{
 public async Task SendFollowUp(Guid assessmentId,string address,string message)
 {
  await access.RequireApplication(assessmentId);
  if(!email.Configured)throw new InvalidOperationException("Email delivery is not configured.");
  if(!System.Net.Mail.MailAddress.TryCreate(address,out _)||string.IsNullOrWhiteSpace(message)||message.Length>5000)throw new InvalidOperationException("Enter a valid recipient and a message up to 5,000 characters.");
  await email.Send(address,"Candidate review follow-up",message.Trim());
 }
}
