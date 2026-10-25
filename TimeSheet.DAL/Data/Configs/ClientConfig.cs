using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeSheet.DAL.Models;

namespace TimeSheet.DAL.Data.Configs
{
    public class ClientConfig : IEntityTypeConfiguration<Client>
    {
        public void Configure(EntityTypeBuilder<Client> builder)
        {
            builder.ToTable("Clients");
            builder.HasKey(c => c.Id);

            builder.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.HasIndex(c => c.Name).IsUnique();

            builder.Property(c => c.ContactName).HasMaxLength(200);
            builder.Property(c => c.ContactEmail).HasMaxLength(256);
            builder.Property(c => c.Phone).HasMaxLength(30);
        }
    }
}
