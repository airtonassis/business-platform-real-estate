using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
namespace RealEstate.Web;

// Renews only the saved OIDC tokens. Core still revalidates rights and active establishment.
public sealed class OidcSessionRenewal(IOptionsMonitor<OpenIdConnectOptions> options,IMemoryCache cache):CookieAuthenticationEvents
{
    private static readonly SemaphoreSlim Gate=new(1,1);
    private sealed record Tokens(string Access,string Refresh,DateTimeOffset Expires);
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var properties=context.Properties;
        var deadline=properties.Items.TryGetValue("imob-session-deadline",out var saved)&&DateTimeOffset.TryParse(saved,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var absolute)?absolute:properties.ExpiresUtc;
        if(deadline is null||deadline<=DateTimeOffset.UtcNow){context.RejectPrincipal();return;}
        properties.Items["imob-session-deadline"]=deadline.Value.ToString("o",CultureInfo.InvariantCulture);
        if(!DateTimeOffset.TryParse(properties.GetTokenValue("expires_at"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var expires)||expires>DateTimeOffset.UtcNow.AddSeconds(30))return;
        var refresh=properties.GetTokenValue("refresh_token");
        if(string.IsNullOrWhiteSpace(refresh)){context.RejectPrincipal();return;}
        var key="imob-oidc-renew:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refresh)));
        await Gate.WaitAsync(context.HttpContext.RequestAborted);
        try
        {
            if(!cache.TryGetValue<Tokens>(key,out var renewed))
            {
                var oidc=options.Get("Oidc");
                var configuration=oidc.Configuration??await oidc.ConfigurationManager!.GetConfigurationAsync(context.HttpContext.RequestAborted);
                using var response=await oidc.Backchannel.PostAsync(configuration.TokenEndpoint,new FormUrlEncodedContent(new Dictionary<string,string>{["grant_type"]="refresh_token",["refresh_token"]=refresh,["client_id"]=oidc.ClientId!,["client_secret"]=oidc.ClientSecret??""}),context.HttpContext.RequestAborted);
                if(!response.IsSuccessStatusCode){context.RejectPrincipal();return;}
                using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));var root=json.RootElement;
                var access=root.GetProperty("access_token").GetString();var seconds=root.GetProperty("expires_in").GetInt32();
                if(string.IsNullOrWhiteSpace(access)||seconds<=0){context.RejectPrincipal();return;}
                renewed=new(access,root.TryGetProperty("refresh_token",out var rotated)?rotated.GetString()??refresh:refresh,DateTimeOffset.UtcNow.AddSeconds(seconds));
                // Coalesce concurrent requests carrying the same pre-rotation cookie; never cache grants.
                cache.Set(key,renewed,TimeSpan.FromSeconds(30));
            }
            var tokens=properties.GetTokens().ToDictionary(t=>t.Name,t=>t.Value);
            tokens["access_token"]=renewed!.Access;tokens["refresh_token"]=renewed.Refresh;tokens["expires_at"]=renewed.Expires.ToString("o",CultureInfo.InvariantCulture);
            properties.StoreTokens(tokens.Select(t=>new AuthenticationToken{Name=t.Key,Value=t.Value}));
            properties.IssuedUtc=DateTimeOffset.UtcNow;properties.ExpiresUtc=deadline;context.ShouldRenew=true;
        }
        catch(Exception e) when(e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException){context.RejectPrincipal();}
        finally{Gate.Release();}
        // Do not extend the existing 30-minute cookie/session lifetime.
    }
}
