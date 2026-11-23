using System.Collections.Concurrent;
using System.Net.Mail;
using MassTransit;
using ShopFlow.Contracts;

namespace Notification.Api;

public record Notice(Guid Id, string BuyerId, string Email, string Subject, string Body, DateTime SentAt);

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

/// <summary>Sends through SMTP when Smtp:Host is set (e.g. MailHog in docker compose), otherwise just logs.</summary>
public class EmailSender(IConfiguration config, ILogger<EmailSender> log) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        string? host = config["Smtp:Host"];
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(to))
        {
            log.LogInformation("Email to {To}: {Subject}", to, subject);
            return;
        }
        using var client = new SmtpClient(host, config.GetValue("Smtp:Port", 1025));
        await client.SendMailAsync(new MailMessage("orders@shopflow.dev", to, subject, body), ct);
    }
}

/// <summary>Keeps recent notifications so the storefront can show them.</summary>
public class NotificationFeed
{
    private readonly ConcurrentQueue<Notice> _items = new();

    public void Add(Notice n)
    {
        _items.Enqueue(n);
        while (_items.Count > 1000 && _items.TryDequeue(out _)) { }
    }

    public IEnumerable<Notice> For(string buyerId) => _items.Where(n => n.BuyerId == buyerId).OrderByDescending(n => n.SentAt);
}

public class OrderConfirmedConsumer(IEmailSender email, NotificationFeed feed) : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> ctx)
    {
        var m = ctx.Message;
        string subject = $"Order {m.OrderId.ToString()[..8]} confirmed";
        string body = $"Thanks for your order! We charged {m.Total:0.00} (transaction {m.TransactionId}).";
        await email.SendAsync(m.BuyerEmail, subject, body, ctx.CancellationToken);
        feed.Add(new Notice(Guid.NewGuid(), m.BuyerId, m.BuyerEmail, subject, body, DateTime.UtcNow));
    }
}

public class OrderCancelledConsumer(IEmailSender email, NotificationFeed feed) : IConsumer<OrderCancelled>
{
    public async Task Consume(ConsumeContext<OrderCancelled> ctx)
    {
        var m = ctx.Message;
        string subject = $"Order {m.OrderId.ToString()[..8]} could not be completed";
        string body = $"Sorry, your order was cancelled: {m.Reason}. You have not been charged.";
        await email.SendAsync(m.BuyerEmail, subject, body, ctx.CancellationToken);
        feed.Add(new Notice(Guid.NewGuid(), m.BuyerId, m.BuyerEmail, subject, body, DateTime.UtcNow));
    }
}
