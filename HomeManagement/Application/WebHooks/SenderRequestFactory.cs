using HomeManagement.Application.WebHooks.Email;
using HomeManagement.Application.WebHooks.Telegram;

namespace HomeManagement.Application.WebHooks;

public class SenderRequestFactory
{
    private static readonly Dictionary<Type, Func<string, ISenderContent>> ContentFactoryBySenderType = new()
    {
        [typeof(TelegramSender)] = message => new TelegramSenderContent
        {
            Message = message
        },
        [typeof(EmailSender)] = message => new EmailSenderContent
        {
            Message = message
        }
    };

    public SenderRequest? Create(string message, ISender service)
    {
        if (!ContentFactoryBySenderType.TryGetValue(service.GetType(), out var createContent))
        {
            return null;
        }

        return new SenderRequest
        {
            Content = createContent(message)
        };
    }
}