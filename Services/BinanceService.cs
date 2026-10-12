using System.Text;
using System.Text.RegularExpressions;
using Binance.Net.Clients;
using Binance.Net.Interfaces;
using BinanceTelegramBot.Configuration;
using BinanceTelegramBot.Helpers;
using CryptoExchange.Net.Authentication;

namespace BinanceTelegramBot.Services
{
    public class BinanceService
    {
        private readonly BinanceRestClient _client;
        private readonly BinanceConfig _config;

        private readonly SemaphoreSlim _spotLock = new(1, 1);
        private readonly SemaphoreSlim _futuresLock = new(1, 1);

        private List<IBinanceTick>? _cachedSpotTickers;
        private DateTime _lastSpotFetch = DateTime.MinValue;

        private List<IBinance24HPrice>? _cachedFuturesTickers;
        private DateTime _lastFuturesFetch = DateTime.MinValue;

        private static readonly string[] CommonQuoteAssets =
            { "USDT", "FDUSD", "USDC", "BTC", "ETH", "BNB", "EUR", "TRY", "TUSD" };

        public BinanceService(BinanceConfig config)
        {
            _config = config;
            _client = new BinanceRestClient();

            if (!string.IsNullOrWhiteSpace(config.ApiKey) && !string.IsNullOrWhiteSpace(config.ApiSecret))
            {
                _client.SetApiCredentials(new Binance.Net.BinanceCredentials(config.ApiKey, config.ApiSecret));
            }
        }

        #region Cache & Tickers

        public async Task<List<IBinanceTick>> GetAllSpotTickersAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedSpotTickers != null &&
                (DateTime.UtcNow - _lastSpotFetch).TotalSeconds < _config.CacheSeconds)
            {
                return _cachedSpotTickers;
            }

