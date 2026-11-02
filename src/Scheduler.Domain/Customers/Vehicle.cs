namespace Scheduler.Domain.Customers;

public sealed class Vehicle(Guid id, Guid customerId, string vin, string make, string model, int year)
{
    public Guid Id { get; private set; } = id;

    public Guid CustomerId { get; private set; } = customerId;

    public string Vin { get; private set; } = vin;

    public string Make { get; private set; } = make;

    public string Model { get; private set; } = model;

    public int Year { get; private set; } = year;
}
