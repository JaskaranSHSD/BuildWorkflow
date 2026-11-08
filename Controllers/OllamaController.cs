using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OllamaSharp;

namespace BuildWorkflow.Controllers;

[Authorize(Roles = "Admin,OrgAdmin,PlatformAdmin")]
public class OllamaController : Controller
{
    private readonly IConfiguration _configuration;

    public OllamaController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private string Endpoint
    {
        get
        {
            var saved = HttpContext.Session.GetString("OllamaEndpoint");
            if (!string.IsNullOrWhiteSpace(saved))
            {
                return saved;
            }
            return _configuration["Ollama:Endpoint"] ?? "http://localhost:11434";
        }
    }

    public async Task<IActionResult> Index(string? endpoint, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            HttpContext.Session.SetString("OllamaEndpoint", endpoint.Trim());
            return RedirectToAction(nameof(Index));
        }

        var client = new OllamaApiClient(new Uri(Endpoint));
        var models = await client.ListLocalModelsAsync(cancellationToken);
        ViewBag.Endpoint = Endpoint;
        return View(models);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pull(string modelName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            TempData["OllamaError"] = "Model name is required.";
            return RedirectToAction(nameof(Index));
        }

        var client = new OllamaApiClient(new Uri(Endpoint));
        await foreach (var _ in client.PullModelAsync(modelName.Trim(), cancellationToken))
        {
        }
        TempData["OllamaMessage"] = $"Pulled {modelName.Trim()}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string modelName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return RedirectToAction(nameof(Index));
        }

        var client = new OllamaApiClient(new Uri(Endpoint));
        await client.DeleteModelAsync(modelName.Trim(), cancellationToken);
        TempData["OllamaMessage"] = $"Deleted {modelName.Trim()}.";
        return RedirectToAction(nameof(Index));
    }
}