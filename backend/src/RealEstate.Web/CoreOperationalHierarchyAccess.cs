using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using RealEstate.Domain;

namespace RealEstate.Web;

/// <summary>Fresh, caller-token-backed access to CORE-HIER-01 decisions; no hierarchy cache is used.</summary>
public sealed class CoreOperationalHierarchyAccess(HttpClient client,IHttpContextAccessor accessor):IOperationalHierarchyAccess
{
    public Task<Outcome<OperationalAuthority>> AuthorityAsync(AccessScope scope,Guid subordinateMembershipId,CancellationToken token)
        =>Get<OperationalAuthority>(scope,subordinateMembershipId,"authority",OperationalAuthorityValid,token);

    public Task<Outcome<OperationalRecipient>> RecipientAsync(AccessScope scope,Guid membershipId,CancellationToken token)
        =>Get<OperationalRecipient>(scope,membershipId,"recipient",RecipientValid,token);

    private async Task<Outcome<T>> Get<T>(AccessScope scope,Guid membershipId,string action,Func<T?,bool> valid,CancellationToken token)
    {
        var http=accessor.HttpContext;
        if(http?.User.Identity?.IsAuthenticated!=true)return Outcome<T>.Fail("Authentication.Required");
        if(scope.OrganizationId==Guid.Empty||membershipId==Guid.Empty)return Outcome<T>.Fail("Hierarchy.InvalidInput");
        if(scope.ActiveEstablishmentId is not {} unit||scope.Establishments?.Any(e=>e.Id==unit)!=true)return Outcome<T>.Fail("Context.ActiveEstablishmentRequired");
        var bearer=http.Request.Path.StartsWithSegments("/api")?http.Request.Headers.Authorization.ToString():"";
        var credential=bearer.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?bearer[7..]:await http.GetTokenAsync("access_token");
        if(string.IsNullOrWhiteSpace(credential))return Outcome<T>.Fail("Authentication.Required");
        var suffix=action=="authority"?$"{membershipId:D}/authority":$"recipients/{membershipId:D}";
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,$"api/v1/organizations/{scope.OrganizationId:D}/operational-hierarchy/{suffix}?establishmentId={unit:D}");
            request.Headers.Authorization=new("Bearer",credential);
            using var response=await client.SendAsync(request,token);
            if(!response.IsSuccessStatusCode)return Outcome<T>.Fail(response.StatusCode switch
            {HttpStatusCode.Unauthorized=>"Authentication.Required",HttpStatusCode.Forbidden=>"Access.Forbidden",HttpStatusCode.NotFound=>"Resource.NotFound",_=>"Dependency.Unavailable"});
            var value=await response.Content.ReadFromJsonAsync<T>(token);
            return value is not null&&valid(value)?new(value):Outcome<T>.Fail("Dependency.InvalidResponse");
        }
        catch(System.Text.Json.JsonException){return Outcome<T>.Fail("Dependency.InvalidResponse");}
        catch(Exception e) when(e is HttpRequestException||e is TaskCanceledException&&!token.IsCancellationRequested){return Outcome<T>.Fail("Dependency.Unavailable");}
    }

    private static bool OperationalAuthorityValid(OperationalAuthority? value)=>value is not null&&value.RelationshipVersion>=0&&value.DecidedAtUtc!=default;
    private static bool RecipientValid(OperationalRecipient? value)=>value is not null&&value.ValidatedAtUtc!=default;
}
