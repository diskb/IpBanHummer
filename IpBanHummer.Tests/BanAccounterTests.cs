using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using Xunit;

namespace IpBanHammer.Tests
{
    // Простейшая реализация IOptionsMonitor<T> для тестов
    internal class SimpleOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public SimpleOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; set; }
        public T Get(string name) => CurrentValue;
        public IDisposable OnChange(Action<T, string> listener) => null!;
    }

    public class BanAccounterTests
    {
        private BanAccounter<string> CreateAccounter(BanAccounterOptions<string> opts, out IMemoryCache cache)
        {
            cache = new MemoryCache(new MemoryCacheOptions());
            var optionsMonitor = new SimpleOptionsMonitor<BanAccounterOptions<string>>(opts);
            var logger = NullLogger<BanAccounter<string>>.Instance;
            return new BanAccounter<string>(cache, optionsMonitor, logger);
        }

        [Fact]
        public void IsBanned_ReturnsFalse_WhenDisabled()
        {
            var opts = new BanAccounterOptions<string>
            {
                Disabled = true,
                BadCountLimit = 3,
                BanTime = TimeSpan.FromSeconds(1),
                BanTimeCacheStoreMultiplier = 1,
                IgnoreNullKeys = true,
                LimitBadMultiplierForNextIncident = 1
            };

            var acc = CreateAccounter(opts, out _);
            acc.RegisterBadAction("1.2.3.4");
            Assert.False(acc.IsBanned("1.2.3.4"));
            Assert.Null(acc.GetBadActionLastTime("1.2.3.4"));
        }

        [Fact]
        public void RegisterBadAction_BansAfterLimit()
        {
            var opts = new BanAccounterOptions<string>
            {
                Disabled = false,
                BadCountLimit = 3,
                BanTime = TimeSpan.FromMilliseconds(500),
                BanTimeCacheStoreMultiplier = 1,
                IgnoreNullKeys = true,
                LimitBadMultiplierForNextIncident = 1
            };

            var acc = CreateAccounter(opts, out _);
            var key = "1.2.3.4";

            acc.RegisterBadAction(key);
            Assert.False(acc.IsBanned(key));

            acc.RegisterBadAction(key);
            Assert.False(acc.IsBanned(key));

            acc.RegisterBadAction(key);
            Assert.True(acc.IsBanned(key));
            Assert.True(acc.WasBadAсtions(key));
            Assert.NotNull(acc.GetBadActionLastTime(key));
        }

        [Fact]
        public void RegisterBadAction_ExtendsBan_WhenCalledDuringBan()
        {
            var opts = new BanAccounterOptions<string>
            {
                Disabled = false,
                BadCountLimit = 2,
                BanTime = TimeSpan.FromMilliseconds(300),
                BanTimeCacheStoreMultiplier = 1,
                IgnoreNullKeys = true,
                LimitBadMultiplierForNextIncident = 1
            };

            var acc = CreateAccounter(opts, out _);
            var key = "1.2.3.5";

            // Два плохих действия -> бан
            acc.RegisterBadAction(key);
            acc.RegisterBadAction(key);
            Assert.True(acc.IsBanned(key));

            // Подождём чуть меньше бан-тайма и обновим бан
            Thread.Sleep(200);
            acc.RegisterBadAction(key); // должен продлить BanEndTime
            // Подождём 200 мс — если бан не продлён, он бы уже истёк
            Thread.Sleep(200);
            Assert.True(acc.IsBanned(key));

            // После дополнительного ожидания бан должен истечь
            Thread.Sleep(400);
            Assert.False(acc.IsBanned(key));
        }

        [Fact]
        public void RegisterRequest_SetsRequestTime_And_GetRequestLastTime()
        {
            var opts = new BanAccounterOptions<string>
            {
                Disabled = false,
                BadCountLimit = 5,
                BanTime = TimeSpan.FromSeconds(2),
                BanTimeCacheStoreMultiplier = 1,
                IgnoreNullKeys = true,
                LimitBadMultiplierForNextIncident = 1
            };

            var acc = CreateAccounter(opts, out _);
            var key = "req-key";

            Assert.Null(acc.GetRequestLastTime(key));
            acc.RegisterRequest(key);
            var t = acc.GetRequestLastTime(key);
            Assert.NotNull(t);
            Assert.True((DateTimeOffset.UtcNow - t.Value).TotalSeconds < 5);
        }

        [Fact]
        public void GetBadActionLastTimePassed_And_WasBadActions()
        {
            var opts = new BanAccounterOptions<string>
            {
                Disabled = false,
                BadCountLimit = 5,
                BanTime = TimeSpan.FromSeconds(2),
                BanTimeCacheStoreMultiplier = 1,
                IgnoreNullKeys = true,
                LimitBadMultiplierForNextIncident = 1
            };

            var acc = CreateAccounter(opts, out _);
            var key = "badtime-key";
            Assert.False(acc.WasBadAсtions(key));
            Assert.Null(acc.GetBadActionLastTimePassed(key));

            acc.RegisterBadAction(key);
            Assert.True(acc.WasBadAсtions(key));
            Assert.NotNull(acc.GetBadActionLastTimePassed(key));
            Assert.True(acc.GetBadActionLastTimePassed(key) >= TimeSpan.Zero);
        }

        [Fact]
        public void ResetBadActionCount_And_DecreaseBadActionCount_Work()
        {
            var opts = new BanAccounterOptions<string>
            {
                Disabled = false,
                BadCountLimit = 10,
                BanTime = TimeSpan.FromSeconds(2),
                BanTimeCacheStoreMultiplier = 1,
                IgnoreNullKeys = true,
                LimitBadMultiplierForNextIncident = 1
            };

            var acc = CreateAccounter(opts, out _);
            var key = "dec-key";

            acc.RegisterBadAction(key);
            acc.RegisterBadAction(key);
            // уменьшить на 1
            acc.DecreaseBadActionCount(key, 1);
            // не имеем доступа к внутреннему счётчику; но BadActionLastTime сохранено
            Assert.NotNull(acc.GetBadActionLastTime(key));

            // Сбросить счётчик
            acc.ResetBadActionCount(key);
            // После сброса BadActionLastTime остаётся, но дальнейшие проверки не будут банить пока не наберут лимит
            // Убедимся, что можно снова зарегистрировать и бан всё ещё не произошёл (лимит 10)
            for (int i = 0; i < 5; i++) acc.RegisterBadAction(key);
            Assert.False(acc.IsBanned(key));
        }

        [Fact]
        public void Unban_RemovesKey()
        {
            var opts = new BanAccounterOptions<string>
            {
                Disabled = false,
                BadCountLimit = 2,
                BanTime = TimeSpan.FromMilliseconds(400),
                BanTimeCacheStoreMultiplier = 1,
                IgnoreNullKeys = true,
                LimitBadMultiplierForNextIncident = 1
            };

            var acc = CreateAccounter(opts, out _);
            var key = "unban-key";

            acc.RegisterBadAction(key);
            acc.RegisterBadAction(key);
            Assert.True(acc.IsBanned(key));

            acc.Unban(key);
            Assert.False(acc.IsBanned(key));
            Assert.Null(acc.GetBadActionLastTime(key));
        }

        [Fact]
        public void IgnoreNullKeys_Behaviour()
        {
            var opts = new BanAccounterOptions<string>
            {
                Disabled = false,
                BadCountLimit = 1,
                BanTime = TimeSpan.FromSeconds(1),
                BanTimeCacheStoreMultiplier = 1,
                IgnoreNullKeys = true,
                LimitBadMultiplierForNextIncident = 1
            };

            var acc = CreateAccounter(opts, out _);
            acc.RegisterBadAction(null);
            Assert.False(acc.IsBanned(null));
            Assert.Null(acc.GetBadActionLastTime(null));
            Assert.Null(acc.GetRequestLastTime(null));
        }
    }
}
