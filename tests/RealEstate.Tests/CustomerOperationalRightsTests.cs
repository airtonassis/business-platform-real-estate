using RealEstate.Domain;
using RealEstate.Web;
using Xunit;
namespace RealEstate.Tests;
public sealed class CustomerOperationalRightsTests
{
    private sealed class Access(string? denied):ICoreAccess
    {
        public readonly List<string> Checked=[];
        public Task<Outcome<AccessScope>> RequireAsync(string resource,string permission,CancellationToken token)
        {Assert.Equal("customers",resource);Checked.Add(permission);return Task.FromResult(permission==denied?Outcome<AccessScope>.Fail("Access.Forbidden"):new Outcome<AccessScope>(new(Guid.NewGuid(),"actor","Company",[])));}
    }
    [Fact]public async Task Customer_read_gate_checks_only_the_read_right()
    {var a=new Access(null);Assert.True((await CustomerOperationalRights.ReadScopeAsync(a,CancellationToken.None)).IsSuccess);Assert.Equal(new[]{"CUSTOMER_READ"},a.Checked);}
    [Fact]public async Task Missing_customer_read_closes_the_read_gate()
    {var a=new Access("CUSTOMER_READ");Assert.Equal("Access.Forbidden",(await CustomerOperationalRights.ReadScopeAsync(a,CancellationToken.None)).Code);Assert.Equal(new[]{"CUSTOMER_READ"},a.Checked);}
}
