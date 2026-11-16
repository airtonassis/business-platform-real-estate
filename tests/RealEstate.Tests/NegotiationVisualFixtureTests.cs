using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RealEstate.Domain;
using System.Net;
using System.Text.RegularExpressions;
using RealEstate.Web;
using Xunit;

namespace RealEstate.Tests;

public sealed class NegotiationVisualFixtureTests
{
    private static string Visible(string html)=>WebUtility.HtmlDecode(Regex.Replace(html,"<[^>]+>"," "));
    private static string Token(string html)=>Regex.Match(html,"name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    [Fact]
    public async Task Synthetic_routes_render_real_pages_and_transfer_visibility_is_explicit()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{NegotiationVisualFixture.Configure(b);b.WebHost.UseTestServer();});
        await app.StartAsync();var client=app.GetTestClient();
        var list=await client.GetStringAsync(NegotiationVisualFixture.ListUrl);
        Assert.Contains("Maria Silva",list);Assert.Contains("Lima",list);
        Assert.Contains("IM-123-V",list);Assert.Contains("IM-456-L",list);
        Assert.Contains("Negociações",list);
        Assert.Contains("Ana Costa",Visible(list));Assert.Contains("Bruno Reis",Visible(list));
        Assert.DoesNotContain("test-only-actor",Visible(list));Assert.DoesNotContain("second-actor",Visible(list));
        Assert.Contains("Nenhuma negociação encontrada",await client.GetStringAsync(NegotiationVisualFixture.EmptyUrl));
        var opening=await client.GetStringAsync(NegotiationVisualFixture.OpenUrl);
        Assert.Contains("IM-123",opening);Assert.Contains("não reserva",opening);
        var dossier=await client.GetStringAsync(NegotiationVisualFixture.DossierUrl);
        Assert.Contains("Transferir responsabilidade",dossier);Assert.Contains("Histórico",dossier);
        Assert.Contains("Ações",Visible(dossier));Assert.Contains("Adicionar ação",Visible(dossier));
        Assert.Contains("Jornada de Negociação",Visible(dossier));Assert.DoesNotContain("Dossiê",Visible(dossier));
        Assert.DoesNotContain("test-only-actor",Visible(dossier));Assert.DoesNotContain("second-actor",Visible(dossier));
        Assert.DoesNotContain("Transferir responsabilidade",await client.GetStringAsync(NegotiationVisualFixture.DossierWithoutTransferUrl));
        var property=await client.GetStringAsync(NegotiationVisualFixture.PropertyUrl);
        Assert.Contains("Origin=property",property);
        Assert.Contains("Ana Costa",Visible(property));Assert.DoesNotContain("test-only-actor",Visible(property));
        var customer=await client.GetStringAsync(NegotiationVisualFixture.CustomerUrl);
        Assert.Contains("Iniciar negociação",customer);
        var propertyOpening=await client.GetStringAsync(NegotiationVisualFixture.OpenUrl+"&Origin=property");
        Assert.Contains("/commercial/properties/"+NegotiationVisualFixture.PropertySale,propertyOpening);
    }

    [Fact]
    public async Task Contact_action_can_be_created_and_completed_from_the_negotiation_journey()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{NegotiationVisualFixture.Configure(b);b.WebHost.UseTestServer();});await app.StartAsync();
        var client=app.GetTestClient();var first=await client.GetAsync(NegotiationVisualFixture.DossierUrl);var html=await first.Content.ReadAsStringAsync();
        client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",first.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0])));
        Assert.Contains("data-negotiation-action-form",html);
        var due=TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow.AddDays(2),TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")).ToString("yyyy-MM-ddTHH:mm");
        var request=Guid.NewGuid();var fields=new Dictionary<string,string>{{"__RequestVerificationToken",Token(html)},{"Key",NegotiationVisualFixture.NegotiationSale.ToString()},{"ActionRequestId",request.ToString()},{"ActionKind","Contato"},{"ActionDescription","Retornar para confirmar as condições"},{"ActionDueLocal",due},{"ActionResponsibleSubject","test-only-actor"}};
        var saved=await client.PostAsync(NegotiationVisualFixture.DossierUrl+"?handler=CreateAction",new FormUrlEncodedContent(fields));Assert.Equal(HttpStatusCode.Redirect,saved.StatusCode);
        var scope=new AccessScope(NegotiationVisualFixture.Unit,"test-only-actor","TEST ONLY",[new(NegotiationVisualFixture.Unit,"Unit")],ActiveEstablishmentId:NegotiationVisualFixture.Unit);
        var store=app.Services.GetRequiredService<INegotiationActionStore>();var action=Assert.Single(await store.ListActionsAsync(scope,NegotiationVisualFixture.NegotiationSale,default));
        html=await client.GetStringAsync(NegotiationVisualFixture.DossierUrl);Assert.Contains("Retornar para confirmar as condições",Visible(html));Assert.Contains("Concluir",Visible(html));
        var completed=await client.PostAsync(NegotiationVisualFixture.DossierUrl+"?handler=CompleteAction",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",Token(html)},{"Key",NegotiationVisualFixture.NegotiationSale.ToString()},{"actionId",action.Id.ToString()},{"version",action.Version.ToString()},{"requestId",Guid.NewGuid().ToString()},{"CompletionResult","Cliente retornará amanhã"}}));
        Assert.Equal(HttpStatusCode.Redirect,completed.StatusCode);
        var after=Assert.Single(await store.ListActionsAsync(scope,NegotiationVisualFixture.NegotiationSale,default));Assert.Equal("Concluída",after.Status);Assert.Equal("Cliente retornará amanhã",after.Result);Assert.Equal(2,after.Version);
        html=Visible(await client.GetStringAsync(NegotiationVisualFixture.DossierUrl));Assert.Contains("Ana Costa",html);Assert.DoesNotContain("test-only-actor",html);
    }

    [Fact]
    public async Task Missing_directory_name_uses_neutral_label_without_exposing_subject()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{NegotiationVisualFixture.Configure(b);b.Services.RemoveAll<ICustomerDirectory>();b.Services.AddSingleton<ICustomerDirectory,UnavailableDirectory>();b.WebHost.UseTestServer();});
        await app.StartAsync();var html=await app.GetTestClient().GetStringAsync(NegotiationVisualFixture.ListUrl);
        Assert.Contains("Responsável não identificado",Visible(html));
        Assert.DoesNotContain("test-only-actor",Visible(html));Assert.DoesNotContain("second-actor",Visible(html));
    }

    [Fact]
    public async Task Foreign_directory_projection_is_not_displayed()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{NegotiationVisualFixture.Configure(b);b.Services.RemoveAll<ICustomerDirectory>();b.Services.AddSingleton<ICustomerDirectory,ForeignDirectory>();b.WebHost.UseTestServer();});
        await app.StartAsync();var html=Visible(await app.GetTestClient().GetStringAsync(NegotiationVisualFixture.ListUrl));
        Assert.Contains("Responsável não identificado",html);
        Assert.DoesNotContain("Nome de outra empresa",html);
    }

    private sealed class UnavailableDirectory:ICustomerDirectory
    {
        public Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope scope,Guid unit,string? subject=null,CancellationToken token=default)=>Task.FromResult(Outcome<CaptureResponsibleDirectory>.Fail("Dependency.Unavailable"));
    }
    private sealed class ForeignDirectory:ICustomerDirectory
    {
        public Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope scope,Guid unit,string? subject=null,CancellationToken token=default)=>Task.FromResult(new Outcome<CaptureResponsibleDirectory>(new(FixtureProgram.OrganizationB,unit,[new("test-only-actor","Nome de outra empresa",Guid.NewGuid(),"Corretor",Guid.NewGuid())])));
    }
}
