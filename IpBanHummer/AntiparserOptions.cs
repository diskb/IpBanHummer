using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;

#nullable enable

namespace IpBanHammer
{
    /// <summary>Опции для сервиса <see cref="AntiparserAccounter"/> и фильтра <see cref="AntiparserFilter"/>. Чтобы не плодить сущности</summary>
    public class AntiparserOptions : BanAccounterOptions<string?>
    {
        private Func<ActionContext, string?>? ipKeyExtractor;

        public AntiparserOptions() : base()
        {
            //Для режима антипарсера по умолчанию стоит слегка изменить дефолтные лимиты и коэффициенты аккаунтера
            BadCountLimit = 150;
            BanTime = TimeSpan.FromMinutes(5);
            BanTimeCacheStoreMultiplier = 2;
            LimitBadMultiplierForNextIncident = 1.15; //прогрессивная шкала
        }

        /// <summary>Делегат для извлечения IP-адреса или другого идентификатора из HTTP контекста.
        /// Должен возвращать "идентификатор" клиента для регистрации плохих событий и последущего бана.
        /// По умолчанию Ip-адрес с учётом перечисленных реверс-прокси <see cref="ReverseProxyIps"/>.
        /// </summary>
        /// <example>(context) => context.HttpContext.Connection.RemoteIpAddress.ToString();
        /// (context) => context.HttpContext.RemoteIpRegardsXForwardedFor(new[] { "192.168.0.5" }) + context.HttpContext.Request.Headers["User-Agent"];
        /// (context) => context.HttpContext.RemoteIpRegardsXForwardedFor(ReverseProxyIps);
        /// </example>
        public Func<ActionContext, string?> IpKeyExtractor
        {
            get
            {
                if (ipKeyExtractor == null)
                    ipKeyExtractor = (context) =>
                    context.HttpContext.RemoteIpRegardsXForwardedFor(ReverseProxyIps);
                return ipKeyExtractor;
            }

            set => ipKeyExtractor = value;
        }

        //TODO: реализовать логику IpKeySkipBadAction
        public Func<ActionContext, string?>? IpKeySkipBadAction { get; set; }

        /// <summary>Проверять условия и в случае чего автоматически регистрировать 
        /// "плохое" действие после выполнения Action'а. По умолчанию true.</summary>
        public bool RegisterBadActionOnActionExecuted { get; set; } = true;

        /// <summary>
        /// Регистрировать плохое действие, если разница с предыдущим запросом меньше этого времени
        /// </summary>
        public TimeSpan RegisterBadActionIfPreviousRequestIsLess { get; set; } = TimeSpan.FromSeconds(1.5);

        /// <summary>
        /// Уменьшить счётчик плохих сработок, если разница с предыдущим запросом больше этого времени
        /// </summary>
        public TimeSpan DecreaseCouterIfPreviousRequestIsMore { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Http-статускод, который будет возвращён при обращении забаненного юзера.
        /// </summary>
        public int DefaultBanStatusCode { get; set; } = StatusCodes.Status429TooManyRequests;

        /// <summary>Список из Ip реверс-прокси серверов, стоящих перед защищаемым сервером</summary>
        public IEnumerable<string> ReverseProxyIps { get; set; } = Array.Empty<string>();
    }
}
