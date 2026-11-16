using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RealEstate.Domain;
using RealEstate.Web;
namespace RealEstate.Tests;

// Compiled only in the test assembly. No production config switch enables grants.
public static class FixtureProgram
{
    public static readonly Guid OrganizationA=Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OrganizationB=Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static void Configure(WebApplicationBuilder b)
    {
        b.Configuration["EvidenceFixture"]="true";
        b.Services.RemoveAll<ICoreAccess>();b.Services.AddScoped<ICoreAccess,FixtureAccess>();
        b.Services.AddAuthentication(o=>{o.DefaultScheme="Fixture";o.DefaultAuthenticateScheme="Fixture";o.DefaultChallengeScheme="Fixture";}).AddScheme<AuthenticationSchemeOptions,FixtureAuthentication>("Fixture",_=>{});
        b.Services.AddAntiforgery(o=>{o.Cookie.Name="erp-fixture-csrf";o.Cookie.SecurePolicy=CookieSecurePolicy.None;});
    }
    public static void Main(string[] args)
    {
        if(args.Contains("--serve-negotiation-visual-fixture")){NegotiationVisualFixture.Serve(args[Array.IndexOf(args,"--serve-negotiation-visual-fixture")+1]);return;}
        if(args.Contains("--serve-negotiation-b2-visual-fixture")){NegotiationB2VisualFixture.Serve(args[Array.IndexOf(args,"--serve-negotiation-b2-visual-fixture")+1]);return;}
        if(args.Contains("--serve-appointment-fixture")){CommercialAppointmentFixture.Serve(args[Array.IndexOf(args,"--serve-appointment-fixture")+1]);return;}
        if(args.Contains("--serve-commercial-indicator-fixture")){
            var url=args[Array.IndexOf(args,"--serve-commercial-indicator-fixture")+1];
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="http"||uri.Host!="127.0.0.1")throw new InvalidOperationException("Loopback in-memory fixture required");
            var content=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
            var cardApp=Program.Build(["--contentRoot",content,"--urls",url],b=>{CommercialAppointmentFixture.Configure(b);b.Services.AddSingleton<IPropertyListingStore>(new CommercialIndicatorListingStore());});cardApp.Run();return;
        }
        if(args.Contains("--serve-customers-fixture")){
            var url=args[Array.IndexOf(args,"--serve-customers-fixture")+1];
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="http"||uri.Host!="127.0.0.1"||string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")))throw new InvalidOperationException("Disposable loopback fixture required");
            var content=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
            var commercialApp=Program.Build(["--contentRoot",content,"--urls",url],b=>{Configure(b);b.Services.RemoveAll<ICustomerDirectory>();b.Services.AddScoped<ICustomerDirectory,CustomerTestDirectory>();b.Services.RemoveAll<ICaptureDirectory>();b.Services.AddScoped<ICaptureDirectory,CustomerTestDirectory>();});commercialApp.Run();return;
        }
        if(args.Contains("--serve-policy-fixture")){CommercialPolicyFixture.Serve(args[Array.IndexOf(args,"--serve-policy-fixture")+1]);return;}
        if(args.Contains("--serve-directory-fixture")){CaptureDirectoryFixture.Serve(args[Array.IndexOf(args,"--serve-directory-fixture")+1]);return;}
        if(args.FirstOrDefault()=="--serve-real-araia-proof"){AraiaIntegrationHost.Run(args[1..]);return;}
        if(args.Contains("--serve-ui-fixture")){UiFixture.Serve(args[Array.IndexOf(args,"--serve-ui-fixture")+1]);return;}
        if(!args.Contains("--serve-fixture"))return;
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__Erp")))throw new InvalidOperationException("Disposable PostgreSQL required.");
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        var fixtureUrl=args.ElementAtOrDefault(Array.IndexOf(args,"--serve-fixture")+1)??"http://127.0.0.1:65182";
        if(!Uri.TryCreate(fixtureUrl,UriKind.Absolute,out var fixtureUri)||fixtureUri.Scheme!="http"||fixtureUri.Host!="127.0.0.1")throw new InvalidOperationException("Loopback fixture required");
        var app=Program.Build(["--contentRoot",root,"--urls",fixtureUrl],Configure);
        app.Run();
    }
}
public sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> o,ILoggerFactory l,UrlEncoder e):AuthenticationHandler<AuthenticationSchemeOptions>(o,l,e)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if(Request.Headers.ContainsKey("X-Fixture-Anonymous"))return Task.FromResult(AuthenticateResult.NoResult());
        var user=new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","test-only-actor")],Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(user,Scheme.Name)));
    }
}
public sealed class FixtureAccess(IHttpContextAccessor http):ICoreAccess
{
    public Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token)
    {
        var request=http.HttpContext!.Request;
        if(http.HttpContext.User.Identity?.IsAuthenticated!=true)return Task.FromResult(Outcome<AccessScope>.Fail("Authentication.Required"));
        if(request.Headers.ContainsKey("X-Fixture-DenyShared")&&permission is "OWNER_UPDATE" or "CONDOMINIUM_UPDATE")return Task.FromResult(Outcome<AccessScope>.Fail("Access.Forbidden"));
        if(request.Headers.ContainsKey("X-Fixture-Deny"))return Task.FromResult(Outcome<AccessScope>.Fail("Access.Forbidden"));
        if(request.Headers.ContainsKey("X-Fixture-ReadOnly")&&permission!="PROPERTY_READ")return Task.FromResult(Outcome<AccessScope>.Fail("Access.Forbidden"));
        var org=request.Headers["X-Fixture-Organization"]=="B"?FixtureProgram.OrganizationB:FixtureProgram.OrganizationA;
        var unit=Guid.TryParse(request.Headers["X-Fixture-Unit"],out var selected)?selected:org;
        var scope=new AccessScope(org,"test-only-actor","TEST ONLY · Empresa sintética",[new(unit,"Test unit")],ActiveEstablishmentId:unit);http.HttpContext.Items[typeof(AccessScope)]=scope;
        return Task.FromResult(new Outcome<AccessScope>(scope));
    }
}
