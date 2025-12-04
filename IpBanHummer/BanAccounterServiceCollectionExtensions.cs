using IpBanHammer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System;

#nullable enable
namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>Класс для регистрации DI <see cref="BanAccounter{TKey}"/></summary>
    public static class BanAccounterServiceCollectionExtensions
    {
        /// <summary>
        /// Регистрирует сервис <see cref="BanAccounter{TKey}"/> как синглтон и добавляет IMemoryCache в коллекцию сервисов
        /// </summary>
        /// <typeparam name="TKey">Тип ключа для идентификации сущностей в коллекции банов</typeparam>
        /// <param name="services"></param>
        /// <returns></returns>
        public static IServiceCollection AddBanAccounter<TKey>(this IServiceCollection services)
        {
            services.TryAddSingleton<BanAccounter<TKey>>();

            //Требуемый сервис
            services.AddMemoryCache();
            return services;
        }

        /// <summary>
        /// <inheritdoc cref="AddBanAccounter{TKey}(IServiceCollection)"/>. Устанавливает опции <paramref name="configureOtions"/>
        /// </summary>
        /// <typeparam name="TKey">Тип ключа для идентификации сущностей в коллекции банов</typeparam>
        /// <param name="services"></param>
        /// <param name="configureOtions"></param>
        /// <returns>Билдер опций</returns>
        public static OptionsBuilder<BanAccounterOptions<TKey>> AddBanAccounter<TKey>(this IServiceCollection services, Action<BanAccounterOptions<TKey>> configureOtions)
        {
            return services.AddBanAccounter<TKey>()
                .AddOptions<BanAccounterOptions<TKey>>()
                .Configure(configureOtions);
        }

        /// <summary>
        /// <inheritdoc cref="AddBanAccounter{TKey}(IServiceCollection)"/>. Загружает опции <paramref name="config"/> из конфигурации
        /// </summary>
        /// <typeparam name="TKey">Тип ключа для идентификации сущностей в коллекции банов</typeparam>
        /// <param name="services"></param>
        /// <param name="config">Секция конфигурации для опций <see cref="BanAccounterOptions{TKey}"/></param>
        /// <returns>Билдер опций</returns>
        public static OptionsBuilder<BanAccounterOptions<TKey>> AddBanAccounter<TKey>(this IServiceCollection services, IConfiguration config)
        {
            return services.AddBanAccounter<TKey>()
                .AddOptions<BanAccounterOptions<TKey>>()
                .Bind(config);
        }
    }

    public static class AddAntiparserExtensions
    {
        public static IServiceCollection AddAntiparser(this IServiceCollection services)
        {
            // Регистрация фильтра как сервиса
            services.AddScoped<AntiparserFilter>();
            services.TryAddSingleton<AntiparserAccounter>();

            //Требуемый сервис
            services.AddMemoryCache();
            return services;
        }

        public static OptionsBuilder<AntiparserOptions> AddAntiparser(this IServiceCollection services, Action<AntiparserOptions> configureOtions)
        {
            return services.AddAntiparser()
                .AddOptions<AntiparserOptions>()
                .Configure(configureOtions);
        }

        public static OptionsBuilder<AntiparserOptions> AddAntiparser(this IServiceCollection services, IConfiguration config)
        {
            return services.AddAntiparser()
                .AddOptions<AntiparserOptions>()
                .Bind(config);
        }
    }
}
