using Microsoft.AspNetCore.Mvc;

namespace BuildWorkflow.Controllers;

[ApiController]
[Route("api/test/tools")]
public class TestToolsController : ControllerBase
{
    public sealed class PostSampleRequest
    {
        public string? Input { get; set; }
    }

    [HttpGet("get-sample")]
    public IActionResult GetSample(string? input = null)
    {
        return Ok(new
        {
            tool = "get-sample",
            received = input ?? string.Empty,
            sample = new[]
            {
                new { id = 1, name = "First sample" },
                new { id = 2, name = "Second sample" }
            }
        });
    }

    [HttpPost("post-sample")]
    public IActionResult PostSample([FromBody] PostSampleRequest request)
    {
        return Ok(new
        {
            tool = "post-sample",
            received = request.Input ?? string.Empty,
            accepted = true,
            sample = new { status = "ok", timestamp = DateTimeOffset.UtcNow }
        });
    }
}
