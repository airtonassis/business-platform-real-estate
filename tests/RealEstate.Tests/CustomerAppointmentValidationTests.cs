using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;

public sealed class CustomerAppointmentValidationTests
{
    private static string Token(string html)=>Regex.Match(html,"name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    [Fact]public async Task Saving_customer_does_not_require_appointment_kind()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{CommercialAppointmentFixture.Configure(b);b.WebHost.UseTestServer();});await app.StartAsync();
        var client=app.GetTestClient();var form=await client.GetAsync("/commercial/customers/new");var html=await form.Content.ReadAsStringAsync();
        client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",form.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0])));
        var fields=new Dictionary<string,string>{{"__RequestVerificationToken",Token(html)},{"Input.Name","Cliente do teste isolado"},{"Input.PersonType","Física"},{"Input.Phone","11999998888"},{"Input.Origin","Site"},{"Input.Interest","Compra"},{"Input.ResponsibleSubject","test-only-actor"}};
        var saved=await client.PostAsync("/commercial/customers/new",new FormUrlEncodedContent(fields));
        Assert.DoesNotContain("The AppointmentKind field is required.",await saved.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect,saved.StatusCode);
    }
    [Fact]public async Task Other_commercial_forms_ignore_appointment_kind_but_schedule_requires_it()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        await using var app=Program.Build(["--contentRoot",root],b=>{CommercialAppointmentFixture.Configure(b);b.WebHost.UseTestServer();});await app.StartAsync();
        var client=app.GetTestClient();var path="/commercial/customers/"+CommercialAppointmentFixture.CustomerId;
        var form=await client.GetAsync(path);var html=await form.Content.ReadAsStringAsync();
        client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",form.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0])));
        var csrf=Token(html);
        var contact=await client.PostAsync(path+"?handler=Contact",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",csrf},{"ContactKind","Telefone"},{"Description","Atendimento concluído"}}));
        Assert.Equal(HttpStatusCode.NotFound,contact.StatusCode);
        Assert.DoesNotContain("The AppointmentKind field is required.",await contact.Content.ReadAsStringAsync());
        var present=await client.PostAsync(path+"?handler=Present",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",csrf},{"SelectedProperties",UiFixture.PropertyId.ToString()},{"PresentationChannel","Telefone"}}));
        Assert.Equal(HttpStatusCode.Forbidden,present.StatusCode);
        Assert.DoesNotContain("The AppointmentKind field is required.",await present.Content.ReadAsStringAsync());
        var start=TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow.AddDays(2),TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        var schedule=await client.PostAsync(path+"?handler=Schedule",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",csrf},{"AppointmentRequestId",Guid.NewGuid().ToString()},{"AppointmentStart",start.ToString("yyyy-MM-ddTHH:mm")},{"AppointmentEnd",start.AddHours(1).ToString("yyyy-MM-ddTHH:mm")},{"AppointmentLocation","Unidade de teste"}}));
        Assert.Equal(HttpStatusCode.BadRequest,schedule.StatusCode);
        Assert.Contains("Revise o tipo, horário, imóvel e local do compromisso.",System.Net.WebUtility.HtmlDecode(await schedule.Content.ReadAsStringAsync()));
    }
}
