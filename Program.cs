using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Reflection;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions;
using Microsoft.Extensions.Configuration;

namespace AmongUsCapture
{
    class Program
    {
        private static bool muteAfterExile = true;

        private enum GameState
        {
            LOBBY,
            TASKS,
            DISCUSSION
        }
        private static IntPtr GameAssemblyPtr = IntPtr.Zero;
        private static IntPtr UnityPlayerPtr = IntPtr.Zero;
        private static GameState oldState = GameState.LOBBY;
        private DiscordSocketClient _client;
        private const string ConfigPath = "config.json";

        private static string[] playerColors = new string[] { "Red", "Blue", "Green", "Pink", "Orange", "Yellow", "Black", "White", "Purple", "Brown", "Cyan", "Lime" };
        public static void Main(string[] args)
            => new Program().MainAsync().GetAwaiter().GetResult();
        public async Task MainAsync()
        {
            var configurationBuilder = new ConfigurationBuilder()
       .AddJsonFile("config.json");
            var config = configurationBuilder.Build();
            string token = config["token"];

            _client = new DiscordSocketClient();
            _client.MessageReceived += CommandHandler;
            await _client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);
            await _client.StartAsync();

            var clientReady = 0;
            _client.Ready += () =>
            {
                Console.WriteLine("Bot is connected!");
                clientReady = 1;
                return Task.CompletedTask;
            };

