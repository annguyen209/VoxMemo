// Services/Database/AppDbContextFactory.cs
namespace VoxMemo.Services.Database;

public static class AppDbContextFactory
{
    public static AppDbContext Create() => new AppDbContext();
}
