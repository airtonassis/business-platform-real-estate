using Microsoft.AspNetCore.Mvc;
using RealEstate.Domain;
namespace RealEstate.Web.Pages;

public sealed partial class PropertyModel
{
    [BindProperty(SupportsGet=true)] public Guid? SourcePropertyId {get;set;}
    [BindProperty(SupportsGet=true)] public Guid? AnnouncementId {get;set;}
    [BindProperty] public Guid RequestId {get;set;}=Guid.NewGuid();
    [BindProperty] public long? SourceVersion {get;set;}
    public bool IsDuplicate=>IsNew&&SourcePropertyId.HasValue;
    public bool IsAnnouncementForm=>IsDuplicate||AnnouncementId.HasValue;
    public bool SharedReadOnly {get;private set;}
    public bool CanDuplicateAnnouncement {get;private set;}
    public Guid? DuplicateSourceId {get;private set;}
    public Guid PhysicalId=>SourcePropertyId??(Guid.TryParse(Key,out var id)?id:Guid.Empty);

    private async Task<bool> LoadAnnouncementForm(bool prefill)
    {
        if(announcements is null){Deny("Resource.NotFound");return false;}
        var found=await service.FindAsync(PhysicalId,HttpContext.RequestAborted);
        if(!found.IsSuccess){Deny(found.Code);return false;}
        var physical=found.Value!;
        var listed=await announcements.ListAsync(PhysicalId,HttpContext.RequestAborted);
        if(!listed.IsSuccess){Deny(listed.Code);return false;}
        var original=listed.Value!.SingleOrDefault(a=>a.Id==AnnouncementId);
        if(IsDuplicate&&AnnouncementId is null)original=listed.Value!.FirstOrDefault(a=>a.LegacyAnchor);
        if(AnnouncementId.HasValue&&original is null){Deny("Resource.NotFound");return false;}
        SharedReadOnly=IsDuplicate||original is {LegacyAnchor:false};
        var grant=await access.RequireAsync("properties",IsDuplicate?"PROPERTY_CREATE":"PROPERTY_UPDATE",HttpContext.RequestAborted);
        CanWrite=grant.IsSuccess&&grant.Value!.ActiveEstablishmentId==physical.Data.EstablishmentId&&(IsDuplicate||original?.Status!="Closed");
        CanDuplicateAnnouncement=CanCreate&&ActiveEstablishmentId==physical.Data.EstablishmentId&&physical.Data.Purpose is not null;
        DuplicateSourceId=original?.Id;
        if(IsDuplicate&&!CanWrite){Deny(grant.IsSuccess?"Access.Forbidden":grant.Code);return false;}
        var occupied=!prefill&&IsDuplicate&&listed.Value!.Any(a=>a.Status=="Draft"&&a.Purpose==Input.Purpose);
        if(prefill){
            Input=physical.Data;Version=original is {LegacyAnchor:true}&&!IsDuplicate?physical.Version:original?.Version??physical.Version;CommercialCode=IsDuplicate?null:original?.CommercialCode??physical.CommercialCode;
            if(IsDuplicate){
                AnnouncementId=original?.Id;
                SourceVersion=original?.Version;
                var target=original?.Purpose=="Venda"?"Locação":original?.Purpose=="Locação"?"Venda":null;
                Input=Input with{Purpose=target};
                ApplyAnnouncementData(original is null?new(PropertyTax:physical.Data.Capture?.Values.PropertyTax,PropertyTaxPeriod:physical.Data.Capture?.Values.PropertyTaxPeriod,CondominiumFee:physical.Data.Capture?.Values.CondominiumFee,CondominiumPeriod:physical.Data.Capture?.Values.CondominiumPeriod):AnnouncementRules.ReviewFrom(original.Data));
            }else if(original is {LegacyAnchor:false}){Input=Input with{Purpose=original.Purpose};ApplyAnnouncementData(original.Data);}
        }
        else if(SharedReadOnly){
            if(!IsDuplicate&&Input.Purpose!=original?.Purpose){Error="A finalidade é permanente. Use Duplicar para outra finalidade.";Response.StatusCode=409;return false;}
            var submitted=Input;
            Input=physical.Data with{Purpose=submitted.Purpose};
            var capture=Input.Capture??new();capture.Values=submitted.Capture?.Values??new();Input=Input with{Capture=capture};
        }
        IncludeCategory(physical.Data.Category);
        if(Input.Capture?.Condominium.Id is {} condo){var result=await condominiums.FindAsync(condo,token:HttpContext.RequestAborted);LinkedCondominium=result.Value;CondominiumName=result.Value?.Data.Name;}
        var gallery=await media.ListAsync(PhysicalId,HttpContext.RequestAborted);if(gallery.IsSuccess)Gallery=gallery.Value;
        if(SharedReadOnly){CanEditOwner=false;CanCreateOwner=false;CanEditCondominium=false;CanApplyPolicy=false;CanReadPolicies=false;}
        Step=ApplicableStep();PrepareCapture();await LoadCandidates();
        if(occupied){Error=AnnouncementMessages.For("Announcement.ActiveExists");Response.StatusCode=409;return false;}
        return true;
    }
    private void ApplyAnnouncementData(AnnouncementInput data,bool preserveOtherValues=false)
    {
        var capture=Input.Capture??new();
        var values=preserveOtherValues?capture.Values:new ValueData();
        if(Input.Purpose=="Venda")values.Sale=data.Values??new();
        if(Input.Purpose=="Locação")values.Rental=data.Values??new();
        values.PropertyTax=data.PropertyTax;values.PropertyTaxPeriod=data.PropertyTaxPeriod;values.CondominiumFee=data.CondominiumFee;values.CondominiumPeriod=data.CondominiumPeriod;
        if(!preserveOtherValues||Input.Purpose=="Venda"){values.Financing=data.Financing;values.AcceptsExchange=data.AcceptsExchange;values.ExchangeNotes=data.ExchangeNotes;}
        capture.Values=values;
        Input=Input with{Capture=capture};
    }
    private async Task<IActionResult> SaveAnnouncementForm()
    {
        if(!await LoadAnnouncementForm(false))return Page();
        // Anchor maintenance continues through the original property service. Child writes
        // project ONLY the existing commercial contract; submitted physical fields are ignored.
        if(!SharedReadOnly){AnnouncementId=null;return await OnPostAsync();}
        if(!ModelState.IsValid){Error="Revise os valores informados. Seus dados continuam nesta tela.";Response.StatusCode=400;return Page();}
        var v=Input.Capture?.Values??new();
        var data=new AnnouncementInput(Input.Purpose=="Venda"?v.Sale:v.Rental,v.PropertyTax,v.PropertyTaxPeriod,v.CondominiumFee,v.CondominiumPeriod,Input.Purpose=="Venda"?v.Financing:null,Input.Purpose=="Venda"?v.AcceptsExchange:null,Input.Purpose=="Venda"?v.ExchangeNotes:null);
        var result=IsDuplicate
            ?await announcements!.CreateAsync(PhysicalId,new(Input.Purpose??"",RequestId,AnnouncementId,SourceVersion,data),HttpContext.RequestAborted)
            :await announcements!.UpdateAsync(AnnouncementId!.Value,Version,data,ct:HttpContext.RequestAborted);
        if(!result.IsSuccess){Error=AnnouncementMessages.For(result.Code);Response.StatusCode=Program.Status(result.Code);return Page();}
        TempData["Notice"]="Anúncio salvo. "+(result.Value!.CommercialCode??"Código comercial pendente.");
        return RedirectToPage("Index");
    }
}
