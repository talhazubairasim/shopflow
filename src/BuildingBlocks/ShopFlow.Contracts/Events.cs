namespace ShopFlow.Contracts;

// Integration events and commands exchanged over RabbitMQ (MassTransit).
// They form the checkout saga:
//
//   Basket ──BasketCheckedOut──▶ Ordering (saga starts)
//   Ordering ──ReserveStock──▶ Catalog ──StockReserved / StockReservationFailed──▶ Ordering
//   Ordering ──ProcessPayment──▶ Payment ──PaymentSucceeded / PaymentFailed──▶ Ordering
//   Ordering ──OrderConfirmed / OrderCancelled──▶ Notification, Catalog (release stock on cancel)

public record OrderLine(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

public record ShippingAddress(string FullName, string Street, string City, string Country, string PostalCode);

public record BasketCheckedOut(
    Guid OrderId,
    string BuyerId,
    string BuyerEmail,
    IReadOnlyList<OrderLine> Lines,
    ShippingAddress Address,
    string CardNumber,
    DateTime CheckedOutAt)
{
    public decimal Total => Lines.Sum(l => l.LineTotal);
}

public record ReserveStock(Guid OrderId, IReadOnlyList<OrderLine> Lines);
public record StockReserved(Guid OrderId);
public record StockReservationFailed(Guid OrderId, string Reason);
public record ReleaseStock(Guid OrderId, IReadOnlyList<OrderLine> Lines);

public record ProcessPayment(Guid OrderId, string BuyerId, decimal Amount, string CardNumber);
public record PaymentSucceeded(Guid OrderId, string TransactionId);
public record PaymentFailed(Guid OrderId, string Reason);

public record OrderConfirmed(Guid OrderId, string BuyerId, string BuyerEmail, decimal Total, string TransactionId);
public record OrderCancelled(Guid OrderId, string BuyerId, string BuyerEmail, string Reason);
