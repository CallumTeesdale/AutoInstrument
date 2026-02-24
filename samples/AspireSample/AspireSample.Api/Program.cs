using System.Diagnostics;
using AutoInstrument;
using AspireSample.Api.Models;
using AspireSample.Api.Services;
using AspireSample.ServiceDefaults;
using Scalar.AspNetCore;

[assembly: AutoInstrumentSource("AspireSample.Api")]

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddOpenApi();

builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<ShippingService>();
builder.Services.AddScoped<AuditService>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapOpenApi();
app.MapScalarApiReference();

app.MapPost("/orders", async (Order order, OrderService orders, CancellationToken ct) =>
{
    var result = await orders.PlaceOrder(order, ct);
    return Results.Ok(result);
});

app.MapGet("/orders/{id}/status", async (int id, OrderService orders) =>
{
    var status = await orders.GetOrderStatus(id, "internal-secret-token");
    return Results.Ok(new { id, status });
});

app.MapPost("/orders/{id}/fulfill", async (int id, OrderService orders, CancellationToken ct) =>
{
    var result = await orders.FulfillOrder(id, ct);
    return Results.Ok(result);
});

app.MapPost("/orders/{id}/critical-sync", async (int id, OrderService orders, CancellationToken ct) =>
{
    await orders.CriticalSync(id, ct);
    return Results.Ok("Synced");
});

app.MapPost("/auth", async (OrderService orders) =>
{
    var ok = await orders.AuthenticateCustomer("user@example.com", "P@ssw0rd!", "123456");
    return Results.Ok(new { authenticated = ok });
});

app.MapPost("/validate-payment", async (PaymentRequest req, OrderService orders) =>
{
    var ok = await orders.ValidatePayment(req);
    return Results.Ok(new { valid = ok });
});

app.MapPost("/orders/{id}/linked", async (int id, OrderService orders) =>
{
    var parentContext = Activity.Current?.Context ?? default;
    await orders.ProcessLinkedOrder(id, parentContext);
    return Results.Ok("Linked processing complete");
});

app.MapGet("/orders/{id}/validate", async (int id, OrderService orders) =>
{
    var ok = await orders.CallExternalValidator(id, "payload");
    return Results.Ok(new { valid = ok });
});

app.MapGet("/orders/{id}/quick-check", (int id, OrderService orders) =>
{
    orders.QuickCheck(id);
    return Results.Ok("OK");
});

app.MapGet("/tax", (decimal subtotal, string region) =>
{
    var tax = OrderService.ComputeTax(subtotal, region);
    return Results.Ok(new { subtotal, region, tax });
});

app.MapPost("/orders/{id}/refund", async (int id, decimal amount, PaymentService payments) =>
{
    var refundId = await payments.RefundOrder(id, amount);
    return Results.Ok(new { refundId });
});

app.MapPost("/kitchen-sink", async (OrderService orders, PaymentService payments,
    ShippingService shipping, AuditService audit, CancellationToken ct) =>
{
    var results = new List<string>();

    var order = new Order(42, "Alice", "4111-1111-1111-1111", 99.99m);
    var placed = await orders.PlaceOrder(order, ct);
    results.Add($"Placed: {placed.Status}");

    orders.VerboseTracing = true;
    orders.LogOrderMetrics(42, 3, 89.99m);
    results.Add("Metrics logged (verbose=true)");

    orders.VerboseTracing = false;
    orders.LogOrderMetrics(42, 3, 89.99m);
    results.Add("Metrics logged (verbose=false, no span)");

    var authed = await orders.AuthenticateCustomer("alice@shop.com", "s3cret!", "654321");
    results.Add($"Authenticated: {authed}");

    var payReq = new PaymentRequest(42, "4111-1111-1111-1111", "123", 99.99m);
    var valid = await orders.ValidatePayment(payReq);
    results.Add($"Payment valid: {valid}");

    var fulfilled = await orders.FulfillOrder(42, ct);
    results.Add($"Fulfilled: {fulfilled.TrackingNumber}");

    var status = await orders.GetOrderStatus(42, "secret-token-not-tagged");
    results.Add($"Status: {status}");

    await orders.CallExternalValidator(42, "check-fraud");
    results.Add("External validation passed");

    orders.QuickCheck(42);
    results.Add("Quick check OK");

    var tax = OrderService.ComputeTax(99.99m, "us-west-2");
    results.Add($"Tax: {tax}");

    var parentCtx = Activity.Current?.Context ?? default;
    await orders.ProcessLinkedOrder(42, parentCtx);
    results.Add("Linked order processed");

    var refund = await payments.RefundOrder(42, 25.00m);
    results.Add($"Refund: {refund}");

    shipping.CalculateRoute("SEA", "LAX");
    results.Add("Route calculated");

    await audit.RecordAction(new AuditEntry("KitchenSink", "system", DateTimeOffset.UtcNow));
    var lastAction = audit.GetLastAction("system");
    results.Add($"Audit: {lastAction}");

    return Results.Ok(new { steps = results });
});

app.Run();
