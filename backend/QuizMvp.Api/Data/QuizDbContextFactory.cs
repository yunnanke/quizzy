using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QuizMvp.Api.Data;

public sealed class QuizDbContextFactory : IDesignTimeDbContextFactory<QuizDbContext>
{
    public QuizDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<QuizDbContext>()
            .UseNpgsql("Host=localhost;Database=quizmvp;Username=postgres;Password=postgres")
            .Options;
        return new QuizDbContext(options);
    }
}
