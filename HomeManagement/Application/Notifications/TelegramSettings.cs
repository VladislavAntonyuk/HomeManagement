namespace HomeManagement.Application.Notifications;

public class TelegramSettings
{
    public required string Token { get; init; }
    public required long ChatId { get; init; }
}
