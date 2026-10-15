using MailKit.Security;
using MimeKit;
namespace Osynix.Ats.Services;
public interface IAccountEmail { bool Configured {get;} Task Send(string email,string subject,string body); }
public class AccountEmail(IConfiguration config):IAccountEmail {
 public bool Configured=>!string.IsNullOrWhiteSpace(config["Email:Host"])&&!string.IsNullOrWhiteSpace(config["Email:Password"])&&!string.IsNullOrWhiteSpace(config["Email:Username"]);
 public async Task Send(string email,string subject,string body){
  if(!Configured)throw new InvalidOperationException("Email verification is currently unavailable. Please try again later.");
  var message=new MimeMessage();message.From.Add(new MailboxAddress("Osynix Group",config["Email:From"]??"support@osynix.com"));message.To.Add(MailboxAddress.Parse(email));message.Subject=subject;
  message.Body=new TextPart("plain"){Text=body+"\n\nOsynix Group · Applicant Tracking System\nIf you did not request this, ignore this email."};
  using var smtp=new MailKit.Net.Smtp.SmtpClient();smtp.Timeout=30000;var port=config.GetValue("Email:Port",465);
  await smtp.ConnectAsync(config["Email:Host"]!,port,port==465?SecureSocketOptions.SslOnConnect:SecureSocketOptions.StartTls);
  await smtp.AuthenticateAsync(config["Email:Username"]!,config["Email:Password"]!);await smtp.SendAsync(message);await smtp.DisconnectAsync(true);
 }
 public async Task CheckConnection(){
  if(!Configured)throw new InvalidOperationException("Email settings are incomplete.");
  using var smtp=new MailKit.Net.Smtp.SmtpClient();smtp.Timeout=25000;var port=config.GetValue("Email:Port",465);
  await smtp.ConnectAsync(config["Email:Host"]!,port,port==465?SecureSocketOptions.SslOnConnect:SecureSocketOptions.StartTls);
  await smtp.AuthenticateAsync(config["Email:Username"]!,config["Email:Password"]!);await smtp.DisconnectAsync(true);
 }}
