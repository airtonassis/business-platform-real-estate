using RealEstate.Application;
using RealEstate.Domain;
namespace RealEstate.Web;
public static class NegotiationEndpoints
{
    public static void MapNegotiations(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1/negotiations").RequireAuthorization();
        api.MapGet("",async(Guid? customerId,int? page,NegotiationService service,CancellationToken ct)=>Reply(await service.ListAsync(customerId,page??1,ct)));
        api.MapGet("/{id:guid}",async(Guid id,NegotiationService service,CancellationToken ct)=>Reply(await service.FindAsync(id,ct)));
        api.MapGet("/{id:guid}/events",async(Guid id,NegotiationService service,CancellationToken ct)=>Reply(await service.EventsAsync(id,ct)));
        api.MapPost("",async(OpenNegotiation input,NegotiationService service,CancellationToken ct)=>Reply(await service.OpenAsync(input,ct)));
        api.MapPost("/{id:guid}/responsibility",async(Guid id,NegotiationTransfer input,NegotiationService service,CancellationToken ct)=>Reply(await service.TransferAsync(id,input,ct)));
        api.MapGet("/{id:guid}/actions",async(Guid id,NegotiationActionService service,CancellationToken ct)=>Reply(await service.ListAsync(id,ct)));
        api.MapGet("/{id:guid}/actions/events",async(Guid id,NegotiationActionService service,CancellationToken ct)=>Reply(await service.EventsAsync(id,ct)));
        api.MapPost("/{id:guid}/actions",async(Guid id,NegotiationActionInput input,NegotiationActionService service,CancellationToken ct)=>Reply(await service.CreateAsync(id,input,ct)));
        api.MapPost("/{id:guid}/actions/{actionId:guid}/completion",async(Guid id,Guid actionId,NegotiationActionCompletion input,NegotiationActionService service,CancellationToken ct)=>Reply(await service.CompleteAsync(id,actionId,input,ct)));
        api.MapGet("/{id:guid}/transfer-activities",async(Guid id,NegotiationActivityTransferService service,CancellationToken ct)=>Reply(await service.EligibleAsync(id,ct)));
        api.MapPost("/{id:guid}/transfer-activities/preview",async(Guid id,NegotiationActivityTransferPreviewInput input,NegotiationActivityTransferService service,CancellationToken ct)=>Reply(await service.PreviewAsync(id,input,ct)));
        api.MapPost("/{id:guid}/transfer-activities/confirm",async(Guid id,NegotiationActivityTransferConfirm? input,NegotiationActivityTransferService service,CancellationToken ct)=>Reply(await service.ConfirmAsync(id,input?.Token,ct)));
        api.MapGet("/{id:guid}/transfer-activities/followups",async(Guid id,NegotiationActivityTransferService service,CancellationToken ct)=>Reply(await service.FollowupsAsync(id,ct)));
        api.MapPost("/{id:guid}/transfer-activities/followups/{followupId:guid}/resolve",async(Guid id,Guid followupId,NegotiationActivityFollowupResolution input,NegotiationActivityTransferService service,CancellationToken ct)=>Reply(await service.ResolveAsync(id,followupId,input,ct)));
    }
    private static IResult Reply<T>(Outcome<T> result)=>result.IsSuccess?Results.Ok(result.Value):Results.Problem(
        statusCode:result.Code switch{"Negotiation.RequestConflict" or "Negotiation.OfferUnavailable" or "Negotiation.ResponsibleIneligible" or "Negotiation.ResponsibleHistoryUnavailable" or "Negotiation.SameResponsible" or "NegotiationAction.RequestConflict" or "NegotiationAction.AppointmentAlreadyLinked" or "NegotiationAction.AlreadyCompleted" or "Appointment.InvalidTransition" or "NegotiationTransfer.RequestConflict" or "NegotiationTransfer.PreviewExpired" or "NegotiationTransfer.FollowupAlreadyPending"=>409,"Negotiation.VersionConflict" or "NegotiationAction.VersionConflict" or "Appointment.VersionConflict" or "NegotiationTransfer.VersionConflict"=>412,"NegotiationTransfer.InvalidInput"=>400,_=>Program.Status(result.Code)},
        title:"Operação não concluída.",extensions:new Dictionary<string,object?>{{"code",result.Code}});
}
