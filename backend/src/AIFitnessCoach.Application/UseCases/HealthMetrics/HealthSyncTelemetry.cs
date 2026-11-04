using System.Diagnostics.Metrics;

namespace AIFitnessCoach.Application.UseCases.HealthMetrics;

public static class HealthSyncTelemetry
{
    private static readonly Meter Meter = new("AIFitnessCoach.HealthSync");
    private static readonly Counter<long> Batches = Meter.CreateCounter<long>("health_sync_batches");
    private static readonly Counter<long> MetricsReceived = Meter.CreateCounter<long>("health_sync_metrics_received");
    private static readonly Counter<long> MetricsInserted = Meter.CreateCounter<long>("health_sync_metrics_inserted");
    private static readonly Counter<long> MetricsUpdated = Meter.CreateCounter<long>("health_sync_metrics_updated");
    private static readonly Counter<long> MetricsDeduplicated = Meter.CreateCounter<long>("health_sync_metrics_deduplicated");
    private static readonly Counter<long> MetricsDeleted = Meter.CreateCounter<long>("health_sync_metrics_deleted");
    private static readonly Counter<long> Errors = Meter.CreateCounter<long>("health_sync_errors");
    private static readonly Counter<long> Retries = Meter.CreateCounter<long>("health_sync_retries");
    private static readonly Counter<long> MetricsRejected = Meter.CreateCounter<long>("health_sync_metrics_rejected");

    public static void RecordSuccess(int received, HealthMetricBatchResult result, int deleted)
    {
        Batches.Add(1);
        MetricsReceived.Add(received);
        MetricsInserted.Add(result.InsertedCount);
        MetricsUpdated.Add(result.UpdatedCount);
        MetricsDeduplicated.Add(result.DuplicateCount);
        MetricsDeleted.Add(deleted);
    }

    public static void RecordError() => Errors.Add(1);
    public static void RecordRetry() => Retries.Add(1);
    public static void RecordRejected(int count) => MetricsRejected.Add(count);
}
