using System.Globalization;
using System.Text;

namespace BinanceTelegramBot.Helpers
{
    public static class FormatHelper
    {
        private static readonly CultureInfo UsCulture = CultureInfo.InvariantCulture;

        public static string FormatPrice(decimal price)
        {
            if (price == 0) return "0.00";

            decimal abs = Math.Abs(price);
            if (abs >= 1000m)
            {
                return price.ToString("#,##0.00", UsCulture);
            }
            if (abs >= 1m)
            {
                return price.ToString("0.00##", UsCulture);
            }
            if (abs >= 0.001m)
            {
                return price.ToString("0.0000#", UsCulture);
            }
            if (abs >= 0.00001m)
            {
                return price.ToString("0.000000#", UsCulture);
            }

            return price.ToString("0.00000000", UsCulture);
        }

        public static string FormatPercent(decimal percent)
        {
            string sign = percent > 0 ? "+" : string.Empty;
            return $"{sign}{percent.ToString("0.00", UsCulture)}%";
        }

        public static string FormatVolume(decimal volume)
        {
            decimal abs = Math.Abs(volume);
            if (abs >= 1_000_000_000m)
                return $"{(volume / 1_000_000_000m).ToString("0.00", UsCulture)}B";
            if (abs >= 1_000_000m)
                return $"{(volume / 1_000_000m).ToString("0.00", UsCulture)}M";
            if (abs >= 1_000m)
                return $"{(volume / 1_000m).ToString("0.00", UsCulture)}K";

            return volume.ToString("0.00", UsCulture);
        }

        public static string GetTrendIcon(decimal changePercent)
        {
            if (changePercent > 0) return "🟢";
            if (changePercent < 0) return "🔴";
            return "⚪";
        }

        public static string GetTrendArrow(decimal changePercent)
        {
            if (changePercent > 0) return "▲";
            if (changePercent < 0) return "▼";
            return "=";
        }

        public static string EscapeHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        public static string ToMonospace(string text)
        {
            return $"<code>{EscapeHtml(text)}</code>";
        }

        public static string ToCodeBlock(string text)
        {
            return $"<pre>{EscapeHtml(text)}</pre>";
        }
    }
}
