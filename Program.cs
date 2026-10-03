using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using QuizMvp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/";
        options.AccessDeniedPath = "/";
        options.Cookie.Name = "QuizMvp.Auth";
    });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<AppStore>();
builder.Services.AddSingleton<QuizSocketHub>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/register", async (RegisterRequest request, AppStore store, HttpContext http) =>
{
    var result = await store.RegisterAsync(request);
    if (!result.Success)
    {
        return Results.BadRequest(new { result.Message });
    }

    await SignInAsync(http, result.User!.Id, result.User.DisplayName, result.User.Role.ToString());
    return Results.Ok(result.User.ToPublic());
});

app.MapPost("/api/login", async (LoginRequest request, AppStore store, HttpContext http) =>
{
    var user = await store.ValidateUserAsync(request.Email, request.Password);
    if (user is null)
    {
        return Results.BadRequest(new { Message = "Неверная почта или пароль." });
    }

    await SignInAsync(http, user.Id, user.DisplayName, user.Role.ToString());
    return Results.Ok(user.ToPublic());
});

app.MapPost("/api/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok();
});

app.MapGet("/api/me", (HttpContext http, AppStore store) =>
{
    var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    return id is null ? Results.Ok(null) : Results.Ok(store.GetUser(id)?.ToPublic());
});

app.MapGet("/api/quizzes", (HttpContext http, AppStore store) =>
{
    var user = RequireUser(http, store);
    return user is null
        ? Results.Unauthorized()
        : Results.Ok(store.GetQuizzesFor(user));
});

app.MapGet("/api/quizzes/{id}", (string id, HttpContext http, AppStore store) =>
{
    var user = RequireUser(http, store);
    var quiz = user is null ? null : store.GetQuiz(id);
    return quiz is null ? Results.NotFound() : Results.Ok(quiz);
});

app.MapPost("/api/quizzes", async (QuizDraft draft, HttpContext http, AppStore store) =>
{
    var user = RequireUser(http, store);
    if (user is null || user.Role != UserRole.Organizer)
    {
        return Results.Forbid();
    }

    var quiz = await store.SaveQuizAsync(user.Id, draft);
    return Results.Ok(quiz);
});

app.MapPost("/api/sessions", async (StartSessionRequest request, HttpContext http, AppStore store, QuizSocketHub hub) =>
{
    var user = RequireUser(http, store);
    if (user is null || user.Role != UserRole.Organizer)
    {
        return Results.Forbid();
    }

    var session = await store.CreateSessionAsync(user.Id, request.QuizId);
    await hub.BroadcastRoomAsync(session.Code, new { type = "session-created", session });
    return Results.Ok(session);
});

app.MapPost("/api/join", async (JoinRoomRequest request, HttpContext http, AppStore store) =>
{
    var user = RequireUser(http, store);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var result = await store.JoinSessionAsync(request.Code, user.Id);
    return result.Success ? Results.Ok(result.Session) : Results.BadRequest(new { result.Message });
});

app.MapGet("/api/history", (HttpContext http, AppStore store) =>
{
    var user = RequireUser(http, store);
    return user is null ? Results.Unauthorized() : Results.Ok(store.GetHistory(user));
});

app.Map("/ws/quiz", async (HttpContext context, QuizSocketHub hub, AppStore store) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var user = RequireUser(context, store);
    if (user is null)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await hub.HandleAsync(socket, user);
});

app.MapFallbackToFile("index.html");

app.Run();

static AppUser? RequireUser(HttpContext http, AppStore store)
{
    var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    return id is null ? null : store.GetUser(id);
}

static async Task SignInAsync(HttpContext http, string id, string name, string role)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, id),
        new(ClaimTypes.Name, name),
        new(ClaimTypes.Role, role)
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
}
