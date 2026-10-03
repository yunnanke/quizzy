using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QuizMvp.Api.Data;
using QuizMvp.Api.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true, reloadOnChange: false);
var frontendPath = ResolveFrontendPath(builder.Environment.ContentRootPath);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddDbContext<QuizDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("ConnectionStrings:Default не настроен. Укажите строку подключения Neon через user-secrets или переменную окружения.");
    }

    options.UseNpgsql(connectionString);
});
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/";
        options.AccessDeniedPath = "/";
        options.Cookie.Name = "QuizMvp.Auth";
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");
    var connected = false;
    Exception? lastException = null;

    for (var attempt = 1; attempt <= 5 && !connected; attempt++)
    {
        try
        {
            await db.Database.OpenConnectionAsync();
            connected = true;
            await db.Database.CloseConnectionAsync();
        }
        catch (Exception exception)
        {
            lastException = exception;
            logger.LogWarning(exception, "Попытка подключения к PostgreSQL Neon {Attempt}/5 завершилась ошибкой.", attempt);
        }

        if (!connected && attempt < 5)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    if (!connected)
    {
        var detail = lastException?.GetBaseException().Message;
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(detail)
                ? "Не удалось подключиться к PostgreSQL Neon после 5 попыток. Проверьте доступность базы и локальные user-secrets."
                : $"Не удалось подключиться к PostgreSQL Neon: {detail}",
            lastException);
    }
}

app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(frontendPath) });
app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(frontendPath) });

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "quizmvp-api" }));
app.MapGet("/api/health/db", async (QuizDbContext db) =>
{
    try
    {
        return await db.Database.CanConnectAsync()
            ? Results.Ok(new { status = "ok", database = "connected" })
            : Results.Json(new { status = "error", database = "unavailable" }, statusCode: 503);
    }
    catch (Exception exception)
    {
        return Results.Json(new { status = "error", database = "unavailable", message = exception.Message }, statusCode: 503);
    }
});

app.MapPost("/api/register", async (RegisterRequest request, QuizDbContext db, IPasswordHasher<AppUser> hasher, HttpContext http) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(email) || request.Password.Length < 6)
    {
        return Results.BadRequest(new { message = "Укажите имя, email и пароль минимум из 6 символов." });
    }

    if (await db.Users.AnyAsync(x => x.Email == email))
    {
        return Results.Conflict(new { message = "Пользователь с таким email уже существует." });
    }

    var user = new AppUser { DisplayName = request.DisplayName.Trim(), Email = email, Role = request.Role };
    user.PasswordHash = hasher.HashPassword(user, request.Password);
    db.Users.Add(user);
    await db.SaveChangesAsync();
    await SignInAsync(http, user);
    return Results.Ok(ToPublic(user));
});

app.MapPost("/api/login", async (LoginRequest request, QuizDbContext db, IPasswordHasher<AppUser> hasher, HttpContext http) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email);
    if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
    {
        return Results.BadRequest(new { message = "Неверный email или пароль." });
    }

    await SignInAsync(http, user);
    return Results.Ok(ToPublic(user));
});

app.MapPost("/api/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok();
});

app.MapGet("/api/me", async (HttpContext http, QuizDbContext db) =>
{
    var user = await CurrentUserAsync(http, db);
    return Results.Json(user is null ? null : ToPublic(user));
});

app.MapGet("/api/quizzes", async (HttpContext http, QuizDbContext db) =>
{
    var user = await RequireUserAsync(http, db);
    if (user is null) return Results.Unauthorized();

    var query = db.Quizzes.AsNoTracking().AsSplitQuery().Include(x => x.Questions).ThenInclude(x => x.Options).AsQueryable();
    if (user.Role == UserRole.Organizer) query = query.Where(x => x.CreatorId == user.Id);
    var quizzes = await query.OrderByDescending(x => x.CreatedAt).ToListAsync();
    return Results.Ok(quizzes.Select(ToQuizDto));
});