            while (true)
            {
                if (!ProcessMemory.IsHooked)
                {
                    if (!ProcessMemory.HookProcess("Among Us"))
                    {
                        Thread.Sleep(1000);
                        continue;
                    }
                    else
                    {
                        Console.WriteLine("Connected to Among Us process ({0})", ProcessMemory.process.Id);

                        int modulesLeft = 2;
                        foreach (ProcessMemory.Module module in ProcessMemory.modules)
                        {
                            if (modulesLeft == 0)
                                break;
                            else if (module.Name.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
                            {
                                GameAssemblyPtr = module.BaseAddress;
                                modulesLeft--;
                            }
                            else if (module.Name.Equals("UnityPlayer.dll", StringComparison.OrdinalIgnoreCase))
                            {
                                UnityPlayerPtr = module.BaseAddress;
                                modulesLeft--;
                            }
                        }
                    }
                }

                GameState state;
                bool inGame = ProcessMemory.Read<bool>(UnityPlayerPtr, 0x127B310, 0xF4, 0x18, 0xA8);
                bool inMeeting = ProcessMemory.Read<bool>(UnityPlayerPtr, 0x12A7A14, 0x64, 0x54, 0x18);
                int meetingHudState = ProcessMemory.Read<int>(GameAssemblyPtr, 0xDA58D0, 0x5C, 0, 0x84);

                IntPtr allPlayersPtr = ProcessMemory.Read<IntPtr>(GameAssemblyPtr, 0xDA5A60, 0x5C, 0, 0x24);
                IntPtr allPlayers = ProcessMemory.Read<IntPtr>(allPlayersPtr, 0x08);
                int playerCount = ProcessMemory.Read<int>(allPlayersPtr, 0x0C);

                IntPtr playerAddrPtr = allPlayers + 0x10;

                if (!inGame || (inMeeting && meetingHudState > 2 && ExileEndsGame()))
                {
                    state = GameState.LOBBY;
                }
                else if (inMeeting && (muteAfterExile || meetingHudState < 4))
                {
                    state = GameState.DISCUSSION;
                }
                else
                {
                    state = GameState.TASKS;
                }

                List<PlayerInfo> allPlayerInfos = new List<PlayerInfo>();

                for (int i = 0; i < playerCount; i++)
                {
                    IntPtr playerAddr = ProcessMemory.Read<IntPtr>(playerAddrPtr);
                    PlayerInfo pi = ProcessMemory.Read<PlayerInfo>(playerAddr);
                    allPlayerInfos.Add(pi);
                    playerAddrPtr += 4;
                }

                Console.Clear();
                if (config["cheats"] == "true")
                {
                    foreach (PlayerInfo pi in allPlayerInfos)
                    {
                        var IDString = $"ID: {pi.PlayerId}";
                        var NameString = $"Name: {ProcessMemory.ReadString((IntPtr)pi.PlayerName)}";
                        var ColorString = $"Color: {playerColors[pi.ColorId]}";
                        var ImpostorString = $"Impostor: {((pi.IsImpostor > 0) ? "Yes" : "No")}";
                        var DeadString = $"Dead: {((pi.IsDead > 0) ? "Yes" : "No")}";
                        var DisconnectedString = $"Disconnected: {((pi.Disconnected > 0) ? "Yes" : "No")}";
                        if (pi.IsImpostor > 0) Console.ForegroundColor = ConsoleColor.Cyan;
                        if (pi.IsDead > 0) Console.ForegroundColor = ConsoleColor.Red;
                        if (pi.Disconnected > 0) Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine($"{IDString,-2} {NameString,-18} {ColorString,-15} {ImpostorString,-15} {DeadString,-10} {DisconnectedString}");
                        Console.ForegroundColor = ConsoleColor.White;
                    }
                }
                Console.WriteLine($"Game state: {state}");

                if (state == GameState.TASKS)
                {
                    if (config["discord"] == "true" && clientReady == 1)
                    {
                        ulong ownerID = Convert.ToUInt64(config["ownerID"]);
                        ulong guildID = Convert.ToUInt64(config["guildID"]);
                        var guild = _client.GetGuild(guildID);
                        var guildOwner = guild.GetUser(ownerID);
                        var Socketowner = _client.GetUser(ownerID);
                        IVoiceChannel VC = (guildOwner as IVoiceState).VoiceChannel;
                        SocketVoiceChannel VCSocket = (SocketVoiceChannel)VC;
                        var VCUsers = VCSocket.Users;
                        foreach (var user in VCUsers)
                        {
                            await MuteUser(guild, user);
                        }
                    }
                    Console.WriteLine("Shh!");
                }
                else
                {
                    if (config["discord"] == "true" && clientReady == 1)
                    {
                        ulong ownerID = Convert.ToUInt64(config["ownerID"]);
                        ulong guildID = Convert.ToUInt64(config["guildID"]);
                        var guild = _client.GetGuild(guildID);
                        var guildOwner = guild.GetUser(ownerID);
                        var Socketowner = _client.GetUser(ownerID);
                        IVoiceChannel VC = (guildOwner as IVoiceState).VoiceChannel;
                        SocketVoiceChannel VCSocket = (SocketVoiceChannel)VC;
                        var VCUsers = VCSocket.Users;
                        foreach (var user in VCUsers)
                        {
                            await UnmuteUser(guild, user);
                        }
                    }
                    Console.WriteLine("Talky time!");
                }
                oldState = state;
                Thread.Sleep(2500);
            }

        }
        private static bool ExileEndsGame()
        {
            return false;
        }

        private Task CommandHandler(SocketMessage message)
        {
            var configurationBuilder = new ConfigurationBuilder()
       .AddJsonFile("config.json");
            var config = configurationBuilder.Build();

            if (!message.Content.StartsWith(config["prefix"]) || message.Author.IsBot)
                return Task.CompletedTask;

            string command = message.Content.Substring(1, message.Content.Length - 1).ToLower();

            if (command.Equals("info"))
            {
                message.Channel.SendMessageAsync($@"> Hello {message.Author.Mention}.
> Github: https://github.com/Glazelf/AmongDiscord");
            }

            if (command.Equals("help"))
            {
                message.Channel.SendMessageAsync($@"> Hello {message.Author.Mention}.
> Command List: https://github.com/Glazelf/AmongDiscord/wiki/Commands
> Discord: https://discord.gg/2gkybyu");
            }

            return Task.CompletedTask;
        }

        public Task MuteUser(IGuild guild, IUser user)
        {
            return (user as IGuildUser).ModifyAsync(x => x.Mute = true);
        }
        public Task UnmuteUser(IGuild guild, IUser user)
        {
            return (user as IGuildUser).ModifyAsync(x => x.Mute = false);
        }
    }
}