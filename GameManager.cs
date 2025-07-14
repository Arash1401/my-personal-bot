using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace MafiaBot
{
    public class GameManager
    {
        private readonly ITelegramBotClient _botClient;
        private readonly Dictionary<long, Game> _games = new();

        public GameManager(ITelegramBotClient botClient)
        {
            _botClient = botClient;
        }

        public async Task ProcessMessage(Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var userId = message.From.Id;
            var messageText = message.Text;

            // دستورات اصلی
            switch (messageText?.Split(' ')[0].ToLower())
            {
                case "/start":
                    await SendWelcomeMessage(chatId, cancellationToken);
                    break;

                case "/newgame":
                    await CreateNewGame(chatId, userId, message.From.Username ?? message.From.FirstName, cancellationToken);
                    break;

                case "/join":
                    await JoinGame(chatId, userId, message.From.Username ?? message.From.FirstName, cancellationToken);
                    break;

                case "/startgame":
                    await StartGame(chatId, userId, cancellationToken);
                    break;

                case "/endgame":
                    await EndGame(chatId, userId, cancellationToken);
                    break;

                case "/players":
                    await ShowPlayers(chatId, cancellationToken);
                    break;

                case "/help":
                    await SendHelpMessage(chatId, cancellationToken);
                    break;

                default:
                    // پردازش پیام‌های بازی
                    if (_games.ContainsKey(chatId))
                    {
                        await _games[chatId].ProcessGameMessage(message, cancellationToken);
                    }
                    break;
            }
        }

        // اضافه کردن متد برای پردازش callback queries
        public async Task ProcessCallbackQuery(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            var chatId = callbackQuery.Message.Chat.Id;

            if (_games.ContainsKey(chatId))
            {
                await _games[chatId].ProcessCallbackQuery(callbackQuery, cancellationToken);
            }
        }

        private async Task SendWelcomeMessage(long chatId, CancellationToken cancellationToken)
        {
            var welcomeText = "🎭 به بازی مافیا خوش آمدید!\n\n" +
                             "📝 راهنمای شروع:\n" +
                             "1️⃣ یک نفر باید با /newgame بازی جدید بسازد\n" +
                             "2️⃣ بقیه با /join به بازی بپیوندند\n" +
                             "3️⃣ سازنده بازی با /startgame بازی را شروع کند\n\n" +
                             "دستورات:\n" +
                             "/newgame - ساخت بازی جدید\n" +
                             "/join - پیوستن به بازی\n" +
                             "/startgame - شروع بازی\n" +
                             "/players - نمایش بازیکنان\n" +
                             "/endgame - پایان بازی\n" +
                             "/help - راهنما";

            await _botClient.SendTextMessageAsync(chatId, welcomeText, cancellationToken: cancellationToken);
        }

        private async Task CreateNewGame(long chatId, long creatorId, string creatorName, CancellationToken cancellationToken)
        {
            if (_games.ContainsKey(chatId))
            {
                await _botClient.SendTextMessageAsync(chatId, "⚠️ یک بازی در حال اجراست! ابتدا با /endgame آن را پایان دهید.", cancellationToken: cancellationToken);
                return;
            }

            var game = new Game(_botClient, chatId, creatorId);
            game.AddPlayer(creatorId, creatorName);
            _games[chatId] = game;

            await _botClient.SendTextMessageAsync(chatId,
                $"🎮 بازی جدید ایجاد شد!\n" +
                $"👤 سازنده: {creatorName}\n" +
                $"👥 بازیکنان: 1/{game.MaxPlayers}\n\n" +
                $"✅ برای پیوستن /join را بزنید.\n" +
                $"📌 حداقل 4 نفر برای شروع نیاز است.",
                cancellationToken: cancellationToken);
        }

        private async Task JoinGame(long chatId, long userId, string userName, CancellationToken cancellationToken)
        {
            if (!_games.ContainsKey(chatId))
            {
                await _botClient.SendTextMessageAsync(chatId,
                    "❌ بازی فعالی وجود ندارد!\n\n" +
                    "ابتدا یک نفر باید با /newgame بازی جدید بسازد.",
                    cancellationToken: cancellationToken);
                return;
            }

            var game = _games[chatId];
            if (game.IsStarted)
            {
                await _botClient.SendTextMessageAsync(chatId, "❌ بازی شروع شده است! نمی‌توانید بپیوندید.", cancellationToken: cancellationToken);
                return;
            }

            if (game.Players.Count >= game.MaxPlayers)
            {
                await _botClient.SendTextMessageAsync(chatId, "❌ بازی پر است!", cancellationToken: cancellationToken);
                return;
            }

            if (game.AddPlayer(userId, userName))
            {
                await _botClient.SendTextMessageAsync(chatId,
                    $"✅ {userName} به بازی پیوست!\n" +
                    $"👥 تعداد بازیکنان: {game.Players.Count}/{game.MaxPlayers}\n" +
                    $"{(game.Players.Count >= 4 ? "💚 آماده شروع!" : $"⏳ {4 - game.Players.Count} نفر دیگر نیاز است")}",
                    cancellationToken: cancellationToken);
            }
            else
            {
                await _botClient.SendTextMessageAsync(chatId, "⚠️ شما قبلاً در بازی هستید!", cancellationToken: cancellationToken);
            }
        }

        private async Task StartGame(long chatId, long userId, CancellationToken cancellationToken)
        {
            if (!_games.ContainsKey(chatId))
            {
                await _botClient.SendTextMessageAsync(chatId, "❌ بازی فعالی وجود ندارد!", cancellationToken: cancellationToken);
                return;
            }

            var game = _games[chatId];
            if (game.CreatorId != userId)
            {
                await _botClient.SendTextMessageAsync(chatId, "❌ فقط سازنده بازی می‌تواند آن را شروع کند!", cancellationToken: cancellationToken);
                return;
            }

            if (game.IsStarted)
            {
                await _botClient.SendTextMessageAsync(chatId, "⚠️ بازی قبلاً شروع شده است!", cancellationToken: cancellationToken);
                return;
            }

            await game.StartGame(cancellationToken);
        }

        private async Task EndGame(long chatId, long userId, CancellationToken cancellationToken)
        {
            if (!_games.ContainsKey(chatId))
            {
                await _botClient.SendTextMessageAsync(chatId, "❌ بازی فعالی وجود ندارد!", cancellationToken: cancellationToken);
                return;
            }

            var game = _games[chatId];
            if (game.CreatorId != userId)
            {
                await _botClient.SendTextMessageAsync(chatId, "❌ فقط سازنده بازی می‌تواند آن را پایان دهد!", cancellationToken: cancellationToken);
                return;
            }

            _games.Remove(chatId);
            await _botClient.SendTextMessageAsync(chatId, "🏁 بازی پایان یافت!\n\nبرای شروع بازی جدید /newgame بزنید.", cancellationToken: cancellationToken);
        }

        private async Task ShowPlayers(long chatId, CancellationToken cancellationToken)
        {
            if (!_games.ContainsKey(chatId))
            {
                await _botClient.SendTextMessageAsync(chatId, "❌ بازی فعالی وجود ندارد!", cancellationToken: cancellationToken);
                return;
            }

            var game = _games[chatId];
            var playersList = string.Join("\n", game.Players.Values.Select((p, i) =>
                $"{i + 1}. {p.Name} {(p.Id == game.CreatorId ? "👑" : "")} {(p.IsAlive ? "✅" : "💀")}"));

            await _botClient.SendTextMessageAsync(chatId,
                $"👥 لیست بازیکنان ({game.Players.Count}/{game.MaxPlayers}):\n\n{playersList}",
                cancellationToken: cancellationToken);
        }

        private async Task SendHelpMessage(long chatId, CancellationToken cancellationToken)
        {
            var helpText = "📖 راهنمای بازی مافیا\n\n" +
                          "🎯 هدف بازی:\n" +
                          "• شهروندان: پیدا کردن و حذف همه مافیاها\n" +
                          "• مافیا: حذف شهروندان تا برابر شدن تعداد\n\n" +
                          "👥 نقش‌ها:\n" +
                          "• 🏃 شهروند: نقش عادی\n" +
                          "• 🔫 مافیا: هر شب یک نفر را می‌کشد\n" +
                          "• 👨‍⚕️ دکتر: هر شب یک نفر را نجات می‌دهد\n" +
                          "• 🕵️ کاراگاه: هر شب نقش یک نفر را می‌فهمد\n\n" +
                          "🌞 روز: همه با هم بحث و رای‌گیری\n" +
                          "🌙 شب: نقش‌های ویژه عمل می‌کنند\n\n" +
                          "💡 نحوه شروع:\n" +
                          "1. یک نفر /newgame بزند\n" +
                          "2. بقیه /join بزنند\n" +
                          "3. سازنده /startgame بزند";

            await _botClient.SendTextMessageAsync(chatId, helpText, cancellationToken: cancellationToken);
        }
    }
}
