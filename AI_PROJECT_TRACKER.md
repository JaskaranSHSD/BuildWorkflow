# Project Tracker: BuildWorkflow

This document is the AI-facing source of truth for the `BuildWorkflow` .NET web project. It is updated as the project evolves.

## 1. Overview

- **Project name:** BuildWorkflow
- **Type:** ASP.NET Core MVC web application
- **Framework:** .NET 10 (`net10.0`)
- **Purpose:** Authenticated AI chat workspace with admin management for models, tools, RAG knowledge buckets, knowledge documents, users, assignments, file uploads, chat history, workflows, scheduling, and sharing.
- **Status:** Feature-rich local application with chat, admin management, RAG, attachments, history, workflow automation, scheduling, sharing, and extended tooling.
- **Current layout:** App-wide fixed left sidebar with navigation; top bar shows signed-in user/profile/logout. Admin and other pages use full-width containers. Chat is a fixed three-column shell: chat history, conversation, and a right-hand selection sidebar.

## 1.5 Recent Changes

- Chat: Removed assigned-knowledge dropdown from composer; knowledge selection now lives only in the right-hand sidebar.
- Chat: Generated export files now always include their downloadable `file:<guid>` token in chat replies, even if the model omits it; fallback to DB query makes this robust.
- Workflows: Export tokens are similarly guaranteed and scoped to each workflow run.
- UI: Outer chat center box now provides the main bordered container; nested chat-log box styling was removed.
- Chat composer: Text, file, and send button row now appears as one bordered box.
- Chat log: Bottom corners are square while the outer container retains rounded top corners.
- Added SQL Single Query / Procedure tool type for running one predefined read-only SQL statement or stored procedure.
- Added Word `.docx` read support (chat attachments and RAG uploads) and Word `.docx` export support using Open XML.
- RAG scalability: configurable `Rag:MaxChunksToScan`, bounded parallel chunk embeddings, reusable embedding HTTP clients, RAG indexes, and cascading cleanup of chunks/documents.
- Pagination: admin models/tools/users/RAG buckets, chat sessions, and workflow runs are latest-first with configurable page sizes.
- Chat sessions are limited by `Chat:MaxMessagesPerSession` (default 50); over-limit sessions ask the user to start a new chat.
- Auth/security: password complexity rules, optional forced password change, soft-delete users, multi-factor authentication (platform fixed 4-digit code; other users authenticator-app TOTP with admin-shared QR codes).
- Branding: organization display name can override the topbar/login brand, with BuildWorkflow fallback.
- Layout: sticky topbar, single logo, sidebar under topbar.
- Organization/user file policy: allowed extensions and max upload size per organization, user-level subsets, backend and client-side enforcement, upload controls disabled when no types are allowed.
- Chat selection UI: group buttons with selected/total counts and modal panels for tools/knowledge.
- Workflow management: nested steps, schedules, run history with 50-record pagination and step-aware execution-log modal, clear-history action.
- All major listing screens now use server-side pagination and name/description search.
- System (platform-admin only) page for temporary-file cleanup.
- Global JavaScript alerts replaced with Bootstrap modal alerts.

## 2. Technology Stack

| Layer | Technology | Notes |
| --- | --- | --- |
| Runtime | .NET 10 | `Microsoft.NET.Sdk.Web` |
| Web | ASP.NET Core MVC | Controllers/views plus API endpoints |
| Agent framework | Microsoft.Agents.AI | `ChatClientAgent`, connector abstraction |
| Local LLM | Ollama | Runtime on `http://localhost:11434`; default model `beam:cloud` |
| Ollama .NET client | OllamaSharp | Implements `IChatClient` |
| Database | PostgreSQL | Port configured in connection string |
| ORM | EF Core + Npgsql.EntityFrameworkCore.PostgreSQL | DbContext, entities, `EnsureCreated` at startup |
| Frontend | Razor views + Bootstrap + vanilla JS | Light theme, fixed sidebar, responsive chat shell |
| File tools | ClosedXML, PdfPig, SSH.NET | Excel, PDF, SFTP, SQL reading |
| Auth | Cookie authentication | Simple plaintext passwords by current requirement |
| Background processing | `BackgroundService` + `WorkflowRunQueue` | Runs queued workflow jobs and due schedules |

