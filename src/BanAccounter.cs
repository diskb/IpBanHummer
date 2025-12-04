using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;

#nullable enable

namespace IpBanHammer
{
    /// <summary>Банилка IP адресов и ваще чего-бы то ни было. Класс для ведения учёта плохих действий юзера и выдачи результата:
    /// забанен объект или нет. Требуется зависимость <see cref="IMemoryCache"/></summary>
    /// <remarks>
    /// На приложениях за разными прокси, с внутренни API-сервисами и прочих микросервисах, айпишник 99% будет локальным/постоянным/одним и тем же!
    /// Банхаммер как нехрен делать забанит сервис или реверс-прокси, если просто юзать IPAddress соединения из http-контекста. В этих случаях стоит
    /// использовать  извлечение и проверку реального IP через дополнительные заголовки от микросервисов или методы расширения вроде
    /// <see cref="BanAccounterExtensions.RemoteIpRegardsXForwardedFor(HttpContext, IEnumerable{string}, bool)"/>
    /// </remarks>
    /// <typeparam name="TKey">Тип значения ключа-идентификатора для сущностей в коллекции банов. Например, IP адрес или связка ip-юзерагент и т.д.
    /// Допустимы сложные типы, например, кортеж из типа объекта-потребителя сервиса и строкового значения ip-адреса.
    /// Не использовать в качестве типа ключа ссылочный тип на объекты, если они потенциально могут быть "не статическими" и не постоянными.
    /// Например, вместо экземпляра класса контроллера следует использовать его тип или просто строку</typeparam>
    public class BanAccounter<TKey>
    {
        protected readonly IMemoryCache _memCache;
        private readonly IOptionsMonitor<BanAccounterOptions<TKey>> _options;
        private readonly ILogger<BanAccounter<TKey>>? _logger;
        private BanAccounterOptions<TKey> Options => _options.CurrentValue;

        public BanAccounter(IMemoryCache cache, IOptionsMonitor<BanAccounterOptions<TKey>> options, ILogger<BanAccounter<TKey>>? logger)
        {
            _memCache = cache;
            _options = options;
            _logger = logger;
        }

        /// <summary>Текущее время: DateTimeOffset.UtcNow</summary>
        public DateTimeOffset CurrentTime => DateTimeOffset.UtcNow;

        /// <summary>Возвращает, забанен ли объект с ключом <typeparamref name="TKey"/></summary>
        /// <param name="key">Значение ключа (ip-адрес например)</param>
        /// <returns>false, если объект не забанен или <see cref="BanAccounterOptions{TKey}.Disabled"/>==true или игнорируются пустые ключи и ключ==null.
        /// true, если объект забанен.
        /// </returns>
        public bool IsBanned(TKey? key)
        {
            if (Options.Disabled || Options.IgnoreNullKeys && key == null)
                return false;

            if (_memCache.TryGetValue(GetKeyForCacheStorage(key), out StoredItem storedItem))
                return storedItem.BanIsActual(CurrentTime);
            return false;
        }

        /// <summary>
        /// Регистрирует некоторое "плохое" действие, увеличивая счётчик сработок. Банит при превышении допустимого лимита
        /// <see cref="BanAccounterOptions{TKey}.BadCountLimit"/> на время <see cref="BanAccounterOptions{TKey}.BanTime"/>, установленное в опциях.
        /// Если объект уже забанен и время бана не истекло, время бана обновляется на <see cref="BanAccounterOptions{TKey}.BanTime"/>.
        /// </summary>
        /// <param name="key">Значение ключа (ip-адрес например), для которого надо зарегистрировать "плохое действие"</param>
        public void RegisterBadAction(TKey? key)
        {
            if (!CanRegister(key))
                return;

            //При SlidingExpiration может быть вечным по сути, но всё будет норм - т.к. новые объекты-значения мы не плодим,
            //а загружаем из кэша и ИХ же модифицируем - ссылка на кэш остаётся.
            //При каждом обращении слайдинг ПРОДЛЕВАЕТСЯ + помнить, что метод IsBanned тоже освежает срок жизни кэша!

            var storedItem = _memCache.GetOrCreate(GetKeyForCacheStorage(key), entry =>
            {
                entry.SlidingExpiration = Options.BanTime.Multiply(Options.BanTimeCacheStoreMultiplier); return new StoredItem();
            });

            var currentTime = CurrentTime;
            lock (storedItem)
            {
                var banIsActual = storedItem.BanIsActual(currentTime);

                //Последний раз регистрировали плохое действие давно. Обнулим счётчик.
                if (storedItem.BadActionLastTime?.Add(Options.BanTime) < currentTime)
                    storedItem.BadCount = 0;

                //не в бане. Увеличим счётчик
                if (storedItem.BadCount < Options.BadCountLimit / storedItem.CurrentBadCountLimitMultiplier && !banIsActual)
                    storedItem.BadCount++;

                //Превышено допустимое число плохих действий и бана ещё не было или время предыдущего бана истекло. Забаним и сбросим счётчик.
                if (storedItem.BadCount >= Options.BadCountLimit / storedItem.CurrentBadCountLimitMultiplier && !banIsActual)
                {
                    storedItem.BadCount = 0;
                    storedItem.BanEndTime = currentTime + Options.BanTime;
                    _logger?.LogWarning("Объект {1} забанен после {2} неудачных попыток на время {3}!!!", key, Options.BadCountLimit, Options.BanTime);

                    if (Options.LimitBadMultiplierForNextIncident > 1) //В следующий раз после разбана предел числа сработок для бана будет меньше.
                        storedItem.CurrentBadCountLimitMultiplier *= Options.LimitBadMultiplierForNextIncident;
                }
                else if (banIsActual) //Бан актуален. Освежим срок бана.
                    storedItem.BanEndTime = currentTime + Options.BanTime;

                storedItem.BadActionLastTime = currentTime;
            }
        }

