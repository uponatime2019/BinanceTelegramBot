using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BinanceTelegramBot.Helpers;

namespace BinanceTelegramBot.Services
{
    public class CalculatorService
    {
        private readonly BinanceService _binanceService;
        private static readonly CultureInfo UsCulture = CultureInfo.InvariantCulture;

        public CalculatorService(BinanceService binanceService)
        {
            _binanceService = binanceService;
        }

        #region /buy Command

        public async Task<string> CalculateBuyAsync(List<string> args)
        {
            if (args == null || args.Count < 2)
            {
                return "Usage: /buy &lt;asset&gt; &lt;buy_price&gt; [amount_usd]\n" +
                       "Example: <code>/buy btc 60000</code>\n" +
                       "Example: <code>/buy btc 60000 1000</code> ($1000 invested)";
            }

            var asset = args[0].Trim().ToUpperInvariant();
            if (!decimal.TryParse(CleanNumber(args[1]), NumberStyles.Any, UsCulture, out var buyPrice) || buyPrice <= 0)
            {
                return "Invalid buy price.";
            }

            decimal? amountUsd = null;
            if (args.Count >= 3)
            {
                if (decimal.TryParse(CleanNumber(args[2]), NumberStyles.Any, UsCulture, out var amt) && amt > 0)
                {
                    amountUsd = amt;
                }
            }

            var currentPrice = await _binanceService.GetPriceAsync(asset);
            if (!currentPrice.HasValue || currentPrice.Value <= 0)
            {
                return $"Could not fetch market price for <code>{FormatHelper.EscapeHtml(asset)}</code> on Binance.";
            }

            var curr = currentPrice.Value;
            var changePct = (curr - buyPrice) / buyPrice * 100m;
            var changeIcon = changePct >= 0 ? "🟢" : "🔴";

            var sb = new StringBuilder();
            sb.AppendLine($"<b>💰 Buy Calculation for #{asset}:</b>");
            sb.AppendLine("<pre>");
            sb.AppendLine($"Buy Price:     ${FormatHelper.FormatPrice(buyPrice)}");
            sb.AppendLine($"Current Price: ${FormatHelper.FormatPrice(curr)}");
            sb.AppendLine($"Change:        {FormatHelper.FormatPercent(changePct)} {changeIcon}");

            if (amountUsd.HasValue)
            {
                var invested = amountUsd.Value;
                var tokens = invested / buyPrice;
                var currentValue = tokens * curr;
                var profit = currentValue - invested;

                sb.AppendLine($"Invested:      ${FormatHelper.FormatPrice(invested)}");
                sb.AppendLine($"Tokens Bought: {FormatHelper.FormatPrice(tokens)} {asset}");
                sb.AppendLine($"Current Value: ${FormatHelper.FormatPrice(currentValue)}");
                sb.AppendLine($"Profit / Loss: {(profit >= 0 ? "+" : "")}${FormatHelper.FormatPrice(profit)}");
            }

            sb.Append("</pre>");
            return sb.ToString();
        }

        #endregion

        #region /sell Command

        public async Task<string> CalculateSellAsync(List<string> args)
        {
            if (args == null || args.Count < 2)
            {
                return "Usage: /sell &lt;asset&gt; &lt;sold_price&gt; [amount_usd]\n" +
                       "Example: <code>/sell btc 65000</code>\n" +
                       "Example: <code>/sell btc 65000 1000</code>";
            }

            var asset = args[0].Trim().ToUpperInvariant();
            if (!decimal.TryParse(CleanNumber(args[1]), NumberStyles.Any, UsCulture, out var soldPrice) || soldPrice <= 0)
            {
                return "Invalid sold price.";
            }

            decimal? amountUsd = null;
            if (args.Count >= 3)
            {
                if (decimal.TryParse(CleanNumber(args[2]), NumberStyles.Any, UsCulture, out var amt) && amt > 0)
                {
                    amountUsd = amt;
                }
            }

            var currentPrice = await _binanceService.GetPriceAsync(asset);
            if (!currentPrice.HasValue || currentPrice.Value <= 0)
            {
                return $"Could not fetch market price for <code>{FormatHelper.EscapeHtml(asset)}</code> on Binance.";
            }

            var curr = currentPrice.Value;
            var sb = new StringBuilder();
            sb.AppendLine($"<b>💸 Sell Comparison for #{asset}:</b>");
            sb.AppendLine("<pre>");
            sb.AppendLine($"Sold Price:    ${FormatHelper.FormatPrice(soldPrice)}");
            sb.AppendLine($"Current Price: ${FormatHelper.FormatPrice(curr)}");

            if (curr > soldPrice)
            {
                var missedPct = (curr - soldPrice) / soldPrice * 100m;
                sb.AppendLine($"Missed Gain:   +{FormatHelper.FormatPercent(missedPct)} 💔");
                if (amountUsd.HasValue)
                {
                    var missedUsd = (amountUsd.Value / soldPrice * curr) - amountUsd.Value;
                    sb.AppendLine($"Missed Value:  +${FormatHelper.FormatPrice(missedUsd)}");
                }
            }
            else
            {
                var savedPct = (soldPrice - curr) / soldPrice * 100m;
                sb.AppendLine($"Saved / Profit:+{FormatHelper.FormatPercent(savedPct)} ✅");
                if (amountUsd.HasValue)
                {
                    var savedUsd = amountUsd.Value - (amountUsd.Value / soldPrice * curr);
                    sb.AppendLine($"Saved Value:   +${FormatHelper.FormatPrice(savedUsd)}");
                }
            }

            sb.Append("</pre>");
            return sb.ToString();
        }

