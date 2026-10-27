using HomeManagement.Application.Workflows;

namespace HomeManagement.Infrastructure.Extensions;

public static class WorkflowExtensions
{
    public static void AddWorkflow(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<WorkflowAutomationOptions>(builder.Configuration.GetSection("WorkflowAutomation"));
        builder.Services.AddHostedService<WorkflowTriggerService>();
        builder.Services.AddSingleton<IWorkflowRunner, WorkflowRunner>();
        builder.Services.AddSingleton<IWorkflowTriggerPreviewService, WorkflowTriggerPreviewService>();
    }
}