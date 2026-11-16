using RealEstate.Application;
using RealEstate.Domain;
namespace RealEstate.Web;
public static class TeamEndpoints
{
    public sealed record SaveTeam(TeamProfessional Data,long Version=0);
    public static void MapTeam(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1/team").RequireAuthorization();
        api.MapGet("",async(string? query,TeamService service,CancellationToken token)=>Reply(await service.SearchAsync(query,token)));
        api.MapGet("/{id:guid}",async(Guid id,TeamService service,CancellationToken token)=>Reply(await service.FindAsync(id,token)));
        api.MapPut("/{id:guid}",async(Guid id,SaveTeam input,TeamService service,CancellationToken token)=>Reply(await service.SaveAsync(id,input.Data,input.Version,token)));
        api.AddEndpointFilter(async(context,next)=>{
            context.HttpContext.Response.Headers.CacheControl="no-store";
            if(!HttpMethods.IsGet(context.HttpContext.Request.Method)&&!context.HttpContext.Request.Headers.Authorization.ToString().StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)){
                try{await context.HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>().ValidateRequestAsync(context.HttpContext);}
                catch(Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException){return Results.BadRequest();}
            }
            return await next(context);
        });
    }
    private static IResult Reply<T>(Outcome<T> result)=>result.IsSuccess?Results.Ok(result.Value):Results.Problem(statusCode:Status(result.Code),title:Message(result.Code));
    public static int Status(string? code)=>code=="Team.VersionConflict"?412:code=="Team.ContractUnavailable"?503:Program.Status(code);
    public static string Message(string? code)=>code switch{
        "Team.ContractUnavailable"=>"A consulta de equipe ainda não está disponível no Core deste ambiente.",
        "Team.CreciInvalid"=>"Informe o CRECI e a UF. Para corretores, o registro é obrigatório.",
        "Team.InvalidInput"=>"Revise os dados profissionais informados.",
        "Team.IdentityPending"=>"Aguarde a ativação do colaborador no Core para vinculá-lo.",
        "Team.VersionConflict"=>"A ficha foi alterada. Reabra para conferir a versão atual.",
        _=>CustomerMessages.For(code)};
}
