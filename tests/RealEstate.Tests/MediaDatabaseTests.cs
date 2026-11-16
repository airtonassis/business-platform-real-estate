using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
[Collection("PostgreSQL")]
public sealed class MediaDatabaseTests
{
    private sealed class NoFormBuffering:IStartupFilter{
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)=>app=>{app.Use(async(ctx,run)=>{if(ctx.Request.Query["handler"]=="Upload")ctx.Features.Set<IFormFeature>(new RejectForm());await run();});next(app);};
        private sealed class RejectForm:IFormFeature{
            public bool HasFormContentType=>true;
            public IFormCollection? Form{get=>throw new InvalidOperationException("Buffered form access forbidden in streaming proof");set=>throw new InvalidOperationException();}
            public IFormCollection ReadForm()=>throw new InvalidOperationException("Buffered form read forbidden");
            public Task<IFormCollection> ReadFormAsync(CancellationToken ct)=>throw new InvalidOperationException("Buffered form read forbidden");
        }
    }
    private sealed class Grant(AccessScope scope):ICoreAccess{public bool Denied;public Task<Outcome<AccessScope>> RequireAsync(string r,string p,CancellationToken c)=>Task.FromResult(Denied?Outcome<AccessScope>.Fail("Access.Forbidden"):new Outcome<AccessScope>(scope with{ActiveEstablishmentId=scope.Establishments?.FirstOrDefault()?.Id}));}
    private static NpgsqlDataSource Source()=>NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")??throw new InvalidOperationException("Use disposable runner"));
    private static async Task<(AccessScope Scope,PostgresPropertyStore Store,Grant Grant,Guid Id)> Property(NpgsqlDataSource ds){var s=new AccessScope(Guid.NewGuid(),"media-test","Synthetic",[new(Guid.NewGuid(),"Unit")]);var g=new Grant(s);var store=new PostgresPropertyStore(ds);var owner=(await new PropertyService(g,store).CreateOwnerAsync("Media owner")).Value!;var property=(await new PropertyServiceV2(g,store).SaveAsync(new("MEDIA-TEST","Synthetic media",owner.Id,"Residencial","Casa","Venda",3000))).Value!;return(s,store,g,property.Id);}
    private static MediaFile Photo()=>new MediaContentValidator().Validate("foto.png","image/png",MediaTestFiles.Png()).Value!;
    [Fact] public async Task Cover_order_delete_concurrency_rls_and_audit_are_atomic(){
        await using var ds=Source();var (scope,store,grant,id)=await Property(ds);var empty=(await store.ListMediaAsync(scope,id,default))!;
        var uploaded=(await store.ChangeMediaAsync(scope,id,empty.Revision,new("upload",Files:[Photo(),Photo()]),default)).Value!;
        Assert.Equal(2,uploaded.Items.Count);Assert.Single(uploaded.Items,x=>x.IsCover);
        Assert.Equal("Media.VersionConflict",(await store.ChangeMediaAsync(scope,id,empty.Revision,new("remove",uploaded.Items[0].Id),default)).Code);
        var reversed=uploaded.Items.Reverse().Select(x=>x.Id).ToArray();var reordered=(await store.ChangeMediaAsync(scope,id,uploaded.Revision,new("reorder",Order:reversed),default)).Value!;Assert.Equal(reversed,reordered.Items.Select(x=>x.Id));
        Assert.Equal("Media.InvalidOrder",(await store.ChangeMediaAsync(scope,id,reordered.Revision,new("reorder",Order:[Guid.NewGuid()]),default)).Code);
        var race=await Task.WhenAll(store.ChangeMediaAsync(scope,id,reordered.Revision,new("cover",reordered.Items[0].Id),default),store.ChangeMediaAsync(scope,id,reordered.Revision,new("cover",reordered.Items[1].Id),default));
        Assert.Single(race,x=>x.IsSuccess);Assert.Single(race,x=>x.Code=="Media.VersionConflict");
        var current=(await store.ListMediaAsync(scope,id,default))!;var cover=current.Items.Single(x=>x.IsCover);
        var removed=(await store.ChangeMediaAsync(scope,id,current.Revision,new("remove",cover.Id),default)).Value!;Assert.Single(removed.Items);Assert.Equal(0,removed.Items[0].Position);Assert.True(removed.Items[0].IsCover);Assert.Null(await store.ReadMediaAsync(scope,id,cover.Id,default));
        Assert.Null(await store.ListMediaAsync(scope with{OrganizationId=Guid.NewGuid()},id,default));Assert.Null(await store.ReadMediaAsync(scope with{Establishments=[new(Guid.NewGuid(),"Other")]},id,removed.Items[0].Id,default));
        Assert.Empty(await store.CoversAsync(scope with{OrganizationId=Guid.NewGuid()},[id],default));
        grant.Denied=true;var service=new PropertyMediaService(grant,store,store,new MediaContentValidator());Assert.Equal("Access.Forbidden",(await service.ListAsync(id)).Code);grant.Denied=false;
        await using var admin=new NpgsqlConnection(Environment.GetEnvironmentVariable("ErpTestAdminConnection"));await admin.OpenAsync();
        try{await using(var revoke=new NpgsqlCommand("REVOKE INSERT ON real_estate.events FROM erp_runtime",admin))await revoke.ExecuteNonQueryAsync();await Assert.ThrowsAsync<PostgresException>(()=>store.ChangeMediaAsync(scope,id,removed.Revision,new("remove",removed.Items[0].Id),default));}
        finally{await using var restore=new NpgsqlCommand("GRANT INSERT ON real_estate.events TO erp_runtime",admin);await restore.ExecuteNonQueryAsync();}
        Assert.Equal(removed.Revision,(await store.ListMediaAsync(scope,id,default))!.Revision);
        var last=(await store.ChangeMediaAsync(scope,id,removed.Revision,new("remove",removed.Items[0].Id),default)).Value!;Assert.Empty(last.Items);Assert.NotEqual(empty.Revision,last.Revision);
        Assert.Equal("Media.VersionConflict",(await store.ChangeMediaAsync(scope,id,empty.Revision,new("upload",Files:[Photo()]),default)).Code);
    }
    [Fact] public async Task Quota_and_database_constraints_hold_under_runtime_login(){
        await using var ds=Source();var(scope,store,grant,id)=await Property(ds);var current=(await store.ListMediaAsync(scope,id,default))!;
        // Trusted store boundary fixture only: payload validity is separately tested at service/HTTP.
        var video=new MediaFile("quota.mp4","video/mp4","video",new byte[MediaRules.VideoLimit],new string('A',64));
        for(var i=0;i<5;i++){var result=await store.ChangeMediaAsync(scope,id,current.Revision,new("upload",Files:[video]),default);Assert.True(result.IsSuccess,result.Code);current=result.Value!;}
        Assert.Equal("Media.TooLarge",(await store.ChangeMediaAsync(scope,id,current.Revision,new("upload",Files:[Photo()]),default)).Code);
        Assert.Equal("Media.InvalidContent",(await store.ChangeMediaAsync(scope,id,current.Revision,new("cover",current.Items[0].Id),default)).Code);Assert.DoesNotContain(current.Items,x=>x.IsCover);
        await using var c=await ds.OpenConnectionAsync();await using var tx=await c.BeginTransactionAsync();
        await using(var set=new NpgsqlCommand("SELECT set_config('erp.organization_id',@org,true),set_config('erp.establishments',@units,true)",c,tx)){set.Parameters.AddWithValue("org",scope.OrganizationId.ToString());set.Parameters.AddWithValue("units","{"+scope.Establishments![0].Id+"}");await set.ExecuteNonQueryAsync();}
        await using var invalid=new NpgsqlCommand("UPDATE real_estate.property_media SET is_cover=true WHERE organization_id=@org AND property_id=@id",c,tx);invalid.Parameters.AddWithValue("org",scope.OrganizationId);invalid.Parameters.AddWithValue("id",id);Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>invalid.ExecuteNonQueryAsync())).SqlState);
    }
    [Fact] public async Task Razor_upload_streams_atomically_and_content_requires_authorization(){
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{FixtureProgram.Configure(b);b.Services.AddSingleton<IStartupFilter,NoFormBuffering>();b.WebHost.UseTestServer();});await app.StartAsync();using var client=app.GetTestClient();
        var input=new PropertyInputV2("MEDIA-HTTP-"+Guid.NewGuid().ToString("N"),"HTTP media",Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),"Residencial","Casa","Venda",3000,EstablishmentId:FixtureProgram.OrganizationA);
        var saved=(await(await client.PostAsJsonAsync("/api/v2/properties",input)).Content.ReadFromJsonAsync<PropertyV2>())!;var path="/properties/"+saved.Id;
        var page=await client.GetAsync(path);var html=await page.Content.ReadAsStringAsync();var token=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;Assert.NotEmpty(token);
        client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",page.Headers.GetValues("Set-Cookie").Select(c=>c.Split(';')[0])));client.DefaultRequestHeaders.Add("RequestVerificationToken",WebUtility.HtmlDecode(token));
        await using var ds=Source();var store=new PostgresPropertyStore(ds);var scope=new AccessScope(FixtureProgram.OrganizationA,"test-only-actor","Synthetic",[new(FixtureProgram.OrganizationA,"Unit")]);
        async Task<HttpResponseMessage> Upload(params (string Name,string Mime,byte[] Bytes)[] files){var current=(await store.ListMediaAsync(scope,saved.Id,default))!;client.DefaultRequestHeaders.Remove("X-Media-Revision");client.DefaultRequestHeaders.Add("X-Media-Revision",current.Revision);using var body=new MultipartFormDataContent();foreach(var f in files){var file=new ByteArrayContent(f.Bytes);file.Headers.ContentType=new(f.Mime);body.Add(file,"files",f.Name);}return await client.PostAsync(path+"?handler=Upload",body);}
        Assert.Equal(HttpStatusCode.OK,(await Upload(("one.png","image/png",MediaTestFiles.Png()),("two.png","image/png",MediaTestFiles.Png()))).StatusCode);
        var gallery=(await store.ListMediaAsync(scope,saved.Id,default))!;Assert.Equal(2,gallery.Items.Count);var contentPath=path+"?handler=Content&mediaId="+gallery.Items[0].Id;
        var content=await client.GetAsync(contentPath);Assert.Equal(HttpStatusCode.OK,content.StatusCode);Assert.Equal("image/png",content.Content.Headers.ContentType!.MediaType);Assert.Equal(MediaTestFiles.Png(),await content.Content.ReadAsByteArrayAsync());
        using(var request=new HttpRequestMessage(HttpMethod.Get,contentPath)){request.Headers.Range=new(0,7);var range=await client.SendAsync(request);Assert.Equal(HttpStatusCode.PartialContent,range.StatusCode);Assert.Equal(8,(await range.Content.ReadAsByteArrayAsync()).Length);}
        Assert.Equal(HttpStatusCode.BadRequest,(await Upload(("valid.png","image/png",MediaTestFiles.Png()),("bad.jpg","image/jpeg","fake"u8.ToArray()))).StatusCode);Assert.Equal(gallery.Revision,(await store.ListMediaAsync(scope,saved.Id,default))!.Revision);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await Upload(("large.png","image/png",new byte[MediaRules.ImageLimit+1]))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await Upload(Enumerable.Range(0,21).Select(i=>("f"+i+".png","image/png",MediaTestFiles.Png())).ToArray())).StatusCode);
        client.DefaultRequestHeaders.Add("X-Fixture-ReadOnly","true");Assert.Equal(HttpStatusCode.Forbidden,(await Upload(("no.png","image/png",MediaTestFiles.Png()))).StatusCode);Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(contentPath)).StatusCode);client.DefaultRequestHeaders.Remove("X-Fixture-ReadOnly");
        foreach(var (header,value,status) in new[]{("X-Fixture-Organization","B",404),("X-Fixture-Unit",Guid.NewGuid().ToString(),404),("X-Fixture-Anonymous","true",401)}){client.DefaultRequestHeaders.Add(header,value);Assert.Equal(status,(int)(await client.GetAsync(contentPath)).StatusCode);client.DefaultRequestHeaders.Remove(header);}
        var list=await client.GetStringAsync("/");Assert.Contains("property-cover",list);Assert.Contains(gallery.Items.Single(x=>x.IsCover).Id.ToString(),list);
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");Assert.Equal(HttpStatusCode.BadRequest,(await Upload(("csrf.png","image/png",MediaTestFiles.Png()))).StatusCode);
    }
}
