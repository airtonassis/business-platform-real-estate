using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public sealed class TeamRulesTests
{
 [Theory][InlineData("Corretor",null,null,false)][InlineData("Corretora","12345","XX",false)][InlineData("Corretor","12345","SP",true)][InlineData("Atendente",null,null,true)]
 public void Professional_rules_validate_registration(string title,string? creci,string? uf,bool expected)=>Assert.Equal(expected,TeamRules.Validate(new(title,creci,uf,["Locação"],null)).IsSuccess);
 private sealed class Wire(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t)=>Task.FromResult(send(r));}
 [Theory][InlineData("valid",true)][InlineData("foreign",false)][InlineData("unit",false)][InlineData("grant",false)][InlineData("duplicate",false)][InlineData("denied",false)][InlineData("old-core",false)]
 public async Task Adapter_forwards_token_and_fails_closed(string scenario,bool expected)
 {
  var org=Guid.NewGuid();var unit=Guid.NewGuid();var id=Guid.NewGuid();var context=new DefaultHttpContext();context.User=new(new ClaimsIdentity([new Claim("sub","actor")],"test"));context.Request.Path="/api/v1/team";context.Request.Headers.Authorization="Bearer synthetic-only";
  var person=new TeamPerson(id,"broker","Broker","broker@corp","Active",true,Guid.NewGuid(),[new(scenario=="grant"?"other":"real-estate","customers","CUSTOMER_RESPONSIBLE")],[new(unit,"Matriz",true,true,true)]);
  var client=new HttpClient(new Wire(request=>{Assert.Equal("synthetic-only",request.Headers.Authorization!.Parameter);Assert.Equal("/api/v1/enterprise-directory/team",request.RequestUri!.AbsolutePath);return new(scenario=="denied"?HttpStatusCode.Forbidden:scenario=="old-core"?HttpStatusCode.NotFound:HttpStatusCode.OK){Content=JsonContent.Create(new TeamDirectory(scenario=="foreign"?Guid.NewGuid():org,scenario=="unit"?Guid.NewGuid():unit,"real-estate",scenario=="duplicate"?[person,person]:[person]))};})){BaseAddress=new("https://core.test/")};
  var result=await new CoreTeamDirectory(client,new HttpContextAccessor{HttpContext=context}).ReadAsync(new(org,"actor","Company",[new(unit,"Matriz")],true,unit));Assert.Equal(expected,result.IsSuccess);if(scenario=="old-core")Assert.Equal("Team.ContractUnavailable",result.Code);
 }
}
