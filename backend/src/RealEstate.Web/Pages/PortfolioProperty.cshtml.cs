using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RealEstate.Application;
using RealEstate.Domain;

namespace RealEstate.Web.Pages;

public sealed class PortfolioPropertyModel(PropertyServiceV2 properties, ICoreAccess access, PropertyMediaService media, ShowcaseService showcase) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public PropertyV2? Property { get; private set; }
    public string? EstablishmentName { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanSubmit => CanEdit && Property?.Status == PropertyLifecycle.Draft && Property.Data.EstablishmentId == ActiveEstablishmentId;
    public bool CanApprove { get; private set; }
    public bool CanOpenShowcase { get; private set; }
    private Guid? ActiveEstablishmentId { get; set; }
    public MediaCollection? Gallery { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        var read = await access.RequireAsync("properties", "PROPERTY_READ", HttpContext.RequestAborted);
        if (!read.IsSuccess) { Deny(read.Code); return; }
        var found = await properties.FindAsync(Id, HttpContext.RequestAborted);
        if (!found.IsSuccess) { Deny(found.Code); return; }
        var item = found.Value!;
        var unit = read.Value!.Establishments?.FirstOrDefault(e => e.Id == item.Data.EstablishmentId);
        if (unit is null || item.OrganizationId != read.Value.OrganizationId) { Deny("Resource.NotFound"); return; }
        Property = item;
        ActiveEstablishmentId = read.Value.ActiveEstablishmentId;
        EstablishmentName = unit.Name;
        var gallery = await media.ListAsync(Id, HttpContext.RequestAborted);
        if (gallery.IsSuccess) Gallery = gallery.Value;
        if (ShowcaseRules.CanPrepare(item.Status))
        {
            var configured = await showcase.FindAsync(Id, HttpContext.RequestAborted);
            var editor = await access.RequireAsync("properties", "PROPERTY_UPDATE", HttpContext.RequestAborted);
            CanOpenShowcase = configured.IsSuccess && configured.Value?.State == "Authorized" ||
                editor.IsSuccess && editor.Value!.OrganizationId == item.OrganizationId &&
                editor.Value.ActiveEstablishmentId == item.Data.EstablishmentId &&
                editor.Value.Establishments?.Any(e => e.Id == item.Data.EstablishmentId) == true;
        }
        var write = await access.RequireAsync("properties", "PROPERTY_UPDATE", HttpContext.RequestAborted);
        CanEdit = item.Status == PropertyLifecycle.Draft && write.IsSuccess && write.Value!.OrganizationId == item.OrganizationId &&
            write.Value.Establishments?.Any(e => e.Id == item.Data.EstablishmentId) == true;
        if (item.Status == PropertyLifecycle.InReview)
        {
            var approve = await access.RequireAsync("properties", "PROPERTY_APPROVE", HttpContext.RequestAborted);
            CanApprove = approve.IsSuccess && approve.Value!.OrganizationId == item.OrganizationId &&
                approve.Value.ActiveEstablishmentId == item.Data.EstablishmentId &&
                approve.Value.Establishments?.Any(e => e.Id == item.Data.EstablishmentId) == true;
        }
    }

    public async Task<IActionResult> OnPostSubmitAsync(long version)
    {
        var result = await properties.SubmitForReviewAsync(Id, version, HttpContext.RequestAborted);
        if (result.IsSuccess) return RedirectToPage(new { id = Id });
        await OnGetAsync();
        Deny(result.Code);
        return Page();
    }
    public async Task<IActionResult> OnPostApproveAsync(long version)
    {
        var result = await properties.ApproveAsync(Id, version, HttpContext.RequestAborted);
        if (result.IsSuccess) return RedirectToPage(new { id = Id });
        await OnGetAsync();
        Deny(result.Code);
        return Page();
    }

    private void Deny(string? code)
    {
        Error = Messages.For(code);
        Response.StatusCode = Program.Status(code);
    }
}
