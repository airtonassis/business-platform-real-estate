using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public sealed class CommercialAppointmentHttpTests
{
    private static readonly string Path="/commercial/customers/"+CommercialAppointmentFixture.CustomerId;
    private static string Token(string html)=>Regex.Match(html,"name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    private static string Local(DateTimeOffset time)=>TimeZoneInfo.ConvertTime(time,TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")).ToString("yyyy-MM-ddTHH:mm");
    [Fact]public async Task Presented_property_opens_prefilled_visit_and_revalidates_link_before_save()
    {
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{CommercialAppointmentFixture.Configure(b);b.WebHost.UseTestServer();});await app.StartAsync();
        app.Services.GetRequiredService<FixtureInteractions>().HasPresentation=true;
        var client=app.GetTestClient();var url=Path+"?VisitPresentation="+CommercialAppointmentFixture.PresentationId+"#customer-agenda";
        var first=await client.GetAsync(url);var html=await first.Content.ReadAsStringAsync();
        client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",first.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0])));
        Assert.Contains("Agendar visita",html);Assert.Contains("IM-001",html);Assert.Contains("Cliente de teste",html);Assert.Contains("ContextualPresentationId",html);
        var start=DateTimeOffset.UtcNow.AddDays(3);var requestId=Guid.NewGuid();
        var fields=new Dictionary<string,string>{{"__RequestVerificationToken",Token(html)},{"AppointmentRequestId",requestId.ToString()},{"ContextualPresentationId",Guid.NewGuid().ToString()},{"AppointmentPropertyId",Guid.NewGuid().ToString()},{"AppointmentStart",Local(start)},{"AppointmentEnd",Local(start.AddHours(1))},{"AppointmentLocation","Local de teste"}};
        Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsync(Path+"?handler=ContextualVisit",new FormUrlEncodedContent(fields))).StatusCode);
        fields["ContextualPresentationId"]=CommercialAppointmentFixture.PresentationId.ToString();
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync(Path+"?handler=ContextualVisit",new FormUrlEncodedContent(fields))).StatusCode);
        var scope=new AccessScope(FixtureProgram.OrganizationA,"test-only-actor","Teste",[new(FixtureProgram.OrganizationA,"Unidade de teste")],ActiveEstablishmentId:FixtureProgram.OrganizationA);
        var item=Assert.Single(await app.Services.GetRequiredService<ICommercialAppointmentStore>().ListAsync(scope,CommercialAppointmentFixture.CustomerId,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddDays(10),default));
        Assert.Equal(UiFixture.PropertyId,item.PropertyId);Assert.Equal(AppointmentRules.PropertyVisit,item.Kind);
        Assert.Contains("Imóvel vinculado",await client.GetStringAsync(Path+"?Panel=agenda"));
        Assert.Contains("Cliente de teste",await client.GetStringAsync("/commercial/agenda?View=week&Date="+DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(item.StartsAt,TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")).DateTime).ToString("yyyy-MM-dd")));
    }
    [Fact]public async Task Calendar_filters_show_named_details_and_manage_customer_appointment()
    {
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{CommercialAppointmentFixture.Configure(b);b.WebHost.UseTestServer();});await app.StartAsync();
        var client=app.GetTestClient();var initial=await client.GetAsync(Path);var html=await initial.Content.ReadAsStringAsync();
        client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",initial.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0])));
        var start=DateTimeOffset.UtcNow.AddDays(3);var request=new Dictionary<string,string>{{"__RequestVerificationToken",Token(html)},{"AppointmentRequestId",Guid.NewGuid().ToString()},{"AppointmentKind",AppointmentRules.Office},{"AppointmentStart",Local(start)},{"AppointmentEnd",Local(start.AddHours(1))},{"AppointmentLocation","Unidade de teste"}};
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync(Path+"?handler=Schedule",new FormUrlEncodedContent(request))).StatusCode);
        var store=app.Services.GetRequiredService<ICommercialAppointmentStore>();var scope=new AccessScope(FixtureProgram.OrganizationA,"test-only-actor","Teste",[new(FixtureProgram.OrganizationA,"Unidade de teste")],ActiveEstablishmentId:FixtureProgram.OrganizationA);
        var appointment=Assert.Single(await store.ListAsync(scope,CommercialAppointmentFixture.CustomerId,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddDays(10),default));
        var date=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(appointment.StartsAt,TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")).DateTime).ToString("yyyy-MM-dd");
        var page="/commercial/agenda?View=day&Date="+date;html=WebUtility.HtmlDecode(await client.GetStringAsync(page));
        Assert.Contains("Cliente de teste",html);Assert.Contains("Detalhes do compromisso",html);Assert.Contains("Histórico de alterações",html);Assert.Contains("Este período",html);
        var filtered=await client.GetStringAsync(page+"&Kind="+Uri.EscapeDataString(AppointmentRules.Capture));Assert.DoesNotContain("href=\"#appointment-"+appointment.Id+"\"",filtered);
        html=await client.GetStringAsync(page);var cancel=new Dictionary<string,string>{{"__RequestVerificationToken",Token(html)},{"AppointmentId",appointment.Id.ToString()},{"AppointmentVersion",appointment.Version.ToString()},{"CustomerId",CommercialAppointmentFixture.CustomerId.ToString()},{"View","day"},{"Date",date}};
        var result=await client.PostAsync("/commercial/agenda?handler=Appointment&action=Cancelar",new FormUrlEncodedContent(cancel));Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);
        Assert.Equal("Cancelado",(await store.FindAppointmentAsync(scope,appointment.Id,default))!.Status);
        Assert.Contains("Cancelado",WebUtility.HtmlDecode(await client.GetStringAsync(page)));
    }
    [Fact]public async Task Denied_user_cannot_open_appointment_actions()
    {
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{CommercialAppointmentFixture.Configure(b);b.WebHost.UseTestServer();});await app.StartAsync();
        var client=app.GetTestClient();client.DefaultRequestHeaders.Add("X-Ui-Mode","denied");var response=await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);Assert.DoesNotContain("Agendar visita ou atendimento",await response.Content.ReadAsStringAsync());
    }
    [Fact]public async Task Three_types_and_lifecycle_work_through_razor_with_isolated_store()
    {
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{CommercialAppointmentFixture.Configure(b);b.WebHost.UseTestServer();});await app.StartAsync();
        var client=app.GetTestClient();var store=app.Services.GetRequiredService<ICommercialAppointmentStore>();
        var initial=await client.GetAsync(Path);var html=await initial.Content.ReadAsStringAsync();Assert.Equal(HttpStatusCode.OK,initial.StatusCode);Assert.Contains("Agenda e atividades",html);Assert.Contains("Agendar visita ou atendimento",html);
        var cookies=string.Join("; ",initial.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0]));client.DefaultRequestHeaders.Add("Cookie",cookies);
        var start=DateTimeOffset.UtcNow.AddDays(2);
        foreach(var kind in AppointmentRules.Kinds)
        {
            html=await client.GetStringAsync(Path);var fields=new Dictionary<string,string>{{"__RequestVerificationToken",Token(html)},{"AppointmentRequestId",Guid.NewGuid().ToString()},{"AppointmentKind",kind},{"AppointmentStart",Local(start)},{"AppointmentEnd",Local(start.AddHours(1))},{"AppointmentLocation",kind==AppointmentRules.Office?"Unidade de teste":"Local de teste"}};
            if(kind==AppointmentRules.PropertyVisit)fields["AppointmentPropertyId"]=UiFixture.PropertyId.ToString();
            var saved=await client.PostAsync(Path+"?handler=Schedule",new FormUrlEncodedContent(fields));Assert.Equal(HttpStatusCode.Redirect,saved.StatusCode);
            start=start.AddDays(1);
        }
        var scope=new AccessScope(FixtureProgram.OrganizationA,"test-only-actor","Teste",[new(FixtureProgram.OrganizationA,"Unidade de teste")],ActiveEstablishmentId:FixtureProgram.OrganizationA);
        var items=await store.ListAsync(scope,CommercialAppointmentFixture.CustomerId,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddMonths(1),default);Assert.Equal(3,items.Count);
        var office=items.Single(x=>x.Kind==AppointmentRules.Office);html=await client.GetStringAsync(Path);
        var version=new Dictionary<string,string>{{"__RequestVerificationToken",Token(html)},{"AppointmentId",office.Id.ToString()},{"AppointmentVersion",office.Version.ToString()},{"AppointmentRequestId",Guid.NewGuid().ToString()},{"AppointmentKind",office.Kind},{"AppointmentStart",Local(office.StartsAt.AddDays(1))},{"AppointmentEnd",Local(office.EndsAt.AddDays(1))},{"AppointmentLocation",office.Location}};
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync(Path+"?handler=Appointment&action=Reagendar",new FormUrlEncodedContent(version))).StatusCode);
        office=(await store.FindAppointmentAsync(scope,office.Id,default))!;Assert.Equal(2,office.Version);
        html=await client.GetStringAsync(Path);var canceled=new Dictionary<string,string>{{"__RequestVerificationToken",Token(html)},{"AppointmentId",office.Id.ToString()},{"AppointmentVersion",office.Version.ToString()}};
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync(Path+"?handler=Appointment&action=Cancelar",new FormUrlEncodedContent(canceled))).StatusCode);
        Assert.Equal("Cancelado",(await store.FindAppointmentAsync(scope,office.Id,default))!.Status);
        var capture=items.Single(x=>x.Kind==AppointmentRules.Capture);app.Services.GetRequiredService<FixtureAppointments>().MakeDue(capture.Id);
        html=await client.GetStringAsync(Path);var completed=new Dictionary<string,string>{{"__RequestVerificationToken",Token(html)},{"AppointmentId",capture.Id.ToString()},{"AppointmentVersion",capture.Version.ToString()},{"AppointmentResult","Visita de captação concluída"}};
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync(Path+"?handler=Appointment&action=Realizar",new FormUrlEncodedContent(completed))).StatusCode);
        Assert.Equal("Visita de captação concluída",(await store.FindAppointmentAsync(scope,capture.Id,default))!.Result);
        Assert.Contains("Visita de captação concluída",WebUtility.HtmlDecode(await client.GetStringAsync(Path+"?Panel=agenda")));
        var events=await store.EventsAsync(scope,CommercialAppointmentFixture.CustomerId,default);Assert.Contains(events,x=>x.AppointmentId==office.Id&&x.Action=="Reagendado");Assert.Contains(events,x=>x.AppointmentId==office.Id&&x.Action=="Cancelado");
        Assert.Contains(events,x=>x.AppointmentId==capture.Id&&x.Action=="Realizado");
        var agendaPage=await client.GetStringAsync("/commercial/agenda?View=week&OwnerSearch=Propriet");Assert.Contains("Proprietário de teste",WebUtility.HtmlDecode(agendaPage));
        var ownerFields=new Dictionary<string,string>{{"__RequestVerificationToken",Token(agendaPage)},{"RequestId",Guid.NewGuid().ToString()},{"OwnerId",CommercialAppointmentFixture.OwnerId.ToString()},{"ResponsibleSubject","test-only-actor"},{"StartLocal",Local(DateTimeOffset.UtcNow.AddDays(2))},{"EndLocal",Local(DateTimeOffset.UtcNow.AddDays(2).AddHours(1))},{"Location","Endereço de captação"}};
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/commercial/agenda?handler=Capture",new FormUrlEncodedContent(ownerFields))).StatusCode);
        var all=await store.ListAsync(scope,null,DateTimeOffset.UtcNow.AddHours(-3),DateTimeOffset.UtcNow.AddMonths(1),default);
        var ownerVisit=Assert.Single(all,x=>x.OwnerId==CommercialAppointmentFixture.OwnerId);Assert.Null(ownerVisit.CustomerId);
        agendaPage=await client.GetStringAsync("/commercial/agenda?View=week&Date="+DateOnly.FromDateTime(ownerVisit.StartsAt.Date).ToString("yyyy-MM-dd"));
        var ownerCancel=new Dictionary<string,string>{{"__RequestVerificationToken",Token(agendaPage)},{"AppointmentId",ownerVisit.Id.ToString()},{"AppointmentVersion",ownerVisit.Version.ToString()},{"OwnerId",CommercialAppointmentFixture.OwnerId.ToString()}};
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/commercial/agenda?handler=Appointment&action=Cancelar",new FormUrlEncodedContent(ownerCancel))).StatusCode);
        Assert.Equal("Cancelado",(await store.FindAppointmentAsync(scope,ownerVisit.Id,default))!.Status);
    }
}
