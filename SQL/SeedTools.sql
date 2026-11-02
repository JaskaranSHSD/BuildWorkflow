-- Run once against the BuildWorkflow PostgreSQL database.
-- Seeds built-in tool definitions. API and SQL Reader examples are disabled
-- because their connection strings, URLs, or keys must be configured first.

INSERT INTO "Tools" ("Id", "Name", "Description", "ToolType", "IsActive", "CreatedAt")
SELECT gen_random_uuid(), 'Excel Reader', 'Read rows from an Excel (.xlsx) file.', 'Excel', TRUE, CURRENT_TIMESTAMP
WHERE NOT EXISTS (SELECT 1 FROM "Tools" WHERE "Name" = 'Excel Reader');

INSERT INTO "Tools" ("Id", "Name", "Description", "ToolType", "IsActive", "CreatedAt")
SELECT gen_random_uuid(), 'Text File Reader', 'Read or write text files.', 'TextFile', TRUE, CURRENT_TIMESTAMP
WHERE NOT EXISTS (SELECT 1 FROM "Tools" WHERE "Name" = 'Text File Reader');

INSERT INTO "Tools" ("Id", "Name", "Description", "ToolType", "IsActive", "CreatedAt")
SELECT gen_random_uuid(), 'PDF Reader', 'Extract text from a PDF file.', 'Pdf', TRUE, CURRENT_TIMESTAMP
WHERE NOT EXISTS (SELECT 1 FROM "Tools" WHERE "Name" = 'PDF Reader');

INSERT INTO "Tools" ("Id", "Name", "Description", "ToolType", "IsActive", "CreatedAt")
SELECT gen_random_uuid(), 'API Example (disabled)', 'Example API-backed tool. Configure endpoint, auth, headers, and templates before enabling.', 'Api', FALSE, CURRENT_TIMESTAMP
WHERE NOT EXISTS (SELECT 1 FROM "Tools" WHERE "Name" = 'API Example (disabled)');

INSERT INTO "ApiToolConfigurations" ("ToolId", "HttpMethod", "Endpoint", "HeadersJson", "AuthType", "Credential", "RequestTemplate", "ResponsePath")
SELECT t."Id", 'GET', 'https://api.example.com/data', NULL, 'None', NULL, NULL, NULL
FROM "Tools" t
WHERE t."Name" = 'API Example (disabled)'
  AND NOT EXISTS (SELECT 1 FROM "ApiToolConfigurations" c WHERE c."ToolId" = t."Id");

INSERT INTO "Tools" ("Id", "Name", "Description", "ToolType", "IsActive", "CreatedAt")
SELECT gen_random_uuid(), 'SQL Reader Example (disabled)', 'Example read-only SQL reporting tool. Configure database type and connection string before enabling.', 'SqlReader', FALSE, CURRENT_TIMESTAMP
WHERE NOT EXISTS (SELECT 1 FROM "Tools" WHERE "Name" = 'SQL Reader Example (disabled)');

INSERT INTO "SqlToolConfigurations" ("ToolId", "DatabaseType", "ConnectionString")
SELECT t."Id", 'PostgreSQL', 'Host=localhost;Port=5432;Database=your_database;Username=your_user;Password=your_password'
FROM "Tools" t
WHERE t."Name" = 'SQL Reader Example (disabled)'
  AND NOT EXISTS (SELECT 1 FROM "SqlToolConfigurations" c WHERE c."ToolId" = t."Id");

-- Default AI model
INSERT INTO "AiModels" ("Id", "Name", "Provider", "Endpoint", "ModelName", "SystemPrompt", "IsActive", "CreatedAt")
SELECT gen_random_uuid(), 'Beam Cloud', 'Ollama', 'http://localhost:11434', 'beam:cloud', 'You are a helpful AI assistant. Answer concisely and accurately.', TRUE, CURRENT_TIMESTAMP
WHERE NOT EXISTS (SELECT 1 FROM "AiModels" WHERE "Name" = 'Beam Cloud');

-- Test API GET tool
INSERT INTO "Tools" ("Id", "Name", "Description", "ToolType", "IsActive", "CreatedAt")
SELECT gen_random_uuid(), 'Test Api GET Sample', 'Calls the test GET API with sample data.', 'Api', TRUE, CURRENT_TIMESTAMP
WHERE NOT EXISTS (SELECT 1 FROM "Tools" WHERE "Name" = 'Test Api GET Sample');

INSERT INTO "ApiToolConfigurations" ("ToolId", "HttpMethod", "Endpoint", "HeadersJson", "AuthType", "Credential", "RequestTemplate", "ResponsePath")
SELECT t."Id", 'GET', '/api/test/tools/get-sample', NULL, 'None', NULL, NULL, NULL
FROM "Tools" t
WHERE t."Name" = 'Test Api GET Sample'
  AND NOT EXISTS (SELECT 1 FROM "ApiToolConfigurations" c WHERE c."ToolId" = t."Id");

-- Test API POST tool
INSERT INTO "Tools" ("Id", "Name", "Description", "ToolType", "IsActive", "CreatedAt")
SELECT gen_random_uuid(), 'Test Api POST Sample', 'Calls the test POST API with sample data.', 'Api', TRUE, CURRENT_TIMESTAMP
WHERE NOT EXISTS (SELECT 1 FROM "Tools" WHERE "Name" = 'Test Api POST Sample');

INSERT INTO "ApiToolConfigurations" ("ToolId", "HttpMethod", "Endpoint", "HeadersJson", "AuthType", "Credential", "RequestTemplate", "ResponsePath")
SELECT t."Id", 'POST', '/api/test/tools/post-sample', NULL, 'None', NULL, '{"input":"{{input}}"}', NULL
FROM "Tools" t
WHERE t."Name" = 'Test Api POST Sample'
  AND NOT EXISTS (SELECT 1 FROM "ApiToolConfigurations" c WHERE c."ToolId" = t."Id");
