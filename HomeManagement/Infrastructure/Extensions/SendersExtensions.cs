using HomeManagement.Application.Notifications;

namespace HomeManagement.Infrastructure.Extensions;

public static class SendersExtensions
{
    public static void AddSenders(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<TelegramSettings>(builder.Configuration.GetSection("TelegramSettings"));
        builder.Services.AddSingleton<INotificationSender, TelegramNotificationSender>();
    }
}
