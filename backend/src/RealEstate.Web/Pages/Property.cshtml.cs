using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RealEstate.Application;
using RealEstate.Domain;
namespace RealEstate.Web.Pages;
[RealEstate.Web.MediaStreaming]
public sealed partial class PropertyModel(PropertyServiceV2 service,ICoreAccess access,PropertyMediaService media,CondominiumService condominiums,ICaptureDirectory? directory=null,CommercialPolicyService? policies=null,AnnouncementService? announcements=null):PageModel
{
    [BindProperty(SupportsGet=true)] public string? Key {get;set;}
    [BindProperty] public PropertyInputV2 Input {get;set;}=new(Address:new(Country:"BR"));
    public IReadOnlyList<string> Categories {get;private set;}=[];
    private void IncludeCategory(string? value)=>Categories=Categories.Append(value).OfType<string>().Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.CurrentCulture).ToArray();
    [BindProperty] public long Version {get;set;}
    public string? CommercialCode {get;private set;}
    [BindProperty(SupportsGet=true)] public int Step {get;set;}=1;
    private int ApplicableStep()
    {
        var requested=Step is >=1 and <=14?Step:1;
        var position=Array.IndexOf(CadastroPresentation.NavigationOrder,requested);
        return CadastroPresentation.NavigationCategories.Where(c=>CadastroPresentation.IsApplicable(c,Input)).MinBy(c=>Math.Abs(Array.IndexOf(CadastroPresentation.NavigationOrder,c.Id)-position))!.Id;
    }
    public CondominiumProfile? LinkedCondominium {get;private set;}
    public bool CanEditOwner {get;private set;}
    public bool CanEditCondominium {get;private set;}
    public string? CondominiumName {get;private set;}
    public IReadOnlyList<Owner> Owners {get;private set;}=[];
    public string? Error {get;private set;}
    public bool CanWrite {get;private set;}
    public bool CanCreate {get;private set;}
    public bool CanReadPolicies {get;private set;}
    public bool CanApplyPolicy {get;private set;}
    public string? CurrentSubject {get;private set;}
    public IReadOnlyList<CommercialPolicyRevision> PolicyRevisions {get;private set;}=[];
    public async Task<IActionResult> OnPostApplyPolicyAsync(Guid policyRevisionId)
    {
        if(policies is null)return StatusCode(503);
        var revision=await policies.ForApplicationAsync(policyRevisionId,HttpContext.RequestAborted);
        if(!revision.IsSuccess){Error=PolicyMessages.For(revision.Code);Response.StatusCode=Program.Status(revision.Code);await Load();return Page();}
        Input=Input with{Capture=CommercialPolicyRules.Apply(revision.Value!,Input.Capture)};
        return await OnPostAsync();
    }
    private async Task LoadPolicies()
    {
        if(policies is null||Step is not(12 or 13))return;
        var result=await policies.ListAsync(HttpContext.RequestAborted);
        if(result.IsSuccess)PolicyRevisions=result.Value!.Where(r=>r.Status=="Approved"&&(r.Input.PropertyId is null||r.Input.PropertyId.ToString()==Key)).ToArray();
    }
    public IReadOnlyList<CaptureResponsible> ResponsibleCandidates {get;private set;}=[];
    public CaptureResponsible? SuggestedResponsible=>string.IsNullOrWhiteSpace(Input.Capture?.Acquisition.ResponsibleSubject)?ResponsibleCandidates.FirstOrDefault(c=>c.Subject==CurrentSubject):null;
    public bool DirectoryUnavailable {get;private set;}
    private AccessScope? writeScope;
    public Guid? CurrentOrganizationId=>writeScope?.OrganizationId;
    private async Task LoadCandidates()
    {
        // Categories are navigated client-side: the initial page must already contain authorized options.
        if(!CanWrite)return;
        var unit=IsNew?ActiveEstablishmentId:Input.EstablishmentId;
        if(directory is null||writeScope is null||unit is null){DirectoryUnavailable=true;return;}
        var result=await directory.ReadAsync(writeScope,unit.Value,token:HttpContext.RequestAborted);
        if(result.IsSuccess)ResponsibleCandidates=result.Value!.Items;
        else DirectoryUnavailable=true;
    }
    public bool CanCreateOwner {get;private set;}
    public IReadOnlyList<EstablishmentAccess> Establishments {get;private set;}=[];
    public bool Allowed {get;private set;}
    public Guid? ActiveEstablishmentId {get;private set;}
    public string EstablishmentLabel=>Establishments.FirstOrDefault(e=>e.Id==(IsNew?ActiveEstablishmentId:Input.EstablishmentId))?.Name??"Contexto de filial ativa indisponível";
    public bool IsNew=>Key=="new";
    public async Task OnGetAsync()
    {
        if(!await Load())return;
        // Razor must render the resolved step, not the original query's ModelState value.
        ModelState.Remove(nameof(Step));
        if(IsPropertyCopy){if(await LoadPropertyCopy(true)){await LoadCandidates();await LoadPolicies();}return;}
        if(IsAnnouncementForm){await LoadAnnouncementForm(true);return;}
        if(IsNew){Step=ApplicableStep();PrepareCapture();await LoadCandidates();await LoadPolicies();return;}
        if(!Guid.TryParse(Key,out var id)){Deny("Resource.NotFound");return;}
        var result=await service.FindAsync(id,HttpContext.RequestAborted);
        if(!result.IsSuccess){Deny(result.Code);return;}
        Input=result.Value!.Data;Version=result.Value.Version;CommercialCode=result.Value.CommercialCode;Step=ApplicableStep();
        if(Input.Capture?.Condominium.Id is {} condominiumId){var found=await condominiums.FindAsync(condominiumId,token:HttpContext.RequestAborted);LinkedCondominium=found.Value;CondominiumName=found.Value?.Data.Name;}
        IncludeCategory(Input.Category);
        var gallery=await media.ListAsync(id,HttpContext.RequestAborted);if(gallery.IsSuccess)Gallery=gallery.Value;else Deny(gallery.Code);
        PrepareCapture();
        await LoadCandidates();
        await LoadPolicies();
    }
    private void PrepareCapture()
    {
        var capture=Input.Capture??new();
        foreach(var shared in LinkedCondominium?.Data.Characteristics??[])if(!capture.Selections.Any(s=>s.GroupId==shared.GroupId&&s.OptionId==shared.OptionId))capture.Selections.Add(new(){GroupId=shared.GroupId,OptionId=shared.OptionId,Label=shared.Label});
        foreach(var group in CaptureCatalog.Groups)
        foreach(var option in group.Options)
            if(!capture.Selections.Any(s=>s.GroupId==group.Id&&s.OptionId==option.Id))capture.Selections.Add(new(){GroupId=group.Id,OptionId=option.Id,Label=option.Label});
        if(Gallery is {} gallery)
        {
            capture.Media=capture.Media.Where(m=>gallery.Items.Any(i=>i.Id==m.MediaId)).ToList();
            foreach(var item in gallery.Items)if(!capture.Media.Any(m=>m.MediaId==item.Id))capture.Media.Add(new(){MediaId=item.Id});
        }
        Input=Input with{Capture=capture};
    }
    private void Deny(string? code){Allowed=false;Error=Messages.For(code);Response.StatusCode=Program.Status(code);}
    private async Task<bool> Load()
    {
        var result=await service.ListAsync(HttpContext.RequestAborted);
        if(!result.IsSuccess){Deny(result.Code);return false;}
        Owners=result.Value!.Owners;Establishments=result.Value.Establishments??[];
        Categories=result.Value.Properties.Select(p=>p.Data.Category).OfType<string>().Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.CurrentCulture).ToArray();
        var create=await access.RequireAsync("properties","PROPERTY_CREATE",HttpContext.RequestAborted);
        CanEditOwner=(await access.RequireAsync("owners","OWNER_UPDATE",HttpContext.RequestAborted)).IsSuccess;
        CanEditCondominium=(await access.RequireAsync("condominiums","CONDOMINIUM_UPDATE",HttpContext.RequestAborted)).IsSuccess;
        CanCreateOwner=(await access.RequireAsync("owners","OWNER_CREATE",HttpContext.RequestAborted)).IsSuccess;
        CanReadPolicies=(await access.RequireAsync("commercial-policies","POLICY_READ",HttpContext.RequestAborted)).IsSuccess;
        CanApplyPolicy=(await access.RequireAsync("commercial-policies","POLICY_APPLY",HttpContext.RequestAborted)).IsSuccess;
        CanCreate=create.IsSuccess;ActiveEstablishmentId=create.Value?.ActiveEstablishmentId;
        var write=IsNew?create:await access.RequireAsync("properties","PROPERTY_UPDATE",HttpContext.RequestAborted);
        CanWrite=write.IsSuccess;CurrentSubject=write.Value?.Subject;writeScope=write.Value;
        Allowed=true;return true;
    }
    public async Task<IActionResult> OnPostAsync()
    {
        HttpContext.Items["DraftSaveStage"]="Handler";
        if(!await Load())return Page();
        if(IsPropertyCopy&&!await LoadPropertyCopy(false))return Page();
        if(IsAnnouncementForm)return await SaveAnnouncementForm();
        await LoadCandidates();
        await LoadPolicies();
        string? country="BR";
        if(!IsNew){
            if(!Guid.TryParse(Key,out var existingId)){Deny("Resource.NotFound");return Page();}
            var existing=await service.FindAsync(existingId,HttpContext.RequestAborted);
            if(!existing.IsSuccess){Deny(existing.Code);return Page();}
            Input=Input with{Code=existing.Value!.Data.Code};
            CommercialCode=existing.Value.CommercialCode;
            IncludeCategory(existing.Value.Data.Category);country=existing.Value.Data.Address?.Country;
        }
        var address=Input.Address??new();
        // One visible number editor maps to the unchanged structured-address contract.
        // The API/domain still reject conflicting Number + WithoutNumber combinations.
        if(Request.Form.ContainsKey("Input.Address.Number"))
        {
            var number=string.IsNullOrWhiteSpace(address.Number)?null:address.Number.Trim();
            var withoutNumber=string.Equals(number,"s/n",StringComparison.OrdinalIgnoreCase);
            address=address with{Number=withoutNumber?null:number,WithoutNumber=withoutNumber?true:number is null?null:false};
        }
        Input=Input with{Code=IsNew?null:Input.Code,Address=address with{Country=country}};
        if(!string.IsNullOrWhiteSpace(Input.Category) && !Categories.Contains(Input.Category,StringComparer.Ordinal)){Error="Selecione uma categoria disponível. Nova categoria depende da habilitação do catálogo.";Response.StatusCode=400;return Page();}
        if(!ModelState.IsValid){Error="Revise os campos e utilize valores numéricos válidos.";Response.StatusCode=400;return Page();}
        Guid? id=null;if(!IsNew){if(!Guid.TryParse(Key,out var parsed)){Deny("Resource.NotFound");return Page();}id=parsed;}
        HttpContext.Items["DraftSaveStage"]="Service";
        PropertyCopySource? copy=null;
        if(IsPropertyCopy){var command=await CopyCommand();if(!command.IsSuccess){Error=Messages.For(command.Code);Response.StatusCode=Program.Status(command.Code);return Page();}copy=command.Value;}
        var result=await service.SaveAsync(Input,id,Version,HttpContext.RequestAborted,copy);
        HttpContext.Items["DraftSaveCode"]=result.IsSuccess?"Saved":result.Code;
        if(!result.IsSuccess){Error=PolicyMessages.For(result.Code);Response.StatusCode=Program.Status(result.Code);return Page();}
        TempData["Notice"]=IsNew?"Imóvel cadastrado como rascunho.":"Imóvel atualizado.";
        Step=ApplicableStep();
        return Step==1?RedirectToPage("Property",new{key=result.Value!.Id}):RedirectToPage("Property",new{key=result.Value!.Id,step=Step});
    }
}
