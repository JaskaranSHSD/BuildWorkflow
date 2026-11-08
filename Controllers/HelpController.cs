using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildWorkflow.Controllers;

[AllowAnonymous]
public class HelpController : Controller
{
    public IActionResult Index() => View();
}
