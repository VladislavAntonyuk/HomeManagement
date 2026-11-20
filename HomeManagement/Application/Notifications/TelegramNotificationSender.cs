using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace HomeManagement.Application.Notifications;

/// <summary>
/// Sends notifications to a Telegram chat. The bot client is created once, so its HTTP connections are reused.
/// </summary>
public class TelegramNotificationSender(IOptions<TelegramSettings> options) : INotificationSender
{
    private readonly Lazy<TelegramBotClient> _client = new(() => new TelegramBotClient(options.Value.Token));

    public async Task<OperationResult> SendAsync(string message, CancellationToken token)
    {
        try
        {
            await _client.Value.SendMessage(new ChatId(options.Value.ChatId), message, cancellationToken: token);
            return OperationResult.Success("Sent");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return OperationResult.Failure(e.Message);
        }
    }
}
