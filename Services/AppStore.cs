using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuizMvp.Services;

public sealed class AppStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private StoreData _data = new();

    public AppStore(IWebHostEnvironment env)
    {
        _filePath = Path.Combine(env.ContentRootPath, "Data", "store.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        Load();
        SeedIfEmpty();
    }

    public AppUser? GetUser(string id) => _data.Users.FirstOrDefault(x => x.Id == id);

    public IEnumerable<Quiz> GetQuizzesFor(AppUser user) =>
        user.Role == UserRole.Organizer
            ? _data.Quizzes.Where(x => x.OrganizerId == user.Id).OrderByDescending(x => x.CreatedAt)
            : _data.Quizzes.OrderByDescending(x => x.CreatedAt);

    public Quiz? GetQuiz(string id) => _data.Quizzes.FirstOrDefault(x => x.Id == id);

    public async Task<OperationResult> RegisterAsync(RegisterRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            var email = request.Email.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(email) || request.Password.Length < 4)
            {
                return new(false, "Заполните имя, почту и пароль минимум из 4 символов.");
            }

            if (_data.Users.Any(x => x.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
            {
                return new(false, "Пользователь с такой почтой уже существует.");
            }

            var user = new AppUser
            {
                DisplayName = request.DisplayName.Trim(),
                Email = email,
                PasswordHash = Hash(request.Password),
                Role = request.Role
            };
            _data.Users.Add(user);
            await SaveUnsafeAsync();
            return new(true, "", user);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<AppUser?> ValidateUserAsync(string email, string password)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var hash = Hash(password);
        return Task.FromResult(_data.Users.FirstOrDefault(x => x.Email == normalized && x.PasswordHash == hash));
    }

    public async Task<Quiz> SaveQuizAsync(string organizerId, QuizDraft draft)
    {
        await _gate.WaitAsync();
        try
        {
            var quiz = string.IsNullOrWhiteSpace(draft.Id)
                ? new Quiz { OrganizerId = organizerId }
                : _data.Quizzes.FirstOrDefault(x => x.Id == draft.Id && x.OrganizerId == organizerId) ?? new Quiz { OrganizerId = organizerId };

            quiz.Title = string.IsNullOrWhiteSpace(draft.Title) ? "Новый квиз" : draft.Title.Trim();
            quiz.Category = string.IsNullOrWhiteSpace(draft.Category) ? "Общее" : draft.Category.Trim();
            quiz.SecondsPerQuestion = Math.Clamp(draft.SecondsPerQuestion, 10, 180);
            quiz.Rules = draft.Rules?.Trim() ?? "";
            quiz.Questions = NormalizeQuestions(draft.Questions);

            if (!_data.Quizzes.Any(x => x.Id == quiz.Id))
            {
                _data.Quizzes.Add(quiz);
            }

            await SaveUnsafeAsync();
            return quiz;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<QuizSession> CreateSessionAsync(string organizerId, string quizId)
    {
        await _gate.WaitAsync();
        try
        {
            var quiz = _data.Quizzes.First(x => x.Id == quizId && x.OrganizerId == organizerId);
            var session = new QuizSession
            {
                QuizId = quiz.Id,
                OrganizerId = organizerId,
                Code = GenerateCode()
            };
            _data.Sessions.Add(session);
            await SaveUnsafeAsync();
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationResult> JoinSessionAsync(string code, string userId)
    {
        await _gate.WaitAsync();
        try
        {
            var session = _data.Sessions.LastOrDefault(x => x.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));
            var user = GetUser(userId);
            if (session is null || user is null || session.Status == SessionStatus.Finished)
            {
                return new(false, "Комната не найдена или уже завершена.");
            }

            if (session.Players.All(x => x.UserId != userId))
            {
                session.Players.Add(new SessionPlayer { UserId = userId, DisplayName = user.DisplayName });
                await SaveUnsafeAsync();
            }

            return new(true, "", Session: session);
        }
        finally
        {
            _gate.Release();
        }
    }

    public QuizSession? GetSessionByCode(string code) =>
        _data.Sessions.LastOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    public async Task<QuizSession?> StartSessionAsync(string code, string organizerId)
    {
        await _gate.WaitAsync();
        try
        {
            var session = GetSessionByCode(code);
            if (session is null || session.OrganizerId != organizerId)
            {
                return null;
            }

            session.Status = SessionStatus.Running;
            session.CurrentQuestionIndex = 0;
            await SaveUnsafeAsync();
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<QuizSession?> MoveNextAsync(string code, string organizerId)
    {
        await _gate.WaitAsync();
        try
        {
            var session = GetSessionByCode(code);
            var quiz = session is null ? null : GetQuiz(session.QuizId);
            if (session is null || quiz is null || session.OrganizerId != organizerId)
            {
                return null;
            }

            if (session.CurrentQuestionIndex < quiz.Questions.Count - 1)
            {
                session.CurrentQuestionIndex++;
            }
            else
            {
                session.Status = SessionStatus.Finished;
                session.FinishedAt = DateTime.UtcNow;
            }

            await SaveUnsafeAsync();
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<QuizSession?> EndSessionAsync(string code, string organizerId)
    {
        await _gate.WaitAsync();
        try
        {
            var session = GetSessionByCode(code);
            if (session is null || session.OrganizerId != organizerId)
            {
                return null;
            }

            session.Status = SessionStatus.Finished;
            session.FinishedAt = DateTime.UtcNow;
            await SaveUnsafeAsync();
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<QuizSession?> SubmitAnswerAsync(string code, string userId, string questionId, List<string> optionIds)
    {
        await _gate.WaitAsync();
        try
        {
            var session = GetSessionByCode(code);
            var quiz = session is null ? null : GetQuiz(session.QuizId);
            var question = quiz?.Questions.ElementAtOrDefault(session!.CurrentQuestionIndex);
            if (session is null || quiz is null || question is null || question.Id != questionId || session.Status != SessionStatus.Running)
            {
                return null;
            }

            if (session.Answers.Any(x => x.UserId == userId && x.QuestionId == questionId))
            {
                return session;
            }

            var correctIds = question.Options.Where(x => x.IsCorrect).Select(x => x.Id).Order().ToArray();
            var submittedIds = optionIds.Distinct().Order().ToArray();
            var correct = correctIds.SequenceEqual(submittedIds);
            session.Answers.Add(new SubmittedAnswer
            {
                UserId = userId,
                QuestionId = questionId,
                OptionIds = submittedIds.ToList(),
                Correct = correct
            });

            var player = session.Players.FirstOrDefault(x => x.UserId == userId);
            if (player is not null && correct)
            {
                player.Score += 100;
            }

            await SaveUnsafeAsync();
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public object GetHistory(AppUser user)
    {
        if (user.Role == UserRole.Organizer)
        {
            return _data.Sessions
                .Where(x => x.OrganizerId == user.Id)
                .Select(x => new { x.Code, x.Status, x.CreatedAt, x.FinishedAt, Quiz = GetQuiz(x.QuizId)?.Title, Players = x.Players.Count })
                .OrderByDescending(x => x.CreatedAt);
        }

        return _data.Sessions
            .Where(x => x.Players.Any(p => p.UserId == user.Id))
            .Select(x => new { x.Code, x.Status, x.CreatedAt, Quiz = GetQuiz(x.QuizId)?.Title, Score = x.Players.First(p => p.UserId == user.Id).Score })
            .OrderByDescending(x => x.CreatedAt);
    }

    public object BuildRoomState(string code)
    {
        var session = GetSessionByCode(code);
        var quiz = session is null ? null : GetQuiz(session.QuizId);
        var question = quiz is null || session!.CurrentQuestionIndex < 0 || session.CurrentQuestionIndex >= quiz.Questions.Count
            ? null
            : quiz.Questions[session.CurrentQuestionIndex];

        return new
        {
            type = "state",
            session,
            quiz = quiz is null ? null : new { quiz.Id, quiz.Title, quiz.Category, quiz.SecondsPerQuestion, quiz.Rules, TotalQuestions = quiz.Questions.Count },
            question = question is null ? null : HideCorrectAnswers(question),
            leaderboard = session?.Players.OrderByDescending(x => x.Score).ThenBy(x => x.DisplayName).ToList()
        };
    }

    private static Question HideCorrectAnswers(Question question) => new()
    {
        Id = question.Id,
        Text = question.Text,
        Kind = question.Kind,
        ImageUrl = question.ImageUrl,
        ChoiceMode = question.ChoiceMode,
        Options = question.Options.Select(x => new AnswerOption { Id = x.Id, Text = x.Text }).ToList()
    };

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        var json = File.ReadAllText(_filePath);
        _data = JsonSerializer.Deserialize<StoreData>(json, _json) ?? new StoreData();
    }

    private void SeedIfEmpty()
    {
        if (_data.Users.Count > 0)
        {
            return;
        }

        var organizer = new AppUser
        {
            DisplayName = "Организатор",
            Email = "organizer@example.com",
            PasswordHash = Hash("1234"),
            Role = UserRole.Organizer
        };
        var participant = new AppUser
        {
            DisplayName = "Участник",
            Email = "participant@example.com",
            PasswordHash = Hash("1234"),
            Role = UserRole.Participant
        };
        _data.Users.AddRange([organizer, participant]);
        _data.Quizzes.Add(BuildDemoQuiz(organizer.Id));
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_data, _json));
    }

    private async Task SaveUnsafeAsync()
    {
        var json = JsonSerializer.Serialize(_data, _json);
        await File.WriteAllTextAsync(_filePath, json);
    }

    private static List<Question> NormalizeQuestions(IEnumerable<Question> questions) =>
        questions
            .Where(x => !string.IsNullOrWhiteSpace(x.Text) && x.Options.Count >= 2)
            .Select(x =>
            {
                x.Id = string.IsNullOrWhiteSpace(x.Id) ? Guid.NewGuid().ToString("N") : x.Id;
                foreach (var option in x.Options)
                {
                    option.Id = string.IsNullOrWhiteSpace(option.Id) ? Guid.NewGuid().ToString("N") : option.Id;
                }
                if (x.Options.All(o => !o.IsCorrect))
                {
                    x.Options[0].IsCorrect = true;
                }
                return x;
            })
            .ToList();

    private static string GenerateCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 6).Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("quiz-mvp:" + value));
        return Convert.ToHexString(bytes);
    }

    private static Quiz BuildDemoQuiz(string organizerId) => new()
    {
        OrganizerId = organizerId,
        Title = "Демо-квиз по веб-разработке",
        Category = "IT",
        SecondsPerQuestion = 30,
        Rules = "За правильный ответ начисляется 100 баллов. Ответ доступен только на текущем вопросе.",
        Questions =
        [
            new()
            {
                Text = "Какой протокол используется для обмена событиями в реальном времени в этом MVP?",
                ChoiceMode = ChoiceMode.Single,
                Options =
                [
                    new() { Text = "WebSocket", IsCorrect = true },
                    new() { Text = "FTP" },
                    new() { Text = "SMTP" }
                ]
            },
            new()
            {
                Text = "Какие роли есть в системе?",
                ChoiceMode = ChoiceMode.Multiple,
                Options =
                [
                    new() { Text = "Организатор", IsCorrect = true },
                    new() { Text = "Участник", IsCorrect = true },
                    new() { Text = "Гость базы данных" }
                ]
            }
        ]
    };
}
