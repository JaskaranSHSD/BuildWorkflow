# BuildWorkflow

BuildWorkflow is an ASP.NET Core MVC application for authenticated AI chat, RAG knowledge management, tool execution, file attachments, workflow automation, scheduling, and sharing. It uses a local Ollama server for chat and embeddings and PostgreSQL for persistence.

## Features

- Cookie-based login and admin/user roles.
- Chat with Ollama models through `Microsoft.Agents.AI`.
- RAG knowledge buckets and document ingestion/embeddings.
- Admin CRUD for AI models, users, tools, RAG buckets, and knowledge.
- Assign tools/knowledge to users.
- Chat history and session restoration.
- Upload, attach, and link generated files.
- Integrated test tools for API/SQL/SFTP.
- Workflow creation, editing, file/tool/knowledge selection, scheduling, sharing, run history, and generated file links.
- Export data to Excel/TXT from tools and workflows.
- Background workflow runner and due schedule execution.
- Admin/system areas: organization file upload policies, MFA management, soft-deleted users, forced password changes, system cleanup, and total/search-driven pagination across listing pages.

## Prerequisites

- .NET SDK 10.
- PostgreSQL running locally or remotely.
- Ollama running locally or reachable by URL.
- Required Ollama models pulled:
  - chat: `beam:cloud` (or update `Ollama:Model`)
  - embedding: `nomic-embed-text` when embeddings are used.

## Configuration

Update `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5433;Database=BuildWorkflow;Username=new_admin;Password=Test12345"
  },
  "Ollama": {
    "Endpoint": "http://localhost:11434",
    "Model": "beam:cloud",
    "EmbeddingModel": "nomic-embed-text"
  }
}
```

## Database

The application creates/updates the PostgreSQL schema through `EnsureCreated()` at startup. On first run with no users, it seeds:

- Username: `admin`
- Password: `admin`
- Role: `Admin`

Passwords are currently stored as plaintext by intentional current design.

## Run Locally

```powershell
dotnet build
dotnet run --urls http://localhost:5199
```

Open `http://localhost:5199`, then sign in with `admin/admin`.

## Main Areas

### Chat

- Open `Chat`.
- Choose messages in the conversation center.
- Select tools and knowledge buckets from the right sidebar.
- Optionally attach a file.
- Generated Excel/TXT files appear as download links when the relevant export tool is called.
- Chat history is listed on the left and can be selected to reload messages.

### Admin

- `Admin > Models`: add/update AI models and provider configuration.
- `Admin > Tools`: configure SQL, API, SFTP, Excel, Export, and other tool types.
- `Admin > Users`: manage users and basic info.
- `Admin > RAG Buckets`: create buckets and manage knowledge documents/chunks.
- Assign tools or tags/buckets to users through user management pages.
- `Admin > RAG Knowledge` is managed inside each bucket details page.

### Workflows

- Open `Workflows`.
- Create a workflow with system/user instructions and optional model.
- Select tools, RAG buckets, and files.
- Run a workflow manually.
- Schedule it with a one-time run or interval.
- Run history is shown per workflow and opens a separate history view with progress/messages.
- Generated files from workflow runs appear as download links in run messages.
- Owners can share run-only access with other users.

### Files

- Uploads are stored under `TempFilesLocal`.
- Download endpoint is `/api/files/{id}/download`.
- Tool-generated Excel/TXT files are stored and tracked like uploads.

## Recent Additions

- **SQL Single Query / Procedure tool**: run one predefined read-only SQL query or stored procedure defined in the tool.
- **Word document support**: read `.docx` files (chat attachments, RAG uploads) and export generated `.docx` files using Open XML (works on Linux/Ubuntu without Microsoft Word).
- **RAG scalability improvements**: configurable chunk scan limits, bounded parallel embeddings, reusable HTTP clients, database indexes, and cleanup of orphaned chunks/documents.
- **Pagination**: admin lists (models, tools, users, RAG buckets), chat history, and workflow runs use 50 records per page by default, except chat history uses 10.
- **Chat limits**: configurable `Chat:MaxMessagesPerSession` (default 50); opening or continuing an over-limit chat asks the user to start a new chat.

## Development Notes
- The workflow runner is a `BackgroundService` plus `WorkflowRunQueue`.
- Database schema is auto-created at startup; no migration commands are required for the current setup.
- Build artifacts/processes may lock `BuildWorkflow.exe`; stop the running app before rebuilding.
- External test tools may need local endpoints or test APIs.

## Security Notes

- This is a development-oriented application. Web credentials, cookie authentication, and plaintext passwords are not production-grade.
- Do not expose this app to untrusted environments without hardening authentication and secrets handling.
- Tool execution can use SQL, SFTP, APIs, and file system access according to configured permissions.

## Project Tracker

See `AI_PROJECT_TRACKER.md` for detailed component, model, endpoint, and change history.
