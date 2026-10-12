using BinanceTelegramBot.Configuration;
using BinanceTelegramBot.Services;
using Microsoft.Extensions.Configuration;

namespace BinanceTelegramBot
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            PrintBanner();

            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var botConfig = new BotConfig();
            configuration.Bind(botConfig);

            // Allow environment variable override: TELEGRAM_BOT_TOKEN
            var envToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
            if (!string.IsNullOrWhiteSpace(envToken))
            {
                botConfig.Telegram.BotToken = envToken;
            }

            // Allow command line token override: --token <token>
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "--token" || args[i] == "-t") && i + 1 < args.Length)
                {
                    botConfig.Telegram.BotToken = args[i + 1];
                }
            }

            // Self-test mode (runs without requiring Telegram token)
            if (args.Contains("--test"))
            {
                Console.WriteLine("[Self-Test] Running market and calculator diagnostics...\n");
                var binance = new BinanceService(botConfig.Binance);
                var calc = new CalculatorService(binance);

                Console.WriteLine("1. Testing Spot Prices (/p btc eth sol):");
                var pTable = await binance.GetPricesTableAsync(new List<string> { "btc", "eth", "sol" });
                Console.WriteLine(pTable + "\n");

                Console.WriteLine("2. Testing Spot 24h Detail (/pp btc):");
                var ppDetail = await binance.GetDetailPriceAsync("btc");
                Console.WriteLine(ppDetail + "\n");

                Console.WriteLine("3. Testing All Pairs (/pa btc):");
                var paPairs = await binance.GetAllPairsAsync("btc");
                Console.WriteLine(paPairs + "\n");

                Console.WriteLine("4. Testing Futures Prices (/pf btc eth):");
                var pfTable = await binance.GetFuturesPricesTableAsync(new List<string> { "btc", "eth" });
                Console.WriteLine(pfTable + "\n");

                Console.WriteLine("5. Testing Top Gainers (/tpp 5):");
                var tpp = await binance.GetTopGainersAsync(5);
                Console.WriteLine(tpp + "\n");

                Console.WriteLine("6. Testing Calculator (/calc 10k * 1.5 - 200):");
                var calcResult = calc.EvaluateExpression("10k * 1.5 - 200");
                Console.WriteLine(calcResult + "\n");

                Console.WriteLine("7. Testing Buy ROI (/buy btc 60000 1000):");
                var buyResult = await calc.CalculateBuyAsync(new List<string> { "btc", "60000", "1000" });
                Console.WriteLine(buyResult + "\n");

                Console.WriteLine("8. Testing Value (/val 0.5 btc):");
                var valResult = await calc.CalculateValueAsync(new List<string> { "0.5", "btc" });
                Console.WriteLine(valResult + "\n");

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[Self-Test] All diagnostics passed successfully!");
                Console.ResetColor();
                return;
            }

            if (string.IsNullOrWhiteSpace(botConfig.Telegram.BotToken) ||
                botConfig.Telegram.BotToken.Contains("YOUR_TELEGRAM_BOT_TOKEN"))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[Configuration Required]");
                Console.WriteLine("No valid Telegram Bot token provided.");
                Console.WriteLine("Please provide your bot token via one of the following methods:");
                Console.WriteLine("  1. Update 'Telegram:BotToken' in appsettings.json");
                Console.WriteLine("  2. Set environment variable: export TELEGRAM_BOT_TOKEN=\"your_token\"");
                Console.WriteLine("  3. Run with argument: dotnet run -- --token \"your_token\"\n");
                Console.ResetColor();
                return;
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\n[Shutdown] Stopping bot...");
                cts.Cancel();
            };

            try
            {
                var binanceService = new BinanceService(botConfig.Binance);
                var calcService = new CalculatorService(binanceService);
                var botEngine = new BotEngine(botConfig, binanceService, calcService);

                await botEngine.StartAsync(cts.Token);

                Console.WriteLine("[App] Bot is running. Press Ctrl+C to terminate.");
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[Shutdown] Bot stopped gracefully.");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Fatal Error] {ex.Message}");
                Console.ResetColor();
            }
        }

        private static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
==================================================
        Binance Telegram Bot (Open Source)
   Real-Time Market Tracking & Trading Calculator
==================================================
");
            Console.ResetColor();
        }
    }
}