            await _spotLock.WaitAsync();
            try
            {
                if (!forceRefresh && _cachedSpotTickers != null &&
                    (DateTime.UtcNow - _lastSpotFetch).TotalSeconds < _config.CacheSeconds)
                {
                    return _cachedSpotTickers;
                }

                var response = await _client.SpotApi.ExchangeData.GetTickersAsync();
                if (response.Success && response.Data != null)
                {
                    _cachedSpotTickers = response.Data.Where(t => t.LastPrice > 0).ToList();
                    _lastSpotFetch = DateTime.UtcNow;
                }

                return _cachedSpotTickers ?? new List<IBinanceTick>();
            }
            finally
            {
                _spotLock.Release();
            }
        }

        public async Task<List<IBinance24HPrice>> GetAllFuturesTickersAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedFuturesTickers != null &&
                (DateTime.UtcNow - _lastFuturesFetch).TotalSeconds < _config.CacheSeconds)
            {
                return _cachedFuturesTickers;
            }

            await _futuresLock.WaitAsync();
            try
            {
                if (!forceRefresh && _cachedFuturesTickers != null &&
                    (DateTime.UtcNow - _lastFuturesFetch).TotalSeconds < _config.CacheSeconds)
                {
                    return _cachedFuturesTickers;
                }

                var response = await _client.UsdFuturesApi.ExchangeData.GetTickersAsync();
                if (response.Success && response.Data != null)
                {
                    _cachedFuturesTickers = response.Data.Where(t => t.LastPrice > 0).ToList();
                    _lastFuturesFetch = DateTime.UtcNow;
                }

                return _cachedFuturesTickers ?? new List<IBinance24HPrice>();
            }
            finally
            {
                _futuresLock.Release();
            }
        }

        #endregion

        #region Symbol Helpers

        public string NormalizeSymbol(string input, string defaultQuote = "USDT")
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            var clean = Regex.Replace(input.Trim().ToUpperInvariant(), @"[^A-Z0-9_\-\/]", "");
            clean = clean.Replace("-", "").Replace("/", "").Replace("_", "");

            foreach (var quote in CommonQuoteAssets)
            {
                if (clean.EndsWith(quote) && clean.Length > quote.Length)
                {
                    return clean;
                }
            }

            return clean + defaultQuote;
        }

        public (string BaseAsset, string QuoteAsset) ParsePair(string symbol)
        {
            symbol = symbol.ToUpperInvariant();
            foreach (var quote in CommonQuoteAssets)
            {
                if (symbol.EndsWith(quote) && symbol.Length > quote.Length)
                {
                    var baseAsset = symbol[..^quote.Length];
                    return (baseAsset, quote);
                }
            }
            return (symbol, string.Empty);
        }

        public async Task<decimal?> GetPriceAsync(string asset)
        {
            var tickers = await GetAllSpotTickersAsync();
            var normalized = NormalizeSymbol(asset, _config.DefaultQuoteAsset);

            var tick = tickers.FirstOrDefault(t => t.Symbol.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                     ?? tickers.FirstOrDefault(t => t.Symbol.Equals($"{asset}USDT", StringComparison.OrdinalIgnoreCase));

            return tick?.LastPrice;
        }

        #endregion

        #region Spot Commands

        public async Task<string> GetPricesTableAsync(List<string> rawSymbols)
        {
            if (rawSymbols == null || rawSymbols.Count == 0)
            {
                return "Usage: /p &lt;symbols...&gt;\nExample: <code>/p btc eth sol bnb</code>";
            }

            var tickers = await GetAllSpotTickersAsync();
            var found = new List<(string DisplaySymbol, decimal Price, decimal Change, string Arrow, string Icon)>();

            foreach (var raw in rawSymbols)
            {
                var normalized = NormalizeSymbol(raw, _config.DefaultQuoteAsset);
                var tick = tickers.FirstOrDefault(t => t.Symbol.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                         ?? tickers.FirstOrDefault(t => t.Symbol.Equals(raw.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase));

                if (tick != null)
                {
                    var pair = ParsePair(tick.Symbol);
                    var display = pair.QuoteAsset == "USDT" ? pair.BaseAsset : $"{pair.BaseAsset}-{pair.QuoteAsset}";
                    found.Add((
                        display,
                        tick.LastPrice,
                        tick.PriceChangePercent,
                        FormatHelper.GetTrendArrow(tick.PriceChangePercent),
                        FormatHelper.GetTrendIcon(tick.PriceChangePercent)
                    ));
                }
                else
                {
                    found.Add((raw.ToUpperInvariant(), 0m, 0m, "-", "❌"));
                }
            }

            if (found.Count == 0)
            {
                return "No matching symbols found on Binance Spot.";
            }

            int maxSymLen = Math.Max(4, found.Max(f => f.DisplaySymbol.Length));
            int maxPriceLen = Math.Max(6, found.Max(f => FormatHelper.FormatPrice(f.Price).Length));

            var lines = new List<string>();
            foreach (var item in found)
            {
                if (item.Price == 0)
                {
                    lines.Add($"{item.DisplaySymbol.PadRight(maxSymLen)}  [Not Found] {item.Icon}");
                }
                else
                {
                    string sym = item.DisplaySymbol.PadRight(maxSymLen);
                    string price = ("$" + FormatHelper.FormatPrice(item.Price)).PadLeft(maxPriceLen + 1);
                    string pct = FormatHelper.FormatPercent(item.Change).PadLeft(8);
                    lines.Add($"{sym} {price} {pct} {item.Arrow}");
                }
            }

            return $"<pre>{string.Join("\n", lines)}</pre>";
        }

        public async Task<string> GetDetailPriceAsync(string rawSymbol)
        {
            if (string.IsNullOrWhiteSpace(rawSymbol))
            {
                return "Usage: /pp &lt;symbol&gt;\nExample: <code>/pp btc</code>";
            }

            var tickers = await GetAllSpotTickersAsync();
            var normalized = NormalizeSymbol(rawSymbol, _config.DefaultQuoteAsset);

            var tick = tickers.FirstOrDefault(t => t.Symbol.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                     ?? tickers.FirstOrDefault(t => t.Symbol.Equals(rawSymbol.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase));

            if (tick == null)
            {
                return $"Symbol <code>{FormatHelper.EscapeHtml(rawSymbol.ToUpperInvariant())}</code> not found on Binance Spot.";
            }

            var icon = FormatHelper.GetTrendIcon(tick.PriceChangePercent);
            var changeSign = tick.PriceChange >= 0 ? "+" : "";

            var sb = new StringBuilder();
            sb.AppendLine($"<b>📊 {tick.Symbol} (Binance Spot)</b>");
            sb.Append("<pre>");
            sb.AppendLine($"Price:        ${FormatHelper.FormatPrice(tick.LastPrice)}");
            sb.AppendLine($"24h Change:   {changeSign}${FormatHelper.FormatPrice(tick.PriceChange)} ({FormatHelper.FormatPercent(tick.PriceChangePercent)}) {icon}");
            sb.AppendLine($"24h High:     ${FormatHelper.FormatPrice(tick.HighPrice)}");
            sb.AppendLine($"24h Low:      ${FormatHelper.FormatPrice(tick.LowPrice)}");
            sb.AppendLine($"24h Base Vol: {FormatHelper.FormatVolume(tick.Volume)} {ParsePair(tick.Symbol).BaseAsset}");
            sb.AppendLine($"24h Quote Vol:${FormatHelper.FormatVolume(tick.QuoteVolume)}");
            sb.AppendLine($"24h Trades:   {tick.TotalTrades:N0}");
            sb.Append("</pre>");

            return sb.ToString();
        }

        public async Task<string> GetAllPairsAsync(string asset)
        {
            if (string.IsNullOrWhiteSpace(asset))
            {
                return "Usage: /pa &lt;coin&gt;\nExample: <code>/pa btc</code>";
            }

            asset = asset.Trim().ToUpperInvariant();
            var tickers = await GetAllSpotTickersAsync();

            var matching = tickers.Where(t =>
            {
                var pair = ParsePair(t.Symbol);
                return pair.BaseAsset.Equals(asset, StringComparison.OrdinalIgnoreCase) &&
                       !t.Symbol.Contains("UP") && !t.Symbol.Contains("DOWN") &&
                       !t.Symbol.Contains("BEAR") && !t.Symbol.Contains("BULL");
            }).OrderByDescending(t => t.QuoteVolume).ToList();

            if (matching.Count == 0)
            {
                return $"No active trading pairs found for <code>{FormatHelper.EscapeHtml(asset)}</code> on Binance.";
            }

            int maxSymLen = matching.Max(m => m.Symbol.Length);
            decimal totalQuoteVolUsd = 0;
            long totalTrades = 0;

            var sb = new StringBuilder();
            sb.AppendLine($"<b>All Binance Spot Pairs for {FormatHelper.EscapeHtml(asset)}:</b>");
            sb.Append("<pre>");

            foreach (var coin in matching)
            {
                var sym = coin.Symbol.PadRight(maxSymLen);
                var price = FormatHelper.FormatPrice(coin.LastPrice).PadLeft(12);
                var pct = FormatHelper.FormatPercent(coin.PriceChangePercent).PadLeft(8);
                var vol = ("Vol " + FormatHelper.FormatVolume(coin.QuoteVolume)).PadLeft(11);

                sb.AppendLine($"{sym} {price} {pct} {vol}");
                totalQuoteVolUsd += coin.QuoteVolume;
                totalTrades += coin.TotalTrades;
            }

            sb.AppendLine(new string('-', maxSymLen + 34));
            sb.AppendLine($"Total Trades: {totalTrades:N0}");
            sb.AppendLine($"Total Vol:    ${FormatHelper.FormatVolume(totalQuoteVolUsd)}");
            sb.Append("</pre>");

            return sb.ToString();
        }

        #endregion

        #region Futures Commands

        public async Task<string> GetFuturesPricesTableAsync(List<string> rawSymbols)
        {
            if (rawSymbols == null || rawSymbols.Count == 0)
            {
                return "Usage: /pf &lt;symbols...&gt;\nExample: <code>/pf btc eth sol</code>";
            }

            var tickers = await GetAllFuturesTickersAsync();
            var found = new List<(string DisplaySymbol, decimal Price, decimal Change, string Arrow, string Icon)>();

            foreach (var raw in rawSymbols)
            {
                var normalized = NormalizeSymbol(raw, "USDT");
                var tick = tickers.FirstOrDefault(t => t.Symbol.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                         ?? tickers.FirstOrDefault(t => t.Symbol.Equals(raw.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase));

                if (tick != null)
                {
                    var pair = ParsePair(tick.Symbol);
                    var display = pair.QuoteAsset == "USDT" ? pair.BaseAsset : $"{pair.BaseAsset}-{pair.QuoteAsset}";
                    found.Add((
                        display,
                        tick.LastPrice,
                        tick.PriceChangePercent,
                        FormatHelper.GetTrendArrow(tick.PriceChangePercent),
                        FormatHelper.GetTrendIcon(tick.PriceChangePercent)
                    ));
                }
                else
                {
                    found.Add((raw.ToUpperInvariant(), 0m, 0m, "-", "❌"));
                }
            }

            if (found.Count == 0)
            {
                return "No matching futures symbols found on Binance.";
            }

            int maxSymLen = Math.Max(4, found.Max(f => f.DisplaySymbol.Length));
            int maxPriceLen = Math.Max(6, found.Max(f => FormatHelper.FormatPrice(f.Price).Length));

            var lines = new List<string>();
            foreach (var item in found)
            {
                if (item.Price == 0)
                {
                    lines.Add($"{item.DisplaySymbol.PadRight(maxSymLen)}  [Not Found] {item.Icon}");
                }
                else
                {
                    string sym = item.DisplaySymbol.PadRight(maxSymLen);
                    string price = ("$" + FormatHelper.FormatPrice(item.Price)).PadLeft(maxPriceLen + 1);
                    string pct = FormatHelper.FormatPercent(item.Change).PadLeft(8);
                    lines.Add($"{sym} {price} {pct} {item.Arrow}");
                }
            }

            return $"<pre>{string.Join("\n", lines)}</pre>";
        }

        public async Task<string> GetFuturesDetailPriceAsync(string rawSymbol)
        {
            if (string.IsNullOrWhiteSpace(rawSymbol))
            {
                return "Usage: /ppf &lt;symbol&gt;\nExample: <code>/ppf btc</code>";
            }

            var tickers = await GetAllFuturesTickersAsync();
            var normalized = NormalizeSymbol(rawSymbol, "USDT");

            var tick = tickers.FirstOrDefault(t => t.Symbol.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                     ?? tickers.FirstOrDefault(t => t.Symbol.Equals(rawSymbol.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase));

            if (tick == null)
            {
                return $"Futures symbol <code>{FormatHelper.EscapeHtml(rawSymbol.ToUpperInvariant())}</code> not found on Binance.";
            }

            var icon = FormatHelper.GetTrendIcon(tick.PriceChangePercent);
            var changeSign = tick.PriceChange >= 0 ? "+" : "";

            var sb = new StringBuilder();
            sb.AppendLine($"<b>⚡ {tick.Symbol} (Binance Futures USDT-M)</b>");
            sb.Append("<pre>");
            sb.AppendLine($"Price:        ${FormatHelper.FormatPrice(tick.LastPrice)}");
            sb.AppendLine($"24h Change:   {changeSign}${FormatHelper.FormatPrice(tick.PriceChange)} ({FormatHelper.FormatPercent(tick.PriceChangePercent)}) {icon}");
            sb.AppendLine($"24h High:     ${FormatHelper.FormatPrice(tick.HighPrice)}");
            sb.AppendLine($"24h Low:      ${FormatHelper.FormatPrice(tick.LowPrice)}");
            sb.AppendLine($"24h Base Vol: {FormatHelper.FormatVolume(tick.Volume)} {ParsePair(tick.Symbol).BaseAsset}");
            sb.AppendLine($"24h Quote Vol:${FormatHelper.FormatVolume(tick.QuoteVolume)}");
            sb.Append("</pre>");

            return sb.ToString();
        }

        #endregion

        #region Top Rankings

        public async Task<string> GetTopGainersAsync(int count = 10, decimal minVolumeM = 0)
        {
            count = Math.Clamp(count, 3, 30);
            var tickers = await GetAllSpotTickersAsync();

            var query = tickers.Where(t => t.Symbol.EndsWith("USDT") &&
                                           !t.Symbol.Contains("UP") && !t.Symbol.Contains("DOWN") &&
                                           !t.Symbol.Contains("BEAR") && !t.Symbol.Contains("BULL"));

            if (minVolumeM > 0)
            {
                query = query.Where(t => t.QuoteVolume >= minVolumeM * 1_000_000m);
            }

            var list = query.OrderByDescending(t => t.PriceChangePercent).Take(count).ToList();
            if (list.Count == 0) return "No data found for Top Gainers.";

            return FormatRankings(list, $"Top {count} Gainers 24h (Binance Spot USDT)" + (minVolumeM > 0 ? $" [Vol &gt;= ${minVolumeM}M]" : ""));
        }

        public async Task<string> GetTopLosersAsync(int count = 10, decimal minVolumeM = 0)
        {
            count = Math.Clamp(count, 3, 30);
            var tickers = await GetAllSpotTickersAsync();

            var query = tickers.Where(t => t.Symbol.EndsWith("USDT") &&
                                           !t.Symbol.Contains("UP") && !t.Symbol.Contains("DOWN") &&
                                           !t.Symbol.Contains("BEAR") && !t.Symbol.Contains("BULL"));

            if (minVolumeM > 0)
            {
                query = query.Where(t => t.QuoteVolume >= minVolumeM * 1_000_000m);
            }

            var list = query.OrderBy(t => t.PriceChangePercent).Take(count).ToList();
            if (list.Count == 0) return "No data found for Top Losers.";

            return FormatRankings(list, $"Top {count} Losers 24h (Binance Spot USDT)" + (minVolumeM > 0 ? $" [Vol &gt;= ${minVolumeM}M]" : ""));
        }

        public async Task<string> GetTopVolumeAsync(int count = 10)
        {
            count = Math.Clamp(count, 3, 30);
            var tickers = await GetAllSpotTickersAsync();

            var list = tickers.Where(t => t.Symbol.EndsWith("USDT") &&
                                          !t.Symbol.Contains("UP") && !t.Symbol.Contains("DOWN") &&
                                          !t.Symbol.Contains("BEAR") && !t.Symbol.Contains("BULL"))
                              .OrderByDescending(t => t.QuoteVolume)
                              .Take(count)
                              .ToList();

            if (list.Count == 0) return "No data found for Top Volume.";

            return FormatRankings(list, $"Top {count} Volume 24h (Binance Spot USDT)", showVolume: true);
        }

        private static string FormatRankings(List<IBinanceTick> list, string title, bool showVolume = false)
        {
            int maxSymLen = list.Max(t => t.Symbol[..^4].Length);
            var sb = new StringBuilder();
            sb.AppendLine($"<b>🚀 {FormatHelper.EscapeHtml(title)}</b>");
            sb.Append("<pre>");

            int rank = 1;
            foreach (var coin in list)
            {
                string r = rank.ToString().PadLeft(2) + ".";
                string sym = coin.Symbol[..^4].PadRight(maxSymLen);
                string price = ("$" + FormatHelper.FormatPrice(coin.LastPrice)).PadLeft(11);
                string pct = FormatHelper.FormatPercent(coin.PriceChangePercent).PadLeft(8);

                if (showVolume)
                {
                    string vol = ("$" + FormatHelper.FormatVolume(coin.QuoteVolume)).PadLeft(9);
                    sb.AppendLine($"{r} {sym} {price} {vol} {coin.PriceChangePercent switch { > 0 => "🟢", < 0 => "🔴", _ => "⚪" }}");
                }
                else
                {
                    sb.AppendLine($"{r} {sym} {price} {pct} {FormatHelper.GetTrendArrow(coin.PriceChangePercent)}");
                }
                rank++;
            }

            sb.Append("</pre>");
            return sb.ToString();
        }

        #endregion
    }
}
