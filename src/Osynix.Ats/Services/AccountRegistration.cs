using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;
namespace Osynix.Ats.Services;
public class AccountRegistration(UserManager<AtsUser> users,IAccountEmail mail,IDataProtectionProvider protection,IDbContextFactory<AtsDbContext>? templateFactory=null){
 public static readonly string[] Roles=["Recruiter","Employer","Candidate"];
 static readonly ConcurrentDictionary<string,SemaphoreSlim> gates=new();
 readonly IDataProtector protector=protection.CreateProtector("Osynix.EmailOtp.v1");
 public static AtsUser NewUser(string email,string name,string role)=>new(){UserName=email,Email=email,AccountType=role=="Recruiter"?AccountTypes.Freelance:role,FullName=name.Trim(),Status="Pending",EmailConfirmed=false,CanCreatePositions=false,CanViewTalentPool=false,CanExportReports=false};
 public async Task Register(string email,string name,string password,string role,bool teamOnly=false){
  if(!Roles.Contains(role))throw new InvalidOperationException("Choose Recruiter, Employer or Candidate.");
  if(!mail.Configured)throw new InvalidOperationException("Email verification is currently unavailable. Please try again later.");
  var existing=await users.FindByEmailAsync(email);
  if(existing is not null){
   if(!existing.EmailConfirmed&&await users.CheckPasswordAsync(existing,password)){await SendCode(existing);return;}
   throw new InvalidOperationException("Unable to register this address. If you already have an account, sign in or reset your password.");
  }
  var account=NewUser(email,name,role);if(teamOnly&&role=="Recruiter")account.AccountType=AccountTypes.Employee;var created=await users.CreateAsync(account,password);
  if(!created.Succeeded)throw new InvalidOperationException(string.Join(" ",created.Errors.Select(x=>x.Description)));
  var assigned=await users.AddToRoleAsync(account,role);if(!assigned.Succeeded){await users.DeleteAsync(account);throw new InvalidOperationException("Unable to create this account.");}
  if(role=="Recruiter")await users.AddClaimAsync(account,new Claim("WorkspaceScope","Assigned"));
  await SendCode(account);
 }
 public async Task SendCode(AtsUser account){
  if(account.EmailConfirmed)return;var gate=gates.GetOrAdd(account.Id,_=>new(1,1));await gate.WaitAsync();
  try{
   var old=await Read(account);if(old is not null&&DateTimeOffset.UtcNow-old.Sent<TimeSpan.FromSeconds(60))throw new InvalidOperationException("Please wait one minute before requesting another code.");
   var code=RandomNumberGenerator.GetInt32(100000,1000000).ToString();
   var state=new Otp(Hash(account.Id,code),DateTimeOffset.UtcNow.AddMinutes(10),DateTimeOffset.UtcNow,0);
   var result=await users.SetAuthenticationTokenAsync(account,"Osynix","EmailOtp",protector.Protect(JsonSerializer.Serialize(state)));
   if(!result.Succeeded)throw new InvalidOperationException("Unable to prepare a verification code.");
   try{if(templateFactory is null)await mail.Send(account.Email!,"Your Osynix Verification Code",$"Your verification code is {code}.\n\nIt expires in 10 minutes.");else await EmailTemplateService.Send(templateFactory,mail,null,"email-verification",account.Email!,"Your Osynix Verification Code",$"Your verification code is {code}.\n\nIt expires in 10 minutes.",new(){["VerificationCode"]=code});}
   catch{await users.RemoveAuthenticationTokenAsync(account,"Osynix","EmailOtp");throw new InvalidOperationException("We could not send the verification email. Please try again later.");}
  }finally{gate.Release();}
 }
 public async Task<bool> Verify(AtsUser account,string code){
  var gate=gates.GetOrAdd(account.Id,_=>new(1,1));await gate.WaitAsync();
  try{
   var state=await Read(account);if(state is null||state.Expiry<DateTimeOffset.UtcNow||state.Attempts>=5||account.EmailConfirmed)return false;
   if(!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(state.Hash),Convert.FromHexString(Hash(account.Id,code)))){
    await users.SetAuthenticationTokenAsync(account,"Osynix","EmailOtp",protector.Protect(JsonSerializer.Serialize(state with{Attempts=state.Attempts+1})));return false;
   }
   account.EmailConfirmed=true;account.Status="Active";if(!(await users.UpdateAsync(account)).Succeeded)return false;
   await users.RemoveAuthenticationTokenAsync(account,"Osynix","EmailOtp");return true;
  }finally{gate.Release();}
 }
 async Task<Otp?> Read(AtsUser account){var value=await users.GetAuthenticationTokenAsync(account,"Osynix","EmailOtp");try{return value is null?null:JsonSerializer.Deserialize<Otp>(protector.Unprotect(value));}catch{return null;}}
 static string Hash(string id,string code)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id+":"+code)));
 record Otp(string Hash,DateTimeOffset Expiry,DateTimeOffset Sent,int Attempts);
 public static string Home(IEnumerable<string> roles)=>roles.Contains("SubAdmin")?"/settings":roles.Contains("Admin")||roles.Contains("Recruiter")?"/":roles.Contains("Employer")?"/employer":"/Account/Portal";
}
