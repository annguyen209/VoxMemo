using Microsoft.EntityFrameworkCore;
using VoxMemo.Models;
using VoxMemo.Services.Database;
using VoxMemo.Services.Security;

namespace VoxMemo.Tests;

public class DatabaseIntegrationTests
{
    private static string TestDbPath => Path.Combine(Path.GetTempPath(), $"voxmemo_test_{Guid.NewGuid()}.db");

    private AppDbContext CreateTestDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={TestDbPath}")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task AppDbContext_CanCreateDatabase()
    {
        var db = CreateTestDb();
        await db.Database.EnsureCreatedAsync();
        Assert.True(await db.Database.CanConnectAsync());
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task Meeting_CanSaveAndRetrieve()
    {
        var db = CreateTestDb();
        await db.Database.EnsureCreatedAsync();
        
        var meeting = new Meeting
        {
            Id = "test-meeting-1",
            Title = "Test Meeting",
            Language = "en"
        };
        
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();
        
        var retrieved = await db.Meetings.FindAsync("test-meeting-1");
        Assert.NotNull(retrieved);
        Assert.Equal("Test Meeting", retrieved.Title);
        
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task Meeting_WithTranscript_CanSave()
    {
        var db = CreateTestDb();
        await db.Database.EnsureCreatedAsync();
        
        var meeting = new Meeting { Id = "meeting-with-transcript", Title = "Meeting with Transcript" };
        var transcript = new Transcript
        {
            MeetingId = "meeting-with-transcript",
            FullText = "This is a test transcript.",
            Language = "en"
        };
        
        db.Meetings.Add(meeting);
        db.Transcripts.Add(transcript);
        await db.SaveChangesAsync();
        
        var retrieved = await db.Meetings
            .Include(m => m.Transcripts)
            .FirstOrDefaultAsync(m => m.Id == "meeting-with-transcript");
        
        Assert.NotNull(retrieved);
        Assert.Single(retrieved!.Transcripts);
        Assert.Equal("This is a test transcript.", retrieved.Transcripts[0].FullText);
        
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task Meeting_WithTranscriptSegments_CanSave()
    {
        var db = CreateTestDb();
        await db.Database.EnsureCreatedAsync();
        
        var transcript = new Transcript
        {
            Id = "transcript-with-segments",
            MeetingId = "meeting-segments-test",
            FullText = "Full transcript text"
        };
        transcript.Segments.Add(new TranscriptSegment
        {
            TranscriptId = "transcript-with-segments",
            StartMs = 0,
            EndMs = 5000,
            Text = "Hello, this is segment one.",
            Confidence = 0.95f
        });
        transcript.Segments.Add(new TranscriptSegment
        {
            TranscriptId = "transcript-with-segments",
            StartMs = 5000,
            EndMs = 10000,
            Text = "This is segment two.",
            Confidence = 0.89f
        });
        
        db.Meetings.Add(new Meeting { Id = "meeting-segments-test", Title = "Segments Test Meeting" });
        db.Transcripts.Add(transcript);
        await db.SaveChangesAsync();

        var retrieved = await db.Transcripts
            .Include(t => t.Segments)
            .FirstOrDefaultAsync(t => t.Id == "transcript-with-segments");
        
        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved!.Segments.Count);
        Assert.Equal("Hello, this is segment one.", retrieved.Segments[0].Text);
        
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task Meeting_WithSummary_CanSave()
    {
        var db = CreateTestDb();
        await db.Database.EnsureCreatedAsync();
        
        var meeting = new Meeting { Id = "meeting-with-summary", Title = "Meeting with Summary" };
        var summary = new Summary
        {
            MeetingId = "meeting-with-summary",
            Provider = "ollama",
            Model = "llama2",
            Content = "This is a summary."
        };
        
        db.Meetings.Add(meeting);
        db.Summaries.Add(summary);
        await db.SaveChangesAsync();
        
        var retrieved = await db.Meetings
            .Include(m => m.Summaries)
            .FirstOrDefaultAsync(m => m.Id == "meeting-with-summary");
        
        Assert.NotNull(retrieved);
        Assert.Single(retrieved!.Summaries);
        
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task AppSettings_CanSaveAndRetrieve()
    {
        var db = CreateTestDb();
        await db.Database.EnsureCreatedAsync();
        
        var setting = new AppSettings { Key = "test_key", Value = "test_value" };
        db.AppSettings.Add(setting);
        await db.SaveChangesAsync();
        
        var retrieved = await db.AppSettings.FindAsync("test_key");
        Assert.NotNull(retrieved);
        Assert.Equal("test_value", retrieved.Value);
        
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task AppSettings_EncryptedValue_CanSave()
    {
        var db = CreateTestDb();
        await db.Database.EnsureCreatedAsync();
        
        var setting = new AppSettings 
        { 
            Key = "api_key_test", 
            Value = "",
            EncryptedValue = " encrypted_test_value"
        };
        db.AppSettings.Add(setting);
        await db.SaveChangesAsync();
        
        var retrieved = await db.AppSettings.FindAsync("api_key_test");
        Assert.NotNull(retrieved);
        Assert.Equal(" encrypted_test_value", retrieved.EncryptedValue);
        
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task Meeting_CascadeDelete_Works()
    {
        var db = CreateTestDb();
        await db.Database.EnsureCreatedAsync();
        
        // Create meeting with related data
        var meeting = new Meeting { Id = "cascade-test", Title = "Cascade Test" };
        var transcript = new Transcript { MeetingId = "cascade-test", FullText = "Test" };
        var summary = new Summary { MeetingId = "cascade-test", Content = "Summary" };
        
        db.Meetings.Add(meeting);
        db.Transcripts.Add(transcript);
        db.Summaries.Add(summary);
        await db.SaveChangesAsync();
        
        // Delete meeting
        db.Meetings.Remove(meeting);
        await db.SaveChangesAsync();
        
        // Verify related data is deleted
        var transcripts = await db.Transcripts.Where(t => t.MeetingId == "cascade-test").ToListAsync();
        var summaries = await db.Summaries.Where(s => s.MeetingId == "cascade-test").ToListAsync();
        
        Assert.Empty(transcripts);
        Assert.Empty(summaries);
        
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task MultipleMeetings_CanQuery()
    {
        var db = CreateTestDb();
        await db.Database.EnsureCreatedAsync();
        
        // Add multiple meetings
        for (int i = 0; i < 10; i++)
        {
            db.Meetings.Add(new Meeting { Id = $"meeting-{i}", Title = $"Meeting {i}" });
        }
        await db.SaveChangesAsync();
        
        // Query
        var count = await db.Meetings.CountAsync();
        Assert.Equal(10, count);
        
        var ordered = await db.Meetings.OrderBy(m => m.Title).ToListAsync();
        Assert.Equal("Meeting 0", ordered[0].Title);
        
        db.Database.EnsureDeleted();
    }
}

public class SecureStorageTests
{
    [Fact]
    public void Encrypt_DifferentFromPlainText()
    {
        var plainText = "sk-test-api-key-12345";
        var encrypted = SecureStorage.Encrypt(plainText);
        
        Assert.NotEmpty(encrypted);
        Assert.NotEqual(plainText, encrypted);
    }

    [Fact]
    public void Decrypt_ReturnsOriginal()
    {
        var plainText = "sk-test-api-key-12345";
        var encrypted = SecureStorage.Encrypt(plainText);
        var decrypted = SecureStorage.Decrypt(encrypted);
        
        Assert.Equal(plainText, decrypted);
    }

    [Fact]
    public void Encrypt_EmptyString_ReturnsEmpty()
    {
        var result = SecureStorage.Encrypt("");
        Assert.Equal("", result);
    }

    [Fact]
    public void Decrypt_EmptyString_ReturnsEmpty()
    {
        var result = SecureStorage.Decrypt("");
        Assert.Equal("", result);
    }

    [Fact]
    public void IsEncrypted_DetectsBase64()
    {
        var encrypted = SecureStorage.Encrypt("test-key");
        
        // Encrypted string should be valid Base64
        Assert.True(SecureStorage.IsEncrypted(encrypted));
        
        // Plain text should not be detected as encrypted
        Assert.False(SecureStorage.IsEncrypted("plain-text-key"));
    }

    [Fact]
    public void RoundTrip_MultipleCalls_Consistent()
    {
        var original = "my-secret-api-key";
        
        var encrypted1 = SecureStorage.Encrypt(original);
        var decrypted1 = SecureStorage.Decrypt(encrypted1);
        
        var encrypted2 = SecureStorage.Encrypt(original);
        var decrypted2 = SecureStorage.Decrypt(encrypted2);
        
        Assert.Equal(original, decrypted1);
        Assert.Equal(original, decrypted2);
        
        // Each encryption should produce different output (due to DPAPI entropy)
        Assert.NotEqual(encrypted1, encrypted2);
    }
}