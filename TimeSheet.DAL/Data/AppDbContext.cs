using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TimeSheet.DAL.Data.Configs;
using TimeSheet.DAL.Models;

namespace TimeSheet.DAL.Data
{
    public class AppDbContext : IdentityDbContext<ApplicationUser>
    {
        public DbSet<Client> Clients => Set<Client>();
        public DbSet<Project> Projects => Set<Project>();
        public DbSet<ProjectAssignment> ProjectAssignments => Set<ProjectAssignment>();
        public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
        public DbSet<TimeEntryReview> TimeEntryReviews => Set<TimeEntryReview>();

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public override int SaveChanges()
        {
            ApplyBaseEntityRules();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyBaseEntityRules();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void ApplyBaseEntityRules()
        {
            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    if (entry.Entity.Id == Guid.Empty)
                        entry.Entity.Id = Guid.NewGuid();

                    entry.Entity.CreatedAt = DateTime.UtcNow;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.ApplyConfiguration(new ApplicationUserConfig());
            builder.ApplyConfiguration(new ClientConfig());
            builder.ApplyConfiguration(new ProjectConfig());
            builder.ApplyConfiguration(new ProjectAssignmentConfig());
            builder.ApplyConfiguration(new TimeEntryConfig());
            builder.ApplyConfiguration(new TimeEntryReviewConfig());
        }
    }
}
