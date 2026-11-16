using Microsoft.AspNetCore.DataProtection;
using RealEstate.Domain;

namespace RealEstate.Web;

public sealed class TransferPreviewProtector(IDataProtectionProvider provider) : ITransferPreviewProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("AraiA.S05B2.NegotiationTransferPreview.v1");
    public string Protect(string value) => protector.Protect(value);
    public string? Unprotect(string protectedValue)
    { try { return protector.Unprotect(protectedValue); } catch (System.Security.Cryptography.CryptographicException) { return null; } }
}
