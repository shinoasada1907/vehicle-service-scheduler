using System.Diagnostics;
using System.Diagnostics.Metrics;
using Scheduler.Application;

namespace Scheduler.UnitTests.Telemetry;

internal sealed class TelemetryCapture : IDisposable
{
    private readonly MeterListener meterListener = new();
    private readonly ActivityListener activityListener;
    private readonly List<Measurement> measurements = [];
    private readonly List<Activity> activities = [];

    public TelemetryCapture()
    {
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == SchedulerTelemetry.Name)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            lock (measurements)
            {
                measurements.Add(new Measurement(
                    instrument.Name,
                    value,
                    tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString())));
            }
        });
        meterListener.Start();

        activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SchedulerTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                lock (activities)
                {
                    activities.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(activityListener);
    }

    public IReadOnlyList<Measurement> Measurements(string instrumentName)
    {
        lock (measurements)
        {
            return measurements.Where(measurement => measurement.Instrument == instrumentName).ToList();
        }
    }

    public IReadOnlyList<Activity> Activities(string operationName)
    {
        lock (activities)
        {
            return activities.Where(activity => activity.OperationName == operationName).ToList();
        }
    }

    public void Dispose()
    {
        meterListener.Dispose();
        activityListener.Dispose();
    }

    internal sealed record Measurement(string Instrument, long Value, IReadOnlyDictionary<string, string?> Tags);
}
