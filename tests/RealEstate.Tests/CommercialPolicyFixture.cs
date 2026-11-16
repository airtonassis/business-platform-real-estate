using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RealEstate.Domain;
namespace RealEstate.Tests;
public sealed class CommercialPolicyFixture(IHttpContextAccessor accessor):ICoreAccess
{
    public async Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token)
    {
        var request=accessor.HttpContext!.Request;
        if(request.Headers["X-Fixture-PolicyDeny"]==permission)return Outcome<AccessScope>.Fail("Access.Forbidden");
        var result=await new FixtureAccess(accessor).RequireAsync(resource,permission,token);
        return result.IsSuccess?new(result.Value! with{Subject=request.Headers["X-Fixture-PolicyActor"]=="reviewer"?"synthetic-reviewer":"synthetic-author",LegacyManager=true}):result;
    }
    public static void Serve(string url)
    {
        if(url!="http://127.0.0.1:65194")throw new InvalidOperationException("Dedicated policy fixture required");
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../backend/src/RealEstate.Web"));
        RealEstate.Web.Program.Build(["--contentRoot",root,"--urls",url],b=>{FixtureProgram.Configure(b);b.Services.RemoveAll<ICoreAccess>();b.Services.AddScoped<ICoreAccess,CommercialPolicyFixture>();}).Run();
    }
}
