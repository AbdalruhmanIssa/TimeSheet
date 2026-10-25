using Microsoft.Extensions.Options;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.BLL.Settings;

namespace TimeSheet.BLL.Services.Classes
{
    public class BusinessClock : IBusinessClock
    {
        private readonly TimeProvider _time;
        private readonly TimeZoneInfo _zone;
        private readonly DayOfWeek _weekStart;

        public BusinessClock(TimeProvider time, IOptions<TimeSheetSettings> settings)
        {
            _time = time;
            // Validated at startup (Program.cs), so this can't fail here.
            _zone = TimeZoneInfo.FindSystemTimeZoneById(settings.Value.BusinessTimeZone);
            _weekStart = settings.Value.WeekStart;
        }

        public DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

        public DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _zone);

        public DateOnly Today => DateOnly.FromDateTime(LocalNow);

        public DateOnly StartOfWeek(DateOnly date)
        {
            var diff = ((int)date.DayOfWeek - (int)_weekStart + 7) % 7;
            return date.AddDays(-diff);
        }
    }
}
