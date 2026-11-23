using Notification.Api;
using ShopFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("notification");
builder.Services.AddSingleton<IEmailSender, EmailSender>();
builder.Services.AddSingleton<NotificationFeed>();
builder.Services.AddMessaging(builder.Configuration, x =>
{
    x.AddConsumer<OrderConfirmedConsumer>();
    x.AddConsumer<OrderCancelledConsumer>();
});

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/api/notifications", (HttpContext http, NotificationFeed feed) => feed.For(http.User.UserId()))
    .WithTags("Notifications").RequireAuthorization();
app.Run();

public partial class Program;
