using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rose.Domain.Lingo;

namespace Rose.Infrastructure.Lingo;

/// <summary>
/// The ROSE Lingo database (data/lingo.db, a single SQLite file). A DbContext is EF Core's
/// unit of work: it tracks the entities you load and writes every change in SaveChanges.
/// </summary>
public sealed class LingoDbContext(DbContextOptions<LingoDbContext> options) : DbContext(options)
{
    public DbSet<Card> Cards => Set<Card>();

    public DbSet<QuizResult> QuizResults => Set<QuizResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The Fluent API keeps the domain classes free of database attributes.
        modelBuilder.ApplyConfiguration(new CardConfiguration());
        modelBuilder.ApplyConfiguration(new QuizResultConfiguration());
    }
}

internal sealed class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> card)
    {
        card.ToTable("Cards");
        card.HasKey(c => c.Id);
        card.Property(c => c.Id).HasMaxLength(36);
        card.Property(c => c.Language).HasMaxLength(2).IsRequired();
        card.Property(c => c.Term).HasMaxLength(Card.MaxTextLength).IsRequired();
        card.Property(c => c.Reading).HasMaxLength(Card.MaxTextLength).IsRequired();
        card.Property(c => c.Meaning).HasMaxLength(Card.MaxTextLength).IsRequired();
        card.Property(c => c.Notes).HasMaxLength(500).IsRequired();

        // "What is due in Japanese today?" is the most common question, so index it.
        card.HasIndex(c => new { c.Language, c.DueOn });
        card.HasIndex(c => new { c.Language, c.Term });
    }
}

internal sealed class QuizResultConfiguration : IEntityTypeConfiguration<QuizResult>
{
    public void Configure(EntityTypeBuilder<QuizResult> result)
    {
        result.ToTable("QuizResults");
        result.HasKey(r => r.Id);
        result.Property(r => r.Language).HasMaxLength(2).IsRequired();

        // The database itself refuses a second quiz for the same day and language.
        result.HasIndex(r => new { r.Day, r.Language }).IsUnique();
    }
}
