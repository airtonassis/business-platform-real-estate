using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RealEstate.Application;
using RealEstate.Domain;
namespace RealEstate.Web.Pages;

public sealed class CommercialPolicyModel(CommercialPolicyService service,PropertyServiceV2 properties,ICoreAccess access):PageModel
{
    public bool CanEdit {get;private set;}
    public bool CanApprove {get;private set;}
    public IReadOnlyList<CommercialPolicyRevision> Revisions {get;private set;}=[];
    public string? Error {get;private set;}
    [BindProperty(SupportsGet=true)]public Guid? PropertyId {get;set;}
    [BindProperty]public Guid? RevisionId {get;set;}
    [BindProperty]public Guid? BaseRevisionId {get;set;}
    [BindProperty]public long Version {get;set;}
    [BindProperty]public string Name {get;set;}="";
    [BindProperty]public string Reason {get;set;}="";
    [BindProperty]public PreliminaryFees Fees {get;set;}=new();
    [BindProperty]public List<CaptureSelection> Guarantees {get;set;}=[];
    public async Task OnGetAsync(Guid? revision)
    {
        if(!await Load())return;
        var selected=revision.HasValue?Revisions.FirstOrDefault(r=>r.Id==revision):null;
        if(selected is not null){RevisionId=selected.Status=="Draft"?selected.Id:null;Version=selected.Status=="Draft"?selected.Version:0;Name=selected.Input.Name;PropertyId=selected.Input.PropertyId;BaseRevisionId=selected.Status=="Draft"?selected.Input.BaseRevisionId:selected.Id;Fees=selected.Input.Data.Fees;Guarantees=selected.Input.Data.Guarantees.ToList();}
        else{
            var active=Revisions.FirstOrDefault(r=>r.Status=="Approved"&&r.Input.PropertyId is null&&!Revisions.Any(n=>n.Status=="Approved"&&n.Input.PropertyId is null&&n.Input.BaseRevisionId==r.Id));BaseRevisionId=active?.Id;
            if(PropertyId is {} property){var found=await properties.FindAsync(property,HttpContext.RequestAborted);if(!found.IsSuccess){Failure(found.Code);return;}Fees=found.Value!.Data.Capture?.Fees??new();Guarantees=found.Value.Data.Capture?.Selections.Where(s=>s.GroupId=="guarantees").ToList()??[];BaseRevisionId=found.Value.Data.Capture?.Policy?.RevisionId??BaseRevisionId;}
            else if(active is not null){Fees=active.Input.Data.Fees;Guarantees=active.Input.Data.Guarantees.ToList();}
        }
        Prepare();
    }
    void Prepare(){foreach(var option in CaptureCatalog.Groups.Single(g=>g.Id=="guarantees").Options)if(!Guarantees.Any(s=>s.OptionId==option.Id))Guarantees.Add(new(){GroupId="guarantees",OptionId=option.Id,Label=option.Label});}
    async Task<bool> Load(){var result=await service.ListAsync(HttpContext.RequestAborted);if(!result.IsSuccess){Failure(result.Code);return false;}Revisions=result.Value!;CanEdit=(await access.RequireAsync("commercial-policies","POLICY_EDIT",HttpContext.RequestAborted)).IsSuccess;CanApprove=(await access.RequireAsync("commercial-policies","POLICY_APPROVE",HttpContext.RequestAborted)).IsSuccess;return true;}
    void Failure(string? code){Response.StatusCode=Program.Status(code);Error=PolicyMessages.For(code);}
    public async Task<IActionResult> OnPostAsync()
    {
        Fees.Allocations=Fees.Allocations.Where(x=>!string.IsNullOrWhiteSpace(x.RoleLabel)||x.Percent is not null||x.Amount is not null).ToList();
        if(!ModelState.IsValid){Failure("Policy.InvalidInput");await Load();Prepare();return Page();}
        var result=await service.SaveDraftAsync(new(Name,Reason,new(Fees,Guarantees),PropertyId,BaseRevisionId),RevisionId,Version,HttpContext.RequestAborted);
        if(!result.IsSuccess){Failure(result.Code);await Load();Prepare();return Page();}
        return RedirectToPage(new{revision=result.Value!.Id});
    }
    public async Task<IActionResult> OnPostApproveAsync(Guid id,long expectedVersion,string approvalReason)
    {
        var result=await service.ApproveAsync(id,expectedVersion,approvalReason,HttpContext.RequestAborted);
        if(!result.IsSuccess){Failure(result.Code);await Load();Prepare();return Page();}
        return RedirectToPage(new{revision=result.Value!.Id});
    }
}
public static class PolicyMessages
{
    public static string For(string? code)=>code switch{
        "Profile.VersionConflict"=>"Esta revisão foi alterada. Reabra os dados antes de salvar ou aprovar novamente.",
        "Profile.VersionRequired"=>"Reabra a revisão para obter a versão atual antes de alterá-la.",
        "Policy.SelfApproval"=>"A aprovação deve ser feita por outro gestor, sem participação na edição desta revisão.",
        "Policy.Immutable"=>"A revisão aprovada é preservada. Crie uma nova revisão para alterá-la.",
        "Policy.ExceptionRequired"=>"Estas condições diferem da política aplicada. Proponha uma exceção e solicite aprovação gerencial.",
        "Policy.BindingRequired"=>"Preserve a referência da política aplicada ao alterar o cadastro.",
        "Policy.Unavailable" or "Policy.BaseUnavailable"=>"A revisão aprovada não está disponível para este imóvel e empresa.",
        "Policy.InvalidInput" or "Policy.PercentInvalid" or "Policy.AllocationInvalid" or "Policy.ReasonRequired"=>"Revise os valores, participantes, modalidades e a justificativa da política.",
        _=>Messages.For(code)};
}