        /// <summary>Регистрирует факт запроса</summary>
        /// <param name="key"></param>
        public void RegisterRequest(TKey? key)
        {
            if (!CanRegister(key))
                return;

            var storedItem = _memCache.GetOrCreate(GetKeyForCacheStorage(key), entry =>
            {
                entry.SlidingExpiration = Options.BanTime.Multiply(Options.BanTimeCacheStoreMultiplier); return new StoredItem();
            });
            storedItem.RequestLastTime = CurrentTime;
        }

        /// <summary>
        /// Время последнего зарегистрированного запроса
        /// </summary>
        /// <param name="key"></param>
        /// <returns>null, если запроса ещё не было зарегистрировано</returns>
        public DateTimeOffset? GetRequestLastTime(TKey? key)
        {
            if (Options.IgnoreNullKeys && key == null)
                return null;

            if (_memCache.TryGetValue(GetKeyForCacheStorage(key), out StoredItem storedItem))
                return storedItem.RequestLastTime;

            return null;
        }

        /// <summary>
        /// Время регистрации последнего плохого действия
        /// </summary>
        /// <param name="key"></param>
        /// <returns>null, если плохого действия ещё не было или игнорируются пустые ключи и ключ==null. Иначе время.</returns>
        public DateTimeOffset? GetBadActionLastTime(TKey? key)
        {
            if (Options.IgnoreNullKeys && key == null)
                return null;

            if (_memCache.TryGetValue(GetKeyForCacheStorage(key), out StoredItem storedItem))
                return storedItem.BadActionLastTime;

            return null;
        }

        /// <summary>Время, прошедшее с момента последней регистрации плохого действия.</summary>
        /// <param name="key"></param>
        /// <returns>null, если плохого действия ещё не было или игнорируются пустые ключи и ключ==null. Иначе промежуток времени</returns>
        public TimeSpan? GetBadActionLastTimePassed(TKey? key)
        {
            return CurrentTime - GetBadActionLastTime(key);
        }

        /// <summary>
        /// Были ли регистрации плохих действий
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool WasBadAсtions(TKey? key)
        {
            return GetBadActionLastTime(key) != null;
        }

        /// <summary>
        /// Сбрасывает счётчик плохих действий
        /// </summary>
        /// <param name="key"></param>
        public void ResetBadActionCount(TKey? key)
        {
            if (Options.IgnoreNullKeys && key == null)
                return;

            if (_memCache.TryGetValue(GetKeyForCacheStorage(key), out StoredItem storedItem))
                lock (storedItem)
                {
                    storedItem.BadCount = 0;
                }
        }

        /// <summary>
        /// Уменьшает счётчик плохих действий
        /// </summary>
        /// <param name="key"></param>
        /// <param name="count"></param>
        public void DecreaseBadActionCount(TKey? key, uint count = 1)
        {
            if (Options.IgnoreNullKeys && key == null)
                return;

            if (_memCache.TryGetValue(GetKeyForCacheStorage(key), out StoredItem storedItem))
            {
                lock (storedItem)
                {
                    if (storedItem.BadCount > 0) storedItem.BadCount -= count;
                    if (storedItem.BadCount < 0) storedItem.BadCount = 0;
                }
            }
        }

        /// <summary>Полностью разбанивает заданный ключ и забывает о нём. При этом опция
        /// <see cref="BanAccounterOptions{TKey}.Disabled"/> игнорируется.</summary>
        /// <param name="key">Значение ключа (ip-адрес например), которое надо разбанить.</param>
        public void Unban(TKey? key)
        {
            _memCache.Remove(GetKeyForCacheStorage(key));
        }

        /// <summary>
        /// Для максимальной уникализации ключа в мемори-кэше. Т.к. в общем кэше может содержаться много объектов из разных объектов приложения,
        /// а TKey может быть простой строкой в виде IP-адреса, плюс может быть много разных вариаций банхаммеров. Так что добавим в ключ ссылку на свой тип.
        /// </summary>
        /// <param name="key">Значение ключа (ip-адрес например)</param>
        /// <returns>Кортеж из своего типа и заданного значения ключа</returns>
        private (Type, TKey?) GetKeyForCacheStorage(TKey? key)
        {
            return (GetType(), key);
        }

        private bool CanRegister(TKey? key)
        {
            return !Options.Disabled && (!Options.IgnoreNullKeys || key != null);
        }

        /// <summary>
        /// Значения данных элемента (value), хранящегося в MemoryCache
        /// </summary>
        private class StoredItem
        {
            /// <summary>
            /// Текущее число сработок "плохих" действий
            /// </summary>
            public uint BadCount { get; set; }

            /// <summary>
            /// Текущий множитель для предельного количества плохих действий
            /// </summary>
            public double CurrentBadCountLimitMultiplier { get; set; } = 1;

            /// <summary>
            /// Время, до которого объект забанен
            /// </summary>
            public DateTimeOffset? BanEndTime { get; set; }

            /// <summary>
            /// Время последней регистрации плохого действия.
            /// </summary>
            public DateTimeOffset? BadActionLastTime { get; set; }

            /// <summary>Время последнего запроса</summary>
            public DateTimeOffset? RequestLastTime { get; set; }

            /// <summary>
            /// Бан актуален. Время отсидки ещё не закончилось.
            /// </summary>
            /// <param name="now">"сейчас" - момент, на который проверяем актуальность бана</param>
            /// <returns></returns>
            public bool BanIsActual(DateTimeOffset now) => BanEndTime != null && BanEndTime > now;
        }
    }
}
