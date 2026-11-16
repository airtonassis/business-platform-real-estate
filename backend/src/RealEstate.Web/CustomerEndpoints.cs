using RealEstate.Application;
using RealEstate.Domain;
namespace RealEstate.Web;
public static class CustomerEndpoints
{
    public sealed record SaveCustomer(CustomerInput Data,long Version=0,bool AcknowledgeDuplicates=false);
    public static void MapCustomers(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1/customers").RequireAuthorization();
        api.MapGet("",async(string? query,int? page,CustomerService service,CancellationToken ct)=>Reply(await service.SearchAsync(query,page??1,ct)));
        api.MapGet("/{id:guid}",async(Guid id,CustomerService service,CancellationToken ct)=>Reply(await service.FindAsync(id,ct)));
        api.MapGet("/responsibles",async(bool? editing,CustomerService service,CancellationToken ct)=>Reply(await service.CandidatesAsync(editing??false,ct)));
        api.MapPost("",async(SaveCustomer input,CustomerService service,CancellationToken ct)=>Reply(await service.SaveAsync(input.Data,acknowledgeDuplicates:input.AcknowledgeDuplicates,token:ct)));
        api.MapPut("/{id:guid}",async(Guid id,SaveCustomer input,CustomerService service,CancellationToken ct)=>Reply(await service.SaveAsync(input.Data,id,input.Version,input.AcknowledgeDuplicates,ct)));
        api.MapPost("/{id:guid}/contacts",async(Guid id,ContactInput input,CustomerService service,CancellationToken ct)=>Reply(await service.AddContactAsync(id,input,ct)));
        api.MapGet("/{id:guid}/commercial-history",async(Guid id,CommercialInteractionService service,CancellationToken ct)=>Reply(await service.CustomerHistoryAsync(id,ct)));
        api.MapPost("/{id:guid}/presentations",async(Guid id,PresentationInput input,CommercialInteractionService service,CancellationToken ct)=>Reply(await service.PresentAsync(id,input,ct)));
        api.MapPost("/{id:guid}/feedback",async(Guid id,FeedbackInput input,CommercialInteractionService service,CancellationToken ct)=>Reply(await service.FeedbackAsync(id,input,ct)));
        app.MapGet("/api/v1/properties/{id:guid}/commercial-history",async(Guid id,CommercialInteractionService service,CancellationToken ct)=>Reply(await service.PropertyHistoryAsync(id,ct))).RequireAuthorization();
        app.MapGet("/api/v1/properties/{id:guid}/commercial-summary",async(Guid id,DateTimeOffset? from,DateTimeOffset? until,CommercialInteractionService service,CancellationToken ct)=>Reply(await service.SummaryAsync(id,from??DateTimeOffset.UtcNow.AddDays(-30),until??DateTimeOffset.UtcNow,ct))).RequireAuthorization();
        // Cookie-based API mutations require the same antiforgery protection as Razor.
        api.AddEndpointFilter(async(context,next)=>{
            var http=context.HttpContext;
            if(!HttpMethods.IsGet(http.Request.Method)&&!http.Request.Headers.Authorization.ToString().StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)){
                try{await http.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>().ValidateRequestAsync(http);}
                catch(Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException){return Results.BadRequest();}
            }
            return await next(context);
        });
    }
    private static IResult Reply<T>(Outcome<T> result)=>result.IsSuccess?Results.Ok(result.Value):Results.Problem(statusCode:CustomerStatus(result.Code),title:CustomerMessages.For(result.Code),extensions:new Dictionary<string,object?>{{"code",result.Code}});
    public static int CustomerStatus(string? code)=>code switch {"Customer.VersionConflict"=>412,"Customer.PossibleDuplicate" or "Customer.DocumentUnavailable"=>409,_=>Program.Status(code)};
}
public static class CustomerMessages
{
    public static string For(string? code)=>code switch {
        "Commercial.InvalidPresentation"=>"Selecione de 1 a 25 imóveis distintos e informe o canal de apresentação.",
        "Commercial.InvalidFeedback"=>"Revise a situação, o canal e os limites do retorno informado.",
        "Commercial.ReasonRequired"=>"Para desinteresse, informe a categoria e o motivo individual.",
        "Commercial.PropertyUnavailable"=>"Um dos imóveis não está disponível nesta filial. Confira a seleção; nenhum imóvel foi vinculado.",
        "Commercial.InvalidPeriod"=>"Informe um período válido de até 366 dias.",
        "Customer.NameRequired"=>"Informe o nome do cliente (até 200 caracteres).",
        "Customer.ContactRequired"=>"Informe telefone/WhatsApp ou e-mail.",
        "Customer.PhoneInvalid"=>"Revise o telefone informado.",
        "Customer.EmailInvalid"=>"Revise o e-mail informado.",
        "Customer.DocumentInvalid"=>"Revise o CPF/CNPJ e o tipo de pessoa.",
        "Customer.ResponsibleRequired" or "Customer.ResponsibleUnavailable"=>"Selecione um responsável autorizado nesta filial.",
        "Customer.PossibleDuplicate"=>"Já existe cliente nesta filial com este telefone ou e-mail. Confira a pesquisa; se forem pessoas diferentes, confirme abaixo e salve.",
        "Customer.DocumentUnavailable"=>"Este documento não está disponível para cadastro. Solicite a verificação ao gestor.",
        "Customer.VersionConflict"=>"O cadastro mudou. Reabra a ficha antes de salvar novamente.",
        "Customer.ContactInvalid"=>"Informe o tipo de atendimento e uma descrição de até 4.000 caracteres.",
        "Customer.QueryInvalid"=>"Revise a pesquisa (até 200 caracteres) e a página.",
        "Appointment.InvalidInput" or "Appointment.InvalidRange"=>"Revise o tipo, horário, imóvel e local do compromisso.",
        "Appointment.PropertyUnavailable"=>"Selecione um imóvel disponível na filial ativa.",
        "Appointment.ResponsibleUnavailable"=>"O responsável comercial não está disponível nesta filial.",
        "Appointment.UnitInvalid"=>"O atendimento na imobiliária deve usar a unidade ativa.",
        "Appointment.ResultRequired"=>"Informe o resultado para concluir o compromisso.",
        "Appointment.VersionConflict"=>"O compromisso mudou. Reabra a ficha antes de salvar.",
        "Appointment.InvalidTransition"=>"Este compromisso já foi concluído ou cancelado.",
        "Appointment.RequestConflict"=>"O pedido de agendamento já foi usado com informações diferentes.",
        "Access.Forbidden"=>"Acesso não autorizado. Solicite ao gestor as permissões comerciais. A seleção de responsáveis depende do diretório empresarial do Core.",
        "Authentication.Required"=>"Entre com sua identidade AraIa para continuar.",
        "Resource.NotFound"=>"Cliente não encontrado nesta filial.",
        "Context.ActiveEstablishmentRequired"=>"Selecione uma filial autorizada no Portal AraIa.",
        "Dependency.Unavailable" or "Dependency.InvalidResponse"=>"O diretório AraIa está indisponível. Seus dados não foram enviados; tente novamente.",
        _=>"Revise os campos informados e tente novamente."
    };
}
