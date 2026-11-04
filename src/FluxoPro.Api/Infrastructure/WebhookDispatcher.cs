using FluxoPro.Api.Infrastructure;

namespace FluxoPro.Api.Infrastructure;

public sealed class WebhookDispatcher(QuoteRepository repository, IHttpClientFactory clients, IConfiguration config, ILogger<WebhookDispatcher> log) : BackgroundService
{
    private readonly string _owner = Guid.NewGuid().ToString("N");

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await repository.ExpireStale();
                var secret = config["FLUXOPRO_N8N_WEBHOOK_KEY"];
                if (Uri.TryCreate(config["FLUXOPRO_N8N_WEBHOOK_URL"], UriKind.Absolute, out var url) && !string.IsNullOrWhiteSpace(secret))
                    foreach (var id in await repository.LeaseDue(_owner, 20))
                    {
                        try
                        {
                            using var req = new HttpRequestMessage(HttpMethod.Post, url);
                            req.Headers.Add("X-Webhook-Key", secret);
                            req.Content = System.Net.Http.Json.JsonContent.Create(new { id, eventId = id });
                            using var response = await clients.CreateClient("webhook").SendAsync(req, stop);
                            await repository.DispatchResult(id, _owner, response.IsSuccessStatusCode, response.IsSuccessStatusCode ? null : $"Webhook HTTP {(int)response.StatusCode}");
                        }
                        catch (Exception ex) when (!stop.IsCancellationRequested)
                        {
                            log.LogWarning("Webhook falhou para {Id}: {Type}", id, ex.GetType().Name);
                            await repository.DispatchResult(id, _owner, false, ex.GetType().Name);
                        }
                    }
            }
            catch (Exception ex) when (!stop.IsCancellationRequested)
            {
                log.LogError("Dispatcher interrompido nesta rodada: {Type}", ex.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(10), stop);
        }
    }
}
