using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using RealEstate.Application;
using RealEstate.Domain;

namespace RealEstate.Web.Pages;

public sealed class ShowcaseModel(PropertyListingService listings, ShowcaseService showcase,
    PropertyMediaService media, AnnouncementService announcements, ICoreAccess access) : PageModel
{
    public sealed record Card(PropertyV2 Property, ShowcaseEntry? Entry, Guid? CoverId,
        IReadOnlyList<(string Purpose, decimal Amount)> Prices)
    {
        public bool Shows(string field) => Entry?.State == "Authorized" && Entry.Selection.Fields.Contains(field);
        public string Location => string.Join(" · ", new[] {
            Shows("neighborhood") ? Property.Data.Address?.Neighborhood : null,
            Shows("municipality") ? Property.Data.Address?.Municipality : null,
            Shows("state") ? Property.Data.Address?.State : null
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public PropertyListPage? Items { get; private set; }
    public IReadOnlyList<Card> Cards { get; private set; } = [];
    public string? Error { get; private set; }
    public async Task OnGetAsync()
    {
        var scope = await access.RequireAsync("properties", "PROPERTY_READ", HttpContext.RequestAborted);
        if (!scope.IsSuccess) { Error = Messages.For(scope.Code); Response.StatusCode = Program.Status(scope.Code); return; }
        var active = scope.Value!.ActiveEstablishmentId;
        if (active is null) { Error = Messages.For("Context.ActiveEstablishmentRequired"); Response.StatusCode = 409; return; }
        var result = await listings.ListAsync(new PropertyListQuery { Page = PageNumber, EstablishmentId = active }, HttpContext.RequestAborted);
        if (result.IsSuccess)
        {
            Items = result.Value;
            var cards = new List<Card>();
            foreach (var property in Items!.Properties)
            {
                var configured = await showcase.FindAsync(property.Id, HttpContext.RequestAborted);
                var entry = configured.IsSuccess && configured.Value?.State == "Authorized" && ShowcaseRules.CanPrepare(property.Status) ? configured.Value : null;
                Guid? cover = null;
                var prices = new List<(string Purpose, decimal Amount)>();
                if (entry is not null)
                {
                    if (entry.Selection.MediaIds is { Count: > 0 } ids)
                    {
                        var gallery = await media.ListAsync(property.Id, HttpContext.RequestAborted);
                        cover = ids.FirstOrDefault(id => gallery.IsSuccess && gallery.Value!.Items.Any(m => m.Id == id && m.Kind == "image"));
                        if (cover == Guid.Empty) cover = null;
                    }
                    if (entry.Selection.Fields.Contains("price"))
                    {
                        var linked = await announcements.ListAsync(property.Id, HttpContext.RequestAborted);
                        if (linked.IsSuccess)
                            foreach (var a in linked.Value!.Where(a => a.Status != "Closed"))
                            {
                                var value = a.Data.Values;
                                var amount = value?.DifferentAdvertised == true ? value.Advertised : value?.Authorized;
                                if (amount is not null) prices.Add((a.Purpose, amount.Value));
                            }
                    }
                }
                cards.Add(new(property, entry, cover, prices));
            }
            Cards = cards;
        }
        else { Error = Messages.For(result.Code); Response.StatusCode = Program.Status(result.Code); }
    }
}
