using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using RealEstate.Domain;
namespace RealEstate.Web;

public sealed class CoreAccess(HttpClient client,IHttpContextAccessor accessor,ILogger<CoreAccess>? logger=null,CoreContextCache? cache=null,IConfiguration? configuration=null):ICoreAccess
{
    public const string Product="real-estate";
    public async Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token)
    {
        var http=accessor.HttpContext;
        if(http?.User.Identity?.IsAuthenticated!=true)return Outcome<AccessScope>.Fail("Authentication.Required");
        var bearer=http.Request.Path.StartsWithSegments("/api")?http.Request.Headers.Authorization.ToString():"";
        var accessToken=bearer.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?bearer[7..]:await http.GetTokenAsync("access_token");
        if(string.IsNullOrWhiteSpace(accessToken))return Outcome<AccessScope>.Fail("Authentication.Required");
        if(configuration?["Core:ContextVersion"]=="1")return await RequireSnapshot(http,accessToken,resource,permission,token);
        try
        {
            async Task<HttpResponseMessage> Get(string path)
            {
                using var request=new HttpRequestMessage(HttpMethod.Get,path);
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",accessToken);
                return await client.SendAsync(request,token);
            }
            using var contextResponse=await Get("api/v1/me/context");
            if(!contextResponse.IsSuccessStatusCode){logger?.LogWarning("Core context returned {Status}",(int)contextResponse.StatusCode);return Denied(contextResponse.StatusCode);}
            var context=await contextResponse.Content.ReadFromJsonAsync<ContextResponse>(token);
            if(context?.State=="NotLinked" && context.OrganizationId==Guid.Empty)return Outcome<AccessScope>.Fail("Context.OrganizationRequired");
            if(context?.State!="Linked" || context.OrganizationId==Guid.Empty)return Outcome<AccessScope>.Fail("Context.Invalid");
            using var grantsResponse=await Get("api/v1/access/effective");
            if(!grantsResponse.IsSuccessStatusCode){logger?.LogWarning("Core authorization returned {Status}",(int)grantsResponse.StatusCode);return Denied(grantsResponse.StatusCode);}
            var rights=await grantsResponse.Content.ReadFromJsonAsync<GrantsResponse>(token);
            if(rights?.Grants is null)return Outcome<AccessScope>.Fail("Dependency.InvalidResponse");
            if(rights.Grants.Any(g=>g.ProductKey==Product&&g.ResourceKey==resource&&g.PermissionKey==permission)!=true)return Outcome<AccessScope>.Fail("Access.Forbidden");
            using var unitsResponse=await Get("api/v1/access/establishments");
            if(!unitsResponse.IsSuccessStatusCode)return Denied(unitsResponse.StatusCode);
            var units=await unitsResponse.Content.ReadFromJsonAsync<UnitsResponse>(token);
            if(units?.Establishments is null)return Outcome<AccessScope>.Fail("Dependency.InvalidResponse");
            if(units.Establishments.Any(e=>e.Id==Guid.Empty) || units.Establishments.Select(e=>e.Id).Distinct().Count()!=units.Establishments.Length)return Outcome<AccessScope>.Fail("Context.Invalid");
            if(units.Establishments.Length==0)return Outcome<AccessScope>.Fail("Access.Forbidden");
            if(context.ActiveEstablishmentId==Guid.Empty)return Outcome<AccessScope>.Fail("Context.Invalid");
            if(context.ActiveEstablishmentId is {} active && !units.Establishments.Any(e=>e.Id==active))return Outcome<AccessScope>.Fail("Access.Forbidden");
            var subject=http.User.FindFirst("sub")?.Value;
            if(string.IsNullOrWhiteSpace(subject))return Outcome<AccessScope>.Fail("Authentication.Required");
            var scope=new AccessScope(context.OrganizationId,subject,context.OrganizationName??"Empresa",units.Establishments,context.Role is "Owner" or "Admin",context.ActiveEstablishmentId,context.MembershipId);
            // Request-local display data, not an authority cache. No extra Core call.
            http.Items[typeof(AccessScope)]=scope;
            return new(scope);
        }
        catch(System.Text.Json.JsonException){return Outcome<AccessScope>.Fail("Dependency.InvalidResponse");}
        catch(Exception e) when(e is HttpRequestException || e is TaskCanceledException && !token.IsCancellationRequested)
        {logger?.LogWarning("Core dependency failed: {FailureType}",e.GetType().Name);return Outcome<AccessScope>.Fail("Dependency.Unavailable");}
    }
    private async Task<Outcome<AccessScope>> RequireSnapshot(HttpContext http,string accessToken,string resource,string permission,CancellationToken token)
    {
        // OIDC's standard claim actions omit protocol `iss` from the cookie principal.
        // The configured authority is trusted by the existing OIDC handler; it is never supplied by the browser.
        var subject=http.User.FindFirst("sub")?.Value;var issuer=http.User.FindFirst("iss")?.Value??configuration?["Oidc:Authority"]?.TrimEnd('/');
        if(string.IsNullOrWhiteSpace(subject)||string.IsNullOrWhiteSpace(issuer))return Outcome<AccessScope>.Fail("Authentication.Required");
        async Task<Outcome<CoreContextSnapshot>> Load()
        {
            try {
                using var request=new HttpRequestMessage(HttpMethod.Get,"api/v1/access/context");request.Headers.Authorization=new("Bearer",accessToken);
                using var response=await client.SendAsync(request,CancellationToken.None);
                if(!response.IsSuccessStatusCode)return Outcome<CoreContextSnapshot>.Fail(response.StatusCode switch{HttpStatusCode.Unauthorized=>"Authentication.Required",HttpStatusCode.Forbidden=>"Access.Forbidden",HttpStatusCode.Conflict=>"Context.Invalid",_ when (int)response.StatusCode>=500=>"Dependency.Unavailable",_=>"Dependency.InvalidResponse"});
                var context=await response.Content.ReadFromJsonAsync<CoreContextSnapshot>();var now=DateTimeOffset.UtcNow;
                if(context is null||context.SchemaVersion!=1||context.State!="Linked"||context.Subject!=subject||context.Issuer!=issuer||context.OrganizationId==Guid.Empty||context.ContextVersion<1||context.Establishments is null||context.Grants is null||context.ExpiresAtUtc<=now||context.ExpiresAtUtc>context.IssuedAtUtc.AddSeconds(5)||context.IssuedAtUtc>now.AddSeconds(1)||context.Establishments.Any(e=>e.Id==Guid.Empty)||context.Establishments.Select(e=>e.Id).Distinct().Count()!=context.Establishments.Length||context.ActiveEstablishmentId==Guid.Empty||context.ActiveEstablishmentId is {} active&&!context.Establishments.Any(e=>e.Id==active))return Outcome<CoreContextSnapshot>.Fail("Dependency.InvalidResponse");
                // The trusted Core lease cannot outlive the identity token used to obtain it. Parsing here only shortens validity.
                var jwtReader=new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                if(jwtReader.CanReadToken(accessToken)){var jwt=jwtReader.ReadJwtToken(accessToken);context=context with{ExpiresAtUtc=context.ExpiresAtUtc<jwt.ValidTo?context.ExpiresAtUtc:new DateTimeOffset(jwt.ValidTo)};}
                var conservativeExpiry=context.ExpiresAtUtc.AddMilliseconds(-250);
                context=context with{ExpiresAtUtc=conservativeExpiry<now.AddMilliseconds(4750)?conservativeExpiry:now.AddMilliseconds(4750)};
                if(context.ExpiresAtUtc<=now)return Outcome<CoreContextSnapshot>.Fail("Authentication.Required");
                return new(context);
            }catch(System.Text.Json.JsonException){return Outcome<CoreContextSnapshot>.Fail("Dependency.InvalidResponse");}
            catch(Exception e) when(e is HttpRequestException or TaskCanceledException){return Outcome<CoreContextSnapshot>.Fail("Dependency.Unavailable");}
        }
        var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new[]{client.BaseAddress!.AbsoluteUri,issuer,subject,accessToken}))));
        var requestKey=(typeof(CoreContextSnapshot),key,http.Request.Method);
        Outcome<CoreContextSnapshot> loaded;
        if(http.Items.TryGetValue(requestKey,out var local)&&local is CoreContextSnapshot existing&&existing.ExpiresAtUtc>DateTimeOffset.UtcNow)loaded=new(existing);
        else if((http.Request.Path.Value??"").TrimEnd('/').Equals("/properties/new",StringComparison.OrdinalIgnoreCase)||HttpMethods.IsPost(http.Request.Method)||HttpMethods.IsPut(http.Request.Method)||HttpMethods.IsPatch(http.Request.Method)||HttpMethods.IsDelete(http.Request.Method)){
            // A write must observe the active unit/current rights now. Subsequent checks within this command reuse this trusted snapshot.
            cache?.Invalidate(key);loaded=await Load();
        }else loaded=cache is null?await Load():await cache.GetAsync(key,Load,token);
        if(!loaded.IsSuccess)return Outcome<AccessScope>.Fail(loaded.Code!);
        var snapshot=loaded.Value!;
        if(snapshot.ExpiresAtUtc<=DateTimeOffset.UtcNow)return Outcome<AccessScope>.Fail("Dependency.Unavailable");
        http.Items[requestKey]=snapshot;
        if(!snapshot.Grants!.Any(g=>g.ProductKey==Product&&g.ResourceKey==resource&&g.PermissionKey==permission)||snapshot.Establishments!.Length==0)return Outcome<AccessScope>.Fail("Access.Forbidden");
        var scope=new AccessScope(snapshot.OrganizationId,subject,snapshot.OrganizationName??"Empresa",snapshot.Establishments!,snapshot.Role is "Owner" or "Admin",snapshot.ActiveEstablishmentId,snapshot.MembershipId);
        http.Items[typeof(AccessScope)]=scope;return new(scope);
    }
    private static Outcome<AccessScope> Denied(HttpStatusCode status)=>Outcome<AccessScope>.Fail(status switch {HttpStatusCode.Unauthorized=>"Authentication.Required",HttpStatusCode.Forbidden=>"Access.Forbidden",HttpStatusCode.Conflict=>"Context.Invalid",_ when (int)status>=500=>"Dependency.Unavailable",_=>"Dependency.InvalidResponse"});
    public sealed record ContextResponse(string State,Guid OrganizationId,string? OrganizationName,string? Role=null,Guid? ActiveEstablishmentId=null,Guid MembershipId=default);
    public sealed record UnitsResponse(EstablishmentAccess[]? Establishments);
    public sealed record Grant(string ProductKey,string ResourceKey,string PermissionKey);
    public sealed record GrantsResponse(Grant[]? Grants);
}

