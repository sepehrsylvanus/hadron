namespace Hadron.Erp.Api.Modules.OrderPlanning.Application;

public interface IOrderPlanningGenerationFaultInjector
{
    Task AfterCandidateCapture(Guid generationId, CancellationToken cancellationToken);
}

public sealed class NoOpOrderPlanningGenerationFaultInjector : IOrderPlanningGenerationFaultInjector
{
    public Task AfterCandidateCapture(Guid generationId, CancellationToken cancellationToken) => Task.CompletedTask;
}
