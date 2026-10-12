using LibraryApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Catalog;

public sealed class PublisherConfiguration : IEntityTypeConfiguration<Publisher>
{
    public void Configure(EntityTypeBuilder<Publisher> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(Publisher.NameMaxLength).IsRequired();
    }
}
