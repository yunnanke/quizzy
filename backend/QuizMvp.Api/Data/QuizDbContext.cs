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
    public DbSet<SessionParticipant> SessionParticipants => Set<SessionParticipant>();
    public DbSet<ParticipantAnswer> ParticipantAnswers => Set<ParticipantAnswer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<Quiz>(entity =>
        {
            entity.ToTable("quizzes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(100).IsRequired();
            entity.HasOne(x => x.Organizer).WithMany(x => x.Quizzes).HasForeignKey(x => x.OrganizerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Question>(entity =>
        {
            entity.ToTable("questions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Text).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ChoiceMode).HasConversion<string>().HasMaxLength(32);
            entity.HasOne(x => x.Quiz).WithMany(x => x.Questions).HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AnswerOption>(entity =>
        {
            entity.ToTable("answer_options");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Text).HasMaxLength(500).IsRequired();
            entity.HasOne(x => x.Question).WithMany(x => x.Options).HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuizSession>(entity =>
        {
            entity.ToTable("quiz_sessions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Code);
            entity.Property(x => x.Code).HasMaxLength(12).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasOne(x => x.Quiz).WithMany(x => x.Sessions).HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Organizer).WithMany(x => x.HostedSessions).HasForeignKey(x => x.OrganizerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SessionParticipant>(entity =>
        {
            entity.ToTable("session_participants");
            entity.HasKey(x => new { x.SessionId, x.UserId });
            entity.HasOne(x => x.Session).WithMany(x => x.Participants).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User).WithMany(x => x.Participation).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ParticipantAnswer>(entity =>
        {
            entity.ToTable("participant_answers");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.SessionId, x.UserId, x.QuestionId }).IsUnique();
            entity.Property(x => x.OptionIds).IsRequired();
            entity.HasOne(x => x.Session).WithMany(x => x.Answers).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
