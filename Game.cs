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
    public class Game
    {
        private readonly ITelegramBotClient _botClient;
        private readonly long _chatId;
        private readonly Random _random = new();
        
        public long CreatorId { get; }
        public Dictionary<long, Player> Players { get; } = new();
        public bool IsStarted { get; private set; }
        public GamePhase CurrentPhase { get; private set; } = GamePhase.Waiting;
        public int DayNumber { get; private set; } = 0;
        public int MaxPlayers { get; } = 10; // حداکثر تعداد بازیکنان
        private Dictionary<long, long> _nightActions = new();
        private Dictionary<long, int> _dayVotes = new();
        private long? _mafiaTarget;
        private long? _doctorSave;
        private long? _detectiveCheck;

        public Game(ITelegramBotClient botClient, long chatId, long creatorId)
        {
            _botClient = botClient;
            _chatId = chatId;
            CreatorId = creatorId;
        }

        public bool AddPlayer(long userId, string userName)
        {
            if (Players.ContainsKey(userId))
                return false;

            Players[userId] = new Player
            {
                Id = userId,
                Name = userName,
                IsAlive = true
            };
            
            return true;
        }

        public async Task StartGame(CancellationToken cancellationToken)
        {
            if (Players.Count < 4)
            {
                await _botClient.SendTextMessageAsync(_chatId, 
                    "❌ حداقل 4 بازیکن برای شروع بازی نیاز است!", 
                    cancellationToken: cancellationToken);
                return;
            }

            IsStarted = true;
            await AssignRoles();
            await SendRolesToPlayers(cancellationToken);
            await StartDay(cancellationToken);
        }

        private async Task AssignRoles()
        {
            var playerList = Players.Values.ToList();
            var shuffled = playerList.OrderBy(x => _random.Next()).ToList();

            int mafiaCount = Math.Max(1, playerList.Count / 3);
            
            // تعیین مافیا
            for (int i = 0; i < mafiaCount; i++)
            {
                shuffled[i].Role = Role.Mafia;
            }

            // تعیین دکتر
            if (playerList.Count >= 5)
            {
                shuffled[mafiaCount].Role = Role.Doctor;
            }

            // تعیین کاراگاه
            if (playerList.Count >= 6)
            {
                shuffled[mafiaCount + 1].Role = Role.Detective;
            }

            // بقیه شهروند
            foreach (var player in shuffled.Where(p => p.Role == Role.None))
            {
                player.Role = Role.Citizen;
            }
        }

        private async Task SendRolesToPlayers(CancellationToken cancellationToken)
        {
            foreach (var player in Players.Values)
            {
                string roleMessage = player.Role switch
                {
                    Role.Mafia => "🔫 شما مافیا هستید! هر شب می‌توانید یک نفر را بکشید.",
                    Role.Doctor => "👨‍⚕️ شما دکتر هستید! هر شب می‌توانید یک نفر را نجات دهید.",
                    Role.Detective => "🕵️ شما کاراگاه هستید! هر شب می‌توانید نقش یک نفر را بفهمید.",
                    Role.Citizen => "🏃 شما شهروند هستید! سعی کنید مافیاها را پیدا کنید.",
                    _ => "نقش نامشخص"
                };

                try
                {
                    await _botClient.SendTextMessageAsync(player.Id, 
                        $"🎭 بازی مافیا شروع شد!\n\n{roleMessage}", 
                        cancellationToken: cancellationToken);
                }
                catch
                {
                    // اگر نتوانستیم پیام خصوصی بفرستیم
                }
            }

            // اعلام مافیاها به هم
            var mafias = Players.Values.Where(p => p.Role == Role.Mafia).ToList();
            if (mafias.Count > 1)
            {
                foreach (var mafia in mafias)
                {
                    var otherMafias = mafias.Where(m => m.Id != mafia.Id).Select(m => m.Name);
                    try
                    {
                        await _botClient.SendTextMessageAsync(mafia.Id,
                            $"🤝 هم‌تیمی‌های مافیای شما: {string.Join(", ", otherMafias)}",
                            cancellationToken: cancellationToken);
                    }
                    catch { }
                }
            }
        }

        private async Task StartDay(CancellationToken cancellationToken)
        {
            DayNumber++;
            CurrentPhase = GamePhase.Day;
            _dayVotes.Clear();

            var aliveCount = Players.Values.Count(p => p.IsAlive);
            var mafiaCount = Players.Values.Count(p => p.IsAlive && p.Role == Role.Mafia);

            await _botClient.SendTextMessageAsync(_chatId,
                $"☀️ روز {DayNumber} فرا رسید!\n" +
                $"👥 بازیکنان زنده: {aliveCount}\n\n" +
                "برای رای دادن، روی نام فرد مورد نظر در لیست زیر کلیک کنید:",
                cancellationToken: cancellationToken);

            await ShowVotingKeyboard(cancellationToken);

            // بعد از 60 ثانیه به شب برویم
            _ = Task.Run(async () =>
            {
                await Task.Delay(60000);
                if (CurrentPhase == GamePhase.Day && IsStarted)
                {
                    await EndDayVoting(cancellationToken);
                }
            });
        }

        private async Task ShowVotingKeyboard(CancellationToken cancellationToken)
        {
            var alivePlayers = Players.Values.Where(p => p.IsAlive).ToList();
            var buttons = new List<List<InlineKeyboardButton>>();

            foreach (var player in alivePlayers)
            {
                buttons.Add(new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData($"🗳 {player.Name}", $"vote_{player.Id}")
                });
            }

            var keyboard = new InlineKeyboardMarkup(buttons);
            await _botClient.SendTextMessageAsync(_chatId, "انتخاب کنید:", replyMarkup: keyboard, cancellationToken: cancellationToken);
        }

        private async Task EndDayVoting(CancellationToken cancellationToken)
        {
            if (_dayVotes.Any())
            {
                var mostVoted = _dayVotes.GroupBy(v => v.Value)
                    .OrderByDescending(g => g.Count())
                    .First()
                    .Key;

                var votedPlayer = Players[mostVoted];
                votedPlayer.IsAlive = false;

                await _botClient.SendTextMessageAsync(_chatId,
                    $"⚖️ بر اساس رای‌گیری، {votedPlayer.Name} اعدام شد!\n" +
                    $"نقش او: {GetRoleName(votedPlayer.Role)}",
                    cancellationToken: cancellationToken);
            }
            else
            {
                await _botClient.SendTextMessageAsync(_chatId,
                    "⚖️ هیچ رایی داده نشد! کسی اعدام نمی‌شود.",
                    cancellationToken: cancellationToken);
            }

            if (await CheckGameEnd(cancellationToken))
                return;

            await StartNight(cancellationToken);
        }

        private async Task StartNight(CancellationToken cancellationToken)
        {
            CurrentPhase = GamePhase.Night;
            _nightActions.Clear();
            _mafiaTarget = null;
            _doctorSave = null;
            _detectiveCheck = null;

            await _botClient.SendTextMessageAsync(_chatId,
                $"🌙 شب {DayNumber} فرا رسید!\n" +
                "نقش‌های ویژه به پیام خصوصی ربات مراجعه کنند.",
                cancellationToken: cancellationToken);

            // ارسال دستورات به نقش‌های ویژه
            await SendNightActions(cancellationToken);

            // بعد از 45 ثانیه شب تمام شود
            _ = Task.Run(async () =>
            {
                await Task.Delay(45000);
                if (CurrentPhase == GamePhase.Night && IsStarted)
                {
                    await EndNight(cancellationToken);
                }
            });
        }

        private async Task SendNightActions(CancellationToken cancellationToken)
        {
            var alivePlayers = Players.Values.Where(p => p.IsAlive).ToList();

            // مافیا
            foreach (var mafia in Players.Values.Where(p => p.IsAlive && p.Role == Role.Mafia))
            {
                var targets = alivePlayers.Where(p => p.Role != Role.Mafia).ToList();
                var buttons = targets.Select(t => new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData($"🎯 {t.Name}", $"kill_{t.Id}")
                }).ToList();

                var keyboard = new InlineKeyboardMarkup(buttons);
                
                try
                {
                    await _botClient.SendTextMessageAsync(mafia.Id,
                        "🔫 کدام بازیکن را می‌خواهید بکشید؟",
                        replyMarkup: keyboard,
                        cancellationToken: cancellationToken);
                }
                catch { }
            }

            // دکتر
            var doctor = Players.Values.FirstOrDefault(p => p.IsAlive && p.Role == Role.Doctor);
            if (doctor != null)
            {
                var buttons = alivePlayers.Select(t => new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData($"💉 {t.Name}", $"save_{t.Id}")
                }).ToList();

                var keyboard = new InlineKeyboardMarkup(buttons);
                
                try
                {
                    await _botClient.SendTextMessageAsync(doctor.Id,
                        "👨‍⚕️ کدام بازیکن را می‌خواهید نجات دهید؟",
                        replyMarkup: keyboard,
                        cancellationToken: cancellationToken);
                }
                catch { }
            }

            // کاراگاه
            var detective = Players.Values.FirstOrDefault(p => p.IsAlive && p.Role == Role.Detective);
            if (detective != null)
            {
                var targets = alivePlayers.Where(p => p.Id != detective.Id).ToList();
                var buttons = targets.Select(t => new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData($"🔍 {t.Name}", $"check_{t.Id}")
                }).ToList();

                var keyboard = new InlineKeyboardMarkup(buttons);
                
                try
                {
                    await _botClient.SendTextMessageAsync(detective.Id,
                        "🕵️ نقش کدام بازیکن را می‌خواهید بفهمید؟",
                        replyMarkup: keyboard,
                        cancellationToken: cancellationToken);
                }
                catch { }
            }
        }

        private async Task EndNight(CancellationToken cancellationToken)
        {
            var messages = new List<string>();
            messages.Add($"☀️ صبح روز {DayNumber + 1} شد!");

            // اعمال نتیجه عملیات مافیا
            if (_mafiaTarget.HasValue && _mafiaTarget != _doctorSave)
            {
                var victim = Players[_mafiaTarget.Value];
                victim.IsAlive = false;
                messages.Add($"💀 {victim.Name} کشته شد! نقش: {GetRoleName(victim.Role)}");
            }
            else if (_mafiaTarget.HasValue && _mafiaTarget == _doctorSave)
            {
                messages.Add("💉 دکتر موفق شد جان کسی را نجات دهد!");
            }
            else
            {
                messages.Add("🌅 شب آرامی بود، کسی کشته نشد.");
            }

            // نتیجه کاراگاه
            if (_detectiveCheck.HasValue)
            {
                var detective = Players.Values.First(p => p.IsAlive && p.Role == Role.Detective);
                var checkedPlayer = Players[_detectiveCheck.Value];
                var roleInfo = checkedPlayer.Role == Role.Mafia ? "مافیاست! 🔫" : "مافیا نیست ✅";
                
                try
                {
                    await _botClient.SendTextMessageAsync(detective.Id,
                        $"🔍 نتیجه بررسی: {checkedPlayer.Name} {roleInfo}",
                        cancellationToken: cancellationToken);
                }
                catch { }
            }

            await _botClient.SendTextMessageAsync(_chatId,
                string.Join("\n", messages),
                cancellationToken: cancellationToken);

            if (await CheckGameEnd(cancellationToken))
                return;

            await StartDay(cancellationToken);
        }

        private async Task<bool> CheckGameEnd(CancellationToken cancellationToken)
        {
            var alivePlayers = Players.Values.Where(p => p.IsAlive).ToList();
            var aliveMafia = alivePlayers.Count(p => p.Role == Role.Mafia);
            var aliveCitizens = alivePlayers.Count - aliveMafia;

            if (aliveMafia == 0)
            {
                await _botClient.SendTextMessageAsync(_chatId,
                    "🎉 شهروندان برنده شدند! همه مافیاها کشته شدند.",
                    cancellationToken: cancellationToken);
                await ShowFinalResults(cancellationToken);
                IsStarted = false;
                return true;
            }

            if (aliveMafia >= aliveCitizens)
            {
                await _botClient.SendTextMessageAsync(_chatId,
                    "😈 مافیا برنده شد! تعداد مافیاها با شهروندان برابر شد.",
                    cancellationToken: cancellationToken);
                await ShowFinalResults(cancellationToken);
                IsStarted = false;
                return true;
            }

            return false;
        }

        private async Task ShowFinalResults(CancellationToken cancellationToken)
        {
            var results = "📊 نتایج نهایی:\n\n";
            foreach (var player in Players.Values)
            {
                var status = player.IsAlive ? "زنده ✅" : "مرده 💀";
                results += $"{player.Name} - {GetRoleName(player.Role)} - {status}\n";
            }

            await _botClient.SendTextMessageAsync(_chatId, results, cancellationToken: cancellationToken);
        }

        public async Task ProcessGameMessage(Message message, CancellationToken cancellationToken)
        {
            // پردازش callback queries برای رای‌گیری و اعمال شبانه
            if (message.Text?.StartsWith("/vote_") == true)
            {
                var targetId = long.Parse(message.Text.Split('_')[1]);
                if (CurrentPhase == GamePhase.Day && Players[message.From.Id].IsAlive)
                {
                    _dayVotes[message.From.Id] = (int)targetId;
                    await _botClient.SendTextMessageAsync(message.Chat.Id,
                        $"✅ {message.From.Username} رای داد.",
                        cancellationToken: cancellationToken);
                }
            }
        }

        public async Task ProcessCallbackQuery(CallbackQuery query, CancellationToken cancellationToken)
        {
            var data = query.Data;
            var userId = query.From.Id;

            if (!Players.ContainsKey(userId) || !Players[userId].IsAlive)
                return;

            if (data.StartsWith("vote_") && CurrentPhase == GamePhase.Day)
            {
                var targetId = long.Parse(data.Split('_')[1]);
                _dayVotes[userId] = (int)targetId;
                await _botClient.AnswerCallbackQueryAsync(query.Id, "رای شما ثبت شد ✅", cancellationToken: cancellationToken);
            }
            else if (data.StartsWith("kill_") && CurrentPhase == GamePhase.Night && Players[userId].Role == Role.Mafia)
            {
                _mafiaTarget = long.Parse(data.Split('_')[1]);
                await _botClient.AnswerCallbackQueryAsync(query.Id, "هدف انتخاب شد 🎯", cancellationToken: cancellationToken);
            }
            else if (data.StartsWith("save_") && CurrentPhase == GamePhase.Night && Players[userId].Role == Role.Doctor)
            {
                _doctorSave = long.Parse(data.Split('_')[1]);
                await _botClient.AnswerCallbackQueryAsync(query.Id, "فرد مورد نظر محافظت می‌شود 💉", cancellationToken: cancellationToken);
            }
            else if (data.StartsWith("check_") && CurrentPhase == GamePhase.Night && Players[userId].Role == Role.Detective)
            {
                _detectiveCheck = long.Parse(data.Split('_')[1]);
                await _botClient.AnswerCallbackQueryAsync(query.Id, "در حال بررسی... 🔍", cancellationToken: cancellationToken);
            }
        }

        private string GetRoleName(Role role)
        {
            return role switch
            {
                Role.Mafia => "🔫 مافیا",
                Role.Doctor => "👨‍⚕️ دکتر",
                Role.Detective => "🕵️ کاراگاه",
                Role.Citizen => "🏃 شهروند",
                _ => "نامشخص"
            };
        }
    }

    public enum GamePhase
    {
        Waiting,
        Day,
        Night
    }

    public enum Role
    {
        None,
        Citizen,
        Mafia,
        Doctor,
        Detective
    }

    public class Player
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public Role Role { get; set; } = Role.None;
        public bool IsAlive { get; set; } = true;
    }
}
