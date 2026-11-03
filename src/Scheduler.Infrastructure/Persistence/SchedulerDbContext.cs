using System.Text;
using Microsoft.EntityFrameworkCore;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Customers;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Resources;

namespace Scheduler.Infrastructure.Persistence;

public sealed class SchedulerDbContext(DbContextOptions<SchedulerDbContext> options) : DbContext(options)
{
    public DbSet<Dealership> Dealerships => Set<Dealership>();

    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();

    public DbSet<ServiceBay> ServiceBays => Set<ServiceBay>();

    public DbSet<Technician> Technicians => Set<Technician>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.Entity<Dealership>(entity =>
        {
            entity.ToTable("dealerships");
            entity.Property(dealership => dealership.Name).HasMaxLength(200);
            entity.Property(dealership => dealership.TimeZoneId).HasMaxLength(64);
            entity.OwnsMany(dealership => dealership.OpeningHours, hours =>
            {
                hours.ToTable("opening_hours");
                hours.WithOwner().HasForeignKey("DealershipId");
                hours.HasKey("DealershipId", nameof(OpeningHours.Day));
            });
        });

        modelBuilder.Entity<ServiceType>(entity =>
        {
            entity.ToTable("service_types");
            entity.Property(serviceType => serviceType.Name).HasMaxLength(200);
            entity.Property(serviceType => serviceType.RequiredBayType).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<ServiceBay>(entity =>
        {
            entity.ToTable("service_bays");
            entity.Property(bay => bay.Name).HasMaxLength(200);
            entity.Property(bay => bay.BayType).HasConversion<string>().HasMaxLength(32);
            entity.HasOne<Dealership>().WithMany().HasForeignKey(bay => bay.DealershipId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Technician>(entity =>
        {
            entity.ToTable("technicians");
            entity.Property(technician => technician.Name).HasMaxLength(200);
            entity.HasOne<Dealership>().WithMany().HasForeignKey(technician => technician.DealershipId).OnDelete(DeleteBehavior.Restrict);
            entity.OwnsMany(technician => technician.Qualifications, qualifications =>
            {
                qualifications.ToTable("technician_qualifications");
                qualifications.WithOwner().HasForeignKey("TechnicianId");
                qualifications.HasKey("TechnicianId", nameof(Qualification.ServiceTypeId));
                qualifications.HasOne<ServiceType>().WithMany().HasForeignKey(qualification => qualification.ServiceTypeId).OnDelete(DeleteBehavior.Restrict);
            });
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("customers");
            entity.Property(customer => customer.Name).HasMaxLength(200);
            entity.Property(customer => customer.Email).HasMaxLength(320);
        });

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.ToTable("vehicles");
            entity.Property(vehicle => vehicle.Vin).HasMaxLength(17);
            entity.Property(vehicle => vehicle.Make).HasMaxLength(100);
            entity.Property(vehicle => vehicle.Model).HasMaxLength(100);
            entity.HasIndex(vehicle => vehicle.Vin).IsUnique();
            entity.HasOne<Customer>().WithMany().HasForeignKey(vehicle => vehicle.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.ToTable("appointments", table =>
            {
                table.HasCheckConstraint("ck_appointments_end_after_start", "end_at > start_at");
                table.HasCheckConstraint("ck_appointments_cancelled_at", "(status = 'Cancelled') = (cancelled_at IS NOT NULL)");
            });
            entity.Property(appointment => appointment.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(appointment => new { appointment.DealershipId, appointment.StartAt });
            entity.HasOne<Dealership>().WithMany().HasForeignKey(appointment => appointment.DealershipId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Customer>().WithMany().HasForeignKey(appointment => appointment.CustomerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(appointment => appointment.VehicleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ServiceType>().WithMany().HasForeignKey(appointment => appointment.ServiceTypeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ServiceBay>().WithMany().HasForeignKey(appointment => appointment.ServiceBayId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Technician>().WithMany().HasForeignKey(appointment => appointment.TechnicianId).OnDelete(DeleteBehavior.Restrict);
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);

        for (var index = 0; index < name.Length; index++)
        {
            if (index > 0 && char.IsUpper(name[index]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(name[index]));
        }

        return builder.ToString();
    }
}
