using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
namespace Osynix.Ats.Components;
public class PageBase:ComponentBase
{
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
        catch(Exception){Error="The operation failed. Check server logs and configuration, then retry.";}
        finally{Busy=false;}
    }
}
