using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public sealed class CustomerDirectoryTests
{
    private sealed class Wire(Func<HttpRequestMessage,HttpResponseMessage> handler):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(handler(request));}
    [Theory][InlineData("valid",true)][InlineData("foreign",false)][InlineData("wrong-unit",false)][InlineData("wrong-subject",false)][InlineData("duplicate",false)][InlineData("denied",false)]
    public async Task Forwards_real_token_and_rejects_invalid_or_denied_projection(string mode,bool success){
        var org=Guid.NewGuid();var unit=Guid.NewGuid();var scope=new AccessScope(org,"actor","Company",[new(unit,"Unit")],ActiveEstablishmentId:unit);
        var context=new DefaultHttpContext();context.User=new(new ClaimsIdentity([new Claim("sub","actor")],"test"));context.Request.Path="/api/v1/customers/responsibles";context.Request.Headers.Authorization="Bearer test-only-token";
        var client=new HttpClient(new Wire(request=>{
            Assert.Equal("test-only-token",request.Headers.Authorization!.Parameter);Assert.EndsWith("/api/v1/enterprise-directory/operational-customer-candidates",request.RequestUri!.AbsolutePath);Assert.Contains("establishmentId="+unit.ToString("D"),request.RequestUri.Query);Assert.Contains("subject=actor",request.RequestUri.Query);
            var person=new CaptureResponsible(mode=="wrong-subject"?"other":"actor","Name",Guid.NewGuid(),"Administrative");
            return new(mode=="denied"?HttpStatusCode.Forbidden:HttpStatusCode.OK){Content=JsonContent.Create(new CaptureResponsibleDirectory(mode=="foreign"?Guid.NewGuid():org,mode=="wrong-unit"?Guid.NewGuid():unit,mode=="duplicate"?[person,person]:[person]))};
        })){BaseAddress=new("https://core.test/")};
        var result=await new CoreCustomerDirectory(client,new HttpContextAccessor{HttpContext=context}).ReadAsync(scope,unit,"actor");Assert.Equal(success,result.IsSuccess);if(mode=="denied")Assert.Equal("Access.Forbidden",result.Code);
    }
}
