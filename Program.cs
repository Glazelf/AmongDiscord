﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
        static int tableWidth = 100;
        private const string ConfigPath = "config.json";

        private static string[] playerColors = new string[] { "Red", "Blue", "Green", "Pink", "Orange", "Yellow", "Black", "White", "Purple", "Brown", "Cyan", "Lime" };
        public static void Main(string[] args)
            => new Program().MainAsync().GetAwaiter().GetResult();
        public async Task MainAsync()
        {
            var configurationBuilder = new ConfigurationBuilder()
       .AddJsonFile("config.json");
            var myConfiguration = configurationBuilder.Build();
            string token = myConfiguration["token"];

            _client = new DiscordSocketClient();

            await _client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);
            await _client.StartAsync();

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
                    Console.WriteLine($"{IDString,-2} {NameString,-15} {ColorString,-15} {ImpostorString,-15} {DeadString,-10} {DisconnectedString}");
                    Console.ForegroundColor = ConsoleColor.White;
                }
                Console.WriteLine($"Game state: {state}");

                if (state != oldState)
                {
                    if (state == GameState.DISCUSSION)
                    {
                        Console.WriteLine("Discussion started");
                    }
                    else
                    {
                        Console.WriteLine("Nondiscussion lol");
                    }
                }

                oldState = state;
                Thread.Sleep(2500);
            }
        }

        private static bool ExileEndsGame()
        {
            return false;
        }

        public class CommandHandler
        {
            private readonly DiscordSocketClient _client;
            private readonly CommandService _commands;

            // Retrieve client and CommandService instance via ctor
            public CommandHandler(DiscordSocketClient client, CommandService commands)
            {
                _commands = commands;
                _client = client;
            }

            public async Task InstallCommandsAsync()
            {
                // Hook the MessageReceived event into our command handler
                _client.MessageReceived += HandleCommandAsync;

                await _commands.AddModulesAsync(assembly: Assembly.GetEntryAssembly(),
                                                services: null);
            }

            private async Task HandleCommandAsync(SocketMessage messageParam)
            {
                // Don't process the command if it was a system message
                var message = messageParam as SocketUserMessage;
                if (message == null) return;

                // Create a number to track where the prefix ends and the command begins
                int argPos = 0;

                // Determine if the message is a command based on the prefix and make sure no bots trigger commands
                if (!(message.HasCharPrefix('!', ref argPos) ||
                    message.HasMentionPrefix(_client.CurrentUser, ref argPos)) ||
                    message.Author.IsBot)
                    return;

                // Create a WebSocket-based command context based on the message
                var context = new SocketCommandContext(_client, message);

                // Execute the command with the command context we just
                // created, along with the service provider for precondition checks.
                await _commands.ExecuteAsync(
                    context: context,
                    argPos: argPos,
                    services: null);
            }
        }

        public class InfoModule : ModuleBase<SocketCommandContext>
        {
            [Command("help")]
            [Summary("Replies with command list.")]
            public Task HelpAsync([Remainder][Summary("The text to echo")] string echo)
                => ReplyAsync(echo);

            [Command("info")]
            [Summary("Replies with bot info.")]
            public Task InfoAsync([Remainder][Summary("The text to echo")] string echo)
=> ReplyAsync(echo);
        }
    }
}
