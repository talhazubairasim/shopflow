using Payment.Api;
using ShopFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("payment");
builder.Services.AddSingleton(builder.Configuration.GetSection("Payment").Get<PaymentOptions>() ?? new PaymentOptions());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PaymentGateway>();
builder.Services.AddMessaging(builder.Configuration, x => x.AddConsumer<ProcessPaymentConsumer>());

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/api/payments", (PaymentGateway g) => g.All).WithTags("Payments").RequireAuthorization(Extensions.AdminPolicy);
app.Run();

public partial class Program;
