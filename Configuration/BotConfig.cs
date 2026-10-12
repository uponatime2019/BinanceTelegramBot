namespace BinanceTelegramBot.Configuration
{
    public class BotConfig
    {
        public TelegramConfig Telegram { get; set; } = new();
        public BinanceConfig Binance { get; set; } = new();
    }

    public class TelegramConfig
    {
        public string BotToken { get; set; } = string.Empty;
    }

    public class BinanceConfig
    {
        public string? ApiKey { get; set; }
        public string? ApiSecret { get; set; }
        public int CacheSeconds { get; set; } = 2;
        public string DefaultQuoteAsset { get; set; } = "USDT";
    }
}
