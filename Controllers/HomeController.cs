using BuildWorkflow.Data;
using BuildWorkflow.Models;
using BuildWorkflow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Security.Claims;

namespace BuildWorkflow.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;
        public HomeController(AppDbContext db) { _db = db; }

        public async Task<IActionResult> Index()
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var isAdmin = User.IsInRole("Admin") || User.IsInRole("OrgAdmin") || User.IsInRole("PlatformAdmin");
            var dbUser = await _db.Users.FindAsync(userId);

            bool canChat = dbUser?.HasModulePermission(UserModulePermission.Chat) == true;
            bool canWorkflowRead = isAdmin || (dbUser?.HasModulePermission(UserModulePermission.WorkflowsRead) == true);
            bool canWorkflowWrite = isAdmin || (dbUser?.HasModulePermission(UserModulePermission.WorkflowsWrite) == true);

            if (canChat)
            {
                ViewBag.RecentChats = await _db.ChatSessions
                    .Where(s => s.UserId == userId)
                    .OrderByDescending(s => s.UpdatedAt)
                    .Take(5)
                    .ToListAsync();
            }
            else
            {
                ViewBag.RecentChats = new List<ChatSession>();
            }

            if (canWorkflowRead)
            {
                var wfQuery = _db.Workflows.Where(w => w.IsActive && (w.UserId == userId || _db.WorkflowShares.Any(s => s.WorkflowId == w.Id && s.UserId == userId)));
                ViewBag.RecentWorkflows = await wfQuery.OrderBy(w => w.UpdatedAt).Take(5).ToListAsync();

                var now = DateTimeOffset.UtcNow;
                var schedQuery = from s in _db.WorkflowSchedules
                                join w in _db.Workflows on s.WorkflowId equals w.Id
                                where s.Enabled && s.NextRunAt.HasValue && s.NextRunAt.Value >= now
                                      && w.IsActive
                                      && (w.UserId == userId || _db.WorkflowShares.Any(sh => sh.WorkflowId == w.Id && sh.UserId == userId))
                                select new { s, w };
                ViewBag.UpcomingSchedules = await schedQuery
                    .OrderBy(x => x.s.NextRunAt)
                    .Take(5)
                    .Select(x => new WorkflowScheduleView { Schedule = x.s, WorkflowName = x.w.Name })
                    .ToListAsync();
            }
            else
            {
                ViewBag.RecentWorkflows = new List<WorkflowDefinition>();
                ViewBag.UpcomingSchedules = new List<WorkflowScheduleView>();
            }

            ViewBag.CanChat = canChat;
            ViewBag.CanWorkflowWrite = canWorkflowWrite;
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    public class WorkflowScheduleView
    {
        public WorkflowSchedule Schedule { get; set; } = default!;
        public string WorkflowName { get; set; } = string.Empty;
    }
}
