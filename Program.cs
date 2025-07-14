using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace MafiaBot
{
    class Program
    {
        private static ITelegramBotClient botClient;
        private static GameManager gameManager;

        static async Task Main()
        {
            // توکن ربات خود را اینجا قرار دهید
            var botToken = "7583651902:AAFKBgpSzvYo4itoTyuyz4VmR4DPoP-hMzk";

            botClient = new TelegramBotClient(botToken);
            gameManager = new GameManager(botClient);

            using CancellationTokenSource cts = new();

            ReceiverOptions receiverOptions = new()
            {
                AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
            };

            botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: cts.Token
            );

            var me = await botClient.GetMeAsync();
            Console.WriteLine($"Bot started! @{me.Username}");
            Console.WriteLine("Press any key to stop...");
            Console.ReadLine();

            cts.Cancel();
        }

        static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                // پردازش پیام‌ها
                if (update.Type == UpdateType.Message && update.Message?.Text != null)
                {
                    var message = update.Message;
                    var chatId = message.Chat.Id;
                    var username = message.From.Username ?? message.From.FirstName;

                    Console.WriteLine($"Message from @{username} in {chatId}: {message.Text}");
                    await gameManager.ProcessMessage(message, cancellationToken);
                }
                // پردازش callback queries (دکمه‌ها)
                else if (update.Type == UpdateType.CallbackQuery)
                {
                    var callbackQuery = update.CallbackQuery;
                    Console.WriteLine($"Callback from @{callbackQuery.From.Username}: {callbackQuery.Data}");
                    await gameManager.ProcessCallbackQuery(callbackQuery, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in HandleUpdate: {ex.Message}");
            }
        }

        static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Polling error: {exception.Message}");
            return Task.CompletedTask;
        }
    }
}


