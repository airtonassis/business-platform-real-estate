using RealEstate.Domain;
using Xunit;

namespace RealEstate.Tests;

public sealed class NegotiationActionRulesTests
{
    private static NegotiationActionInput Input(string kind, DateTimeOffset? due = null, string description = "Ligar para confirmar condições") =>
        new(Guid.NewGuid(), kind, description, due, null);

    [Theory]
    [InlineData("Contato")]
    [InlineData("Pendência")]
    public void Contact_and_pending_require_a_due_date(string kind)
    {
        Assert.Equal("NegotiationAction.DueRequired", NegotiationActionRules.Validate(Input(kind)).Code);
    }

    [Theory]
    [InlineData("Contato")]
    [InlineData("Compromisso")]
    [InlineData("Pendência")]
    [InlineData("Outros")]
    public void Every_action_requires_type_and_description(string kind)
    {
        Assert.Equal("NegotiationAction.DescriptionRequired", NegotiationActionRules.Validate(Input(kind, DateTimeOffset.UtcNow, "  ")).Code);
    }

    [Fact]
    public void Unknown_type_is_rejected()
    {
        Assert.Equal("NegotiationAction.InvalidType", NegotiationActionRules.Validate(Input("Etapa automática")).Code);
    }

    [Fact]
    public void Other_requires_a_clarifying_description()
    {
        Assert.Equal("NegotiationAction.OtherDescriptionRequired", NegotiationActionRules.Validate(Input("Outros", description: "Outro")).Code);
    }

    [Fact]
    public void Commitment_is_temporally_defined_by_the_agenda_not_a_generic_due_date()
    {
        var input = Input("Compromisso", DateTimeOffset.UtcNow.AddDays(1));
        Assert.Equal("NegotiationAction.AppointmentFieldsRequired", NegotiationActionRules.Validate(input).Code);
    }

    [Fact]
    public void Completion_requires_a_brief_result()
    {
        Assert.Equal("NegotiationAction.ResultRequired", NegotiationActionRules.ValidateCompletion(new(Guid.NewGuid(), 1, " ")).Code);
        Assert.True(NegotiationActionRules.ValidateCompletion(new(Guid.NewGuid(), 1, "Cliente retornará amanhã")).IsSuccess);
    }
}