app.MapGet("/api/quizzes/{id:int}", async (int id, HttpContext http, QuizDbContext db) =>
{
    var user = await RequireUserAsync(http, db);
    var quiz = await db.Quizzes.AsNoTracking().AsSplitQuery().Include(x => x.Questions).ThenInclude(x => x.Options).SingleOrDefaultAsync(x => x.Id == id);
    return user is null ? Results.Unauthorized() : quiz is null ? Results.NotFound() : Results.Ok(ToQuizDto(quiz));
});

app.MapPost("/api/quizzes", async (QuizDraft draft, HttpContext http, QuizDbContext db) =>
{
    var user = await RequireUserAsync(http, db);
    if (user is null) return Results.Unauthorized();
    if (user.Role != UserRole.Organizer) return Results.Forbid();
    if (string.IsNullOrWhiteSpace(draft.Title) || draft.Questions.Count == 0) return Results.BadRequest(new { message = "Викторина должна иметь название и хотя бы один вопрос." });

    Quiz? quiz = null;
    var isNewQuiz = draft.Id is null;
    if (draft.Id is not null)
    {
        quiz = await db.Quizzes.Include(x => x.Questions).ThenInclude(x => x.Options)
            .SingleOrDefaultAsync(x => x.Id == draft.Id && x.CreatorId == user.Id);
        if (quiz is null) return Results.NotFound(new { message = "Викторина не найдена." });
        db.Questions.RemoveRange(quiz.Questions);
    }

    quiz ??= new Quiz { CreatorId = user.Id, CreatedAt = DateTime.UtcNow, IsActive = true };
    quiz.Title = draft.Title.Trim();
    quiz.Category = string.IsNullOrWhiteSpace(draft.Category) ? "Общее" : draft.Category.Trim();
    quiz.SecondsPerQuestion = Math.Clamp(draft.SecondsPerQuestion, 10, 180);
    quiz.Description = draft.Rules?.Trim() ?? "";
    quiz.Rules = draft.Rules?.Trim() ?? "";

    foreach (var (questionDraft, index) in draft.Questions.Select((question, index) => (question, index)))
    {
        var question = new Question { Quiz = quiz, Text = questionDraft.Text.Trim(), Kind = questionDraft.Kind, ImageUrl = questionDraft.ImageUrl, ChoiceMode = questionDraft.ChoiceMode, OrderIndex = index };
        foreach (var option in questionDraft.Options.Where(x => !string.IsNullOrWhiteSpace(x.Text)))
        {
            question.Options.Add(new AnswerOption { Text = option.Text.Trim(), IsCorrect = option.IsCorrect });
        }
        if (question.Options.Count < 2) return Results.BadRequest(new { message = "У каждого вопроса должно быть минимум два варианта ответа." });
        if (!question.Options.Any(x => x.IsCorrect)) question.Options.First().IsCorrect = true;
        quiz.Questions.Add(question);
    }

    if (isNewQuiz) db.Quizzes.Add(quiz);
    await db.SaveChangesAsync();
    return Results.Ok(ToQuizDto(quiz));
});

app.MapPost("/api/sessions", async (CreateSessionRequest request, HttpContext http, QuizDbContext db) =>
{
    var user = await RequireUserAsync(http, db);
    if (user is null) return Results.Unauthorized();
    var quiz = await db.Quizzes.SingleOrDefaultAsync(x => x.Id == request.QuizId && x.CreatorId == user.Id);
    if (quiz is null) return Results.NotFound();
    var session = new QuizSession { QuizId = quiz.Id, OrganizerId = user.Id, Code = await GenerateUniqueCodeAsync(db) };
    db.QuizSessions.Add(session);
    await db.SaveChangesAsync();
    return Results.Ok(new { session.Id, session.Code, session.Status, session.CurrentQuestionIndex });
});

