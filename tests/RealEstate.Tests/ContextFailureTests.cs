using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RealEstate.Domain;
using RealEstate.Application;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public sealed class ContextFailureTests
{
    private sealed class Wire(Func<string,HttpResponseMessage> respond):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t)=>Task.FromResult(respond(r.RequestUri!.AbsolutePath));}
    private static CoreAccess Adapter(Func<string,HttpResponseMessage> respond,bool authenticated=true)
    {
        var http=new DefaultHttpContext();http.Request.Path="/api/v2/properties";
        if(authenticated)http.User=new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","context-test")],"test"));
        http.Request.Headers.Authorization="Bearer synthetic-token";
        return new(new HttpClient(new Wire(respond)){BaseAddress=new Uri("https://core.test")},new HttpContextAccessor{HttpContext=http});
    }
    private static HttpResponseMessage Json(string body)=>new(HttpStatusCode.OK){Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json")};
    [Theory][InlineData("{\"state\":\"NotLinked\"}","Context.OrganizationRequired",409)][InlineData("{\"state\":\"Linked\",\"organizationId\":\"00000000-0000-0000-0000-000000000000\"}","Context.Invalid",409)][InlineData("{","Dependency.InvalidResponse",502)]
    public async Task Invalid_or_missing_organization_is_not_permission_or_service_outage(string body,string code,int status)
    {
        var result=await Adapter(_=>Json(body)).RequireAsync("properties","PROPERTY_CREATE",default);
        Assert.Equal(code,result.Code);Assert.Equal(status,Program.Status(result.Code));
    }
    [Theory][InlineData(401,"Authentication.Required",401)][InlineData(403,"Access.Forbidden",403)][InlineData(404,"Dependency.InvalidResponse",502)][InlineData(503,"Dependency.Unavailable",503)]
    public async Task Upstream_failure_categories_remain_distinct(int upstream,string code,int status)
    {
        var result=await Adapter(_=>new((HttpStatusCode)upstream)).RequireAsync("properties","PROPERTY_READ",default);
        Assert.Equal(code,result.Code);Assert.Equal(status,Program.Status(result.Code));
    }
    [Fact] public async Task Anonymous_requests_never_call_core()
    {
        var result=await Adapter(_=>throw new InvalidOperationException("Must not call Core"),false).RequireAsync("properties","PROPERTY_CREATE",default);
        Assert.Equal("Authentication.Required",result.Code);
    }
    private sealed class Grant(AccessScope scope):ICoreAccess
    {public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(new Outcome<AccessScope>(scope));}
    private sealed class NeverStore:IPropertyStoreV2
    {
        public Task<WorkspaceV2> ListV2Async(AccessScope s,CancellationToken t)=>throw new InvalidOperationException();
        public Task<PropertyV2?> FindV2Async(AccessScope s,Guid id,CancellationToken t)=>throw new InvalidOperationException();
        public Task<Outcome<PropertyV2>> SaveV2Async(AccessScope s,PropertyInputV2 p,Guid? id,long v,CancellationToken t)=>throw new InvalidOperationException("Must not persist invalid context");
    }
    [Fact] public async Task Missing_active_branch_is_context_conflict_without_selecting_first_authorized_unit()
    {
        var scope=new AccessScope(Guid.NewGuid(),"subject","Test",[new(Guid.NewGuid(),"Unit")]);
        var result=await new PropertyServiceV2(new Grant(scope),new NeverStore()).SaveAsync(new());
        Assert.Equal("Context.ActiveEstablishmentRequired",result.Code);Assert.Equal(409,Program.Status(result.Code));
    }
    [Fact] public async Task Empty_organization_is_invalid_context_not_anonymous_identity()
    {
        var scope=new AccessScope(Guid.Empty,"subject","Test",[new(Guid.NewGuid(),"Unit")]);
        Assert.Equal("Context.Invalid",(await new PropertyServiceV2(new Grant(scope),new NeverStore()).SaveAsync(new())).Code);
    }
}
