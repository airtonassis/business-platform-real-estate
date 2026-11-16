using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RealEstate.Application;
using RealEstate.Domain;
namespace RealEstate.Web.Pages;
public sealed class IndexModel(PropertyService service,PropertyListingService listings,ICoreAccess access,PropertyMediaService media):PageModel
{
    public bool CanCreate {get;private set;}
    public bool CanCreateOwner {get;private set;}
    public bool CanEdit {get;private set;}
    public Guid? ActiveEstablishmentId{get;private set;}
    public IReadOnlyList<ListingAnnouncement> ActiveAnnouncements(Guid property)=>Workspace?.Announcements?.GetValueOrDefault(property)??[];
    public IEnumerable<(string Purpose,OperationValue Value,Guid? AnnouncementId)> WalletValues(PropertyV2 property)
    {
        var announcements=ActiveAnnouncements(property.Id);
        foreach(var operation in CommercialValues.Active(property.Data)){
            var announcement=announcements.FirstOrDefault(a=>a.Purpose==operation.Label);
            yield return (operation.Label,announcement is {LegacyAnchor:false}?announcement.Values??new():operation.Value,announcement?.Id);
        }
        foreach(var announcement in announcements.Where(a=>!CommercialValues.Active(property.Data).Any(o=>o.Label==a.Purpose)))
            yield return (announcement.Purpose,announcement.Values??new(),announcement.Id);
    }
    public bool CanDuplicate(PropertyV2 p)=>CanCreate&&ActiveEstablishmentId is not null&&p.Data.EstablishmentId==ActiveEstablishmentId&&p.Data.Purpose is not null&&ActiveAnnouncements(p.Id).Select(a=>a.Purpose).Distinct().Count()<2;
    public PropertyListPage? Workspace {get;private set;}
    public IReadOnlyDictionary<Guid,Guid> Covers {get;private set;}=new Dictionary<Guid,Guid>();
    [BindProperty(SupportsGet=true)] public PropertyListQuery Filter {get;set;}=new();
    public string PageHref(int page)=>Link("Filter.Page",page.ToString());
    public string SortHref(string field)=>Link("Filter.Sort",field,"Filter.Descending",(!(Filter.Sort==field&&Filter.Descending)).ToString(),"Filter.Page","1");
    public string SortState(string field)=>Filter.Sort==field?(Filter.Descending?"descending":"ascending"):"none";
    private string Link(params string[] changes){var values=Request.Query.ToDictionary(x=>x.Key,x=>(string?)x.Value.ToString(),StringComparer.OrdinalIgnoreCase);for(var i=0;i<changes.Length;i+=2)values[changes[i]]=changes[i+1];return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/",values);}
    public string? Error {get;private set;}
    [TempData] public string? Notice {get;set;}
    [BindProperty] public string OwnerName {get;set;}="";
    public async Task OnGetAsync()=>await Load();
    private async Task Load()
    {
        var result=await listings.ListAsync(Filter,HttpContext.RequestAborted);
        if(result.IsSuccess && ModelState.Any(x=>x.Key.StartsWith("Filter",StringComparison.Ordinal)&&x.Value?.Errors.Count>0)){Error="Revise os filtros informados.";Response.StatusCode=400;return;}
        if(result.IsSuccess){Workspace=result.Value;Covers=await media.CoversAsync(Workspace!.Properties.Select(p=>p.Id).ToArray(),HttpContext.RequestAborted);var create=await access.RequireAsync("properties","PROPERTY_CREATE",HttpContext.RequestAborted);CanCreate=create.IsSuccess;ActiveEstablishmentId=create.Value?.ActiveEstablishmentId;CanEdit=(await access.RequireAsync("properties","PROPERTY_UPDATE",HttpContext.RequestAborted)).IsSuccess;CanCreateOwner=(await access.RequireAsync("owners","OWNER_CREATE",HttpContext.RequestAborted)).IsSuccess;}
        else {Error=Messages.For(result.Code);Response.StatusCode=Program.Status(result.Code);}
    }
    public async Task<IActionResult> OnPostOwnerAsync()
    {
        var result=await service.CreateOwnerAsync(OwnerName,HttpContext.RequestAborted);
        if(result.IsSuccess){Notice="Proprietário cadastrado.";return RedirectToPage();}
        await Load();Error=Messages.For(result.Code);Response.StatusCode=Program.Status(result.Code);return Page();
    }
}
public static class Messages
{
    public static string For(string? code)=>code switch {
        "Capture.UnitRequired"=>"Informe a unidade das áreas e medidas não negativas.",
        "Capture.RegionalUnitRequired"=>"Informe a variante regional do alqueire.",
        "Capture.SuitesInvalid"=>"As suítes estão incluídas no total de dormitórios. Revise as quantidades.",
        "Capture.GuaranteeConflict"=>"Sem garantia não pode ser combinada com outras modalidades.",
        "Capture.ChoiceInvalid"=>"Revise as opções, detalhes e duplicatas nos grupos de características.",
        "Capture.MoneyInvalid"=>"Informe valores em reais, não negativos e com até duas casas decimais.",
        "Capture.DateInvalid"=>"O fim da exclusividade deve ser igual ou posterior ao início.",
        "Capture.PhotoUnavailable"=>"Uma mídia vinculada não está disponível neste imóvel. Reabra o cadastro.",
        "Capture.DescriptionTooLong"=>"Use até 200 caracteres na descrição da imagem. Os dados anteriores foram preservados.",
        "Capture.Invalid" or "Capture.CountInvalid" or "Capture.UnitInvalid" or "Capture.PeriodInvalid" or "Capture.FurnishingInvalid" or "Capture.TextTooLong" or "Capture.TooManyChoices" or "Capture.MediaInvalid"=>"Revise os valores e os limites dos campos do cadastro.",
        "Authentication.Required"=>"Entre com sua identidade AraIa para acessar os imóveis.",
        "Listing.InvalidQuery"=>"Revise os filtros, a página e o campo de ordenação informados.",
        "Access.Forbidden"=>"Acesso não autorizado. Verifique com o gestor a licença e as permissões para imóveis.",
        "Context.OrganizationRequired"=>"Conclua seu v\u00ednculo empresarial na plataforma AraIa antes de cadastrar im\u00f3veis.",
        "Context.Invalid"=>"N\u00e3o foi poss\u00edvel confirmar o contexto empresarial. Retorne \u00e0 plataforma e confira a empresa e a filial selecionadas.",
        "Context.ActiveEstablishmentRequired"=>"Selecione uma filial na plataforma AraIa e abra novamente o ERP.",
        "Dependency.InvalidResponse"=>"N\u00e3o foi poss\u00edvel receber uma resposta v\u00e1lida da plataforma. Tente novamente ou informe o suporte.",
        "Dependency.Unavailable"=>"Não foi possível validar seu acesso agora. Tente novamente em instantes.",
        "Resource.NotFound"=>"Imóvel indisponível para sua empresa.",
        "Property.OwnerRequired" or "Property.OwnerUnavailable"=>"Selecione um proprietário ativo cadastrado nesta empresa.",
        "Property.TitleRequired"=>"Informe um título com até 200 caracteres.",
        "Property.PriceInvalid"=>"Informe um preço maior que zero, com até duas casas decimais.",
        "Property.PostalCodeInvalid"=>"Informe um CEP com oito dígitos.",
        "Property.LocationInvalid"=>"Informe latitude entre −90 e 90 e longitude entre −180 e 180.",
        "Property.TypeInvalid"=>"Selecione um tipo de imóvel válido.",
        "Announcement.PurposeImmutable"=>"A finalidade deste anúncio é permanente. Use a opção de duplicar para outra finalidade.",
        "Announcement.Closed"=>"Este anúncio está encerrado. Seus dados comerciais permanecem no histórico.",
        "Property.PurposeInvalid"=>"Selecione Venda ou Locação para um novo anúncio. Finalidades históricas são preservadas.",
        "Property.DuplicateCode"=>"Este código já está cadastrado nesta empresa.",
        "Property.AddressVersionRequired"=>"Este endereço parcial requer a versão 2 do cadastro.",
        "Address.NumberConflict"=>"Informe o número ou selecione S/N, sem preencher ambos.",
        "Address.MunicipalityCodeInvalid"=>"Informe o código IBGE municipal com sete dígitos ou deixe vazio.",
        "Address.StateInvalid"=>"Selecione uma UF válida ou deixe não informada.",
        "Address.CountryInvalid"=>"Informe a sigla do país com duas letras ou deixe vazio.",
        "Address.LegacyTextInvalid"=>"O texto livre deve ter até 500 caracteres; texto legado não é separado automaticamente.",
        "Address.TextTooLong"=>"Revise os limites dos campos de endereço.",
        "Property.StatusConflict"=>"Esta situação do imóvel não permite a operação.",
        "Property.ReviewIncomplete"=>"Complete os dados obrigatórios e o endereço antes de enviar para revisão.",
        "Property.VersionConflict"=>"O imóvel foi alterado por outra operação. Reabra o cadastro antes de salvar.",
        "Owner.NameRequired"=>"Informe o nome do proprietário com até 200 caracteres.",
        _=>"Revise os campos obrigatórios e tente novamente."};
}
