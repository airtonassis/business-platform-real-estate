using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;
using RealEstate.Domain;
namespace RealEstate.Web;

// Correlate rejected form submissions without recording payloads, cookies, tokens or file contents.
internal static class DraftRequestDiagnostics
{
    internal static bool IsPropertyPost(HttpContext http)=>HttpMethods.IsPost(http.Request.Method)&&
        http.Request.Path.Value is {} path&&path.StartsWith("/properties/",StringComparison.Ordinal)&&
        (path=="/properties/new"||Guid.TryParse(path[12..],out _));

    internal static async Task Observe(HttpContext http,RequestDelegate next)
    {
        if(!IsPropertyPost(http)){await next(http);return;}
        var reference=http.TraceIdentifier;
        http.Response.Headers["X-Request-ID"]=reference;
        http.Items["DraftSaveStage"]="BeforeHandler";
        try{await next(http);}
        finally
        {
            var logger=http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RealEstate.DraftSubmission");
            var scope=http.Items[typeof(AccessScope)] as AccessScope;
            logger.LogWarning("Property submission {Reference}: path {Path}, status {Status}, stage {Stage}, outcome {Outcome}, authenticated {Authenticated}, organization {Organization}, establishment {Establishment}, bodyBytes {BodyBytes}",
                reference,http.Request.Path.Value,http.Response.StatusCode,http.Items["DraftSaveStage"],http.Items["DraftSaveCode"]??"NotReported",http.User.Identity?.IsAuthenticated==true,scope?.OrganizationId,scope?.ActiveEstablishmentId,http.Request.ContentLength);
        }
    }
}

internal sealed class DraftRejectionDiagnostics:IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if(DraftRequestDiagnostics.IsPropertyPost(context.HttpContext)&&context.Result is IAntiforgeryValidationFailedResult)
        {
            context.HttpContext.Items["DraftSaveStage"]="Antiforgery";
            context.HttpContext.Items["DraftSaveCode"]="Request.ValidationRejected";
        }
    }
    public void OnResultExecuted(ResultExecutedContext context){}
}
