using Discord;
using Discord.Net;
using Discord.WebSocket;
using Discord.Commands;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

public class InfoModule : ModuleBase<SocketCommandContext>
{
    [Command("help")]
    [Summary("Replies with command list.")]
    public Task HelpAsync([Remainder][Summary("The text to echo")] string echo)
        => ReplyAsync($"sup fam");
    [Command("info")]
    [Summary("Replies with bot info.")]
    public Task InfoAsync([Remainder][Summary("The text to echo")] string echo)
        => ReplyAsync("what");
}
