namespace Scheduler.Infrastructure.Persistence;

public static class AppointmentConstraints
{
    public const string BayNoOverlap = "ex_appointments_bay_no_overlap";

    public const string TechnicianNoOverlap = "ex_appointments_technician_no_overlap";

    public const string VehicleNoOverlap = "ex_appointments_vehicle_no_overlap";
}
