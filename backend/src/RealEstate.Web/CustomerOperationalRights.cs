using RealEstate.Domain;
namespace RealEstate.Web;

public static class CustomerOperationalRights
{
    public static Task<Outcome<AccessScope>> ReadScopeAsync(ICoreAccess access,CancellationToken token)
        =>access.RequireAsync("customers","CUSTOMER_READ",token);
}
