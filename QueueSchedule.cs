using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DirOpusReImagined
{
    /// <summary>Reading the time typed for Start Later in the Operations window.</summary>
    public static class QueueSchedule
    {
        private static readonly Regex TimeRx = new(@"^\s*(\d{1,2})(?:[:.h](\d{2}))?\s*(am|pm|a|p)?\s*$",
                                                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The next moment after <paramref name="now"/> at the typed time of day: "02:00", "2:30",
        /// "23.15", "2am", "11 pm" or "23". A time already gone today means tomorrow.
        /// </summary>
        public static bool TryNextTime(string? text, DateTime now, out DateTime when)
        {
            when = default;
            var m = TimeRx.Match(text ?? "");
            if (!m.Success) return false;
            int hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int minute = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            if (m.Groups[3].Success)
            {
                if (hour < 1 || hour > 12) return false;
                bool pm = char.ToLowerInvariant(m.Groups[3].Value[0]) == 'p';
                hour = hour % 12 + (pm ? 12 : 0);
            }
            if (hour > 23 || minute > 59) return false;
            when = now.Date.AddHours(hour).AddMinutes(minute);
            if (when <= now) when = when.AddDays(1);
            return true;
        }

        /// <summary>"02:00", or "02:00 tomorrow" when it isn't today.</summary>
        public static string Describe(DateTime when, DateTime now)
            => when.ToString("HH:mm", CultureInfo.InvariantCulture) + (when.Date == now.Date ? "" : when.Date == now.Date.AddDays(1) ? " tomorrow" : " on " + when.ToString("d"));
    }
}
