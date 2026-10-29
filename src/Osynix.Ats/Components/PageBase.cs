using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace Osynix.Ats.Components;
public class PageBase:ComponentBase
{
    [Inject] protected ILogger<PageBase> Logger { get; set; } = default!;
    protected bool Busy;
    protected string Error="";
    protected string Notice="";
    protected async Task Run(Func<Task> action)
    {
        if(Busy)return; Busy=true;Error="";Notice="";
        try{await action();}
        catch(DbUpdateConcurrencyException){Error="This record changed in another session. Reload before saving.";}
        catch(DbUpdateException){Error="Database update failed. Check duplicate references and required relationships, then reload.";}
        catch(Exception e) when(e is InvalidOperationException or UnauthorizedAccessException or ArgumentException){Error=e.Message;}
        catch(Exception e){Logger.LogError(e,"Unhandled page operation failed in {Component}",GetType().FullName);Error="The operation failed. Please retry. If it happens again, contact support with the time of the error.";}
        finally{Busy=false;}
    }
}
