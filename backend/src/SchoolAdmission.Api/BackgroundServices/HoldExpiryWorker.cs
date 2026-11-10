using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.BackgroundServices;

/// <summary>
/// Returns expired seats on its own: every second any HELD row whose deadline passed is
/// flipped to EXPIRED. No API call from a client is needed for a seat to become available
/// again. (Reads are also expiry-aware, so seats are already free even between sweeps.)
/// </summary>
public sealed class HoldExpiryWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HoldExpiryWorker> _logger;

    public HoldExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<HoldExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<AdmissionService>();
                var expired = await service.ExpireOverdueHoldsAsync(stoppingToken);

                if (expired > 0)
                {
                    _logger.LogInformation("Released {Count} expired seat hold(s).", expired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The database may still be starting up; keep the loop alive.
                _logger.LogWarning(ex, "Could not sweep expired holds; will retry in {Interval}.", Interval);
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
