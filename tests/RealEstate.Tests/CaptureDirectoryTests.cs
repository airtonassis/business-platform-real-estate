using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;

public sealed class CaptureDirectoryTests
{
    static readonly Guid Org=Guid.NewGuid(),Unit=Guid.NewGuid();
    static AccessScope Scope=>new(Org,"manager","Company",[new(Unit,"Unit")],true,Unit);
    sealed class Access:ICoreAccess{public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken t)=>Task.FromResult(new Outcome<AccessScope>(Scope));}
    sealed class Directory:ICaptureDirectory
    {
        public bool Eligible=true,Foreign,Unavailable;public int Calls;
        public Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope s,Guid u,string? subject=null,CancellationToken token=default){Calls++;return Task.FromResult(Unavailable?Outcome<CaptureResponsibleDirectory>.Fail("Dependency.Unavailable"):new(new(Foreign?Guid.NewGuid():Org,Unit,Eligible?[new(subject??"candidate","Candidate",Guid.NewGuid(),"Broker")]:[])));}
    }
    sealed class Store:IPropertyStoreV2
    {
        public PropertyV2? Existing;public int Writes;
        public Task<WorkspaceV2> ListV2Async(AccessScope s,CancellationToken t)=>throw new NotSupportedException();
        public Task<PropertyV2?> FindV2Async(AccessScope s,Guid id,CancellationToken t)=>Task.FromResult(Existing);
        public Task<Outcome<PropertyV2>> SaveV2Async(AccessScope s,PropertyInputV2 input,Guid? id,long version,CancellationToken t){Writes++;Existing=new(id??Guid.NewGuid(),s.OrganizationId,input,version+1,"Draft");return Task.FromResult(new Outcome<PropertyV2>(Existing));}
    }
    static PropertyInputV2 Input(string? subject)=>new(EstablishmentId:Unit,Capture:new(){Acquisition=new(){ResponsibleSubject=subject}});
    [Fact]public async Task Assignment_is_revalidated_and_revocation_blocks_a_new_choice()
    {var directory=new Directory();var store=new Store();var service=new PropertyServiceV2(new Access(),store,directory);Assert.True((await service.SaveAsync(Input("candidate"))).IsSuccess);directory.Eligible=false;Assert.Equal("Access.Forbidden",(await service.SaveAsync(Input("another"))).Code);Assert.Equal(1,store.Writes);Assert.Equal(2,directory.Calls);}
    [Fact]public async Task Previously_assigned_subject_is_preserved_even_when_no_longer_eligible()
    {var directory=new Directory{Eligible=false};var store=new Store{Existing=new(Guid.NewGuid(),Org,Input("former"),3,"Draft")};Assert.True((await new PropertyServiceV2(new Access(),store,directory).SaveAsync(Input("former"),store.Existing.Id,3)).IsSuccess);Assert.Equal(1,directory.Calls);}
    [Fact]public async Task Existing_assignment_is_revalidated_and_foreign_directory_response_cannot_write()
    {var directory=new Directory{Foreign=true};var store=new Store{Existing=new(Guid.NewGuid(),Org,Input("former"),3,"Draft")};Assert.Equal("Access.Forbidden",(await new PropertyServiceV2(new Access(),store,directory).SaveAsync(Input("former"),store.Existing.Id,3)).Code);Assert.Equal(1,directory.Calls);Assert.Equal(0,store.Writes);}
    [Fact]public async Task Omitted_capture_still_revalidates_preserved_responsible()
    {var directory=new Directory{Eligible=false};var store=new Store{Existing=new(Guid.NewGuid(),Org,Input("former"),3,"Draft")};Assert.True((await new PropertyServiceV2(new Access(),store,directory).SaveAsync(new(Title:"Other field",EstablishmentId:Unit),store.Existing.Id,3)).IsSuccess);Assert.Equal(1,directory.Calls);}
    [Theory][InlineData(true,false,"Access.Forbidden")][InlineData(false,true,"Dependency.Unavailable")]
    public async Task Foreign_context_and_outage_never_write(bool foreign,bool unavailable,string code)
    {var d=new Directory{Foreign=foreign,Unavailable=unavailable};var s=new Store();Assert.Equal(code,(await new PropertyServiceV2(new Access(),s,d).SaveAsync(Input("candidate"))).Code);Assert.Equal(0,s.Writes);}
    [Fact]public async Task Empty_draft_needs_no_directory_and_self_assignment_requires_the_same_eligibility()
    {var s=new Store();var service=new PropertyServiceV2(new Access(),s);Assert.True((await service.SaveAsync(new())).IsSuccess);Assert.Equal("Access.Forbidden",(await service.SaveAsync(Input("manager"))).Code);}
    sealed class Wire(Func<HttpRequestMessage,HttpResponseMessage> run):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t)=>Task.FromResult(run(r));}
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Adapter_forwards_token_and_rejects_foreign_context(bool foreign)
    {
        var http=new DefaultHttpContext();http.Request.Path="/api/v2/properties";http.User=new(new ClaimsIdentity([new Claim("sub","manager")],"fixture"));http.Request.Headers.Authorization="Bearer fixture-token";
        var client=new HttpClient(new Wire(r=>{Assert.Equal("fixture-token",r.Headers.Authorization!.Parameter);Assert.Contains("permissionKey=CAPTURE_RESPONSIBLE",r.RequestUri!.Query);Assert.Contains("subject=candidate",r.RequestUri.Query);return new(HttpStatusCode.OK){Content=JsonContent.Create(new CaptureResponsibleDirectory(foreign?Guid.NewGuid():Org,Unit,[new("candidate","Candidate",Guid.NewGuid(),"Broker")]))};})){BaseAddress=new("https://core.test/")};
        var result=await new CoreCaptureDirectory(client,new HttpContextAccessor{HttpContext=http}).ReadAsync(Scope,Unit,"candidate");Assert.Equal(!foreign,result.IsSuccess);if(foreign)Assert.Equal("Dependency.InvalidResponse",result.Code);
    }
}
