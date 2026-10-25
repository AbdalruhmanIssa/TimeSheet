namespace TimeSheet.DAL.Models
{
    public abstract class BaseEntity
    {
        public Guid Id { get; set; } // we set it in SaveChanges if empty
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
