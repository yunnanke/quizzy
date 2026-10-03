using System.ComponentModel.DataAnnotations.Schema;

namespace QuizMvp.Api.Models;

public enum UserRole { Participant, Organizer }
public enum QuestionKind { Text, Image }
public enum ChoiceMode { Single, Multiple }
public enum SessionStatus { Lobby = 0, Running = 1, Finished = 2 }

public sealed class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; }

    [NotMapped]
    public string DisplayName { get => Username; set => Username = value; }

    public ICollection<Quiz> Quizzes { get; set; } = [];
    public ICollection<SessionParticipant> Participation { get; set; } = [];
}

public sealed class Quiz
{
    public int Id { get; set; }
    public int CreatorId { get; set; }
    public AppUser Creator { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Общее";
    public int TimeLimitPerQuestion { get; set; } = 30;
    public string Rules { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    [NotMapped]
    public int OrganizerId { get => CreatorId; set => CreatorId = value; }

    [NotMapped]
    public int SecondsPerQuestion { get => TimeLimitPerQuestion; set => TimeLimitPerQuestion = value; }

    public ICollection<Question> Questions { get; set; } = [];
    public ICollection<QuizSession> Sessions { get; set; } = [];
}

public sealed class Question
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public Quiz Quiz { get; set; } = null!;
    public int Type { get; set; }
    public string Text { get; set; } = "";
    public string? ImageUrl { get; set; }
    public bool IsMultipleChoice { get; set; }
    public int Points { get; set; } = 100;

    [NotMapped]
    public int OrderIndex { get; set; }

    [NotMapped]
    public QuestionKind Kind { get => (QuestionKind)Type; set => Type = (int)value; }

    [NotMapped]
    public ChoiceMode ChoiceMode { get => IsMultipleChoice ? ChoiceMode.Multiple : ChoiceMode.Single; set => IsMultipleChoice = value == ChoiceMode.Multiple; }

    public ICollection<AnswerOption> Options { get; set; } = [];
}

public sealed class AnswerOption
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public string Text { get; set; } = "";
    public string? ImageUrl { get; set; }
    public bool IsCorrect { get; set; }
}

public sealed class QuizSession
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public Quiz Quiz { get; set; } = null!;
    public string Code { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public SessionStatus Status { get; set; }
    public int CurrentQuestionIndex { get; set; }

    [NotMapped]
    public int OrganizerId { get; set; }

    public ICollection<SessionParticipant> Participants { get; set; } = [];
    public ICollection<ParticipantAnswer> Answers { get; set; } = [];
}

public sealed class SessionParticipant
{
    [NotMapped]
    public int SessionId { get; set; }
    [NotMapped]
    public QuizSession Session { get; set; } = null!;
    [NotMapped]
    public int UserId { get; set; }
    [NotMapped]
    public AppUser User { get; set; } = null!;
    [NotMapped]
    public int Score { get; set; }
    [NotMapped]
    public DateTime JoinedAt { get; set; }
}

public sealed class ParticipantAnswer
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int QuestionId { get; set; }
    public int UserId { get; set; }
    public QuizSession Session { get; set; } = null!;
    public string OptionIds { get; set; } = "[]";
    public DateTime AnsweredAt { get; set; }
    public bool IsCorrect { get; set; }
    public int PointsEarned { get; set; }
}
