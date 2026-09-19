using HomeManagement.Application.WebHooks;
using System.Threading.Channels;

namespace HomeManagement.Infrastructure.IpCameras;

public sealed class FrigateAlertsWorker(
    Channel<string> channel,
    SenderRequestFactory senderRequestFactory,
    IServiceProvider serviceProvider,
    ILogger<FrigateAlertsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var alert in channel.Reader.ReadAllAsync(stoppingToken))
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var senders = scope.ServiceProvider.GetServices<ISender>();

            foreach (var sender in senders)
            {
                var request = senderRequestFactory.Create(alert, sender);
                if (request is null)
                {
                    continue;
                }

                var sendResult = await sender.Send(request, stoppingToken);
                if (!sendResult.IsSuccessful)
                {
                    logger.LogError(sendResult.Error);
                }
            }
        }
    }
}