app.MapPost("/api/join", async (JoinSessionRequest request, HttpContext http, QuizDbContext db) =>
{
    var user = await RequireUserAsync(http, db);
    if (user is null) return Results.Unauthorized();
    var session = await db.QuizSessions.SingleOrDefaultAsync(x => x.Code == request.Code.Trim().ToUpper());
    if (session is null || session.Status == SessionStatus.Finished) return Results.BadRequest(new { message = "Комната не найдена или уже завершена." });
    return Results.Ok(new { session.Id, session.Code, session.Status });
});

app.MapGet("/api/history", async (HttpContext http, QuizDbContext db) =>
{
    var user = await RequireUserAsync(http, db);
    if (user is null) return Results.Unauthorized();
    object history = user.Role == UserRole.Organizer
        ? await db.QuizSessions.AsNoTracking().Where(x => x.Quiz.CreatorId == user.Id).Include(x => x.Quiz).OrderByDescending(x => x.CreatedAt).Select(x => (object)new { x.Code, x.Status, x.CreatedAt, x.FinishedAt, Quiz = x.Quiz.Title, Players = db.ParticipantAnswers.Where(a => a.SessionId == x.Id).Select(a => a.UserId).Distinct().Count() }).ToListAsync()
        : await db.ParticipantAnswers.AsNoTracking().Where(x => x.UserId == user.Id).Include(x => x.Session).ThenInclude(x => x.Quiz).GroupBy(x => x.SessionId).Select(group => (object)new { Code = group.First().Session.Code, Status = group.First().Session.Status, CreatedAt = group.First().Session.CreatedAt, FinishedAt = group.First().Session.FinishedAt, Quiz = group.First().Session.Quiz.Title, Score = group.Sum(x => x.PointsEarned) }).ToListAsync();
    return Results.Ok(history);
});

app.MapFallback(async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(frontendPath, "index.html"));
});

await app.RunAsync();

static object ToPublic(AppUser user) => new { id = user.Id, displayName = user.DisplayName, email = user.Email, role = user.Role.ToString() };
static object ToQuizDto(Quiz quiz) => new
{
    id = quiz.Id,
    organizerId = quiz.OrganizerId,
    title = quiz.Title,
    category = quiz.Category,
    secondsPerQuestion = quiz.SecondsPerQuestion,
    rules = quiz.Rules,
    createdAt = quiz.CreatedAt,
    questions = quiz.Questions.OrderBy(x => x.OrderIndex).Select(x => new { id = x.Id, text = x.Text, kind = x.Kind.ToString(), imageUrl = x.ImageUrl, choiceMode = x.ChoiceMode.ToString(), options = x.Options.Select(o => new { id = o.Id, text = o.Text, isCorrect = o.IsCorrect }) })
};
static async Task<AppUser?> CurrentUserAsync(HttpContext http, QuizDbContext db)
{
    var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    return int.TryParse(id, out var userId) ? await db.Users.SingleOrDefaultAsync(x => x.Id == userId) : null;
}
static Task<AppUser?> RequireUserAsync(HttpContext http, QuizDbContext db) => CurrentUserAsync(http, db);
static async Task SignInAsync(HttpContext http, AppUser user)
{
    var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.DisplayName), new Claim(ClaimTypes.Role, user.Role.ToString()) };
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
}
static async Task<string> GenerateUniqueCodeAsync(QuizDbContext db)
{
    const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    while (true)
    {
        var code = new string(Enumerable.Range(0, 6).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
        if (!await db.QuizSessions.AnyAsync(x => x.Code == code)) return code;
    }
}

static string ResolveFrontendPath(string contentRoot)
{
    var candidates = new[]
    {
        Path.Combine(contentRoot, "frontend"),
        Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "frontend"))
    };

    var path = candidates.FirstOrDefault(Directory.Exists);
    return path ?? throw new DirectoryNotFoundException("Папка frontend не найдена.");
}
