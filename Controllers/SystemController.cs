using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildWorkflow.Controllers;

[Authorize(Roles = "PlatformAdmin")]
public class SystemController : Controller
{
    private readonly IWebHostEnvironment _environment;

    public SystemController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CleanTempFiles()
    {
        var root = Path.Combine(_environment.ContentRootPath, "TempFilesLocal");
        if (Directory.Exists(root))
        {
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                try { System.IO.File.Delete(file); } catch { }
            }
            foreach (var directory in Directory.GetDirectories(root))
            {
                try { Directory.Delete(directory, true); } catch { }
            }
        }

        TempData["Message"] = "Temporary files cleaned.";
        return RedirectToAction(nameof(Index));
    }
}
