using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public sealed class ContextSnapshotTests
{
    private sealed class CookieTokens:IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext http,string? scheme){var properties=new AuthenticationProperties();properties.StoreTokens([new(){Name="access_token",Value="test-token"}]);return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(http.User,properties,"unit-test")));}
        public Task ChallengeAsync(HttpContext c,string? s,AuthenticationProperties? p)=>throw new NotSupportedException();
        public Task ForbidAsync(HttpContext c,string? s,AuthenticationProperties? p)=>throw new NotSupportedException();
        public Task SignInAsync(HttpContext c,string? s,ClaimsPrincipal u,AuthenticationProperties? p)=>throw new NotSupportedException();
        public Task SignOutAsync(HttpContext c,string? s,AuthenticationProperties? p)=>throw new NotSupportedException();
    }
    private sealed class Wire(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    {public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t){Calls++;return Task.FromResult(respond(r));}}
    private static readonly Guid Org=Guid.NewGuid(),Unit=Guid.NewGuid();
    private static string Snapshot(string subject="test-subject",DateTimeOffset? expires=null,Guid? activeUnit=null,string permission="PROPERTY_CREATE")=>JsonSerializer.Serialize(new{
        schemaVersion=1,state="Linked",issuer="https://idp.test",subject,organizationId=Org,organizationName="Test",role="Member",
        activeEstablishmentId=activeUnit??Unit,contextVersion=1,issuedAtUtc=DateTimeOffset.UtcNow,expiresAtUtc=expires??DateTimeOffset.UtcNow.AddSeconds(4),
        establishments=new[]{new{id=activeUnit??Unit,name="Unidade"}},grants=new[]{new{productKey="real-estate",resourceKey="properties",permissionKey=permission}}});
    private static ServiceProvider Services(Wire wire,bool issuerClaim=true)
    {
        var s=new ServiceCollection();s.AddLogging();s.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Core:ContextVersion","1"},{"Oidc:Authority","https://idp.test"}}).Build());
        s.AddSingleton<CoreContextCache>();
        s.AddSingleton<IAuthenticationService,CookieTokens>();
        var http=new DefaultHttpContext();http.User=new(new ClaimsIdentity([new("sub","test-subject"),new("iss","https://idp.test")],"test"));http.Request.Path="/api/v2/properties";http.Request.Headers.Authorization="Bearer test-token";
        if(!issuerClaim)((ClaimsIdentity)http.User.Identity!).RemoveClaim(http.User.FindFirst("iss")!);
        s.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor{HttpContext=http});s.AddHttpClient<ICoreAccess,CoreAccess>(c=>c.BaseAddress=new Uri("https://core.test/")).ConfigurePrimaryHttpMessageHandler(()=>wire);
        var provider=s.BuildServiceProvider();http.RequestServices=provider;return provider;
    }
    [Fact] public async Task Versioned_context_supplies_actual_scope_and_local_permission()
    {
        var wire=new Wire(r=>new(r.RequestUri!.AbsolutePath=="/api/v1/access/context"?HttpStatusCode.OK:HttpStatusCode.NotFound){Content=new StringContent(Snapshot(),System.Text.Encoding.UTF8,"application/json")});
        using var s=Services(wire);var result=await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_CREATE",default);
        Assert.True(result.IsSuccess,result.Code);Assert.Equal(Unit,result.Value!.ActiveEstablishmentId);Assert.Equal(1,wire.Calls);
    }
    [Fact] public async Task Approval_requires_exact_core_grant_even_for_a_member()
    {
        var denied=new Wire(_=>new(HttpStatusCode.OK){Content=new StringContent(Snapshot(),System.Text.Encoding.UTF8,"application/json")});
        using(var services=Services(denied))Assert.Equal("Access.Forbidden",(await services.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_APPROVE",default)).Code);
        var allowed=new Wire(_=>new(HttpStatusCode.OK){Content=new StringContent(Snapshot(permission:"PROPERTY_APPROVE"),System.Text.Encoding.UTF8,"application/json")});
        using(var services=Services(allowed)){var result=await services.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_APPROVE",default);Assert.True(result.IsSuccess,result.Code);Assert.Equal(Unit,result.Value!.ActiveEstablishmentId);Assert.False(result.Value.LegacyManager);}
    }
    [Fact] public async Task Snapshot_is_reused_across_requests_but_each_permission_is_checked_locally()
    {
        var wire=new Wire(_=>new(HttpStatusCode.OK){Content=new StringContent(Snapshot(),System.Text.Encoding.UTF8,"application/json")});
        using var s=Services(wire);
        Assert.True((await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_CREATE",default)).IsSuccess);
        s.GetRequiredService<IHttpContextAccessor>().HttpContext!.Items.Clear();
        Assert.Equal("Access.Forbidden",(await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_UPDATE",default)).Code);
        Assert.Equal(1,wire.Calls);
    }
    [Fact] public async Task Wrong_subject_is_never_cached_or_authorized()
    {
        var wire=new Wire(_=>new(HttpStatusCode.OK){Content=new StringContent(Snapshot("someone-else"),System.Text.Encoding.UTF8,"application/json")});
        using var s=Services(wire);
        for(var i=0;i<2;i++)Assert.Equal("Dependency.InvalidResponse",(await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_CREATE",default)).Code);
        Assert.Equal(2,wire.Calls);
    }
    [Fact] public async Task Expired_snapshot_and_outage_do_not_extend_authority()
    {
        var wire=new Wire(_=>new(HttpStatusCode.OK){Content=new StringContent(Snapshot(expires:DateTimeOffset.UtcNow.AddSeconds(-1)),System.Text.Encoding.UTF8,"application/json")});
        using var s=Services(wire);
        Assert.Equal("Dependency.InvalidResponse",(await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_CREATE",default)).Code);
    }
    [Fact] public async Task Token_change_does_not_reuse_another_session_snapshot()
    {
        var wire=new Wire(_=>new(HttpStatusCode.OK){Content=new StringContent(Snapshot(),System.Text.Encoding.UTF8,"application/json")});using var s=Services(wire);
        Assert.True((await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_CREATE",default)).IsSuccess);
        s.GetRequiredService<IHttpContextAccessor>().HttpContext!.Request.Headers.Authorization="Bearer another-token";
        Assert.True((await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_CREATE",default)).IsSuccess);Assert.Equal(2,wire.Calls);
    }
    [Fact] public async Task Outage_after_lease_expiry_does_not_reuse_previous_grants()
    {
        var calls=0;var wire=new Wire(_=>++calls==1?new(HttpStatusCode.OK){Content=new StringContent(Snapshot(expires:DateTimeOffset.UtcNow.AddMilliseconds(700)),System.Text.Encoding.UTF8,"application/json")}:new(HttpStatusCode.ServiceUnavailable));
        using var s=Services(wire);
        Assert.True((await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_CREATE",default)).IsSuccess);
        await Task.Delay(800);
        Assert.Equal("Dependency.Unavailable",(await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_CREATE",default)).Code);Assert.Equal(2,wire.Calls);
    }
    [Fact] public async Task Cookie_principal_uses_existing_trusted_oidc_authority_when_protocol_issuer_claim_is_omitted()
    {
        var wire=new Wire(_=>new(HttpStatusCode.OK){Content=new StringContent(Snapshot(),System.Text.Encoding.UTF8,"application/json")});using var s=Services(wire,issuerClaim:false);
        Assert.True((await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_CREATE",default)).IsSuccess);Assert.Equal(1,wire.Calls);
    }
    [Fact] public async Task Write_revalidates_changed_unit_then_reuses_one_snapshot_within_the_command()
    {
        var next=Guid.NewGuid();var n=0;var wire=new Wire(_=>new(HttpStatusCode.OK){Content=new StringContent(Snapshot(activeUnit:++n==1?Unit:next),System.Text.Encoding.UTF8,"application/json")});using var s=Services(wire);
        Assert.Equal(Unit,(await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_CREATE",default)).Value!.ActiveEstablishmentId);
        s.GetRequiredService<IHttpContextAccessor>().HttpContext!.Request.Method="POST";
        Assert.Equal(next,(await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_CREATE",default)).Value!.ActiveEstablishmentId);
        Assert.Equal(next,(await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_CREATE",default)).Value!.ActiveEstablishmentId);Assert.Equal(2,wire.Calls);
    }
    [Fact] public async Task Write_cannot_use_a_read_lease_during_core_outage()
    {
        var n=0;var wire=new Wire(_=>++n==1?new(HttpStatusCode.OK){Content=new StringContent(Snapshot(),System.Text.Encoding.UTF8,"application/json")}:new(HttpStatusCode.ServiceUnavailable));using var s=Services(wire);
        Assert.True((await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_CREATE",default)).IsSuccess);s.GetRequiredService<IHttpContextAccessor>().HttpContext!.Request.Method="POST";
        Assert.Equal("Dependency.Unavailable",(await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_CREATE",default)).Code);
    }
    [Fact] public async Task New_draft_form_displays_fresh_active_unit()
    {
        var next=Guid.NewGuid();var n=0;var wire=new Wire(_=>new(HttpStatusCode.OK){Content=new StringContent(Snapshot(activeUnit:++n==1?Unit:next),System.Text.Encoding.UTF8,"application/json")});using var s=Services(wire);
        Assert.Equal(Unit,(await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_CREATE",default)).Value!.ActiveEstablishmentId);
        var http=s.GetRequiredService<IHttpContextAccessor>().HttpContext!;http.Items.Clear();http.Request.Path="/properties/new";
        Assert.Equal(next,(await s.GetRequiredService<ICoreAccess>().RequireAsync("properties","PROPERTY_CREATE",default)).Value!.ActiveEstablishmentId);Assert.Equal(2,wire.Calls);
    }
}
