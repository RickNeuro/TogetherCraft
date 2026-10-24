using System.Globalization;
using System.Text.Json;

namespace TogetherCraft.Services;

public sealed record SystemMetricsSnapshot(
    bool SourceAvailable,
    double? CpuPercent,
    double? MemoryUsedBytes,
    double? MemoryTotalBytes,
    double? MemoryPercent,
    double? DiskUsedBytes,
    double? DiskTotalBytes,
    double? DiskPercent,
    double? CpuTemperatureCelsius,
    double? Fan1Rpm,
    double? Fan3Rpm,
    DateTimeOffset UpdatedAtUtc,
    string Message);

public sealed class PrometheusMetricsService
{
    private readonly HttpClient _httpClient;

    public PrometheusMetricsService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<SystemMetricsSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var nodeUp = await QueryAsync("up{job=\"node\"}", cancellationToken);
        if (nodeUp is null || nodeUp < 1)
        {
            return Empty("Источник метрик Ubuntu пока недоступен.");
        }

        var cpuTask = QueryAsync("100 * (1 - avg(rate(node_cpu_seconds_total{mode=\"idle\"}[1m])))", cancellationToken);
        var memoryTotalTask = QueryAsync("node_memory_MemTotal_bytes", cancellationToken);
        var memoryAvailableTask = QueryAsync("node_memory_MemAvailable_bytes", cancellationToken);
        var diskTotalTask = QueryAsync("node_filesystem_size_bytes{mountpoint=\"/\",fstype!~\"rootfs|tmpfs|overlay|squashfs\"}", cancellationToken);
        var diskAvailableTask = QueryAsync("node_filesystem_avail_bytes{mountpoint=\"/\",fstype!~\"rootfs|tmpfs|overlay|squashfs\"}", cancellationToken);
        // k10temp is exposed by Node Exporter with a PCI address in the chip label,
        // not with the literal text "k10temp". Restrict this query to the PCI CPU sensor.
        var temperatureTask = QueryAsync("node_hwmon_temp_celsius{chip=~\"pci.*\",sensor=\"temp1\"}", cancellationToken);
        var fan1Task = QueryAsync("node_hwmon_fan_rpm{sensor=\"fan1\"}", cancellationToken);
        var fan3Task = QueryAsync("node_hwmon_fan_rpm{sensor=\"fan3\"}", cancellationToken);

        await Task.WhenAll(cpuTask, memoryTotalTask, memoryAvailableTask, diskTotalTask, diskAvailableTask, temperatureTask, fan1Task, fan3Task);

        var cpu = ClampPercent(cpuTask.Result);
        var memoryTotal = memoryTotalTask.Result;
        var memoryAvailable = memoryAvailableTask.Result;
        var diskTotal = diskTotalTask.Result;
        var diskAvailable = diskAvailableTask.Result;
        var memoryUsed = Difference(memoryTotal, memoryAvailable);
        var diskUsed = Difference(diskTotal, diskAvailable);

        var hasMissingSensor = temperatureTask.Result is null;
        var hasMissingFans = fan1Task.Result is null && fan3Task.Result is null;
        var message = hasMissingSensor || hasMissingFans
            ? "Системные метрики подключены; один или несколько аппаратных датчиков недоступны."
            : "Системные метрики Ubuntu подключены.";

        return new SystemMetricsSnapshot(
            SourceAvailable: true,
            CpuPercent: cpu,
            MemoryUsedBytes: memoryUsed,
            MemoryTotalBytes: memoryTotal,
            MemoryPercent: Percent(memoryUsed, memoryTotal),
            DiskUsedBytes: diskUsed,
            DiskTotalBytes: diskTotal,
            DiskPercent: Percent(diskUsed, diskTotal),
            CpuTemperatureCelsius: temperatureTask.Result,
            Fan1Rpm: fan1Task.Result,
            Fan3Rpm: fan3Task.Result,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Message: message);
    }

    private async Task<double?> QueryAsync(string query, CancellationToken cancellationToken)
    {
        var url = "/api/v1/query?query=" + Uri.EscapeDataString(query);
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;

        if (!root.TryGetProperty("status", out var status) || status.GetString() != "success")
            return null;
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("result", out var results))
            return null;

        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("value", out var value) || value.GetArrayLength() < 2)
                continue;

            var text = value[1].GetString();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return double.IsFinite(number) ? number : null;
        }

        return null;
    }

    private static SystemMetricsSnapshot Empty(string message) => new(
        SourceAvailable: false,
        CpuPercent: null,
        MemoryUsedBytes: null,
        MemoryTotalBytes: null,
        MemoryPercent: null,
        DiskUsedBytes: null,
        DiskTotalBytes: null,
        DiskPercent: null,
        CpuTemperatureCelsius: null,
        Fan1Rpm: null,
        Fan3Rpm: null,
        UpdatedAtUtc: DateTimeOffset.UtcNow,
        Message: message);

    private static double? Difference(double? total, double? available) =>
        total is not null && available is not null ? Math.Max(0, total.Value - available.Value) : null;

    private static double? Percent(double? used, double? total) =>
        used is not null && total is > 0 ? ClampPercent(used.Value / total.Value * 100) : null;

    private static double? ClampPercent(double? value) =>
        value is null ? null : Math.Clamp(value.Value, 0, 100);
}
