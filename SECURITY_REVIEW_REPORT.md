# Security Review Report - BuildWorkflow

**Date:** 2026-11-08  
**Branch:** master  
**Reviewer:** Claude Code Security Review

---

## Executive Summary

This application has **critical security vulnerabilities** that must be addressed before any production deployment. The most severe issues are:

1. **Plaintext password storage** - No password hashing whatsoever
2. **Hardcoded default credentials** - "admin"/"admin" and "platformadmin"/"platformadmin"
3. **Insecure RSA key management** - New key generated on every startup, breaking encrypted login
4. **Weak MFA for PlatformAdmin** - Predictable 4-digit code
5. **SQL injection risks** - Minimal query validation
6. **Credential exposure** - API keys, connection strings, SFTP passwords stored in plaintext

---

## Findings by Severity

### 🔴 CRITICAL

| ID | Issue | Location | Impact |
|---|---|---|---|
| CRIT-01 | **Plaintext Password Storage** | `Models/AppUser.cs:13`, `Controllers/AccountController.cs:63` | All user passwords stored in plaintext. If database is compromised, all credentials are immediately exposed. |
| CRIT-02 | **Hardcoded Default Credentials** | `Program.cs:242-261` | Default admin accounts created with passwords "admin" and "platformadmin" - trivial to guess. |
| CRIT-03 | **RSA Key Rotation on Every Startup** | `Services/RsaKeyService.cs:7` | New 2048-bit RSA key generated each app start. Previously encrypted login credentials become undecryptable. Login fails after restart. |
| CRIT-04 | **Predictable PlatformAdmin MFA** | `Services/MfaHelper.cs` (not shown but referenced), `Views/Admin/ManageUser.cshtml:40` | MFA code is "year + day of month" (e.g., 202615 for Nov 15, 2026). Trivial to guess. |
| CRIT-05 | **No Password Hashing** | `Controllers/AccountController.cs:63, 180, 198, 299, 328` | Direct string comparison: `user.Password != password`. No BCrypt, Argon2, or PBKDF2. |

### 🟠 HIGH

| ID | Issue | Location | Impact |
|---|---|---|---|
| HIGH-01 | **SQL Injection Risk** | `AICenter/Helpers/SqlDatabaseReader.cs:75-165` | Only checks query starts with SELECT/WITH and no semicolons. Bypassable via comments, UNION, subqueries, etc. |
| HIGH-02 | **Secrets in Plaintext** | `AdminController.cs:193-233`, `Models/ApiToolConfiguration.cs`, `Models/SqlToolConfiguration.cs`, `Models/SftpToolConfiguration.cs` | API keys, Bearer tokens, DB connection strings, SFTP passwords stored unencrypted in database. |
| HIGH-03 | **Missing Secure Cookie Flags** | `Program.cs:28-34` | Cookie authentication missing `SecurePolicy = CookieSecurePolicy.Always`, `SameSite = SameSiteMode.Strict`. |
| HIGH-04 | **Path Traversal in File Operations** | `AICenter/Tools/DatabaseToolExecutor.cs:254-268, 348-347, 709-776` | `ExecuteTextFileAsync`, `ReadAttachedFileAsync`, `ExecuteSftpAsync` accept arbitrary paths. |
| HIGH-05 | **No Rate Limiting on Auth** | `Controllers/AccountController.cs:38-130` | Login endpoint has no brute-force protection beyond simple lockout (5 attempts = 15 min). No IP-based rate limiting. |
| HIGH-06 | **Overly Permissive CORS/Hosts** | `appsettings.json:8` | `"AllowedHosts": "*"` - allows any host header. |

### 🟡 MEDIUM

| ID | Issue | Location | Impact |
|---|---|---|---|
| MED-01 | **Long Session Timeout** | `Program.cs:22` | 8-hour idle timeout is excessive for sensitive applications. |
| MED-02 | **Missing Security Headers** | `Program.cs` | No CSP, HSTS (only in prod), X-Frame-Options, Referrer-Policy, X-Content-Type-Options. |
| MED-03 | **Information Disclosure in Errors** | Multiple controllers | Detailed exception messages returned to users (e.g., `ex.Message` in catch blocks). |
| MED-04 | **Inadequate Authorization Checks** | `AdminController.cs:1205-1225` | `CanAccessOrganization` returns `true` when `organizationId` is null or current org not found - potential bypass. |
| MED-05 | **WorkflowManager Tool Exposure** | `AICenter/Tools/DatabaseToolExecutor.cs:142-148, 841-971` | AI can create/update/delete workflows via function calls - excessive privilege for LLM. |
| MED-06 | **MFA Secret Displayed in Plaintext** | `Views/Admin/ManageUser.cshtml:45` | TOTP secret shown in admin UI - shoulder surfing risk. |
| MED-07 | **Weak Default File Permissions** | `FileUploadController.cs:39-42` | Default allowed extensions include `.zip` which can contain malicious payloads. |
| MED-08 | **No Audit Logging for Sensitive Operations** | Multiple | User creation, role changes, permission changes, tool creation with credentials not audited. |

