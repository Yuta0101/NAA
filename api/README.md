# API_NAA

Local NAA API for saving and querying chat history in SQL Server.

## Database migration

Before running this version, apply
`database/migrations/20260813_conversation_id_soft_delete.sql` to `MCH_NAA`.
The migration keeps existing conversation grouping values, renames `THREAD_ID` to
`CONVERSATION_ID`, adds soft-delete metadata, and adds nullable `AGENT_THREAD_ID` for
the server-side Agent conversation context. It does not delete question or answer rows.

Then apply `database/005_normalize_audit_fields.sql`. It removes the database-side
local-time default from `UPDATE_DT`, makes that field nullable, and clears only legacy
rows whose empty update operator and exact UTC+8 timestamp show that no real update
occurred. New rows keep `UPDATE_DT` and `UPDATE_OP` null until an actual update.

Then apply `database/006_add_user_card.sql` for NFC card login. It adds the
`USER_CARD` table (one account can own several cards, one card belongs to one
account). Unbinding only sets `ENABLED = 0`.

Development uses Windows authentication. Keep the machine-specific connection string
outside source control with User Secrets:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<connection-string>" --project .\src\API_NAA\API_NAA.csproj
```

If User Secrets are unavailable, the repository-level Git-ignored `NAA.local.txt`
is loaded automatically. See `NAA.local.example.txt` in the repository root.

## Run

```powershell
dotnet run --project .\src\API_NAA\API_NAA.csproj --launch-profile https
```

Default URL: `https://localhost:5210`
