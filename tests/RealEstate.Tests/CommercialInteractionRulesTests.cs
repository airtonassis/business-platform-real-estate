using RealEstate.Domain;
using Xunit;
namespace RealEstate.Tests;
public sealed class CommercialInteractionRulesTests
{
    [Fact]public void Presentation_is_distinct_from_interest_and_duplicate_selection_is_invalid(){
        var id=Guid.NewGuid();Assert.True(CommercialInteractionRules.Validate(new PresentationInput([id],"WhatsApp",null)).IsSuccess);
        Assert.False(CommercialInteractionRules.Validate(new PresentationInput([id,id],"WhatsApp",null)).IsSuccess);
        Assert.False(CommercialInteractionRules.Validate(new PresentationInput([],"WhatsApp",null)).IsSuccess);
    }
    [Fact]public void Negative_feedback_requires_structured_category_and_individual_reason(){
        var input=new FeedbackInput(Guid.NewGuid(),"Sem interesse","Preço e condições","Acima do orçamento",null,"WhatsApp");
        Assert.True(CommercialInteractionRules.Validate(input).IsSuccess);
        Assert.False(CommercialInteractionRules.Validate(input with{Reason=" "}).IsSuccess);
        Assert.False(CommercialInteractionRules.Validate(input with{Category="Inventada"}).IsSuccess);
        Assert.True(CommercialInteractionRules.Validate(input with{Status="Visitado",Category=null,Reason=null}).IsSuccess);
        Assert.False(CommercialInteractionRules.Validate(input with{Status="Proposta aprovada"}).IsSuccess);
    }
}
