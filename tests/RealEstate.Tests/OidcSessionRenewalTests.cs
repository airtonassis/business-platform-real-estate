using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;

public sealed class OidcSessionRenewalTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Expired_access_token_is_renewed_or_fails_closed(bool succeeds)
    {
        var options=new OpenIdConnectOptions{ClientId="imob",ClientSecret="local-test",Configuration=new(){TokenEndpoint="https://idp.invalid/token"},Backchannel=new HttpClient(new Handler(succeeds))};
        var properties=new AuthenticationProperties{ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(20)};
        properties.StoreTokens([new(){Name="access_token",Value="expired"},new(){Name="refresh_token",Value="refresh"},new(){Name="expires_at",Value=DateTimeOffset.UtcNow.AddMinutes(-1).ToString("o")}]);
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","owner")],"ErpCookie"));
        var context=new CookieValidatePrincipalContext(new DefaultHttpContext(),new AuthenticationScheme("ErpCookie",null,typeof(CookieAuthenticationHandler)),new CookieAuthenticationOptions(),new AuthenticationTicket(principal,properties,"ErpCookie"));
        var originalExpiry=properties.ExpiresUtc;
        using var cache=new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        await new OidcSessionRenewal(new Monitor(options),cache).ValidatePrincipal(context);
        if(succeeds){Assert.True(context.ShouldRenew);Assert.Equal("renewed",properties.GetTokenValue("access_token"));Assert.Equal("rotated",properties.GetTokenValue("refresh_token"));Assert.Equal(originalExpiry,properties.ExpiresUtc);}
        else {Assert.Null(context.Principal);Assert.False(context.ShouldRenew);}
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Absolute_deadline_and_unexpired_token_are_respected(bool deadlinePassed)
    {
        var properties=new AuthenticationProperties{ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(20)};
        properties.Items["imob-session-deadline"]=DateTimeOffset.UtcNow.AddMinutes(deadlinePassed?-1:20).ToString("o");
        properties.StoreTokens([new(){Name="access_token",Value="still-valid"},new(){Name="expires_at",Value=DateTimeOffset.UtcNow.AddMinutes(4).ToString("o")}]);
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub","owner")],"ErpCookie"));
        var context=new CookieValidatePrincipalContext(new DefaultHttpContext(),new AuthenticationScheme("ErpCookie",null,typeof(CookieAuthenticationHandler)),new CookieAuthenticationOptions(),new AuthenticationTicket(principal,properties,"ErpCookie"));
        using var cache=new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        await new OidcSessionRenewal(new Monitor(new()),cache).ValidatePrincipal(context);
        Assert.False(context.ShouldRenew);Assert.Equal("still-valid",properties.GetTokenValue("access_token"));Assert.Equal(deadlinePassed,context.Principal is null);
    }
    private sealed class Handler(bool succeeds):HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var body=await request.Content!.ReadAsStringAsync(token);Assert.Contains("grant_type=refresh_token",body);Assert.Contains("refresh_token=refresh",body);
            return new(succeeds?HttpStatusCode.OK:HttpStatusCode.BadRequest){Content=new StringContent(succeeds?"{\"access_token\":\"renewed\",\"refresh_token\":\"rotated\",\"expires_in\":300}":"{\"error\":\"invalid_grant\"}")};
        }
    }
    private sealed class Monitor(OpenIdConnectOptions options):IOptionsMonitor<OpenIdConnectOptions>
    {
        public OpenIdConnectOptions CurrentValue=>options;
        public OpenIdConnectOptions Get(string? name)=>options;
        public IDisposable? OnChange(Action<OpenIdConnectOptions,string?> listener)=>null;
    }
}
