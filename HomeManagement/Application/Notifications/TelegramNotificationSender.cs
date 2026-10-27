using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace HomeManagement.Application.Notifications;

public class TelegramNotificationSender(IOptions<TelegramSettings> options) : INotificationSender
{
    public async Task<HasErrorResult> SendAsync(string message, CancellationToken token)
    {
        try
        {
            var client = new TelegramBotClient(options.Value.Token);
            await client.SendMessage(new ChatId(options.Value.ChatId), message, cancellationToken: token);
            return new HasErrorResult();
        }
        catch (Exception e)
        {
            return new HasErrorResult { Error = e.Message };
        }
    }
}
