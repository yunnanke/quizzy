namespace QuizMvp.Api.Models;

public sealed record RegisterRequest(string DisplayName, string Email, string Password, UserRole Role = UserRole.Participant);
public sealed record LoginRequest(string Email, string Password);
public sealed record CreateSessionRequest(int QuizId);
public sealed record JoinSessionRequest(string Code);
public sealed record SubmitAnswerRequest(int QuestionId, IReadOnlyCollection<int> OptionIds);
public sealed record QuizDraft(int? Id, string Title, string? Category, int SecondsPerQuestion, string? Rules, IReadOnlyCollection<QuestionDraft> Questions);
public sealed record QuestionDraft(int? Id, string Text, QuestionKind Kind, string? ImageUrl, ChoiceMode ChoiceMode, IReadOnlyCollection<AnswerOptionDraft> Options);
public sealed record AnswerOptionDraft(int? Id, string Text, bool IsCorrect);
