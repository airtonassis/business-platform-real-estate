using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RealEstate.Domain;
using RealEstate.Web;
namespace RealEstate.Tests;
// Test-assembly-only observability. No authentication replacement, response rewrite or grants.
internal static class AraiaIntegrationHost
{
    public static void Run(string[] args)
    {
        var app=Program.Build(args,b=>{
            b.Services.AddTransient<CoreReceiptHandler>();
            b.Services.AddHttpClient<ICoreAccess,CoreAccess>().AddHttpMessageHandler<CoreReceiptHandler>();
        });
        app.Run();
    }
}
internal sealed class CoreReceiptHandler(IHttpContextAccessor http):DelegatingHandler
{
    private static readonly object Gate=new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        var response=await base.SendAsync(request,token);
        await Record(request,response,false,token);
        // A separate read-only probe distinguishes authorized membership units from active session context,
        // even when the subsequent product entitlement check correctly denies access.
        if(request.RequestUri!.AbsolutePath=="/api/v1/me/context" && response.IsSuccessStatusCode)
        {
            using var probe=new HttpRequestMessage(HttpMethod.Get,new Uri(request.RequestUri,"/api/v1/access/establishments"));
            probe.Headers.Authorization=request.Headers.Authorization;
            using var units=await base.SendAsync(probe,token);await Record(probe,units,true,token);
        }
        return response;
    }
    private async Task Record(HttpRequestMessage request,HttpResponseMessage response,bool probe,CancellationToken token)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var path=Path.GetFullPath(Environment.GetEnvironmentVariable("ARAIA_CONTEXT_RECEIPT")??throw new InvalidOperationException("Receipt required"));
        if(!new[]{"imob-cad-1a-integration","dcse-01"}.Any(folder=>path.StartsWith(Path.Combine(root,"TestResults",folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Receipt outside proof scope");
        var item=new JsonObject{["path"]=request.RequestUri!.AbsolutePath,["status"]=(int)response.StatusCode,["probeOnly"]=probe,["subject"]=http.HttpContext?.User.FindFirst("sub")?.Value};
        item["claimTypes"]=JsonSerializer.SerializeToNode(http.HttpContext?.User.Claims.Select(c=>c.Type).Distinct().Order().ToArray());
        try
        {
            var json=JsonNode.Parse(await response.Content.ReadAsStringAsync(token)) as JsonObject;
            if(json is not null)
            {
                item["keys"]=JsonSerializer.SerializeToNode(json.Select(p=>p.Key).Order().ToArray());
                foreach(var key in new[]{"schemaVersion","state","issuer","subject","organizationId","activeEstablishmentId","contextVersion","issuedAtUtc","expiresAtUtc"})if(json.TryGetPropertyValue(key,out var value))item[key]=value?.DeepClone();
                if(json["establishments"] is JsonArray units)item["unitIds"]=JsonSerializer.SerializeToNode(units.OfType<JsonObject>().Select(u=>u["id"]?.GetValue<string>()).ToArray());
                if(json["grants"] is JsonArray grants)item["erpGrants"]=new JsonArray(grants.OfType<JsonObject>().Where(g=>g["productKey"]?.GetValue<string>()=="real-estate").Select(g=>(JsonNode?)new JsonObject{["resourceKey"]=g["resourceKey"]?.DeepClone(),["permissionKey"]=g["permissionKey"]?.DeepClone()}).ToArray());
            }
        }
        catch(JsonException){item["invalidJson"]=true;}
        lock(Gate)File.AppendAllText(path,item.ToJsonString()+Environment.NewLine);
    }
}
