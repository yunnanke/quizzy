namespace QuizMvp.Api.Models;

public enum UserRole { Participant, Organizer }
public enum QuestionKind { Text, Image }
public enum ChoiceMode { Single, Multiple }
public enum SessionStatus { Lobby, Running, Finished }

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Quiz> Quizzes { get; set; } = [];
    public ICollection<QuizSession> HostedSessions { get; set; } = [];
    public ICollection<SessionParticipant> Participation { get; set; } = [];
}

public sealed class Quiz
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizerId { get; set; }
    public AppUser Organizer { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Category { get; set; } = "Общее";
    public int SecondsPerQuestion { get; set; } = 30;
    public string Rules { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Question> Questions { get; set; } = [];
    public ICollection<QuizSession> Sessions { get; set; } = [];
}

public sealed class Question
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuizId { get; set; }
    public Quiz Quiz { get; set; } = null!;
    public string Text { get; set; } = "";
    public QuestionKind Kind { get; set; } = QuestionKind.Text;
    public string? ImageUrl { get; set; }
    public ChoiceMode ChoiceMode { get; set; } = ChoiceMode.Single;
    public int OrderIndex { get; set; }
    public ICollection<AnswerOption> Options { get; set; } = [];
}

public sealed class AnswerOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
}

public sealed class QuizSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuizId { get; set; }
    public Quiz Quiz { get; set; } = null!;
    public Guid OrganizerId { get; set; }
    public AppUser Organizer { get; set; } = null!;
    public string Code { get; set; } = "";
    public SessionStatus Status { get; set; } = SessionStatus.Lobby;
    public int CurrentQuestionIndex { get; set; } = -1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public ICollection<SessionParticipant> Participants { get; set; } = [];
    public ICollection<ParticipantAnswer> Answers { get; set; } = [];
}

public sealed class SessionParticipant
{
    public Guid SessionId { get; set; }
    public QuizSession Session { get; set; } = null!;
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public int Score { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ParticipantAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public QuizSession Session { get; set; } = null!;
    public Guid UserId { get; set; }
    public Guid QuestionId { get; set; }
    public string OptionIds { get; set; } = "[]";
    public bool IsCorrect { get; set; }
    public DateTime AnsweredAt { get; set; } = DateTime.UtcNow;
}