### 🟢 LOW

| ID | Issue | Location | Impact |
|---|---|---|---|
| LOW-01 | **Debug/Development Endpoints Exposed** | `Controllers/TestToolsController.cs` | Test endpoints accessible in production. |
| LOW-02 | **Mixed Content (HTTP for Local Services)** | `appsettings.json:19,24` | Ollama and Qdrant use HTTP - should use HTTPS in production. |
| LOW-03 | **QR Code Library from CDN** | `Views/Admin/ManageUser.cshtml:48` | External dependency for MFA QR code - supply chain risk. |
| LOW-04 | **Missing Input Length Limits** | Multiple views | Some inputs lack maxlength attributes (though added via JS in _Layout.cshtml). |

---

## Detailed Remediation Plan

### Phase 1: Critical Fixes (Do First)

#### 1. Implement Password Hashing (CRIT-01, CRIT-05)

**File:** `Models/AppUser.cs`
```csharp
// Add BCrypt.Net-Next package
// Change Password property to store hash only
public string PasswordHash { get; set; } = string.Empty;

// Remove Password property or mark [NotMapped]
```

**File:** `Services/PasswordHasher.cs` (new)
```csharp
using BCrypt.Net;

public static class PasswordHasher
{
    public static string Hash(string password) => BCrypt.HashPassword(password, BCrypt.GenerateSalt(12));
    public static bool Verify(string password, string hash) => BCrypt.Verify(password, hash);
}
```

**File:** `Controllers/AccountController.cs` - Update all password comparisons to use `PasswordHasher.Verify()`

#### 2. Fix RSA Key Persistence (CRIT-03)

**File:** `Services/RsaKeyService.cs`
```csharp
// Persist keys to disk or key vault
// Load existing key on startup, generate only if missing
```

#### 3. Remove Hardcoded Default Credentials (CRIT-02)

**File:** `Program.cs:242-261`
- Remove automatic admin user creation
- Require manual secure setup on first run
- Or generate secure random password and display once

#### 4. Fix PlatformAdmin MFA (CRIT-04)

**File:** `Services/MfaHelper.cs` - Implement proper TOTP for all users including PlatformAdmin

#### 5. Strengthen SQL Query Validation (HIGH-01)

**File:** `AICenter/Helpers/SqlDatabaseReader.cs`
- Use parameterized queries only
- Reject any query with comments, UNION, subqueries, function calls
- Consider using a SQL parser for validation

### Phase 2: High Priority Fixes

#### 6. Encrypt Stored Secrets (HIGH-02)

**New Service:** `Services/SecretEncryptionService.cs`
- Use AES-256-GCM with per-organization keys
- Encrypt: API credentials, connection strings, SFTP passwords
- Decrypt only at point of use

#### 7. Secure Cookie Configuration (HIGH-03)

**File:** `Program.cs:28-34`
```csharp
.AddCookie(options =>
{
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.HttpOnly = true;
    options.SlidingExpiration = true;
});
```

#### 8. Fix Path Traversal (HIGH-04)

**File:** `AICenter/Tools/DatabaseToolExecutor.cs`
- Validate all paths against allowed directories
- Use `Path.GetFullPath` and ensure it's within allowed root
- Reject any path with `..` or symlinks

#### 9. Add Rate Limiting (HIGH-05)

**File:** `Program.cs` - Add ASP.NET Core Rate Limiting middleware
```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", o => {
        o.PermitLimit = 5;
        o.Window = TimeSpan.FromMinutes(15);
    });
});
```

#### 10. Fix AllowedHosts (HIGH-06)

**File:** `appsettings.json:8`
```json
"AllowedHosts": "yourdomain.com,localhost"
```

### Phase 3: Medium Priority Fixes

#### 11. Add Security Headers (MED-02)

**File:** `Program.cs` - Add middleware:
```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline' cdn.jsdelivr.net; style-src 'self' 'unsafe-inline' fonts.googleapis.com cdn.jsdelivr.net; font-src 'self' fonts.gstatic.com; img-src 'self' data:; connect-src 'self';";
    await next();
});
```

#### 12. Reduce Session Timeout (MED-01)

**File:** `Program.cs:22`
```csharp
options.IdleTimeout = TimeSpan.FromMinutes(30); // or 60
```

#### 13. Sanitize Error Messages (MED-03)

Replace `ex.Message` with generic messages in all catch blocks exposed to users.

#### 14. Fix Authorization Bypass (MED-04)

