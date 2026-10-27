namespace TimeSheet.DAL.Utilities
{
    public interface ISeedData
    {
        Task DataSeedingAsync();
        Task IdentitySeedingAsync();
        // today = the business-time-zone date (IBusinessClock.Today), not server UTC.
        Task DemoDataSeedingAsync(DateOnly today);
    }
}