## 3. Configuration

`appsettings.json` includes:

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5433;Database=BuildWorkflow;Username=new_admin;Password=Test12345"
},
"Ollama": {
  "Endpoint": "http://localhost:11434",
  "Model": "beam:cloud",
  "EmbeddingModel": "nomic-embed-text"
},
"Sso": {
  "ValidationEndpoint": ""
}
```

## 4. Key Architecture

Main folders:

- `AICenter` - AI connector abstraction, tools, helpers
- `AICenter/Connectors` - replaceable connectors (`OllamaConnector`)
- `AICenter/Tools` - `DatabaseToolExecutor`, permission-gated tool creation
- `AICenter/Helpers` - file, PDF, Excel, API, SQL, and utility helpers
- `RAGPipeline` - RAG file extraction, chunking, embeddings, context building
- `Controllers` - MVC/API controllers
- `Data` - EF Core `AppDbContext`
- `Models` - entities
- `Views` - Razor views
- `SQL` - seed SQL

Runtime behavior:

- Startup creates/updates schema with `EnsureCreated` plus compensating `CREATE/ALTER TABLE` statements.
- Admin manages models, tools, RAG buckets, knowledge documents per bucket, users, and assignments.
- Chat supports file attachments, RAG knowledge retrieval, persisted sessions/history, markdown rendering, and selection of tools/knowledge.
- Tools are permission-gated per user; admins see all tools.
- File attachments are passed for the current request message and their file IDs are persisted in chat history.
- Chat history is persisted per user and shown in the left sidebar.
- Workflows can be created, edited, shared, scheduled, run, and viewed on a separate history page.

## 5. Entities

Core entities:

- `AppUser` (username, password, role, email, fullName)
- `AiModelDefinition`
- `ToolDefinition`
- `ApiToolConfiguration`
- `SqlToolConfiguration`
- `SftpToolConfiguration`
- `RagBucket`
- `RagDocument`
- `RagChunk`
- `WorkflowDefinition`
- `WorkflowTool`
- `WorkflowFile`
- `WorkflowRagBucket`
- `WorkflowSession`
- `WorkflowMessage`
- `WorkflowRun`
- `WorkflowSchedule`
- `WorkflowShare`
- `UserToolPermission`
- `UserRagBucket`
- `UserAiModel`
- `ChatSession`
- `ChatMessageRecord` (with `AttachedFileIds`)
- `UploadedFile`

## 6. API

### `POST /api/chat`

Request:

```json
{
  "message": "What is in the attached file?",
  "modelId": "optional-guid",
  "attachedFileIds": ["guid"],
  "sessionId": "optional-guid",
  "ragBucketId": "optional-guid",
  "selectedToolIds": ["guid"],
  "selectedRagBucketIds": ["guid"]
}
```

Response:

```json
{
  "reply": "...",
  "sessionId": "guid",
  "modelName": "..."
}
```

### File upload

`POST /api/files/upload`

Response:

```json
{
  "fileId": "guid",
  "fileName": "name",
  "token": "file:guid"
}
```

### Chat history

- `GET /api/chat/sessions` returns the last 10 user sessions.
- `GET /api/chat/sessions/{id}/messages` returns messages for a session.

### Workflows

- `GET /api/workflow` lists owned and shared workflows.
- `POST /api/workflow` creates a workflow.
- `GET /api/workflow/{id}` returns details, tool IDs, RAG bucket IDs, file IDs, and file names.
- `PUT /api/workflow/{id}` updates name, instructions, model, tools, RAG buckets, and optionally files.
- `DELETE /api/workflow/{id}` deletes an owned workflow.
- `POST /api/workflow/{id}/tools`, `/ragbuckets`, and `/files` manage assignments/attachments.
- `POST /api/workflow/{id}/run` queues a run.
- `GET /api/workflow/{id}/runs` lists runs.
- `GET /api/workflow/runs/{runId}/messages` lists run messages.
- `POST /api/workflow/{id}/schedule` creates/schedules a run.
- `GET /api/workflow/{id}/schedules` lists schedules.
- `DELETE /api/workflow/schedules/{scheduleId}` removes a schedule.
- `GET/POST/DELETE /api/workflow/{id}/shares` manages workflow sharing.
- `GET /api/users` lists users for sharing.

### Knowledge

Knowledge documents are managed inside each RAG bucket through the RAG Buckets page. Upload/delete uses `UploadRagKnowledge` and `DeleteRagDocument`.

### Admin utilities

- `POST /Admin/CleanTempFiles` deletes files and subdirectories under `TempFilesLocal`.

### SSO

- `GET /login/sso/backend?token={token}` validates token with configured internal API, creates/logs in user, and redirects.

## 7. Tooling

Supported database tool types:

- `Api` / `Http`
- `SqlReader` / `Database`
- `File` / `FileReader` / text/PDF/Excel tools
- `Sftp`
- `SqlToExcel` / `SqlExcel`
- `ExportExcel`
- `ExportText`

Features:

- API calls
- Read-only SQL for PostgreSQL, SQL Server, MySQL
- Excel read/write
- Text, PDF, CSV processing
- SFTP list/upload/download
- SQL-to-Excel export
- Excel/text export tools for workflows
- Per-user assignment and permission gating
- Tool and knowledge checkboxes in chat; default everything selected

## 8. RAG Pipeline

RAG is stored in PostgreSQL:

- buckets
- documents
- chunks
- embedding JSON and native `double[]` arrays

Processing:

- Admin uploads TXT, CSV, MD, PDF, XLS/XLSX
- Knowledge upload lives on each RAG Bucket row/modal
- Text is extracted
- Content is chunked
- Document and chunk embeddings are generated using configured Ollama embedding model
- Retrieval uses embeddings when available, otherwise keyword/context fallback
- Chat can use all assigned buckets or a selected subset

## 9. Workflows

Workflow features:

- Create workflow modal with checkboxes for tools, RAG buckets, and file attachments.
- Workflow table shows name, tool count, attached file names, run count, and actions.
- Edit workflow modal updates name/instructions, tools, RAG buckets, and attached files.
- History button routes to a dedicated colored history page with run details, dates, durations, errors, messages, and download links.
- Run-now queues a background job and stores results/messages.
- Export tools generate downloadable Excel/text files via `file:<guid>` tokens.
- Scheduling supports one-time, repeat, weekly, monthly, and yearly runs via `WorkflowBackgroundService`.
- Sharing allows other users to run/view without editing.
- Test API tools can be seeded from `SQL/SeedTools.sql`.
- Admin can clean `TempFilesLocal`.

## 10. UI

- Login, logout, and role-based navigation
- Admin dashboard with counts, cleanup, and links
- Model management with modal creation
- Tool management with type-specific configuration popups
- RAG bucket management with per-bucket knowledge modal
- User management consolidated with per-user updates and model/tool/bucket assignments
- Workflow management with create/edit/share/schedule/history actions
- Chat workspace with history sidebar, right-side tool/knowledge selection, uploads, markdown rendering, and message bubbles
- Google-inspired light theme
- Full-width containers and no privacy page
- Footer has copyright only

## 11. Key Files

| File | Purpose |
| --- | --- |
| `Program.cs` | Startup, schema, services, auth, seeding |
| `Controllers/AdminController.cs` | Admin CRUD, assignments, knowledge upload, temp cleanup |
| `Controllers/ChatController.cs` | Chat page and assigned data |
| `Controllers/ChatApiController.cs` | Chat API, history, selected tools/knowledge |
| `Controllers/WorkflowModuleController.cs` | Workflow CRUD, runs, schedules, sharing |
| `Controllers/WorkflowController.cs` | Workflow/history views |
| `Controllers/FileUploadController.cs` | Secure file upload |
| `Controllers/SSOController.cs` | SSO login flow |
| `AICenter/AICenterService.cs` | AI orchestration, RAG context, sessions, selection |
| `AICenter/Connectors/OllamaConnector.cs` | Ollama connector |
| `AICenter/Tools/DatabaseToolExecutor.cs` | Tool mapping and execution |
| `Services/WorkflowBackgroundService.cs` | Scheduler and run queue processor |
| `Data/AppDbContext.cs` | EF Core model |
| `Views/Chat/Index.cshtml` | Chat UI |
| `Views/Workflow/Index.cshtml` | Workflow management UI |
| `Views/Workflow/History.cshtml` | Colored workflow history UI |
| `Views/Admin/*` | Admin UI |
| `SQL/SeedTools.sql` | Seed/import tool definitions |
| `AI_PROJECT_TRACKER.md` | This tracker |

## 12. Run

```bash
dotnet run
```

Default login: `admin` / `admin`

## 14. Capability to File Map

Use this section to locate a feature before editing.

| Capability | Primary Implementation | UI / Related Files |
| --- | --- | --- |
| App startup, DB schema, auth, seeding | `Program.cs` | Connection, services, initial admin |
| App layout/top bar/sidebar | `Views/Shared/_Layout.cshtml`, `wwwroot/css/site.css`, `Views/Shared/_Sidebar.cshtml` (if present) | Bootstrap/Inter styling |
| Auth/login/logout/profile | `Controllers/AuthController.cs` or `AccountController.cs`, `Program.cs` | `_Layout.cshtml` |
| Chat page and selection state | `Controllers/ChatController.cs` | `Views/Chat/Index.cshtml` (chat shell, file links) |
| Chat API/orchestration/RAG/tools | `Controllers/ChatApiController.cs`, `AICenter/AICenterService.cs` | selection payloads and history |
| Ollama connector | `AICenter/Connectors/OllamaConnector.cs`, `AICenter/AiConnectorFactory.cs` | model definitions |
| Tool mapping/execution | `AICenter/Tools/DatabaseToolExecutor.cs`, tool configurations in `Models` | tools UI |
| API tool HTTP calls/timeouts | `AICenter/Helpers/ApiToolHelper.cs` | `SQL/SeedTools.sql` |
| File upload/download | `Controllers/FileUploadController.cs` | JS upload in `Views/Chat/Index.cshtml` |
| Chat session/message persistence | `AICenter/AICenterService.cs`, `Models/ChatSession.cs`, `Models/ChatMessageRecord.cs` | history list |
| File-token to download link rendering | `Views/Chat/Index.cshtml`, `Views/Workflow/History.cshtml` | regex `file:[0-9a-fA-F-]{36}` |
| Workflow views/management | `Controllers/WorkflowController.cs`, `Controllers/WorkflowModuleController.cs` | `Views/Workflow/Index.cshtml`, `Views/Workflow/History.cshtml` |
| Workflow scheduling/sharing | `Controllers/WorkflowModuleController.cs`, `Services/WorkflowBackgroundService.cs` | Workflow views |
| Workflow queue/execution | `Services/WorkflowRunQueue.cs`, `Services/WorkflowExecutionService.cs`, `Services/WorkflowBackgroundService.cs` | run status view |
| Run messages/export links | `Views/Workflow/History.cshtml`, export tool output in `DatabaseToolExecutor.cs` | token capture in `WorkflowExecutionService.cs` |
| RAG buckets/chunks | `Controllers/AdminController.cs` or RAG endpoints, `Data/AppDbContext.cs` | `Views/Admin/*` |
| RAG context ranking | `RAGPipeline/RagEmbeddingGenerator.cs`, `RAGPipeline/RagContextBuilder.cs`, `AICenter/AICenterService.cs` | top-N chunks |
| Knowledge upload/chunking | `Controllers/AdminController.cs`, `Data/AppDbContext.cs` | admin views |
| Admin users/tools/models | `Controllers/AdminController.cs` | `Views/Admin/*` |
| Temp cleanup | `Controllers/AdminController.cs` | admin tools view |
| CSS/JS runtime | `wwwroot/css/site.css`, view scripts | cross-cutting |
| Sample SQL seed | `SQL/SeedTools.sql` | import/update commands |

## 13. Recent Notes

- Chat messages support `**bold**`, `*italic*`, inline code, and line breaks.
- User chat bubbles use white text on the blue background.
- Workflow files can be attached at creation and updated later.
- RAG knowledge is edited inside each bucket modal, not on a separate page.
- Vendor/credit/footer links were simplified to copyright-only text.