using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using VoxMemo.Models;

namespace VoxMemo.Services.Database;

public class AppDbContext : DbContext
{
    public DbSet<Meeting> Meetings => Set<Meeting>();
    public DbSet<Transcript> Transcripts => Set<Transcript>();
    public DbSet<TranscriptSegment> TranscriptSegments => Set<TranscriptSegment>();
    public DbSet<Summary> Summaries => Set<Summary>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

    private readonly string _dbPath;

    public AppDbContext()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoxMemo");
        Directory.CreateDirectory(appData);
        _dbPath = Path.Combine(appData, "voxmemo.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Meeting>()
            .HasMany(m => m.Transcripts)
            .WithOne(t => t.Meeting)
            .HasForeignKey(t => t.MeetingId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Meeting>()
            .HasMany(m => m.Summaries)
            .WithOne(s => s.Meeting)
            .HasForeignKey(s => s.MeetingId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Transcript>()
            .HasMany(t => t.Segments)
            .WithOne(s => s.Transcript)
            .HasForeignKey(s => s.TranscriptId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
