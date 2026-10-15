using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
namespace Osynix.Ats.Services;
public static class HiringReviewEndpoints {
 static void Private(HttpContext c){
  c.Response.Headers.CacheControl="no-store";c.Response.Headers["Referrer-Policy"]="no-referrer";c.Response.Headers["X-Content-Type-Options"]="nosniff";c.Response.Headers["X-Robots-Tag"]="noindex, nofollow";c.Response.Headers["Content-Security-Policy"]="frame-ancestors 'self'";
 }
 public static void MapHiringReview(this WebApplication app){
  app.MapGet("/review/access",async Task<IResult>(string? token,HiringReviewService review,HttpContext c)=>{
   Private(c);var exchange=await review.Exchange(token??"");
   if(exchange is null)return Results.Redirect("/review?expired=true");
   c.Response.Cookies.Append(HiringReviewService.CookieName,exchange.Value.Cookie,new(){HttpOnly=true,Secure=c.Request.IsHttps,SameSite=SameSiteMode.Strict,Path="/review",Expires=new DateTimeOffset(exchange.Value.Expires,TimeSpan.Zero)});
   return Results.Redirect("/review");
  }).AllowAnonymous().RequireRateLimiting("review");
  app.MapGet("/review/data",async Task<IResult>(HiringReviewService review,HttpContext c)=>{
   Private(c);try{var data=await review.Read(c.Request.Cookies[HiringReviewService.CookieName]);return Results.Json(new{name=data.Name,rows=data.Rows});}catch(UnauthorizedAccessException){return Results.Unauthorized();}
  }).AllowAnonymous().RequireRateLimiting("review");
  app.MapPost("/review/feedback",async Task<IResult>(HiringFeedback input,HiringReviewService review,IAntiforgery csrf,HttpContext c)=>{
   Private(c);try{await csrf.ValidateRequestAsync(c);await review.Reply(c.Request.Cookies[HiringReviewService.CookieName],input);return Results.Ok(new{message="Feedback saved. Your Recruiter has been updated."});}
   catch(AntiforgeryValidationException){return Results.BadRequest(new{message="Reload this page and try again."});}
   catch(UnauthorizedAccessException){return Results.Unauthorized();}
   catch(DbUpdateConcurrencyException){return Results.Conflict(new{message="This profile changed. Reload it before submitting feedback."});}
   catch(InvalidOperationException e){return Results.BadRequest(new{message=e.Message});}
  }).AllowAnonymous().RequireRateLimiting("review");
  app.MapGet("/review/report",async Task<IResult>(Guid? submission,Guid? position,string? format,HiringReviewService review,ReportService reports,HttpContext c)=>{
   Private(c);try{var rows=await review.Reports(c.Request.Cookies[HiringReviewService.CookieName],submission,position);var html=reports.HiringManagerHtml(rows);
    return format=="pdf"?Results.File(await reports.Pdf(html),"application/pdf","Osynix-Assessment-Report.pdf"):Results.Content(html,"text/html");
   }catch(UnauthorizedAccessException){return Results.Unauthorized();}catch(InvalidOperationException e){return Results.BadRequest(e.Message);}
  }).AllowAnonymous().RequireRateLimiting("review");
  app.MapGet("/review/preview",async Task<IResult>(string? ids,HiringReviewService review,ReportService reports,HttpContext c)=>{
   Private(c);var values=(ids??"").Split(',',StringSplitOptions.RemoveEmptyEntries);
   if(values.Length is <1 or >50||values.Any(x=>!Guid.TryParse(x,out _)))return Results.BadRequest("Select 1–50 saved profiles.");
   try{return Results.Content(reports.HiringManagerHtml(await review.Preview(values.Select(Guid.Parse))),"text/html");}
   catch(UnauthorizedAccessException){return Results.Forbid();}catch(InvalidOperationException e){return Results.BadRequest(e.Message);}
  }).RequireAuthorization(p=>p.RequireRole("Admin","Recruiter","Employer"));
 }
}
