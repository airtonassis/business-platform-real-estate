using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;

namespace RealEstate.Tests;

public sealed class CoreOperationalHierarchyTests
{
    private sealed class Wire(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(respond(request)); }
    private static HttpResponseMessage Json(string body)=>new(HttpStatusCode.OK){Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json")};
    private static CoreOperationalHierarchyAccess Create(Func<HttpRequestMessage,HttpResponseMessage> respond)
    {
        var context=new DefaultHttpContext();context.Request.Path="/api/v1/negotiations";context.User=new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","actor")],"test"));context.Request.Headers.Authorization="Bearer actual-user-token";
        var client=new HttpClient(new Wire(respond)){BaseAddress=new Uri("https://core.test/")};
        return new(client,new HttpContextAccessor{HttpContext=context});
    }
    private static readonly Guid Org=Guid.NewGuid(),Unit=Guid.NewGuid(),Member=Guid.NewGuid();

    [Fact] public async Task Authority_uses_current_bearer_and_exact_org_unit_membership_route()
    {
        var adapter=Create(request=>{Assert.Equal("actual-user-token",request.Headers.Authorization?.Parameter);Assert.Equal($"/api/v1/organizations/{Org:D}/operational-hierarchy/{Member:D}/authority",request.RequestUri!.AbsolutePath);Assert.Contains($"establishmentId={Unit:D}",request.RequestUri.Query);return Json("{\"isImmediateSupervisor\":true,\"canRecoverOperationalResponsibility\":false,\"relationshipVersion\":4,\"decidedAtUtc\":\"2026-11-10T10:00:00Z\"}");});
        var result=await adapter.AuthorityAsync(new(Org,"actor","Org",[new(Unit,"Unit")],ActiveEstablishmentId:Unit),Member,default);
        Assert.True(result.IsSuccess);Assert.True(result.Value!.IsImmediateSupervisor);Assert.Equal(4,result.Value.RelationshipVersion);
    }

    [Fact] public async Task Core_unavailability_is_denied_without_a_positive_fallback()
    {
        var adapter=Create(_=>new(HttpStatusCode.ServiceUnavailable));
        var result=await adapter.RecipientAsync(new(Org,"actor","Org",[new(Unit,"Unit")],ActiveEstablishmentId:Unit),Member,default);
        Assert.False(result.IsSuccess);Assert.Equal("Dependency.Unavailable",result.Code);
    }

    [Fact] public async Task Missing_branch_or_membership_is_rejected_before_network_call()
    {
        var calls=0;var adapter=Create(_=>{calls++;return Json("{}");});
        var noBranch=await adapter.AuthorityAsync(new(Org,"actor","Org",[new(Unit,"Unit")]),Member,default);
        var noMember=await adapter.RecipientAsync(new(Org,"actor","Org",[new(Unit,"Unit")],ActiveEstablishmentId:Unit),Guid.Empty,default);
        Assert.Equal("Context.ActiveEstablishmentRequired",noBranch.Code);Assert.Equal("Hierarchy.InvalidInput",noMember.Code);Assert.Equal(0,calls);
    }
}
