using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public sealed class CoreAccessTests
{
    private sealed class Wire(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t)=>Task.FromResult(respond(r));}
    private static CoreAccess Adapter(Func<HttpRequestMessage,HttpResponseMessage> wire)
    {
        var http=new DefaultHttpContext();http.Request.Path="/api/v1/properties";http.User=new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","subject")],"test"));http.Request.Headers.Authorization="Bearer test-only-token";
        return new(new HttpClient(new Wire(r=>r.RequestUri!.AbsolutePath.EndsWith("establishments")?Json("{\"establishments\":[{\"id\":\"33333333-3333-3333-3333-333333333333\",\"name\":\"Test unit\"}]}"):wire(r))){BaseAddress=new Uri("https://core.test/")},new HttpContextAccessor{HttpContext=http});
    }
    private static HttpResponseMessage Json(string body)=>new(HttpStatusCode.OK){Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json")};
    private const string Linked="{\"state\":\"Linked\",\"organizationId\":\"11111111-1111-1111-1111-111111111111\",\"organizationName\":\"Empresa\"}";
    [Fact] public async Task Active_branch_from_server_context_is_mapped_and_validated()
    {
        var unit="33333333-3333-3333-3333-333333333333";
        var body=Linked[..^1]+",\"activeEstablishmentId\":\""+unit+"\"}";
        var adapter=Adapter(r=>r.RequestUri!.AbsolutePath.EndsWith("context")?Json(body):Json("{\"grants\":[{\"productKey\":\"real-estate\",\"resourceKey\":\"properties\",\"permissionKey\":\"PROPERTY_CREATE\"}]}"));
        var result=await adapter.RequireAsync("properties","PROPERTY_CREATE",default);
        Assert.True(result.IsSuccess);Assert.Equal(Guid.Parse(unit),result.Value!.ActiveEstablishmentId);
    }
    [Fact] public async Task Active_branch_outside_current_authorization_is_denied()
    {
        var body=Linked[..^1]+",\"activeEstablishmentId\":\"44444444-4444-4444-4444-444444444444\"}";
        var adapter=Adapter(r=>r.RequestUri!.AbsolutePath.EndsWith("context")?Json(body):Json("{\"grants\":[{\"productKey\":\"real-estate\",\"resourceKey\":\"properties\",\"permissionKey\":\"PROPERTY_CREATE\"}]}"));
        Assert.Equal("Access.Forbidden",(await adapter.RequireAsync("properties","PROPERTY_CREATE",default)).Code);
    }
    [Fact] public async Task No_license_or_assignment_grants_means_denied()
    {
        var adapter=Adapter(r=>r.RequestUri!.AbsolutePath.EndsWith("context")?Json(Linked):Json("{\"grants\":[]}"));
        Assert.Equal("Access.Forbidden",(await adapter.RequireAsync("properties","PROPERTY_CREATE",default)).Code);
    }
    [Fact] public async Task Wrong_product_cannot_authorize()
    {
        var adapter=Adapter(r=>r.RequestUri!.AbsolutePath.EndsWith("context")?Json(Linked):Json("{\"grants\":[{\"productKey\":\"other\",\"resourceKey\":\"properties\",\"permissionKey\":\"PROPERTY_CREATE\"}]}"));
        Assert.Equal("Access.Forbidden",(await adapter.RequireAsync("properties","PROPERTY_CREATE",default)).Code);
    }
    [Fact] public async Task Exact_grant_uses_server_organization_and_forwards_bearer()
    {
        var adapter=Adapter(r=>{Assert.Equal("test-only-token",r.Headers.Authorization!.Parameter);return r.RequestUri!.AbsolutePath.EndsWith("context")?Json(Linked):Json("{\"grants\":[{\"productKey\":\"real-estate\",\"resourceKey\":\"properties\",\"permissionKey\":\"PROPERTY_READ\"}]}");});
        var result=await adapter.RequireAsync("properties","PROPERTY_READ",default);Assert.True(result.IsSuccess);Assert.Equal(FixtureProgram.OrganizationA,result.Value!.OrganizationId);
    }
    [Fact] public async Task Core_outage_is_closed_and_distinct_from_no_license()
    {
        var adapter=Adapter(_=>new(HttpStatusCode.ServiceUnavailable));Assert.Equal("Dependency.Unavailable",(await adapter.RequireAsync("properties","PROPERTY_READ",default)).Code);
    }
    [Fact] public async Task Revocation_requeries_core_instead_of_using_cached_claims()
    {
        var granted=true;var adapter=Adapter(r=>r.RequestUri!.AbsolutePath.EndsWith("context")?Json(Linked):Json(granted?"{\"grants\":[{\"productKey\":\"real-estate\",\"resourceKey\":\"properties\",\"permissionKey\":\"PROPERTY_READ\"}]}":"{\"grants\":[]}"));
        Assert.True((await adapter.RequireAsync("properties","PROPERTY_READ",default)).IsSuccess);granted=false;Assert.Equal("Access.Forbidden",(await adapter.RequireAsync("properties","PROPERTY_READ",default)).Code);
    }
}

