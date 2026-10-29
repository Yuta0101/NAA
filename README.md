# NAA

NAA contains three local services:

- `api`: SQL Server history and login API (`https://localhost:5210`)
- `ddm`: AgentBuilder proxy (`https://localhost:7079`)
- `front`: ASP.NET Core + React UI (`https://localhost:5124`)

## Local settings

Copy `NAA.local.example.txt` to `NAA.local.txt` and fill in the real values.
`NAA.local.txt` is ignored by Git. API and DDM automatically load this INI-style
text file when they run from the repository. Set `NAA_LOCAL_SETTINGS_FILE` to use
a different absolute path.

The preferred production configuration remains environment variables or a secret
provider. Never commit `NAA.local.txt`.

## Build

```powershell
dotnet build .\api\API_NAA.slnx
dotnet build .\ddm\API_NAA_DDM.sln
dotnet build .\front\WEB_NAA.slnx

Push-Location .\front\src\WEB_NAA\ClientApp
pnpm install --frozen-lockfile
pnpm build
Pop-Location
```

## Run

Make sure SQL Server Express is running and the current Windows account can access
`MCH_NAA`, then start API, DDM, and Front in that order:

```powershell
dotnet dev-certs https --trust
dotnet run --project .\api\src\API_NAA\API_NAA.csproj --launch-profile https
dotnet run --project .\ddm\src\API_NAA_DDM\API_NAA_DDM.csproj --launch-profile https
dotnet run --project .\front\src\WEB_NAA\WEB_NAA.csproj --launch-profile https
```

## NFC card login

A keyboard-emulation NFC reader (tested with YP201 through Citrix) types the card
UID followed by Enter. The login page listens for that burst and signs in without
a password. Measured through Citrix, the reader's keys arrive 0-18 ms apart, while
human typing averages about 100 ms, so a burst only counts as a card when every
gap is at most 60 ms and the UID has 8-20 characters
(`front/src/WEB_NAA/ClientApp/src/cardReader.ts`).

Flow: Front `/auth/card-login` -> DDM `/CardLogin` -> API `/AuthenticateByCard`
-> `dbo.USER_CARD`. Bind a card after a password login under 個人設定 -> 刷卡登入.

Apply `api/database/006_add_user_card.sql` first. `tools/nfc-probe.html` is a
standalone page for measuring a reader's output format and timing.

A factory UID is readable by any phone and can be cloned, so this is a
convenience login for demonstration, not strong authentication. Both login
endpoints are rate limited to 10 attempts per minute per client address.
