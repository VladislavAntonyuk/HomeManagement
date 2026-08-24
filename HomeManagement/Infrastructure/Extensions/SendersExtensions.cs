using HomeManagement.Application.WebHooks;
using HomeManagement.Application.WebHooks.Email;
using HomeManagement.Application.WebHooks.Telegram;

namespace HomeManagement.Infrastructure.Extensions;

public static class SendersExtensions
{
    public static void AddSenders(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<TelegramSettings>(builder.Configuration.GetSection("TelegramSettings"));
        builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
        builder.Services.AddScoped<ISender, TelegramSender>();
        builder.Services.AddScoped<ISender, EmailSender>();
        builder.Services.AddSingleton<SenderRequestFactory>();
    }
}