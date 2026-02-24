namespace AspireSample.Api.Models;

public record Order(int Id, string Customer, string CreditCard, decimal Total);

public record PaymentRequest(int OrderId, string CardNumber, string Cvv, decimal Amount);

public record ShippingRequest(int OrderId, string Address, string City, string Country, double WeightKg);

public record AuditEntry(string Action, string UserId, DateTimeOffset Timestamp);

public record OrderResult(int OrderId, string Status, string? TrackingNumber);
