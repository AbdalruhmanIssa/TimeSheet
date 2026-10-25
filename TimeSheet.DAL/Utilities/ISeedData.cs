namespace TimeSheet.DAL.Utilities
{
    public interface ISeedData
    {
        Task DataSeedingAsync();
        Task IdentitySeedingAsync();
        Task DemoDataSeedingAsync();
    }
}
