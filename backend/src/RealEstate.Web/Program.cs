using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using RealEstate.Application;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using System.Net;
using System.Security.Cryptography.X509Certificates;
namespace RealEstate.Web;

public partial class Program
{
    public static void Main(string[] args)=>Build(args).Run();
    public static WebApplication Build(string[] args,Action<WebApplicationBuilder>? customize=null)
    {
        var b=WebApplication.CreateBuilder(new WebApplicationOptions {Args=args,ApplicationName=typeof(Program).Assembly.GetName().Name});
        b.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics",LogLevel.Warning);
        b.WebHost.ConfigureKestrel(o=>{o.AddServerHeader=false;o.Limits.MaxRequestBodySize=32768;});
        var forwardedHeaders=b.Configuration.GetSection("ForwardedHeaders");
        var proxyValues=forwardedHeaders.GetSection("KnownProxies").Get<string[]>()??[];
        var forwardedHostValues=forwardedHeaders.GetSection("AllowedHosts").Get<string[]>()??[];
        if(proxyValues.Any(string.IsNullOrWhiteSpace)||forwardedHostValues.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Forwarded headers require non-empty trusted proxy addresses and allowed hosts.");
        if((proxyValues.Length==0)!=(forwardedHostValues.Length==0))
            throw new InvalidOperationException("Forwarded headers require both trusted proxy addresses and allowed hosts.");
        if(forwardedHostValues.Any(host=>host.Contains('*')||Uri.CheckHostName(host)!=UriHostNameType.Dns))
            throw new InvalidOperationException("ForwardedHeaders:AllowedHosts must contain exact DNS hostnames without ports or wildcards.");
        var trustedProxies=proxyValues.Select(value=>IPAddress.TryParse(value,out var address)?address:
            throw new InvalidOperationException("ForwardedHeaders:KnownProxies must contain exact IP addresses.")).ToArray();
        b.Services.Configure<ForwardedHeadersOptions>(options=>{
            options.ForwardLimit=1;
            options.ForwardedHeaders=trustedProxies.Length==0
                ? ForwardedHeaders.None
                : ForwardedHeaders.XForwardedProto|ForwardedHeaders.XForwardedHost;
            if(trustedProxies.Length>0){
                options.KnownProxies.Clear();options.KnownIPNetworks.Clear();
                foreach(var proxy in trustedProxies)options.KnownProxies.Add(proxy);
                options.AllowedHosts.Clear();
                foreach(var host in forwardedHostValues)options.AllowedHosts.Add(host);
            }
        });
        b.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o=>o.ValueCountLimit=4096);
        b.Services.AddProblemDetails();b.Services.AddHttpContextAccessor();
        b.Services.AddSingleton<CoreContextCache>();
        b.Services.AddMemoryCache();b.Services.AddScoped<OidcSessionRenewal>();
        b.Services.AddSingleton(_=>NpgsqlDataSource.Create(b.Configuration.GetConnectionString("Erp")??"Host=127.0.0.1;Port=1;Database=erp_unconfigured;Username=unconfigured;Timeout=2"));
        b.Services.AddScoped<IPropertyStore,PostgresPropertyStore>();b.Services.AddScoped<PropertyService>();
        b.Services.AddScoped<ICustomerStore,PostgresPropertyStore>();b.Services.AddScoped<CustomerService>();
        b.Services.AddScoped<ITeamStore,PostgresPropertyStore>();b.Services.AddScoped<TeamService>();
        b.Services.AddHttpClient<ITeamDirectory,CoreTeamDirectory>(client=>{client.BaseAddress=new Uri(b.Configuration["Core:BaseUrl"]??"https://localhost:1/");if(client.BaseAddress.Scheme!="https"||!string.IsNullOrEmpty(client.BaseAddress.UserInfo)||!string.IsNullOrEmpty(client.BaseAddress.Query))throw new InvalidOperationException("Core requires a trusted HTTPS origin.");client.Timeout=TimeSpan.FromSeconds(5);}).ConfigurePrimaryHttpMessageHandler(()=>TrustedHandler(b.Configuration["Core:CertificateAuthorityFile"]));
        b.Services.AddScoped<ICommercialInteractionStore,PostgresPropertyStore>();b.Services.AddScoped<IAgendaReferenceStore,PostgresPropertyStore>();b.Services.AddScoped<CommercialInteractionService>();
        b.Services.AddScoped<ICommercialAppointmentStore,PostgresPropertyStore>();b.Services.AddScoped<CommercialAppointmentService>();
        b.Services.AddScoped<INegotiationStore,PostgresPropertyStore>();b.Services.AddScoped<NegotiationService>();
        b.Services.AddScoped<INegotiationActionStore,PostgresPropertyStore>();b.Services.AddScoped<NegotiationActionService>();
        b.Services.AddScoped<INegotiationActivityTransferStore,PostgresPropertyStore>();b.Services.AddScoped<NegotiationActivityTransferService>();
        var dataProtectionKeysPath=b.Configuration["DataProtection:KeysPath"]??Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".aspnet","DataProtection-Keys");
        Directory.CreateDirectory(dataProtectionKeysPath);
        var dataProtection=b.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
        var dataProtectionCertificatePath=b.Configuration["DataProtection:CertificatePath"];
        var dataProtectionCertificatePassword=b.Configuration["DataProtection:CertificatePassword"];
        if(b.Environment.IsProduction()&&(string.IsNullOrWhiteSpace(dataProtectionCertificatePath)||string.IsNullOrWhiteSpace(dataProtectionCertificatePassword)))
            throw new InvalidOperationException("Production requires an external Data Protection certificate and password.");
        if(!string.IsNullOrWhiteSpace(dataProtectionCertificatePath)||!string.IsNullOrWhiteSpace(dataProtectionCertificatePassword)){
            if(string.IsNullOrWhiteSpace(dataProtectionCertificatePath)||string.IsNullOrWhiteSpace(dataProtectionCertificatePassword))
                throw new InvalidOperationException("Data Protection certificate path and password must be configured together.");
            var dataProtectionCertificate=X509CertificateLoader.LoadPkcs12FromFile(
                dataProtectionCertificatePath,dataProtectionCertificatePassword,X509KeyStorageFlags.EphemeralKeySet);
            using var dataProtectionPublicKey=dataProtectionCertificate.GetRSAPublicKey();
            if(!dataProtectionCertificate.HasPrivateKey||dataProtectionPublicKey is null||dataProtectionPublicKey.KeySize<2048||
                dataProtectionCertificate.NotBefore.ToUniversalTime()>DateTime.UtcNow||dataProtectionCertificate.NotAfter.ToUniversalTime()<=DateTime.UtcNow)
                throw new InvalidOperationException("Data Protection certificate must be current, contain an RSA private key, and use at least 2048 bits.");
            dataProtection.ProtectKeysWithCertificate(dataProtectionCertificate);
        }
        b.Services.AddSingleton<ITransferPreviewProtector,TransferPreviewProtector>();
        b.Services.AddHttpClient<ICustomerDirectory,CoreCustomerDirectory>(client=>{
            var uri=new Uri(b.Configuration["Core:BaseUrl"]??"https://localhost:1/");
            if(uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query))throw new InvalidOperationException("Core requires a trusted HTTPS origin.");
            client.BaseAddress=uri;client.Timeout=TimeSpan.FromSeconds(5);
        }).ConfigurePrimaryHttpMessageHandler(()=>TrustedHandler(b.Configuration["Core:CertificateAuthorityFile"]));
        b.Services.AddHttpClient<IOperationalHierarchyAccess,CoreOperationalHierarchyAccess>(client=>{
            var uri=new Uri(b.Configuration["Core:BaseUrl"]??"https://localhost:1/");
            if(uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query))throw new InvalidOperationException("Core requires a trusted HTTPS origin.");
            client.BaseAddress=uri;client.Timeout=TimeSpan.FromSeconds(5);
        }).ConfigurePrimaryHttpMessageHandler(()=>TrustedHandler(b.Configuration["Core:CertificateAuthorityFile"]));
        b.Services.AddScoped<ICondominiumStore,PostgresPropertyStore>();b.Services.AddScoped<CondominiumService>();
        b.Services.AddScoped<IOwnerProfileStore,PostgresPropertyStore>();b.Services.AddScoped<OwnerProfileService>();
        b.Services.AddScoped<IPropertyStoreV2,PostgresPropertyStore>();b.Services.AddScoped<PropertyServiceV2>();
        b.Services.AddScoped<IShowcaseStore,PostgresPropertyStore>();b.Services.AddScoped<ShowcaseService>();
        b.Services.AddScoped<IAnnouncementStore,PostgresPropertyStore>();b.Services.AddScoped<AnnouncementService>();
        b.Services.AddScoped<ICommercialPolicyStore,PostgresPropertyStore>();b.Services.AddScoped<CommercialPolicyService>();
        b.Services.AddScoped<IPropertyListingStore,PostgresPropertyStore>();b.Services.AddScoped<PropertyListingService>();
        b.Services.AddScoped<IPropertyMediaStore,PostgresPropertyStore>();b.Services.AddSingleton<IMediaContentValidator,MediaContentValidator>();b.Services.AddScoped<PropertyMediaService>();
        b.Services.AddHttpClient<ICoreAccess,CoreAccess>(client=>{
            var uri=new Uri(b.Configuration["Core:BaseUrl"]??"https://localhost:1/");
            if(uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query))throw new InvalidOperationException("Core requires a trusted HTTPS origin.");
            client.BaseAddress=uri;client.Timeout=TimeSpan.FromSeconds(5);
        }).ConfigurePrimaryHttpMessageHandler(()=>TrustedHandler(b.Configuration["Core:CertificateAuthorityFile"]));
        b.Services.AddHttpClient<ICaptureDirectory,CoreCaptureDirectory>(client=>{
            var uri=new Uri(b.Configuration["Core:BaseUrl"]??"https://localhost:1/");
            if(uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query))throw new InvalidOperationException("Core requires a trusted HTTPS origin.");
            client.BaseAddress=uri;client.Timeout=TimeSpan.FromSeconds(5);
        }).ConfigurePrimaryHttpMessageHandler(()=>TrustedHandler(b.Configuration["Core:CertificateAuthorityFile"]));
        var auth=b.Services.AddAuthentication("Route").AddPolicyScheme("Route","Route",o=>o.ForwardDefaultSelector=c=>c.Request.Path.StartsWithSegments("/api")?"Bearer":"ErpCookie")
            .AddCookie("ErpCookie",o=>{
                o.Cookie.Name="__Host-araia-erp";o.Cookie.SecurePolicy=CookieSecurePolicy.Always;o.Cookie.HttpOnly=true;o.Cookie.SameSite=SameSiteMode.Lax;
                o.ExpireTimeSpan=TimeSpan.FromMinutes(30);o.SlidingExpiration=false;o.LoginPath="/account/login";
                o.EventsType=typeof(OidcSessionRenewal);
            }).AddJwtBearer("Bearer",o=>{
                o.MapInboundClaims=false;o.RequireHttpsMetadata=true;o.IncludeErrorDetails=false;
                o.Authority=b.Configuration["Oidc:Authority"];o.Audience=b.Configuration["Oidc:Audience"];
                o.TokenValidationParameters=new TokenValidationParameters {ValidateIssuer=true,ValidateAudience=true,ValidateLifetime=true,ValidateIssuerSigningKey=true,RequireSignedTokens=true,RequireExpirationTime=true,ValidTypes=["at+jwt","application/at+jwt"],ValidAlgorithms=[SecurityAlgorithms.RsaSha256,SecurityAlgorithms.RsaSsaPssSha256,SecurityAlgorithms.EcdsaSha256],ClockSkew=TimeSpan.FromSeconds(30)};
                o.BackchannelHttpHandler=TrustedHandler(b.Configuration["Oidc:CertificateAuthorityFile"]);
            });
        if(!string.IsNullOrWhiteSpace(b.Configuration["Oidc:ClientId"]))auth.AddOpenIdConnect("Oidc",o=>{
            o.SignInScheme="ErpCookie";o.Authority=b.Configuration["Oidc:Authority"];o.ClientId=b.Configuration["Oidc:ClientId"];o.ClientSecret=b.Configuration["Oidc:ClientSecret"];
            o.ResponseType="code";o.UsePkce=true;o.MapInboundClaims=false;o.SaveTokens=true;o.RequireHttpsMetadata=true;
            o.Scope.Clear();o.Scope.Add("openid");o.Scope.Add("profile");
            o.BackchannelHttpHandler=TrustedHandler(b.Configuration["Oidc:CertificateAuthorityFile"]);
            o.Events.OnTicketReceived=c=>{c.Properties!.ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(30);c.Properties.IsPersistent=false;return Task.CompletedTask;};
        });
        b.Services.AddAuthorization();b.Services.AddRazorPages(o=>{}).AddMvcOptions(o=>{o.ModelBinderProviders.Insert(0,new NativeDecimalBinderProvider());o.Filters.Add(new DraftRejectionDiagnostics());});
        b.Services.AddAntiforgery(o=>{o.Cookie.Name="__Host-araia-erp-csrf";o.Cookie.SecurePolicy=CookieSecurePolicy.Always;});
        customize?.Invoke(b);
        var app=b.Build();
        app.UseForwardedHeaders();
        app.UseExceptionHandler(handler=>handler.Run(async http=>{
            if(http.Request.Path.StartsWithSegments("/api")){http.Response.StatusCode=503;await http.Response.WriteAsJsonAsync(new{title="Serviço temporariamente indisponível.",status=503});} else http.Response.Redirect("/Error");
        }));
        app.Use(async (http,next)=>{
            http.Response.Headers.CacheControl="no-store";http.Response.Headers.XContentTypeOptions="nosniff";
            http.Response.Headers.ContentSecurityPolicy="default-src 'self'; style-src 'self'; img-src 'self' data:; script-src 'self'; connect-src 'self' https://viacep.com.br; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            await next();
        });
        app.Use(async(http,next)=>{
            if(http.Request.Method=="POST"&&!http.Request.Query.ContainsKey("handler")&&http.Request.Path.Value is {} capturePath&&capturePath.StartsWith("/properties/",StringComparison.Ordinal)&&
                (capturePath=="/properties/new"||Guid.TryParse(capturePath[12..],out _)))
            {
                var limit=http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();if(limit is {IsReadOnly:false})limit.MaxRequestBodySize=262144;
            }
            if(http.Request.Method=="POST"&&http.Request.Query["handler"]=="Upload"&&http.Request.Path.Value is {} path&&path.StartsWith("/properties/",StringComparison.Ordinal)&&Guid.TryParse(path[12..],out _)){
                if(string.IsNullOrWhiteSpace(http.Request.Headers["RequestVerificationToken"])){http.Response.StatusCode=400;return;}
                var feature=http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();if(feature is {IsReadOnly:false})feature.MaxRequestBodySize=102L*MediaRules.MiB;
            }
            await next();
        });
        app.Use(DraftRequestDiagnostics.Observe);
        app.UseStaticFiles();app.UseAuthentication();
        app.Use(async(http,next)=>{
            // A stale authenticated form must not disguise a missing session as a CSRF/save error.
            // Authenticated requests still pass the unchanged antiforgery and Core authorization checks.
            if(DraftRequestDiagnostics.IsPropertyPost(http)&&http.User.Identity?.IsAuthenticated!=true){
                http.Items["DraftSaveStage"]="Authentication";http.Items["DraftSaveCode"]="Authentication.Required";
                http.Response.StatusCode=401;
                await http.Response.WriteAsJsonAsync(new{title="Retome seu acesso antes de salvar. Seus dados não foram enviados.",status=401});return;
            }
            await next();
        });
        app.UseAuthorization();app.MapRazorPages();
        app.MapGet("/health/live",()=>Results.Ok(new{status="Healthy"}));
        app.MapGet("/account/login",async(HttpContext http)=>{
            if(!app.Configuration.GetSection("Oidc").Exists()||string.IsNullOrWhiteSpace(app.Configuration["Oidc:ClientId"]))
            {http.Response.StatusCode=503;await http.Response.WriteAsync("Integração de login AraIa ainda não configurada neste ambiente.");return;}
            await http.ChallengeAsync("Oidc",new AuthenticationProperties{RedirectUri="/"});
        });
        app.MapPost("/account/logout",async(HttpContext http,Microsoft.AspNetCore.Antiforgery.IAntiforgery csrf)=>{
            await csrf.ValidateRequestAsync(http);await http.SignOutAsync("ErpCookie");return Results.LocalRedirect("/");
        });
        app.MapGet("/api/v1/properties",async(PropertyService service,CancellationToken ct)=>Respond(await service.ListAsync(ct))).RequireAuthorization();
        app.MapGet("/api/v1/properties/{id:guid}",async(Guid id,PropertyService service,CancellationToken ct)=>Respond(await service.FindAsync(id,ct))).RequireAuthorization();
        app.MapPost("/api/v1/properties",async(PropertyInput input,PropertyService service,CancellationToken ct)=>Respond(await service.SaveAsync(input,token:ct))).RequireAuthorization();
        app.MapPut("/api/v1/properties/{id:guid}",async(Guid id,UpdateProperty command,PropertyService service,CancellationToken ct)=>Respond(await service.SaveAsync(command.Data,id,command.Version,ct))).RequireAuthorization();
        app.MapPost("/api/v1/owners",async(OwnerInput input,PropertyService service,CancellationToken ct)=>Respond(await service.CreateOwnerAsync(input.Name,ct))).RequireAuthorization();
        app.MapGet("/api/v2/properties",async(PropertyServiceV2 service,CancellationToken ct)=>Respond(await service.ListAsync(ct))).RequireAuthorization();
        app.MapGet("/api/v2/properties/{id:guid}",async(Guid id,PropertyServiceV2 service,CancellationToken ct)=>Respond(await service.FindAsync(id,ct))).RequireAuthorization();
        app.MapPost("/api/v2/properties",async(PropertyInputV2 input,PropertyServiceV2 service,CancellationToken ct)=>Respond(await service.SaveAsync(input,token:ct))).RequireAuthorization();
        app.MapPut("/api/v2/properties/{id:guid}",async(Guid id,UpdatePropertyV2 command,PropertyServiceV2 service,CancellationToken ct)=>Respond(await service.SaveAsync(command.Data,id,command.Version,ct))).RequireAuthorization();
        app.MapPatch("/api/v2/properties/{id:guid}",async(Guid id,PatchPropertyV2 command,PropertyServiceV2 service,CancellationToken ct)=>Respond(await service.PatchAsync(id,command.Version,command.Data,ct))).RequireAuthorization();
        app.MapGet("/api/v1/properties/{id:guid}/announcements",async(Guid id,AnnouncementService service,CancellationToken ct)=>Respond(await service.ListAsync(id,ct))).RequireAuthorization();
        app.MapPost("/api/v1/properties/{id:guid}/announcements",async(Guid id,CreateAnnouncement command,AnnouncementService service,CancellationToken ct)=>Respond(await service.CreateAsync(id,command,ct))).RequireAuthorization();
        app.MapPut("/api/v1/announcements/{id:guid}",async(Guid id,UpdateAnnouncement command,AnnouncementService service,CancellationToken ct)=>Respond(await service.UpdateAsync(id,command.Version,command.Data,ct:ct))).RequireAuthorization();
        app.MapPost("/api/v1/announcements/{id:guid}/close",async(Guid id,CloseAnnouncement command,AnnouncementService service,CancellationToken ct)=>Respond(await service.UpdateAsync(id,command.Version,null,true,ct))).RequireAuthorization();
        app.MapGet("/api/v1/showcase/{id:guid}",async(Guid id,ShowcaseService service,CancellationToken ct)=>Respond(await service.FindAsync(id,ct))).RequireAuthorization();
        app.MapPut("/api/v1/showcase/{id:guid}",async(Guid id,ShowcaseChange command,ShowcaseService service,CancellationToken ct)=>Respond(await service.ChangeAsync(id,command,"prepare",ct))).RequireAuthorization();
        app.MapPost("/api/v1/showcase/{id:guid}/authorize",async(Guid id,ShowcaseChange command,ShowcaseService service,CancellationToken ct)=>Respond(await service.ChangeAsync(id,command,"authorize",ct))).RequireAuthorization();
        app.MapPost("/api/v1/showcase/{id:guid}/withdraw",async(Guid id,ShowcaseChange command,ShowcaseService service,CancellationToken ct)=>Respond(await service.ChangeAsync(id,command,"withdraw",ct))).RequireAuthorization();
        app.MapCustomers();app.MapNegotiations();app.MapTeam();
        return app;
    }
    public sealed record UpdateProperty(PropertyInput Data,long Version);
    public sealed record UpdatePropertyV2(PropertyInputV2 Data,long Version);
    public sealed record PatchPropertyV2(System.Text.Json.JsonElement Data,long Version);
    public sealed record OwnerInput(string Name);
    public sealed record CloseAnnouncement(long Version);
    public static int Status(string? code)=>code switch {"Authentication.Required"=>401,"Access.Forbidden" or "Policy.SelfApproval"=>403,"Dependency.Unavailable"=>503,"Dependency.InvalidResponse"=>502,"Context.Invalid" or "Context.OrganizationRequired" or "Context.ActiveEstablishmentRequired"=>409,"Resource.NotFound"=>404,"Property.VersionConflict" or "Profile.VersionConflict" or "Showcase.VersionConflict" or "Negotiation.VersionConflict" or "NegotiationAction.VersionConflict" or "Appointment.VersionConflict" or "NegotiationTransfer.VersionConflict"=>412,"Property.VersionRequired" or "Profile.VersionRequired" or "NegotiationAction.VersionRequired"=>428,"Property.StatusConflict" or "Property.ReviewIncomplete" or "Announcement.ActiveExists" or "Announcement.PurposeImmutable" or "Announcement.Closed" or "Announcement.EditOriginal" or "Announcement.Conflict" or "Announcement.InitialPurposeRequired" or "Property.DuplicateCode" or "Property.AddressVersionRequired" or "Policy.Immutable" or "Policy.ScopeImmutable" or "Policy.ExceptionRequired" or "Policy.BindingRequired" or "Showcase.NotEligible" or "Showcase.StatusConflict" or "Showcase.SelectionConflict" or "NegotiationAction.RequestConflict" or "NegotiationAction.AppointmentAlreadyLinked" or "NegotiationAction.AlreadyCompleted" or "Appointment.InvalidTransition" or "NegotiationTransfer.RequestConflict" or "NegotiationTransfer.PreviewExpired" or "NegotiationTransfer.FollowupAlreadyResolved" or "NegotiationTransfer.FollowupAlreadyPending"=>409,_=>400};
    private static IResult Respond<T>(Outcome<T> result)=>result.IsSuccess?Results.Ok(result.Value):Results.Problem(statusCode:Status(result.Code),title:"Operação não concluída.",extensions:new Dictionary<string,object?>{{"code",result.Code}});
    private static HttpClientHandler TrustedHandler(string? caFile)
    {
        var handler=new HttpClientHandler {AllowAutoRedirect=false};
        if(!string.IsNullOrWhiteSpace(caFile))
        {
            var ca=X509CertificateLoader.LoadCertificateFromFile(caFile);
            handler.ServerCertificateCustomValidationCallback=(_,cert,_,errors)=>{
                if(cert is null || (errors & System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch)!=0)return false;
                using var chain=new X509Chain();chain.ChainPolicy.TrustMode=X509ChainTrustMode.CustomRootTrust;chain.ChainPolicy.CustomTrustStore.Add(ca);chain.ChainPolicy.RevocationMode=X509RevocationMode.NoCheck;return chain.Build(cert);
            };
        }
        return handler;
    }
}



