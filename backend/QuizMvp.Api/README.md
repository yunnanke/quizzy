# QuizMvp API

ASP.NET Core 8 REST API для QuizMvp. Frontend находится в соседней папке `frontend` и раздаётся backend-приложением в режиме разработки.

## Локальное подключение к PostgreSQL

Connection string не хранится в Git. Перед запуском задайте её одним из способов:

```powershell
$env:ConnectionStrings__Default = "Host=...;Database=...;Username=...;Password=...;SSL Mode=Require;Channel Binding=Require"
```

Или используйте user-secrets:

```powershell
dotnet user-secrets init --project .\backend\QuizMvp.Api\QuizMvp.Api.csproj
dotnet user-secrets set "ConnectionStrings:Default" "Host=...;Database=...;Username=...;Password=...;SSL Mode=Require;Channel Binding=Require" --project .\backend\QuizMvp.Api\QuizMvp.Api.csproj
```

Запуск из корня репозитория:

```powershell
dotnet run --project .\backend\QuizMvp.Api\QuizMvp.Api.csproj
```

После запуска:

- REST/Swagger: `/swagger`
- API health: `/api/health`
- PostgreSQL health: `/api/health/db`

При первом запуске приложение создаёт таблицы текущей модели через EF Core `EnsureCreated`. После сверки с реальной схемой Neon это будет заменено на версионируемые миграции.
