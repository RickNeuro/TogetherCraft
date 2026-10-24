using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TogetherCraft.Services;

namespace TogetherCraft.Controllers;

[ApiController]
[Route("api/metrics")]
[Authorize(Policy = "ApprovedUser")]
public sealed class ApiMetricsController : ControllerBase
{
    private readonly PrometheusMetricsService _metrics;
    private readonly ILogger<ApiMetricsController> _logger;

    public ApiMetricsController(PrometheusMetricsService metrics, ILogger<ApiMetricsController> logger)
    {
        _metrics = metrics;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _metrics.ReadAsync(cancellationToken));
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Prometheus is temporarily unavailable.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                sourceAvailable = false,
                message = "Источник метрик временно недоступен. Данные обновятся автоматически."
            });
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                sourceAvailable = false,
                message = "Источник метрик не ответил вовремя. Повторяем подключение."
            });
        }
    }
}
