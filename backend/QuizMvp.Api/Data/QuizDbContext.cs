using Microsoft.EntityFrameworkCore;
using QuizMvp.Api.Models;

namespace QuizMvp.Api.Data;

public sealed class QuizDbContext(DbContextOptions<QuizDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<AnswerOption> AnswerOptions => Set<AnswerOption>();
    public DbSet<QuizSession> QuizSessions => Set<QuizSession>();
    public DbSet<ParticipantAnswer> ParticipantAnswers => Set<ParticipantAnswer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<SessionParticipant>();

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.Id).HasName("PK_Users");
            entity.Property(x => x.Username).HasColumnName("Username").HasMaxLength(120).IsRequired();
            entity.Property(x => x.Email).HasColumnName("Email").HasMaxLength(320).IsRequired();
            entity.Property(x => x.PasswordHash).HasColumnName("PasswordHash").IsRequired();
            entity.Property(x => x.Role).HasColumnName("Role").HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("CreatedAt");
            entity.Ignore(x => x.DisplayName);
        });

        modelBuilder.Entity<Quiz>(entity =>
        {
            entity.ToTable("Quizzes");
            entity.HasKey(x => x.Id).HasName("PK_Quizzes");
            entity.Property(x => x.Title).HasColumnName("Title").HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasColumnName("Description").IsRequired();
            entity.Property(x => x.Category).HasColumnName("Category").HasMaxLength(100).IsRequired();
            entity.Property(x => x.TimeLimitPerQuestion).HasColumnName("TimeLimitPerQuestion").IsRequired();
            entity.Property(x => x.Rules).HasColumnName("Rules").IsRequired();
            entity.Property(x => x.CreatorId).HasColumnName("CreatorId").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("CreatedAt");
            entity.Property(x => x.IsActive).HasColumnName("IsActive").IsRequired();
            entity.Ignore(x => x.SecondsPerQuestion);
            entity.HasOne(x => x.Creator).WithMany(x => x.Quizzes).HasForeignKey(x => x.CreatorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Question>(entity =>
        {
            entity.ToTable("Questions");
            entity.HasKey(x => x.Id).HasName("PK_Questions");
            entity.Property(x => x.Type).HasColumnName("Type").IsRequired();
            entity.Property(x => x.Text).HasColumnName("Text").IsRequired();
            entity.Property(x => x.ImageUrl).HasColumnName("ImageUrl");
            entity.Property(x => x.IsMultipleChoice).HasColumnName("IsMultipleChoice").IsRequired();
            entity.Property(x => x.Points).HasColumnName("Points").IsRequired();
            entity.HasOne(x => x.Quiz).WithMany(x => x.Questions).HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
            entity.Ignore(x => x.Kind);
            entity.Ignore(x => x.ChoiceMode);
        });

        modelBuilder.Entity<AnswerOption>(entity =>
        {
            entity.ToTable("AnswerOptions");
            entity.HasKey(x => x.Id).HasName("PK_AnswerOptions");
            entity.Property(x => x.Text).HasColumnName("Text").IsRequired();
            entity.Property(x => x.ImageUrl).HasColumnName("ImageUrl");
            entity.Property(x => x.IsCorrect).HasColumnName("IsCorrect").IsRequired();
            entity.HasOne(x => x.Question).WithMany(x => x.Options).HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuizSession>(entity =>
        {
            entity.ToTable("Sessions");
            entity.HasKey(x => x.Id).HasName("PK_Sessions");
            entity.Property(x => x.Code).HasColumnName("JoinCode").HasMaxLength(12).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("StartedAt").IsRequired();
            entity.Property(x => x.FinishedAt).HasColumnName("EndedAt");
            entity.Property(x => x.Status).HasColumnName("Status").HasConversion<int>().IsRequired();
            entity.Property(x => x.CurrentQuestionIndex).HasColumnName("CurrentQuestionIndex").IsRequired();
            entity.Ignore(x => x.OrganizerId);
            entity.Ignore(x => x.Participants);
            entity.Ignore(x => x.Answers);
            entity.HasOne(x => x.Quiz).WithMany(x => x.Sessions).HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ParticipantAnswer>(entity =>
        {
            entity.ToTable("ParticipantAnswers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SessionId).HasColumnName("SessionId").IsRequired();
            entity.Property(x => x.QuestionId).HasColumnName("QuestionId").IsRequired();
            entity.Property(x => x.UserId).HasColumnName("UserId").IsRequired();
            entity.Property(x => x.OptionIds).HasColumnName("SelectedOptionIds").IsRequired();
            entity.Property(x => x.AnsweredAt).HasColumnName("SubmittedAt").IsRequired();
            entity.Property(x => x.IsCorrect).HasColumnName("IsCorrect").IsRequired();
            entity.Property(x => x.PointsEarned).HasColumnName("PointsEarned").IsRequired();
            entity.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
