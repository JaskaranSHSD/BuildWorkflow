using System;

namespace BuildWorkflow.Models;

[Flags]
public enum UserModulePermission : long
{
    None = 0,
    Chat = 1L << 0,
    WorkflowsRead = 1L << 1,
    WorkflowsWrite = 1L << 2,
    AiModelsRead = 1L << 3,
    AiModelsWrite = 1L << 4,
    ToolsRead = 1L << 5,
    ToolsWrite = 1L << 6,
    RagRead = 1L << 7,
    RagWrite = 1L << 8,
    All = (1L << 9) - 1
}

public static class UserModulePermissionExtensions
{
    public static bool HasModulePermission(this AppUser? user, UserModulePermission permission)
    {
        if (user == null) return false;
        if (user.IsAdmin) return true;
        // No permission bits granted means no access.
        if (user.Permissions == 0) return false;

        var granted = (UserModulePermission)user.Permissions;
        // Write implies read for the same module.
        if (permission == UserModulePermission.WorkflowsRead) return granted.HasFlag(UserModulePermission.WorkflowsRead) || granted.HasFlag(UserModulePermission.WorkflowsWrite);
        if (permission == UserModulePermission.AiModelsRead) return granted.HasFlag(UserModulePermission.AiModelsRead) || granted.HasFlag(UserModulePermission.AiModelsWrite);
        if (permission == UserModulePermission.ToolsRead) return granted.HasFlag(UserModulePermission.ToolsRead) || granted.HasFlag(UserModulePermission.ToolsWrite);
        if (permission == UserModulePermission.RagRead) return granted.HasFlag(UserModulePermission.RagRead) || granted.HasFlag(UserModulePermission.RagWrite);
        return granted.HasFlag(permission);
    }
}