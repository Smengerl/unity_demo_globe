using System;
using System.Globalization;

namespace Tycoon.Hud
{
    /// <summary>Texts of the HUD: date, clock, cash.</summary>
    public static class HudFormat
    {
        static readonly string[] Days = { "SO", "MO", "DI", "MI", "DO", "FR", "SA" };
        public const string Minus = "−";

        public static string Date(double utcSeconds)
        {
            var t = DateTimeOffset.FromUnixTimeSeconds((long)utcSeconds).UtcDateTime;
            return $"{Days[(int)t.DayOfWeek]} {t.Day:00}.{t.Month:00}.";
        }

        public static string Clock(double utcSeconds)
        {
            var t = DateTimeOffset.FromUnixTimeSeconds((long)utcSeconds).UtcDateTime;
            return $"{t.Hour:00}:{t.Minute:00}";
        }

        /// <summary>At most three digits plus a comma, unit by magnitude (€, T €, Mio €, Mrd €); the number is cut off, not rounded.</summary>
        public static void Cash(double value, out string number, out string unit)
        {
            double a = Math.Abs(value), s;
            if (a >= 1e9) { s = a / 1e9; unit = "Mrd €"; }
            else if (a >= 1e6) { s = a / 1e6; unit = "Mio €"; }
            else if (a >= 1e3) { s = a / 1e3; unit = "T €"; }
            else { s = a; unit = "€"; }

            string n;
            if (s >= 100) n = Math.Floor(s).ToString("0", CultureInfo.InvariantCulture);
            else if (s >= 10) n = (Math.Floor(s * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture);
            else n = (Math.Floor(s * 100) / 100).ToString("0.00", CultureInfo.InvariantCulture);
            number = (value < 0 && s >= 0.005 ? Minus : "") + n.Replace('.', ',');
        }

        /// <summary>Change of the cash per game day, e.g. "+38,2 T/Tag".</summary>
        public static string Delta(double perDay)
        {
            Cash(perDay, out string number, out string unit);
            string shortUnit = unit.Replace(" €", "");
            string sign = perDay < 0 ? "" : "+";    // a negative number already starts with the minus sign
            return $"{sign}{number} {shortUnit}/Tag";
        }
    }
}
