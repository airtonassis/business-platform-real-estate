using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RealEstate.Domain;
namespace RealEstate.Tests;

// Compiled in the test assembly only. Explicitly synthetic UI/persistence proof, not Core/OIDC.
public sealed class CaptureDirectoryFixture(IHttpContextAccessor accessor):ICaptureDirectory
{
    public Task<Outcome<CaptureResponsibleDirectory>> ReadAsync(AccessScope scope,Guid unit,string? subject=null,CancellationToken token=default)
    {
        var mode=accessor.HttpContext!.Request.Headers["X-Fixture-Directory"].ToString();
        if(mode=="outage")return Task.FromResult(Outcome<CaptureResponsibleDirectory>.Fail("Dependency.Unavailable"));
        IReadOnlyList<CaptureResponsible> people=mode=="empty"||subject is not null&&subject!="test-only-candidate"?[]:[new("test-only-candidate","TEST ONLY - Candidate",Guid.Parse("88888888-8888-8888-8888-888888888888"),"Descriptive only")];
        return Task.FromResult(new Outcome<CaptureResponsibleDirectory>(new(scope.OrganizationId,unit,people)));
    }
    public static void Serve(string url)
    {
        if(url!="http://127.0.0.1:65193")throw new InvalidOperationException("Dedicated loopback fixture required");
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        RealEstate.Web.Program.Build(["--contentRoot",root,"--urls",url],b=>{
            FixtureProgram.Configure(b);b.Services.RemoveAll<ICaptureDirectory>();b.Services.AddScoped<ICaptureDirectory,CaptureDirectoryFixture>();
        }).Run();
    }
}
