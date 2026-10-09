namespace QuizMvp.Services;

public enum UserRole
{
    Participant,
    Organizer
}

public enum QuestionKind
{
    Text,
    Image
}

public enum ChoiceMode
{
    Single,
    Multiple
}

public enum SessionStatus
{
    Lobby,
    Running,
    Finished
}

public sealed record RegisterRequest(string DisplayName, string Email, string Password, UserRole Role);
public sealed record LoginRequest(string Email, string Password);
public sealed record StartSessionRequest(string QuizId);
public sealed record JoinRoomRequest(string Code);

public sealed class AppUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public object ToPublic() => new { Id, DisplayName, Email, Role };
}

public sealed class Quiz
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizerId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public int SecondsPerQuestion { get; set; } = 30;
    public string Rules { get; set; } = "";
    public List<Question> Questions { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Question
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public QuestionKind Kind { get; set; } = QuestionKind.Text;
    public string? ImageUrl { get; set; }
    public ChoiceMode ChoiceMode { get; set; } = ChoiceMode.Single;
    public List<AnswerOption> Options { get; set; } = [];
}

public sealed class AnswerOption
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
}

public sealed class QuizDraft
{
    public string? Id { get; set; }
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public int SecondsPerQuestion { get; set; } = 30;
    public string Rules { get; set; } = "";
    public List<Question> Questions { get; set; } = [];
}

public sealed class QuizSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string QuizId { get; set; } = "";
    public string OrganizerId { get; set; } = "";
    public string Code { get; set; } = "";
    public SessionStatus Status { get; set; } = SessionStatus.Lobby;
    public int CurrentQuestionIndex { get; set; } = -1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public List<SessionPlayer> Players { get; set; } = [];
    public List<SubmittedAnswer> Answers { get; set; } = [];
}

public sealed class SessionPlayer
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Score { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

public sealed class SubmittedAnswer
{
    public string UserId { get; set; } = "";
    public string QuestionId { get; set; } = "";
    public List<string> OptionIds { get; set; } = [];
    public bool Correct { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}

public sealed class StoreData
{
    public List<AppUser> Users { get; set; } = [];
    public List<Quiz> Quizzes { get; set; } = [];
    public List<QuizSession> Sessions { get; set; } = [];
}

public sealed record OperationResult(bool Success, string Message, AppUser? User = null, QuizSession? Session = null);
