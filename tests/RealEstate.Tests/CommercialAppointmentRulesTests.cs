using RealEstate.Domain;
using Xunit;
namespace RealEstate.Tests;
public sealed class CommercialAppointmentRulesTests
{
    private static AppointmentInput Input(string kind,Guid? property=null)=>new(Guid.NewGuid(),kind,property,DateTimeOffset.UtcNow.AddDays(1),DateTimeOffset.UtcNow.AddDays(1).AddHours(1),"Local de teste",null);
    [Theory]
    [InlineData(AppointmentRules.PropertyVisit,true)]
    [InlineData(AppointmentRules.Office,false)]
    [InlineData(AppointmentRules.Capture,false)]
    public void Validates_three_appointment_types(string kind,bool needsProperty)
    {
        var input=Input(kind,needsProperty?Guid.NewGuid():null);
        Assert.True(AppointmentRules.Validate(input,DateTimeOffset.UtcNow).IsSuccess);
        if(needsProperty)Assert.False(AppointmentRules.Validate(input with{PropertyId=null},DateTimeOffset.UtcNow).IsSuccess);
    }
    [Fact]public void Rejects_past_invalid_duration_and_reused_office_property()
    {
        var input=Input(AppointmentRules.Office);
        Assert.False(AppointmentRules.Validate(input with{StartsAt=DateTimeOffset.UtcNow.AddMinutes(-1)},DateTimeOffset.UtcNow).IsSuccess);
        Assert.False(AppointmentRules.Validate(input with{EndsAt=input.StartsAt},DateTimeOffset.UtcNow).IsSuccess);
        Assert.False(AppointmentRules.Validate(input with{PropertyId=Guid.NewGuid()},DateTimeOffset.UtcNow).IsSuccess);
    }
    [Fact]public void Only_scheduled_appointments_can_change()=>Assert.True(AppointmentRules.CanTransition("Agendado","Reagendar")&&AppointmentRules.CanTransition("Agendado","Cancelar")&&AppointmentRules.CanTransition("Agendado","Realizar")&&!AppointmentRules.CanTransition("Cancelado","Realizar")&&!AppointmentRules.CanTransition("Realizado","Cancelar"));
}
