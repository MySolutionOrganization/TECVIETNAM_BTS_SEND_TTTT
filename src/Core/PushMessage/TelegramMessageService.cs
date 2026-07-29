using BtsGetwayService;
using Core.Setting;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Core.PushMessage
{
    public class TelegramMessageService : IPushMessageService
    {
        public readonly ITelegramBotClient _botClient;
        public readonly TelegramConfig _telegramConfig;
        public TelegramMessageService(
            ITelegramBotClient botClient,
            IOptions<TelegramConfig> telegramConfig
        )
        {
            _botClient = botClient;
            _telegramConfig = telegramConfig.Value;
        }

        [Obsolete]
        public async Task SendMessageAsync(string message)
        {
            var ip = Utility.GetLocalIpAddress();
            try
            {
                var chatId = new ChatId(_telegramConfig.ChatId);
                var text = $"Server: {_telegramConfig.ServerName}.\nĐịa chỉ IP:{ip}\n{message}";

                var result = await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: text,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
                );
                
            }
            catch (Exception ex)
            {                
            }            
        }
    }
}
