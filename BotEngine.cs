using System.Diagnostics;
using System.Text;
using BinanceTelegramBot.Configuration;
using BinanceTelegramBot.Helpers;
using BinanceTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BinanceTelegramBot
{
    public class BotEngine
    {
        private readonly TelegramBotClient _botClient;
        private readonly BinanceService _binanceService;
        private readonly CalculatorService _calcService;
        private readonly DateTime _startTime = DateTime.UtcNow;
        private string _botUsername = string.Empty;

        public BotEngine(BotConfig config, BinanceService binanceService, CalculatorService calcService)
        {
            if (string.IsNullOrWhiteSpace(config.Telegram.BotToken))
            {
                throw new ArgumentException("Telegram BotToken must be specified in appsettings.json or environment variable.");
            }

            _botClient = new TelegramBotClient(config.Telegram.BotToken);
            _binanceService = binanceService;
            _calcService = calcService;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                var me = await _botClient.GetMeAsync(cancellationToken);
                _botUsername = me.Username ?? string.Empty;
                Console.WriteLine($"[BotEngine] Connected as @{_botUsername} ({me.FirstName})");

                await RegisterBotCommandsAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[BotEngine] Failed to connect to Telegram: {ex.Message}");
                Console.ResetColor();
                throw;
            }

            await _botClient.DeleteWebhookAsync(dropPendingUpdates: true, cancellationToken: cancellationToken);

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = new[]
                {
                    UpdateType.Message,
                    UpdateType.EditedMessage
                }
            };

            _botClient.StartReceiving(
                HandleUpdateAsync,
                HandlePollingErrorAsync,
                receiverOptions,
                cancellationToken
            );

            Console.WriteLine("[BotEngine] Listening for incoming messages...");
        }

        private async Task RegisterBotCommandsAsync(CancellationToken cancellationToken)
        {
            var commands = new List<BotCommand>
            {
                new() { Command = "p", Description = "Spot prices: /p btc eth sol" },
                new() { Command = "pp", Description = "Spot 24h detail: /pp btc" },
                new() { Command = "pa", Description = "All trading pairs: /pa btc" },
                new() { Command = "pf", Description = "Futures prices: /pf btc eth" },
                new() { Command = "ppf", Description = "Futures 24h detail: /ppf btc" },
                new() { Command = "tpp", Description = "Top 24h pump: /tpp [count] [minVol]" },
                new() { Command = "tdd", Description = "Top 24h dump: /tdd [count] [minVol]" },
                new() { Command = "tvv", Description = "Top 24h volume: /tvv [count]" },
                new() { Command = "buy", Description = "Buy ROI calculation: /buy btc 60000 1000" },
                new() { Command = "sell", Description = "Sell profit comparison: /sell btc 65000 1000" },
                new() { Command = "val", Description = "Crypto portfolio value: /val 0.5 btc" },
                new() { Command = "calc", Description = "Calculator: /calc 100 * 1.05 - 20" },
                new() { Command = "ping", Description = "Check bot latency and uptime" },
                new() { Command = "help", Description = "List all commands & syntax" }
            };

            try
            {
                await _botClient.SetMyCommandsAsync(commands, cancellationToken: cancellationToken);
                Console.WriteLine("[BotEngine] Bot commands registered with Telegram.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BotEngine] Could not register commands: {ex.Message}");
            }
        }

        private async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken cancellationToken)
        {
            var message = update.Message ?? update.EditedMessage;
            if (message == null || string.IsNullOrWhiteSpace(message.Text))
            {
                return;
            }

            // RULE: Command MUST start with "/"
            var text = message.Text.Trim();
            if (!text.StartsWith('/'))
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessCommandAsync(message, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BotEngine] Error processing message {message.MessageId}: {ex.Message}");
                }
            }, cancellationToken);
        }

        private async Task ProcessCommandAsync(Message message, CancellationToken cancellationToken)
        {
            var fullText = message.Text!.Trim();

            // Extract command and arguments
            var parts = fullText.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            var rawCommand = parts[0][1..]; // Remove leading '/'

            // Strip bot username if invoked like /p@BotName
            var atIndex = rawCommand.IndexOf('@');
            if (atIndex >= 0)
            {
                rawCommand = rawCommand[..atIndex];
            }

            var command = rawCommand.ToLowerInvariant();
            var args = parts.Skip(1).ToList();

            string response;
            switch (command)
            {
                case "start":
                    response = GetStartMessage();
                    break;

                case "help":
                    response = GetHelpMessage();
                    break;

                case "p":
                case "price":
                    response = await _binanceService.GetPricesTableAsync(args);
                    break;

                case "pp":
                    response = await _binanceService.GetDetailPriceAsync(args.FirstOrDefault() ?? string.Empty);
                    break;

                case "pa":
                    response = await _binanceService.GetAllPairsAsync(args.FirstOrDefault() ?? string.Empty);
                    break;

                case "pf":
                    response = await _binanceService.GetFuturesPricesTableAsync(args);
                    break;

                case "ppf":
                    response = await _binanceService.GetFuturesDetailPriceAsync(args.FirstOrDefault() ?? string.Empty);
                    break;

                case "tpp":
                case "toppump":
                case "uptrend":
                    int pumpCount = args.Count > 0 && int.TryParse(args[0], out var pc) ? pc : 10;
                    decimal pumpMinVol = args.Count > 1 && decimal.TryParse(args[1], out var pv) ? pv : 0;
                    response = await _binanceService.GetTopGainersAsync(pumpCount, pumpMinVol);
                    break;

                case "tdd":
                case "topdump":
                case "downtrend":
                    int dumpCount = args.Count > 0 && int.TryParse(args[0], out var dc) ? dc : 10;
                    decimal dumpMinVol = args.Count > 1 && decimal.TryParse(args[1], out var dv) ? dv : 0;
                    response = await _binanceService.GetTopLosersAsync(dumpCount, dumpMinVol);
                    break;

                case "tvv":
                case "topvol":
                    int volCount = args.Count > 0 && int.TryParse(args[0], out var vc) ? vc : 10;
                    response = await _binanceService.GetTopVolumeAsync(volCount);
                    break;

                case "buy":
                    response = await _calcService.CalculateBuyAsync(args);
                    break;

                case "sell":
                    response = await _calcService.CalculateSellAsync(args);
                    break;

                case "val":
                case "value":
                    response = await _calcService.CalculateValueAsync(args);
                    break;

                case "calc":
                    var expr = string.Join(" ", args);
                    response = _calcService.EvaluateExpression(expr);
                    break;

                case "ping":
                    var uptime = DateTime.UtcNow - _startTime;
                    response = $"<b>🏓 Pong!</b>\n" +
                               $"Uptime: <code>{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s</code>\n" +
                               $"Status: <code>Online (Binance Connected)</code>";
                    break;

                default:
                    // Unknown command: silently ignore or show hint
                    return;
            }

            if (!string.IsNullOrWhiteSpace(response))
            {
                await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: response,
                    parseMode: ParseMode.Html,
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
            }
        }

        private static string GetStartMessage()
        {
            return "<b>🤖 Welcome to Binance Telegram Bot!</b>\n\n" +
                   "A fast, lightweight, and open-source Binance market tracker.\n\n" +
                   "<b>Popular Commands:</b>\n" +
                   "• <code>/p btc eth sol</code> - Spot prices table\n" +
                   "• <code>/pp btc</code> - Detailed 24h stats\n" +
                   "• <code>/pa btc</code> - All pairs (USDT, FDUSD, etc.)\n" +
                   "• <code>/pf btc eth</code> - Futures prices\n" +
                   "• <code>/tpp 10</code> - Top 24h gainers\n" +
                   "• <code>/tdd 10</code> - Top 24h losers\n" +
                   "• <code>/tvv 10</code> - Top volume\n" +
                   "• <code>/buy btc 60000 1000</code> - Buy ROI calc\n" +
                   "• <code>/sell btc 65000 1000</code> - Sell profit calc\n" +
                   "• <code>/val 0.5 btc</code> - Portfolio value\n" +
                   "• <code>/calc 100 * 1.05 - 20</code> - Calculator\n\n" +
                   "Type /help for full details.";
        }

        private static string GetHelpMessage()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<b>📖 Binance Telegram Bot - Command Guide</b>\n");
            sb.AppendLine("<b>Spot Market:</b>");
            sb.AppendLine("• <code>/p &lt;symbols...&gt;</code> - Check prices (e.g. <code>/p btc eth sol</code>, <code>/p eth-btc</code>)");
            sb.AppendLine("• <code>/pp &lt;symbol&gt;</code> - Detailed 24h stats (High, Low, Volume, Trades)");
            sb.AppendLine("• <code>/pa &lt;coin&gt;</code> - All spot trading pairs for a coin\n");

            sb.AppendLine("<b>Futures (USDT-M):</b>");
            sb.AppendLine("• <code>/pf &lt;symbols...&gt;</code> - Futures prices");
            sb.AppendLine("• <code>/ppf &lt;symbol&gt;</code> - Detailed 24h futures stats\n");

            sb.AppendLine("<b>Market Rankings:</b>");
            sb.AppendLine("• <code>/tpp [count] [minVolM]</code> - Top 24h pump / gainers (e.g. <code>/tpp 15 50</code>)");
            sb.AppendLine("• <code>/tdd [count] [minVolM]</code> - Top 24h dump / losers");
            sb.AppendLine("• <code>/tvv [count]</code> - Top 24h volume coins\n");

            sb.AppendLine("<b>Tools & Calculations:</b>");
            sb.AppendLine("• <code>/buy &lt;coin&gt; &lt;buyPrice&gt; [investedUsd]</code> - Buy target/ROI");
            sb.AppendLine("• <code>/sell &lt;coin&gt; &lt;sellPrice&gt; [soldUsd]</code> - Sell comparison");
            sb.AppendLine("• <code>/val &lt;qty&gt; &lt;coin&gt; [targetPrice]</code> - Asset value calculation");
            sb.AppendLine("• <code>/calc &lt;math_expression&gt;</code> - Calculator (supports K/M/B suffixes)\n");

            sb.AppendLine("<b>System:</b>");
            sb.AppendLine("• <code>/ping</code> - Check bot responsiveness & uptime");
            sb.AppendLine("• <code>/help</code> - Show this help message");
            return sb.ToString();
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient client, Exception exception, CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[BotEngine] Polling error: {errorMessage}");
            Console.ResetColor();

            return Task.CompletedTask;
        }
    }
}
