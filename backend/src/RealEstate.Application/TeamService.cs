using RealEstate.Domain;
namespace RealEstate.Application;

public sealed class TeamService(ICoreAccess access,ITeamDirectory directory,ITeamStore store)
{
    private async Task<Outcome<AccessScope>> Scope(CancellationToken token)
    {
        // Reuse the product entry permission and the Core's existing administrative authority.
        var scope=await access.RequireAsync("customers","CUSTOMER_READ",token);
        if(!scope.IsSuccess)return scope;
        var s=scope.Value!;
        if(!s.LegacyManager)return Outcome<AccessScope>.Fail("Access.Forbidden");
        return s.OrganizationId!=Guid.Empty&&s.ActiveEstablishmentId is {} unit&&s.Establishments?.Any(e=>e.Id==unit)==true?scope:Outcome<AccessScope>.Fail("Context.Invalid");
    }
    public async Task<Outcome<TeamDirectory>> SearchAsync(string? query=null,CancellationToken token=default)
    {
        var scope=await Scope(token);if(!scope.IsSuccess)return Outcome<TeamDirectory>.Fail(scope.Code!);
        if(query?.Length>200)return Outcome<TeamDirectory>.Fail("Team.InvalidInput");
        var result=await directory.ReadAsync(scope.Value!,token:token);if(!result.IsSuccess)return result;
        var q=query?.Trim()??"";
        return new(result.Value! with{Items=result.Value!.Items.Where(p=>p.DisplayName.Contains(q,StringComparison.OrdinalIgnoreCase)||p.CorporateLogin.Contains(q,StringComparison.OrdinalIgnoreCase)).ToArray()});
    }
    public async Task<Outcome<TeamFile>> FindAsync(Guid id,CancellationToken token=default)
    {
        var scope=await Scope(token);if(!scope.IsSuccess)return Outcome<TeamFile>.Fail(scope.Code!);
        var people=await directory.ReadAsync(scope.Value!,id,token);if(!people.IsSuccess)return Outcome<TeamFile>.Fail(people.Code!);
        var person=people.Value!.Items.SingleOrDefault(p=>p.Id==id);if(person is null)return Outcome<TeamFile>.Fail("Resource.NotFound");
        return new(new TeamFile(person,await store.FindAsync(scope.Value!,id,token)));
    }
    public async Task<Outcome<TeamRecord>> SaveAsync(Guid id,TeamProfessional input,long version,CancellationToken token=default)
    {
        var scope=await Scope(token);if(!scope.IsSuccess)return Outcome<TeamRecord>.Fail(scope.Code!);
        var people=await directory.ReadAsync(scope.Value!,id,token);if(!people.IsSuccess)return Outcome<TeamRecord>.Fail(people.Code!);
        var person=people.Value!.Items.SingleOrDefault(p=>p.Id==id);
        if(person is null)return Outcome<TeamRecord>.Fail("Resource.NotFound");
        if(!person.Activated||string.IsNullOrWhiteSpace(person.Subject))return Outcome<TeamRecord>.Fail("Team.IdentityPending");
        var valid=TeamRules.Validate(input);if(!valid.IsSuccess)return Outcome<TeamRecord>.Fail(valid.Code!);
        if(version<0)return Outcome<TeamRecord>.Fail("Team.InvalidInput");
        return await store.SaveAsync(scope.Value!,id,valid.Value!,version,token);
    }
}

