using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Azure.Data.Tables;
using System.Text.Json;

public class SaveResult
{
    private readonly ILogger _log;

    public SaveResult(ILoggerFactory loggerFactory)
    {
        _log = loggerFactory.CreateLogger<SaveResult>();
    }

    [Function("SaveResult")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = null)] HttpRequestData req)
    {
        // Manejar preflight CORS
        if (req.Method.ToUpper() == "OPTIONS")
        {
            var preflight = req.CreateResponse(HttpStatusCode.OK);
            preflight.Headers.Add("Access-Control-Allow-Origin", "*");
            preflight.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
            preflight.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
            return preflight;
        }

        // Leer body
        string body = await new StreamReader(req.Body).ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(body))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Body vacío.");
            return bad;
        }

        // Validar JSON
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("JSON inválido.");
            return bad;
        }

        // Guardar en Azure Table Storage
        string connStr = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "UseDevelopmentStorage=true";
        var tableClient = new TableClient(connStr, "resultados");
        await tableClient.CreateIfNotExistsAsync();

        var root = doc.RootElement;
        string lang      = root.TryGetProperty("lang",  out var l) ? l.GetString() ?? "N/A" : "N/A";
        string tool      = root.TryGetProperty("tool",  out var t) ? t.GetString() ?? "adaptativa" : "adaptativa";
        string date      = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string rowKey    = Guid.NewGuid().ToString();

        var entity = new TableEntity(date, rowKey)
        {
            ["lang"]      = lang,
            ["tool"]      = tool,
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
            ["payload"]   = body
        };

        await tableClient.AddEntityAsync(entity);
        _log.LogInformation("Guardado: {RowKey} | lang={Lang} | tool={Tool}", rowKey, lang, tool);

        // Respuesta OK con CORS
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(new { saved = true, id = rowKey }));
        return response;
    }
}
