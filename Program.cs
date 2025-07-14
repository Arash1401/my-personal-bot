using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MafiaBot
{
    class Program
    {
        private static ITelegramBotClient botClient;
        private static Dictionary<long, Game> activeGames = new Dictionary<long, Game>();
        
        static async Task Main(string[] args)
        {
            var token = "7583651902:AAFV5eJosmlf_CXAGUB5HiKKRt2eXw6R-cs";
            if (string.IsNullOrEmpty(token))
            {
                Console.WriteLine("لطفا BOT_TOKEN رو تنظیم کنید!");
                return;
            }

            botClient = new TelegramBotClient(token);
            
            botClient.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync
            );
            
            var me = await botClient.GetMeAsync();
            Console.WriteLine($"بات @{me.Username} شروع به کار کرد!");
            
            // برای Railway
            await Task.Delay(-1);
        }

        private static async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken cancellationToken)
        {
            try
            {
                // پیام‌های معمولی
                if (update.Type == UpdateType.Message && update.Message?.Text != null)
                {
                    await HandleMessage(update.Message);
                }
                // کلیک روی دکمه‌ها
                else if (update.Type == UpdateType.CallbackQuery)
                {
                    await HandleCallbackQuery(update.CallbackQuery);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"خطا: {ex.Message}");
            }
        }

        private static async Task HandleMessage(Message message)
        {
            var chatId = message.Chat.Id;
            var userId = message.From.Id;
            var text = message.Text;

            // فقط در گروه‌ها
            if (message.Chat.Type == ChatType.Private)
            {
                await botClient.SendTextMessageAsync(chatId, "❌ بات فقط در گروه‌ها کار می‌کند!");
                return;
            }

            // بررسی محدودیت چت در حین بازی
            if (activeGames.ContainsKey(chatId))
            {
                var game = activeGames[chatId];
                if (game.IsStarted && !game.CanPlayerChat(userId))
                {
                    // حذف پیام کسانی که نباید چت کنند
                    try
                    {
                        await botClient.DeleteMessageAsync(chatId, message.MessageId);
                    }
                    catch { }
                    return;
                }
            }

            // دستورات
            switch (text?.ToLower())
            {
                case "/start":
                case "/start@" + "MahmoudiMafia_bot": // نام بات خودتون
                    await StartNewGame(chatId, userId, message.From.FirstName);
                    break;
                    
                case "/help":
                    await SendHelp(chatId);
                    break;
            }
        }

        private static async Task HandleCallbackQuery(CallbackQuery query)
        {
            var chatId = query.Message.Chat.Id;
            var userId = query.From.Id;
            var data = query.Data;

            await botClient.AnswerCallbackQueryAsync(query.Id);

            if (!activeGames.ContainsKey(chatId))
            {
                await botClient.EditMessageTextAsync(chatId, query.Message.MessageId, "❌ بازی‌ای وجود ندارد!");
                return;
            }

            var game = activeGames[chatId];

            // اعمال مختلف بر اساس data
            if (data == "join_game")
            {
                await JoinGame(chatId, userId, query.From.FirstName, query.Message.MessageId);
            }
            else if (data == "start_game")
            {
                await StartGamePlay(chatId, userId, query.Message.MessageId);
            }
            else if (data.StartsWith("vote_"))
            {
                var targetId = long.Parse(data.Replace("vote_", ""));
                await ProcessVote(chatId, userId, targetId, query.Message.MessageId);
            }
            else if (data.StartsWith("kill_"))
            {
                var targetId = long.Parse(data.Replace("kill_", ""));
                await ProcessMafiaKill(chatId, userId, targetId, query.Message.MessageId);
            }
            else if (data.StartsWith("save_"))
            {
                var targetId = long.Parse(data.Replace("save_", ""));
                await ProcessDoctorSave(chatId, userId, targetId, query.Message.MessageId);
            }
            else if (data.StartsWith("shoot_"))
            {
                var targetId = long.Parse(data.Replace("shoot_", ""));
                await ProcessSniperShoot(chatId, userId, targetId, query.Message.MessageId);
            }
        }

        private static async Task StartNewGame(long chatId, long creatorId, string creatorName)
        {
            if (activeGames.ContainsKey(chatId))
            {
                await botClient.SendTextMessageAsync(chatId, "❌ یک بازی در حال انجام است!");
                return;
            }

            var game = new Game(creatorId);
            game.AddPlayer(creatorId, creatorName);
            activeGames[chatId] = game;

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🎮 ورود به بازی", "join_game") },
                new[] { InlineKeyboardButton.WithCallbackData("▶️ شروع بازی", "start_game") }
            });

            await botClient.SendTextMessageAsync(
                chatId, 
                "🎯 **بازی مافیا**\n\n" +
                $"👤 سازنده: {creatorName}\n" +
                $"👥 بازیکنان: 1 نفر\n\n" +
                "حداقل 4 بازیکن نیاز است!",
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard
            );
        }

        private static async Task JoinGame(long chatId, long userId, string userName, int messageId)
        {
            var game = activeGames[chatId];

            if (game.Players.ContainsKey(userId))
            {
                await botClient.AnswerCallbackQueryAsync("", "✅ شما قبلاً وارد بازی شده‌اید!");
                return;
            }

            game.AddPlayer(userId, userName);

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🎮 ورود به بازی", "join_game") },
                new[] { InlineKeyboardButton.WithCallbackData("▶️ شروع بازی", "start_game") }
            });

            var playersList = string.Join("\n", game.Players.Values.Select((p, i) => $"{i + 1}. {p.Name}"));

            await botClient.EditMessageTextAsync(
                chatId,
                messageId,
                "🎯 **بازی مافیا**\n\n" +
                $"👥 بازیکنان ({game.Players.Count} نفر):\n{playersList}\n\n" +
                (game.Players.Count < 4 ? "⏳ منتظر بازیکنان بیشتر..." : "✅ آماده شروع!"),
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard
            );
        }

        private static async Task StartGamePlay(long chatId, long userId, int messageId)
        {
            var game = activeGames[chatId];

            if (userId != game.CreatorId)
            {
                await botClient.AnswerCallbackQueryAsync("", "❌ فقط سازنده می‌تواند بازی را شروع کند!");
                return;
            }

            if (game.Players.Count < 4)
            {
                await botClient.AnswerCallbackQueryAsync("", "❌ حداقل 4 بازیکن نیاز است!");
                return;
            }

            // شروع بازی و تقسیم نقش‌ها
            game.Start();
            
            await botClient.EditMessageTextAsync(
                chatId,
                messageId,
                "🎮 **بازی شروع شد!**\n\n" +
                "📩 نقش‌ها به صورت خصوصی ارسال شد.\n" +
                "🌙 شب اول فرا رسیده..."
            );

            // ارسال نقش‌ها به همه بازیکنان
            foreach (var player in game.Players.Values)
            {
                try
                {
                    var roleInfo = GetRoleInfo(player.Role);
                    await botClient.SendTextMessageAsync(
                        player.Id,
                        $"🎭 **نقش شما: {roleInfo.Name}**\n\n" +
                        $"📝 توضیحات:\n{roleInfo.Description}\n\n" +
                        $"💡 وظیفه:\n{roleInfo.Task}",
                        parseMode: ParseMode.Markdown
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"خطا در ارسال نقش به {player.Name}: {ex.Message}");
                    // اگر بات رو استارت نکرده باشن
                    await botClient.SendTextMessageAsync(
                        chatId,
                        $"⚠️ {player.Name} لطفاً ابتدا در PV بات را /start کنید!"
                    );
                }
            }

            // شروع فاز شب
            await Task.Delay(3000);
            await StartNightPhase(chatId);
        }

        private static async Task StartNightPhase(long chatId)
        {
            var game = activeGames[chatId];
            game.CurrentPhase = GamePhase.Night;

            await botClient.SendTextMessageAsync(chatId, "🌙 **شب فرا رسید...**\n\nهمه به خواب رفتند!");

            // عملیات مافیا
            await SendMafiaOptions(chatId);
            
            // عملیات دکتر
            await SendDoctorOptions(chatId);
            
            // عملیات اسنایپر
            await SendSniperOptions(chatId);
        }

        private static async Task SendMafiaOptions(long chatId)
        {
            var game = activeGames[chatId];
            var mafiaMembers = game.GetAlivePlayers().Where(p => p.Role == Role.Godfather || p.Role == Role.Mafia || p.Role == Role.Bomber);

            foreach (var mafia in mafiaMembers)
            {
                var targets = game.GetAlivePlayers().Where(p => p.Role != Role.Godfather && p.Role != Role.Mafia && p.Role != Role.Bomber);
                
                var buttons = targets.Select(t => 
                    new[] { InlineKeyboardButton.WithCallbackData($"🔫 {t.Name}", $"kill_{t.Id}") }
                ).ToArray();

                var keyboard = new InlineKeyboardMarkup(buttons);

                try
                {
                    var mafiaList = string.Join(", ", mafiaMembers.Select(m => m.Name));
                    await botClient.SendTextMessageAsync(
                        mafia.Id,
                        $"🌙 **شب - فاز مافیا**\n\n" +
                        $"👥 اعضای مافیا: {mafiaList}\n\n" +
                        "یک نفر را برای کشتن انتخاب کنید:",
                        parseMode: ParseMode.Markdown,
                        replyMarkup: keyboard
                    );
                }
                catch { }
            }
        }

        private static async Task SendDoctorOptions(long chatId)
        {
            var game = activeGames[chatId];
            var doctor = game.GetAlivePlayers().FirstOrDefault(p => p.Role == Role.Doctor);

            if (doctor == null) return;

            var targets = game.GetAlivePlayers();
            var buttons = targets.Select(t => 
                new[] { InlineKeyboardButton.WithCallbackData($"💉 {t.Name}", $"save_{t.Id}") }
            ).ToArray();

            var keyboard = new InlineKeyboardMarkup(buttons);

            try
            {
                await botClient.SendTextMessageAsync(
                    doctor.Id,
                    "🌙 **شب - فاز دکتر**\n\n" +
                    "یک نفر را برای نجات انتخاب کنید:",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard
                );
            }
            catch { }
        }

        private static async Task SendSniperOptions(long chatId)
        {
            var game = activeGames[chatId];
            var sniper = game.GetAlivePlayers().FirstOrDefault(p => p.Role == Role.Sniper);

            if (sniper == null || !sniper.HasBullet) return;

            var targets = game.GetAlivePlayers().Where(p => p.Id != sniper.Id);
            var buttons = targets.Select(t => 
                new[] { InlineKeyboardButton.WithCallbackData($"🎯 {t.Name}", $"shoot_{t.Id}") }
            ).ToArray();

            // اضافه کردن گزینه عدم شلیک
            var buttonsList = buttons.ToList();
            buttonsList.Add(new[] { InlineKeyboardButton.WithCallbackData("❌ شلیک نکن", "shoot_0") });

            var keyboard = new InlineKeyboardMarkup(buttonsList);

            try
            {
                await botClient.SendTextMessageAsync(
                    sniper.Id,
                    "🌙 **شب - فاز اسنایپر**\n\n" +
                    "⚠️ فقط یک گلوله دارید!\n" +
                    "به کی شلیک می‌کنید؟",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard
                );
            }
            catch { }
        }

        private static async Task ProcessVote(long chatId, long voterId, long targetId, int messageId)
        {
            var game = activeGames[chatId];

            // بررسی عدم رای به خودش
            if (voterId == targetId)
            {
                await botClient.AnswerCallbackQueryAsync("", "❌ نمی‌توانید به خودتان رای دهید!");
                return;
            }

            game.AddVote(voterId, targetId);

            // اگر همه رای دادند
            if (game.AllAlivePlayersVoted())
            {
                await ProcessVotingResults(chatId);
            }
            else
            {
                await botClient.EditMessageTextAsync(
                    chatId,
                    messageId,
                    $"✅ رای شما ثبت شد!\n\n" +
                    $"⏳ منتظر رای {game.GetPlayersNotVoted().Count} نفر دیگر..."
                );
            }
        }

        private static async Task ProcessMafiaKill(long chatId, long mafiaId, long targetId, int messageId)
        {
            var game = activeGames[chatId];
            game.SetMafiaTarget(targetId);

            await botClient.EditMessageTextAsync(
                mafiaId,
                messageId,
                "✅ هدف انتخاب شد!"
            );

            // چک کردن آیا همه عملیات شب انجام شده
            await CheckNightActionsComplete(chatId);
        }

        private static async Task ProcessDoctorSave(long chatId, long doctorId, long targetId, int messageId)
        {
            var game = activeGames[chatId];
            game.SetDoctorTarget(targetId);

            await botClient.EditMessageTextAsync(
                doctorId,
                messageId,
                "✅ هدف برای نجات انتخاب شد!"
            );

            await CheckNightActionsComplete(chatId);
        }

        private static async Task ProcessSniperShoot(long chatId, long sniperId, long targetId, int messageId)
        {
            var game = activeGames[chatId];
            
            if (targetId != 0)
            {
                game.SetSniperTarget(targetId);
                var sniper = game.Players[sniperId];
                sniper.HasBullet = false;
            }

            await botClient.EditMessageTextAsync(
                sniperId,
                messageId,
                targetId == 0 ? "✅ شلیک نکردید!" : "✅ شلیک انجام شد!"
            );

            await CheckNightActionsComplete(chatId);
        }

        private static async Task CheckNightActionsComplete(long chatId)
        {
            var game = activeGames[chatId];

            if (game.AllNightActionsComplete())
            {
                await ProcessNightResults(chatId);
            }
        }

        private static async Task ProcessNightResults(long chatId)
        {
            var game = activeGames[chatId];
            var results = game.ProcessNight();

            await Task.Delay(2000);

            await botClient.SendTextMessageAsync(
                chatId,
                "☀️ **صبح شد!**\n\n" + results,
                parseMode: ParseMode.Markdown
            );

            // چک پایان بازی
            var winner = game.CheckWinCondition();
            if (winner != null)
            {
                await EndGame(chatId, winner);
                return;
            }

            // شروع فاز روز
            await StartDayPhase(chatId);
        }

        private static async Task StartDayPhase(long chatId)
        {
            var game = activeGames[chatId];
            game.CurrentPhase = GamePhase.Day;
            game.ClearVotes();

            var alivePlayers = game.GetAlivePlayers();
            var buttons = alivePlayers.Select(p => 
                new[] { InlineKeyboardButton.WithCallbackData($"👤 {p.Name}", $"vote_{p.Id}") }
            ).ToArray();

            var keyboard = new InlineKeyboardMarkup(buttons);

            await botClient.SendTextMessageAsync(
                chatId,
                "🗳️ **زمان رای‌گیری!**\n\n" +
                "به کسی که فکر می‌کنید مافیاست رای دهید:",
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard
            );
        }

        private static async Task ProcessVotingResults(long chatId)
        {
            var game = activeGames[chatId];
            var eliminated = game.ProcessVoting();

            if (eliminated != null)
            {
                await botClient.SendTextMessageAsync(
                    chatId,
                    $"⚖️ **نتیجه رای‌گیری:**\n\n" +
                    $"💀 {eliminated.Name} ({GetRoleInfo(eliminated.Role).Name}) اعدام شد!",
                    parseMode: ParseMode.Markdown
                );
            }
            else
            {
                await botClient.SendTextMessageAsync(
                    chatId,
                    "⚖️ **نتیجه رای‌گیری:**\n\n" +
                    "🤝 رای‌ها برابر شد! کسی اعدام نشد.",
                    parseMode: ParseMode.Markdown
                );
            }

            // چک پایان بازی
            var winner = game.CheckWinCondition();
            if (winner != null)
            {
                await EndGame(chatId, winner);
                return;
            }

            // ادامه به شب بعدی
            await Task.Delay(3000);
            await StartNightPhase(chatId);
        }

        private static async Task EndGame(long chatId, string winner)
        {
            var game = activeGames[chatId];
            
            var rolesList = string.Join("\n", game.Players.Values.Select(p => 
                $"{(p.IsAlive ? "✅" : "💀")} {p.Name} - {GetRoleInfo(p.Role).Name}"
            ));

            await botClient.SendTextMessageAsync(
                chatId,
                $"🏆 **بازی تمام شد!**\n\n" +
                $"🎉 برنده: **{winner}**\n\n" +
                $"📋 نقش‌ها:\n{rolesList}",
                parseMode: ParseMode.Markdown
            );

            activeGames.Remove(chatId);
        }

        private static async Task SendHelp(long chatId)
        {
            await botClient.SendTextMessageAsync(
                chatId,
                "📚 **راهنمای بازی مافیا**\n\n" +
                "🎮 دستورات:\n" +
                "/start - شروع بازی جدید\n" +
                "/help - نمایش راهنما\n\n" +
                "👥 نقش‌ها:\n" +
                "• شهروند - نقش عادی\n" +
                "• دکتر - هر شب یک نفر را نجات می‌دهد\n" +
                "• اسنایپر - یک گلوله دارد\n" +
                "• کارآگاه - هر شب هویت یک نفر را می‌فهمد\n" +
                "• تفنگدار - اگر مافیا به او شلیک کند، با خود می‌برد\n" +
                "• پدرخوانده - رئیس مافیا\n" +
                "• مافیا - عضو ساده مافیا\n" +
                "• بمب‌گذار - با مرگ، قاتلش را می‌کشد\n" +
                "• زودیاک - نقش مستقل، برای برد باید تنها بماند",
                parseMode: ParseMode.Markdown
            );
        }

        private static RoleInfo GetRoleInfo(Role role)
        {
            return role switch
            {
                Role.Citizen => new RoleInfo("شهروند", "یک شهروند عادی هستید", "مافیا را پیدا کنید و اعدام کنید"),
                Role.Doctor => new RoleInfo("دکتر", "پزشک شهر هستید", "هر شب یک نفر را از مرگ نجات دهید"),
                Role.Sniper => new RoleInfo("اسنایپر", "تک‌تیرانداز شهر با یک گلوله", "به مافیا شلیک کنید (فقط یک بار)"),
                Role.Detective => new RoleInfo("کارآگاه", "کارآگاه مخفی شهر", "هر شب نقش یک نفر را بفهمید"),
                Role.Gunsmith => new RoleInfo("تفنگدار", "مسلح و آماده دفاع", "اگر مافیا به شما حمله کند، قاتل هم می‌میرد"),
                Role.Godfather => new RoleInfo("پدرخوانده", "رئیس خانواده مافیا", "شهر را تصرف کنید"),
                Role.Mafia => new RoleInfo("مافیا", "عضو خانواده مافیا", "با پدرخوانده همکاری کنید"),
                Role.Bomber => new RoleInfo("بمب‌گذار", "مافیای انتحاری", "با مرگتان، قاتل را با خود ببرید"),
                Role.Zodiac => new RoleInfo("زودیاک", "قاتل زنجیره‌ای مستقل", "همه را بکشید و تنها بمانید"),
                _ => new RoleInfo("ناشناخته", "", "")
            };
        }

        private static Task HandleErrorAsync(ITelegramBotClient client, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"خطا: {exception.Message}");
            return Task.CompletedTask;
        }
    }

    // کلاس‌های مورد نیاز
    public class Game
    {
        public long CreatorId { get; set; }
        public Dictionary<long, Player> Players { get; set; }
        public bool IsStarted { get; set; }
        public GamePhase CurrentPhase { get; set; }
        public Dictionary<long, long> Votes { get; set; }
        public long? MafiaTarget { get; set; }
        public long? DoctorTarget { get; set; }
        public long? SniperTarget { get; set; }
        public int NightCount { get; set; }

        public Game(long creatorId)
        {
            CreatorId = creatorId;
            Players = new Dictionary<long, Player>();
            Votes = new Dictionary<long, long>();
            IsStarted = false;
            NightCount = 0;
        }

        public void AddPlayer(long id, string name)
        {
            Players[id] = new Player(id, name);
        }

        public void Start()
        {
            IsStarted = true;
            AssignRoles();
        }

        private void AssignRoles()
        {
            var playerCount = Players.Count;
            var roles = new List<Role>();

            // تقسیم نقش‌ها بر اساس تعداد بازیکنان
            if (playerCount <= 6)
            {
                roles.Add(Role.Godfather);
                roles.Add(Role.Doctor);
                roles.Add(Role.Sniper);
                while (roles.Count < playerCount) roles.Add(Role.Citizen);
            }
            else if (playerCount <= 10)
            {
                roles.Add(Role.Godfather);
                roles.Add(Role.Mafia);
                roles.Add(Role.Doctor);
                roles.Add(Role.Sniper);
                roles.Add(Role.Detective);
                if (playerCount >= 8) roles.Add(Role.Bomber);
                if (playerCount >= 9) roles.Add(Role.Zodiac);
                while (roles.Count < playerCount) roles.Add(Role.Citizen);
            }
            else
            {
                // بازی‌های بزرگتر
                roles.Add(Role.Godfather);
                roles.Add(Role.Mafia);
                roles.Add(Role.Mafia);
                roles.Add(Role.Bomber);
                roles.Add(Role.Doctor);
                roles.Add(Role.Sniper);
                roles.Add(Role.Detective);
                roles.Add(Role.Gunsmith);
                roles.Add(Role.Zodiac);
                while (roles.Count < playerCount) roles.Add(Role.Citizen);
            }

            // تصادفی کردن نقش‌ها
            var random = new Random();
            roles = roles.OrderBy(x => random.Next()).ToList();

            // اختصاص نقش‌ها
            var playersList = Players.Values.ToList();
            for (int i = 0; i < playersList.Count; i++)
            {
                playersList[i].Role = roles[i];
                if (playersList[i].Role == Role.Sniper)
                {
                    playersList[i].HasBullet = true;
                }
            }
        }

        public bool CanPlayerChat(long playerId)
        {
            if (!Players.ContainsKey(playerId)) return false;
            var player = Players[playerId];
            
            // مرده‌ها نمی‌توانند چت کنند
            if (!player.IsAlive) return false;
            
            // در فاز شب فقط مافیا می‌توانند چت کنند
            if (CurrentPhase == GamePhase.Night)
            {
                return player.Role == Role.Godfather || 
                       player.Role == Role.Mafia || 
                       player.Role == Role.Bomber;
            }
            
            return true;
        }

        public List<Player> GetAlivePlayers()
        {
            return Players.Values.Where(p => p.IsAlive).ToList();
        }

        public void AddVote(long voterId, long targetId)
        {
            Votes[voterId] = targetId;
        }

        public bool AllAlivePlayersVoted()
        {
            var aliveCount = GetAlivePlayers().Count;
            var votedCount = Votes.Count(v => Players[v.Key].IsAlive);
            return votedCount >= aliveCount;
        }

        public List<Player> GetPlayersNotVoted()
        {
            return GetAlivePlayers().Where(p => !Votes.ContainsKey(p.Id)).ToList();
        }

        public void SetMafiaTarget(long targetId)
        {
            MafiaTarget = targetId;
        }

        public void SetDoctorTarget(long targetId)
        {
            DoctorTarget = targetId;
        }

        public void SetSniperTarget(long targetId)
        {
            SniperTarget = targetId;
        }

        public bool AllNightActionsComplete()
        {
            var hasDoctor = GetAlivePlayers().Any(p => p.Role == Role.Doctor);
            var hasSniper = GetAlivePlayers().Any(p => p.Role == Role.Sniper && p.HasBullet);
            var hasMafia = GetAlivePlayers().Any(p => p.Role == Role.Godfather || p.Role == Role.Mafia || p.Role == Role.Bomber);

            if (hasMafia && !MafiaTarget.HasValue) return false;
            if (hasDoctor && !DoctorTarget.HasValue) return false;
            // اسنایپر اختیاری است

            return true;
        }

        public string ProcessNight()
        {
            NightCount++;
            var results = new List<string>();
            var deaths = new List<Player>();

            // عملیات مافیا
            if (MafiaTarget.HasValue && Players.ContainsKey(MafiaTarget.Value))
            {
                var target = Players[MafiaTarget.Value];
                
                // چک دکتر
                if (DoctorTarget.HasValue && DoctorTarget.Value == MafiaTarget.Value)
                {
                    results.Add($"💉 دکتر جان {target.Name} را نجات داد!");
                }
                else
                {
                    target.IsAlive = false;
                    deaths.Add(target);
                    results.Add($"💀 {target.Name} توسط مافیا کشته شد!");

                    // چک تفنگدار
                    if (target.Role == Role.Gunsmith)
                    {
                        var killer = GetAlivePlayers().FirstOrDefault(p => p.Role == Role.Godfather || p.Role == Role.Mafia);
                        if (killer != null)
                        {
                            killer.IsAlive = false;
                            deaths.Add(killer);
                            results.Add($"🔫 {target.Name} که تفنگدار بود، {killer.Name} را با خود برد!");
                        }
                    }
                }
            }

            // عملیات اسنایپر
            if (SniperTarget.HasValue && SniperTarget.Value != 0 && Players.ContainsKey(SniperTarget.Value))
            {
                var target = Players[SniperTarget.Value];
                target.IsAlive = false;
                deaths.Add(target);
                results.Add($"🎯 اسنایپر {target.Name} را کشت!");

                // چک بمب‌گذار
                if (target.Role == Role.Bomber)
                {
                    var sniper = GetAlivePlayers().FirstOrDefault(p => p.Role == Role.Sniper);
                    if (sniper != null)
                    {
                        sniper.IsAlive = false;
                        deaths.Add(sniper);
                        results.Add($"💣 {target.Name} که بمب‌گذار بود، اسنایپر را با خود برد!");
                    }
                }
            }

            // ریست کردن اکشن‌ها
            MafiaTarget = null;
            DoctorTarget = null;
            SniperTarget = null;

            return results.Count > 0 ? string.Join("\n", results) : "😴 شب آرامی بود! کسی نمرد.";
        }

        public Player ProcessVoting()
        {
            if (Votes.Count == 0) return null;

            // شمارش آرا
            var voteCounts = new Dictionary<long, int>();
            foreach (var vote in Votes.Where(v => Players[v.Key].IsAlive))
            {
                if (!voteCounts.ContainsKey(vote.Value))
                    voteCounts[vote.Value] = 0;
                voteCounts[vote.Value]++;
            }

            // پیدا کردن بیشترین رای
            var maxVotes = voteCounts.Values.Max();
            var topVoted = voteCounts.Where(v => v.Value == maxVotes).ToList();

            // اگر برابر بود
            if (topVoted.Count > 1) return null;

            var eliminatedId = topVoted.First().Key;
            var eliminated = Players[eliminatedId];
            eliminated.IsAlive = false;

            // چک بمب‌گذار
            if (eliminated.Role == Role.Bomber)
            {
                // یک رای‌دهنده تصادفی هم می‌میرد
                var voters = Votes.Where(v => v.Value == eliminatedId && Players[v.Key].IsAlive).Select(v => v.Key).ToList();
                if (voters.Count > 0)
                {
                    var random = new Random();
                    var victimId = voters[random.Next(voters.Count)];
                    Players[victimId].IsAlive = false;
                }
            }

            return eliminated;
        }

        public void ClearVotes()
        {
            Votes.Clear();
        }

        public string CheckWinCondition()
        {
            var alivePlayers = GetAlivePlayers();
            var aliveMafia = alivePlayers.Where(p => p.Role == Role.Godfather || p.Role == Role.Mafia || p.Role == Role.Bomber).ToList();
            var aliveCitizens = alivePlayers.Where(p => p.Role != Role.Godfather && p.Role != Role.Mafia && p.Role != Role.Bomber && p.Role != Role.Zodiac).ToList();
            var aliveZodiac = alivePlayers.FirstOrDefault(p => p.Role == Role.Zodiac);

            // زودیاک برنده شده
            if (aliveZodiac != null && alivePlayers.Count == 1)
            {
                return "🔮 زودیاک";
            }

            // مافیا برنده شده
            if (aliveMafia.Count >= aliveCitizens.Count)
            {
                return "🔴 مافیا";
            }

            // شهر برنده شده
            if (aliveMafia.Count == 0)
            {
                return "🔵 شهروندان";
            }

            return null; // بازی ادامه دارد
        }
    }

    public class Player
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public Role Role { get; set; }
        public bool IsAlive { get; set; }
        public bool HasBullet { get; set; } // برای اسنایپر

        public Player(long id, string name)
        {
            Id = id;
            Name = name;
            IsAlive = true;
            HasBullet = false;
        }
    }

    public enum Role
    {
        Citizen,     // شهروند
        Doctor,      // دکتر
        Sniper,      // اسنایپر
        Detective,   // کارآگاه
        Gunsmith,    // تفنگدار
        Godfather,   // پدرخوانده
        Mafia,       // مافیا ساده
        Bomber,      // بمب‌گذار
        Zodiac       // زودیاک
    }

    public enum GamePhase
    {
        Waiting,
        Night,
        Day
    }

    public class RoleInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Task { get; set; }

        public RoleInfo(string name, string desc, string task)
        {
            Name = name;
            Description = desc;
            Task = task;
        }
    }
}
