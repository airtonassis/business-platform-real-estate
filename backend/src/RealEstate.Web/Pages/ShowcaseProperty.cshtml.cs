using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RealEstate.Application;
using RealEstate.Domain;

namespace RealEstate.Web.Pages;

public sealed class ShowcasePropertyModel(PropertyServiceV2 properties, PropertyMediaService media,
    AnnouncementService announcements, ShowcaseService showcase, ICoreAccess access) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? View { get; set; }
    [BindProperty] public long Version { get; set; }
    [BindProperty] public string[] Fields { get; set; } = [];
    [BindProperty] public Guid[] MediaIds { get; set; } = [];
    [BindProperty] public string? Description { get; set; }
    public PropertyV2? Property { get; private set; }
    public ShowcaseEntry? Entry { get; private set; }
    public MediaCollection? Gallery { get; private set; }
    public IReadOnlyList<PropertyAnnouncement> Announcements { get; private set; } = [];
    public bool CanManage { get; private set; }
    public bool CanAuthorize { get; private set; }
    public bool CanShare => Entry?.State == "Authorized" && Property is not null && ShowcaseRules.CanPrepare(Property.Status);
    public bool IsPreparation => View == "prepare" && (CanManage || CanAuthorize);
    public IReadOnlyList<PropertyMedia> SelectedGallery => Entry?.Selection.MediaIds?
        .Select(id => Gallery?.Items.FirstOrDefault(m => m.Id == id))
        .OfType<PropertyMedia>().ToArray() ?? [];
    public string? Error { get; private set; }
    public static IReadOnlyDictionary<string, string> FieldLabels { get; } = new Dictionary<string, string>
    {
        ["title"] = "Título", ["commercialCode"] = "Código comercial", ["price"] = "Preço",
        ["purpose"] = "Finalidade", ["type"] = "Tipo", ["category"] = "Categoria",
        ["municipality"] = "Cidade", ["state"] = "UF", ["neighborhood"] = "Bairro",
        ["description"] = "Descrição comercial", ["area"] = "Área", ["bedrooms"] = "Dormitórios",
        ["suites"] = "Suítes", ["bathrooms"] = "Banheiros", ["parking"] = "Vagas"
    };
    public bool Shows(string field) => Entry?.Selection.Fields.Contains(field) == true;
    public bool SelectedMedia(Guid id) => Entry?.Selection.MediaIds?.Contains(id) == true;

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var result = await properties.FindAsync(Id, HttpContext.RequestAborted);
        if (!result.IsSuccess) { Deny(result.Code); return; }
        Property = result.Value!;
        var property = Property;
        var configured = await showcase.FindAsync(Id, HttpContext.RequestAborted);
        if (!configured.IsSuccess) { Property = null; Deny(configured.Code); return; }
        Entry = configured.Value;
        var edit = await access.RequireAsync("properties", "PROPERTY_UPDATE", HttpContext.RequestAborted);
        CanManage = edit.IsSuccess && edit.Value!.OrganizationId == property.OrganizationId &&
            edit.Value.ActiveEstablishmentId == property.Data.EstablishmentId &&
            edit.Value.Establishments?.Any(e => e.Id == property.Data.EstablishmentId) == true;
        var approve = await access.RequireAsync("properties", "PROPERTY_APPROVE", HttpContext.RequestAborted);
        CanAuthorize = approve.IsSuccess && approve.Value!.OrganizationId == property.OrganizationId &&
            approve.Value.ActiveEstablishmentId == property.Data.EstablishmentId &&
            approve.Value.Establishments?.Any(e => e.Id == property.Data.EstablishmentId) == true;
        if ((!CanShare || Entry is null) && !CanManage && !CanAuthorize) { Deny("Resource.NotFound"); Property = null; return; }
        var gallery = await media.ListAsync(Id, HttpContext.RequestAborted);
        if (gallery.IsSuccess) Gallery = gallery.Value;
        var linked = await announcements.ListAsync(Id, HttpContext.RequestAborted);
        if (linked.IsSuccess) Announcements = linked.Value!.Where(a => a.Status != "Closed").ToArray();
        if (Entry is not null)
        {
            Version = Entry.Version;
            Fields = Entry.Selection.Fields.ToArray();
            MediaIds = Entry.Selection.MediaIds?.ToArray() ?? [];
            Description = Entry.Selection.Description;
        }
    }

    public async Task<IActionResult> OnPostPrepareAsync()
    {
        var result = await showcase.ChangeAsync(Id, new(Version, new(Fields, MediaIds, Description)), "prepare", HttpContext.RequestAborted);
        if (result.IsSuccess) return RedirectToPage(new { id = Id, view = "prepare" });
        View = "prepare"; await LoadAsync(); Deny(result.Code); return Page();
    }
    public async Task<IActionResult> OnPostAuthorizeAsync()
    {
        var current = await showcase.FindAsync(Id, HttpContext.RequestAborted);
        if (!current.IsSuccess || current.Value is null) { Deny(current.Code ?? "Resource.NotFound"); return Page(); }
        var result = await showcase.ChangeAsync(Id, new(Version, current.Value.Selection), "authorize", HttpContext.RequestAborted);
        if (result.IsSuccess) return RedirectToPage(new { id = Id });
        View = "prepare"; await LoadAsync(); Deny(result.Code); return Page();
    }
    public async Task<IActionResult> OnPostWithdrawAsync()
    {
        var result = await showcase.ChangeAsync(Id, new(Version, new([])), "withdraw", HttpContext.RequestAborted);
        if (result.IsSuccess) return RedirectToPage(new { id = Id, view = "prepare" });
        View = "prepare"; await LoadAsync(); Deny(result.Code); return Page();
    }
    private void Deny(string? code)
    {
        Error = code switch
        {
            "Showcase.NotEligible" => "Este imóvel ainda não foi liberado para apresentação comercial.",
            "Showcase.InvalidSelection" => "Escolha ao menos uma informação permitida e mídias deste imóvel.",
            "Showcase.VersionConflict" or "Showcase.SelectionConflict" => "A seleção mudou. Reabra a Vitrine antes de decidir.",
            "Showcase.StatusConflict" => "Prepare a seleção antes de autorizá-la.",
            _ => Messages.For(code)
        };
        Response.StatusCode = code is "Showcase.VersionConflict" or "Showcase.SelectionConflict" or "Showcase.StatusConflict" ? 409 : Program.Status(code);
    }
}
