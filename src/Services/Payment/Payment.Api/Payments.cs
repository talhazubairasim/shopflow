using System.Collections.Concurrent;
using MassTransit;
using ShopFlow.Contracts;

namespace Payment.Api;

public record PaymentRecord(Guid OrderId, string BuyerId, decimal Amount, string CardLast4, bool Succeeded, string? TransactionId, string? FailureReason, DateTime ProcessedAt);

public class PaymentOptions
{
    /// <summary>Charges above this amount are declined (simulates a card limit).</summary>
    public decimal CardLimit { get; set; } = 5000m;
    /// <summary>Test card that is always declined, like the providers' sandbox cards.</summary>
    public string DeclinedTestCard { get; set; } = "4000000000000002";
}

/// <summary>
/// A simulated payment gateway. Results are stored per order, so a redelivered
/// ProcessPayment message never charges twice (idempotency).
/// </summary>
public class PaymentGateway(PaymentOptions options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<Guid, PaymentRecord> _payments = new();

    public IReadOnlyCollection<PaymentRecord> All => _payments.Values.OrderByDescending(p => p.ProcessedAt).ToList();

    public PaymentRecord Charge(ProcessPayment cmd) => _payments.GetOrAdd(cmd.OrderId, _ =>
    {
        string last4 = cmd.CardNumber.Length >= 4 ? cmd.CardNumber[^4..] : cmd.CardNumber;
        string? failure =
            !LuhnValid(cmd.CardNumber) ? "Card number is invalid" :
            cmd.CardNumber == options.DeclinedTestCard ? "Card declined by issuer" :
            cmd.Amount <= 0 ? "Amount must be positive" :
            cmd.Amount > options.CardLimit ? $"Amount exceeds card limit of {options.CardLimit:0.00}" :
            null;
        return new PaymentRecord(cmd.OrderId, cmd.BuyerId, cmd.Amount, last4, failure == null,
            failure == null ? $"txn_{Guid.NewGuid():N}"[..20] : null, failure, clock.GetUtcNow().UtcDateTime);
    });

    /// <summary>The Luhn checksum every real card number satisfies.</summary>
    public static bool LuhnValid(string number)
    {
        if (string.IsNullOrEmpty(number) || !number.All(char.IsDigit)) return false;
        int sum = 0;
        bool dbl = false;
        for (int i = number.Length - 1; i >= 0; i--)
        {
            int d = number[i] - '0';
            if (dbl) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
            dbl = !dbl;
        }
        return sum % 10 == 0;
    }
}

public class ProcessPaymentConsumer(PaymentGateway gateway, ILogger<ProcessPaymentConsumer> log) : IConsumer<ProcessPayment>
{
    public async Task Consume(ConsumeContext<ProcessPayment> ctx)
    {
        var result = gateway.Charge(ctx.Message);
        if (result.Succeeded)
        {
            log.LogInformation("Charged {Amount} for order {OrderId}", result.Amount, result.OrderId);
            await ctx.Publish(new PaymentSucceeded(result.OrderId, result.TransactionId!));
        }
        else
        {
            log.LogWarning("Payment failed for order {OrderId}: {Reason}", result.OrderId, result.FailureReason);
            await ctx.Publish(new PaymentFailed(result.OrderId, result.FailureReason!));
        }
    }
}