        #endregion

        #region /val Command

        public async Task<string> CalculateValueAsync(List<string> args)
        {
            if (args == null || args.Count < 2)
            {
                return "Usage: /val &lt;quantity&gt; &lt;asset&gt; [target_price]\n" +
                       "Example: <code>/val 0.5 btc</code>\n" +
                       "Example: <code>/val 10 bnb 700</code> (value if BNB reaches $700)";
            }

            if (!decimal.TryParse(CleanNumber(args[0]), NumberStyles.Any, UsCulture, out var quantity) || quantity <= 0)
            {
                return "Invalid quantity.";
            }

            var asset = args[1].Trim().ToUpperInvariant();
            decimal? targetPrice = null;
            if (args.Count >= 3)
            {
                if (decimal.TryParse(CleanNumber(args[2]), NumberStyles.Any, UsCulture, out var target) && target > 0)
                {
                    targetPrice = target;
                }
            }

            var currentPrice = await _binanceService.GetPriceAsync(asset);
            if (!currentPrice.HasValue || currentPrice.Value <= 0)
            {
                return $"Could not fetch price for <code>{FormatHelper.EscapeHtml(asset)}</code> on Binance.";
            }

            var curr = currentPrice.Value;
            var totalUsdt = quantity * curr;

            var sb = new StringBuilder();
            sb.AppendLine($"<b>💼 Value for {FormatHelper.FormatPrice(quantity)} #{asset}:</b>");
            sb.AppendLine("<pre>");
            sb.AppendLine($"Current Value: ${FormatHelper.FormatPrice(totalUsdt)} USDT");

            // Compare to other major reference coins
            if (asset != "BTC")
            {
                var btcPrice = await _binanceService.GetPriceAsync("BTC");
                if (btcPrice.HasValue && btcPrice.Value > 0)
                {
                    sb.AppendLine($"In BTC:        {FormatHelper.FormatPrice(totalUsdt / btcPrice.Value)} ₿");
                }
            }
            if (asset != "ETH")
            {
                var ethPrice = await _binanceService.GetPriceAsync("ETH");
                if (ethPrice.HasValue && ethPrice.Value > 0)
                {
                    sb.AppendLine($"In ETH:        {FormatHelper.FormatPrice(totalUsdt / ethPrice.Value)} Ξ");
                }
            }

            if (targetPrice.HasValue)
            {
                var targetVal = quantity * targetPrice.Value;
                var targetRoi = (targetPrice.Value - curr) / curr * 100m;
                sb.AppendLine(new string('-', 28));
                sb.AppendLine($"At Target:     ${FormatHelper.FormatPrice(targetPrice.Value)}");
                sb.AppendLine($"Target Value:  ${FormatHelper.FormatPrice(targetVal)} USDT");
                sb.AppendLine($"Projected ROI: {FormatHelper.FormatPercent(targetRoi)} {(targetRoi >= 0 ? "🟢" : "🔴")}");
            }

            sb.Append("</pre>");
            return sb.ToString();
        }

        #endregion

        #region /calc Command

        public string EvaluateExpression(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr))
            {
                return "Usage: /calc &lt;expression&gt;\n" +
                       "Example: <code>/calc 100 * 1.05 - 20</code>\n" +
                       "Supports suffixes: K (thousand), M (million), B (billion)\n" +
                       "Example: <code>/calc 10K * 1.5</code>";
            }

            try
            {
                var processed = ReplaceSuffixes(expr.Trim());
                var clean = Regex.Replace(processed, @"[^0-9\+\-\*\/\.\(\)\s]", "");

                var dt = new DataTable();
                var result = dt.Compute(clean, null);

                if (result != null && decimal.TryParse(result.ToString(), NumberStyles.Any, UsCulture, out var decResult))
                {
                    return $"<b>🖥 Calculation:</b>\n" +
                           $"<code>{FormatHelper.EscapeHtml(expr)}</code> = <b>{FormatHelper.FormatPrice(decResult)}</b>";
                }

                return "Could not compute the expression.";
            }
            catch (Exception ex)
            {
                return $"Calculation error: <code>{FormatHelper.EscapeHtml(ex.Message)}</code>";
            }
        }

        private static string ReplaceSuffixes(string expression)
        {
            expression = Regex.Replace(expression, @"(\d+(?:\.\d+)?)B", "$1*1000000000", RegexOptions.IgnoreCase);
            expression = Regex.Replace(expression, @"(\d+(?:\.\d+)?)M", "$1*1000000", RegexOptions.IgnoreCase);
            expression = Regex.Replace(expression, @"(\d+(?:\.\d+)?)K", "$1*1000", RegexOptions.IgnoreCase);
            return expression;
        }

        private static string CleanNumber(string raw)
        {
            raw = raw.Replace("$", "").Replace(",", "").Trim();
            raw = ReplaceSuffixes(raw);
            if (raw.Contains("*"))
            {
                try
                {
                    var dt = new DataTable();
                    var v = dt.Compute(raw, null);
                    return v?.ToString() ?? raw;
                }
                catch
                {
                    return raw;
                }
            }
            return raw;
        }

        #endregion
    }
}