**File:** `AdminController.cs:1205-1225`
```csharp
private bool CanAccessOrganization(Guid? organizationId)
{
    if (IsPlatformAdmin()) return true;
    if (!organizationId.HasValue) return false; // Deny by default
    var currentOrg = CurrentOrganizationId();
    return currentOrg.HasValue && currentOrg.Value == organizationId;
}
```

#### 15. Restrict WorkflowManager Tool (MED-05)

**File:** `AICenter/Tools/DatabaseToolExecutor.cs:841-971`
- Remove or heavily restrict this tool
- If needed, only allow read operations
- Require explicit admin approval for write operations

#### 16. Hide MFA Secret in UI (MED-06)

**File:** `Views/Admin/ManageUser.cshtml:45`
- Show only QR code, not the secret
- Add "Show Secret" button with confirmation

#### 17. Remove .zip from Default Extensions (MED-07)

**File:** `FileUploadController.cs:39-42`
```csharp
var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt", ".png", ".jpg", ".jpeg", ".gif"
    // Remove .zip
};
```

#### 18. Add Audit Logging (MED-08)

Create `Services/AuditService.cs` to log:
- User creation/modification/deletion
- Role/permission changes
- Tool creation with credentials
- Login successes/failures
- MFA changes

### Phase 4: Low Priority / Hardening

#### 19. Remove Test Endpoints (LOW-01)

**File:** `Controllers/TestToolsController.cs` - Remove or guard with `#if DEBUG`

#### 20. Enforce HTTPS for Local Services (LOW-02)

Document production requirement for HTTPS endpoints.

#### 21. Self-Host QR Code Library (LOW-03)

Download `qrcode.min.js` locally instead of CDN.

---

## Files Requiring Immediate Changes

| Priority | File | Changes Needed |
|---|---|---|
| 🔴 CRITICAL | `Models/AppUser.cs` | Add PasswordHash, remove Password |
| 🔴 CRITICAL | `Services/PasswordHasher.cs` | New file - BCrypt implementation |
| 🔴 CRITICAL | `Controllers/AccountController.cs` | Use PasswordHasher everywhere |
| 🔴 CRITICAL | `Program.cs` | Remove default users, fix cookie config |
| 🔴 CRITICAL | `Services/RsaKeyService.cs` | Persist keys to disk/key vault |
| 🔴 CRITICAL | `Services/MfaHelper.cs` | Proper TOTP for PlatformAdmin |
| 🟠 HIGH | `AICenter/Helpers/SqlDatabaseReader.cs` | Stronger SQL validation |
| 🟠 HIGH | `Services/SecretEncryptionService.cs` | New file - encrypt stored secrets |
| 🟠 HIGH | `AICenter/Tools/DatabaseToolExecutor.cs` | Path validation, restrict WorkflowManager |
| 🟠 HIGH | `FileUploadController.cs` | Remove .zip, add path validation |
| 🟢 MED | `Program.cs` | Security headers, session timeout, rate limiting |
| 🟢 MED | `AdminController.cs` | Fix CanAccessOrganization logic |
| 🟢 MED | `Views/Admin/ManageUser.cshtml` | Hide MFA secret |
| 🟢 MED | `Services/AuditService.cs` | New file - audit logging |

---

## Testing Checklist After Fixes

- [ ] Login works with hashed passwords
- [ ] New user registration requires strong password
- [ ] Password change verifies current password hash
- [ ] RSA keys persist across restarts
- [ ] Encrypted login works after restart
- [ ] PlatformAdmin MFA uses TOTP
- [ ] SQL injection attempts blocked
- [ ] Stored credentials are encrypted in DB
- [ ] Cookies have Secure, HttpOnly, SameSite=Strict
- [ ] Rate limiting active on /Account/Login
- [ ] Path traversal attempts blocked
- [ ] Session timeout reduced
- [ ] Security headers present
- [ ] Error messages don't leak stack traces
- [ ] Authorization properly isolates organizations
- [ ] WorkflowManager tool removed/restricted
- [ ] MFA secret not displayed in plaintext
- [ ] Audit log captures sensitive operations

---

## Recommended NuGet Packages

```xml
<PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />
<PackageReference Include="Microsoft.AspNetCore.RateLimiting" Version="9.0.0" />
<PackageReference Include="System.Security.Cryptography.ProtectedData" Version="9.0.0" /> <!-- For key storage -->
```

---

## Compliance Notes

This application handles authentication, authorization, and potentially sensitive data. After fixes, consider:
- **OWASP Top 10** coverage
- **GDPR** - if processing EU personal data
- **SOC 2** - for audit logging requirements
- **PCI DSS** - if handling payment data (not currently apparent)

---

*This report was generated by automated security review. Manual verification of all fixes is required before production deployment.*