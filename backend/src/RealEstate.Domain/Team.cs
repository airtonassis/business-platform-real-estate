namespace RealEstate.Domain;

public sealed record TeamGrant(string ProductKey,string ResourceKey,string PermissionKey);
public sealed record TeamEstablishment(Guid Id,string Name,bool IsHeadquarters,bool Selected,bool Available);
public sealed record TeamPerson(Guid Id,string? Subject,string DisplayName,string CorporateLogin,string ActivationState,bool Activated,Guid ProfileId,IReadOnlyList<TeamGrant> Permissions,IReadOnlyList<TeamEstablishment> Establishments);
public sealed record TeamDirectory(Guid OrganizationId,Guid EstablishmentId,string ProductKey,IReadOnlyList<TeamPerson> Items);
public sealed record TeamProfessional(string JobTitle,string? Creci,string? CreciUf,string[] Areas,string? Notes,string Status="Ativo");
public sealed record TeamRecord(Guid DirectoryId,TeamProfessional Data,long Version);
public sealed record TeamFile(TeamPerson Person,TeamRecord? Professional);
public interface ITeamDirectory
{
    Task<Outcome<TeamDirectory>> ReadAsync(AccessScope scope,Guid? id=null,CancellationToken token=default);
}
public interface ITeamStore
{
    Task<TeamRecord?> FindAsync(AccessScope scope,Guid id,CancellationToken token);
    Task<Outcome<TeamRecord>> SaveAsync(AccessScope scope,Guid id,TeamProfessional input,long version,CancellationToken token);
}
public static class TeamRules
{
    public static readonly string[] Areas=["Captação","Locação","Venda","Temporada","Vistoria"];
    public static readonly string[] States="AC AL AP AM BA CE DF ES GO MA MT MS MG PA PB PR PE PI RJ RN RS RO RR SC SP SE TO".Split(' ');
    public static Outcome<TeamProfessional> Validate(TeamProfessional? input)
    {
        if(input is null||CustomerRules.Clean(input.JobTitle) is not {} title||title.Length>100||input.Status is not("Ativo" or "Inativo")||input.Notes?.Length>4000||input.Areas is null||input.Areas.Length>Areas.Length||input.Areas.Any(a=>!Areas.Contains(a))||input.Areas.Distinct().Count()!=input.Areas.Length)return Outcome<TeamProfessional>.Fail("Team.InvalidInput");
        var creci=CustomerRules.Clean(input.Creci);var uf=CustomerRules.Clean(input.CreciUf)?.ToUpperInvariant();
        if((creci is null)!=(uf is null)||creci is not null&&(creci.Length>30||!creci.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '.' or '/'))||uf is not null&&!States.Contains(uf)||title.StartsWith("Corretor",StringComparison.OrdinalIgnoreCase)&&creci is null)return Outcome<TeamProfessional>.Fail("Team.CreciInvalid");
        return new(input with{JobTitle=title,Creci=creci,CreciUf=uf,Notes=CustomerRules.Clean(input.Notes)});
    }
}
