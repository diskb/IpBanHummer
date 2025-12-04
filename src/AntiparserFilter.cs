using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;

#nullable enable

namespace IpBanHammer
{
    /// <summary>Класс учёта и выполнения банов для антипарсер-фильтра</summary>
    public class AntiparserAccounter : BanAccounter<string?>
    {
        public AntiparserAccounter(IMemoryCache cache,
            IOptionsMonitor<AntiparserOptions> antiparserOptions, //- вложенное свойство BanAccounterOptions от IOptionsMonitor к предку просто так не протащить
            ILogger<AntiparserAccounter> logger) : base(cache, antiparserOptions, logger)
        {
        }
    }

    /// <summary>Фильтр для контроля парсинга </summary>
    /// <example>Использование:
    /// [TypeFilter(typeof(AntiparserFilter), Arguments = new object[] { 429 })]
    /// [ServiceFilter(typeof(AntiparserFilter))]
    /// Для использования в ServiceFilter требуется зависимлсть AntiparserFilter.
    /// </example>
    public class AntiparserFilter : IActionFilter
    {
        /// <summary>
        /// Кастомный http StatusCode, когда срабатывает бан и доступ блокируется. Если значение null, будет взято значение из опций.
        /// </summary>
        private readonly int? _statusCodeResult;

        private readonly AntiparserAccounter? _banAccounter;
        private readonly IOptionsMonitor<AntiparserOptions> _antiparserOptions;

        /// <summary>
        /// Конструктор, где в параметрах только сервисы. Все остальные параметры передаются через сконфигурированные опции.
        /// Нужен при использовании атрибутов типа ServiceFilterAttribute.
        /// </summary>
        /// <param name="accounter">Служба учёта банов</param>
        /// <param name="antiparserOptions">Опции</param>
        public AntiparserFilter(AntiparserAccounter? accounter,
            IOptionsMonitor<AntiparserOptions> antiparserOptions)
        {
            _antiparserOptions = antiparserOptions;
            _banAccounter = accounter;
        }

        /// <summary>
        /// Конструктор с дополнительными параметрыми для использования через <see cref="ParserProtectorCustomizedAttribute"/>.
        /// </summary>
        /// <param name="accounter"></param>
        /// <param name="antiparserOptions"></param>
        /// <param name="statusCodeResult">Если не null, будет использовано это значение. Иначе протянуто из опций.</param>
        public AntiparserFilter(AntiparserAccounter? accounter,
            IOptionsMonitor<AntiparserOptions> antiparserOptions,
            int? statusCodeResult) : this(accounter, antiparserOptions)
        {
            _statusCodeResult = statusCodeResult;
        }

        public AntiparserOptions Options => _antiparserOptions.CurrentValue;

        void IActionFilter.OnActionExecuted(ActionExecutedContext context)
        {
            //Регистрировать BadAction надо только если есть подозрения парсинга - прошло слишком мало времени между предыдущим
            //и текущим запросами. Т.к. если после каждого запроса регистрировать BadAction, увеличивается их счётчик, освежается
            //время последней сработки плохого действия и продлевается жизнь кэша. Тогда даже если НОРМАЛЬНЫЙ юзер будет запрашивать
            //страницу раз в минуту, его после просмотра BadCountLimit страниц забанит!
            //Поэтому регистрируем отдельно все запросы, чтобы запомнить время последнего, а BadAction регистрируем, только если
            //между запросами прошло очень мало времени.

            if (_banAccounter == null || !Options.RegisterBadActionOnActionExecuted)
                return;

            var userIp = Options.IpKeyExtractor(context);
            var requestLastTime = _banAccounter.GetRequestLastTime(userIp);

            //Между обращениями прошло слишком мало времени
            if (_banAccounter.CurrentTime - requestLastTime < Options.RegisterBadActionIfPreviousRequestIsLess)
            {
                _banAccounter.RegisterBadAction(userIp);
            }
            else if (_banAccounter.GetBadActionLastTimePassed(userIp) > Options.DecreaseCouterIfPreviousRequestIsMore)
            {
                _banAccounter.DecreaseBadActionCount(userIp); //Обращения идут редко и плохие действия не регистрируются. Уменьшим счётчик сработок
            }

            _banAccounter.RegisterRequest(userIp);
        }

        void IActionFilter.OnActionExecuting(ActionExecutingContext context)
        {
            if (_banAccounter == null) return;

            string? userIp = Options.IpKeyExtractor(context);

            if (_banAccounter?.IsBanned(userIp) == true)
            {
                context.Result = new StatusCodeResult(_statusCodeResult ?? Options.DefaultBanStatusCode); //new BadRequestObjectResult($"УСЁ.");

                //Если объект забанен, то OnActionExecuted уже не вызовется. При необходимости для забаненных тут тоже можно регистрировать, считать
                //частоту запросов и продлевать срок бана, вызывая RegisterBadAction. Но это будет лишняя нагрузка, проще при банах снижать им лимиты
                //сработок на следующие проверки через опцию LimitBadMultiplierForNextIncident. Если чё, вот эта реализация:

                //if (_banAccounter.CurrentTime - _banAccounter.GetRequestLastTime(userIp) < Options.RegisterBadActionIfPreviousRequestIsLess)
                //    _banAccounter.RegisterBadAction(userIp);
                //_banAccounter.RegisterRequest(userIp);
            }
        }
    }

    //Через атрибут типа ServiceFilterAttribute параметры непосредственно в класс фильтра не протащить,
    //только конфигурировать на этапе добавления в сервис-коллекцию. Все параметры исключительно через DI.
    //Зато фильтр извлекается из ServiceProvider'а и работает заметно быстрее.
    /// <summary>
    /// Атрибут ActionFilter для защиты от парсинга (частого сканирования), используется на контроллерах. Конфигурируется через <see cref="AntiparserOptions"/>.
    /// Требуется зависимость <see cref="AntiparserFilter"/>, он же делает реализацию фильтра. Аналогичен установке атрибута
    /// [ServiceFilter(typeof(<see cref="AntiparserFilter"/>))].
    /// </summary>
    public class ParserProtectorAttribute : ServiceFilterAttribute
    {
        public ParserProtectorAttribute() : base(typeof(AntiparserFilter))
        {
        }
    }

    /// <summary>
    /// Атрибут TypeFilter для защиты от парсинга, используется на контроллерах. Конфигурируется через <see cref="AntiparserOptions"/>.
    /// Работу выполняет <see cref="AntiparserFilter"/>, зависимость не требуется. Создаваемый им класс фильтра всегда Transient,
    /// работает помедленнее и ваще используется рефлексия. В него можно передавать кастомизированные параметры. Но если они не требуются,
    /// лучше использовать его DI-аналог <see cref="ParserProtectorAttribute"/>. Аналогичен установке атрибута
    /// [TypeFilter(typeof(<see cref="AntiparserFilter"/>), Arguments = new object[] { 429 })]
    /// </summary>
    public class ParserProtectorCustomizedAttribute : TypeFilterAttribute
    {
        public ParserProtectorCustomizedAttribute(int banStatusCode)
            : base(typeof(AntiparserServiceFactory))
        {
            // Можно передать дополнительные параметры в фабрику через свойство Arguments
            Arguments = new object[] { banStatusCode };
            //Лишних параметров тут быть не должно. Допустимы те, и только те, которые есть в публичных
            //конструкторах AntiparserServiceFactory. Либо они должны разрешаться через сервиспровайдера.
        }
    }

    /// <summary>Фабрика для создания экземпляра фильтра <see cref="AntiparserFilter"/> 
    /// для атрибута <see cref="ParserProtectorCustomizedAttribute"/> (<see cref="TypeFilterAttribute"/>)</summary>
    internal class AntiparserServiceFactory : IFilterFactory
    {
        private readonly int? _statusCodeResult;

        /// <summary>Конструктор фабрики принимает параметры из атрибута</summary>
        /// <param name="statusCodeResult"></param>
        public AntiparserServiceFactory(int? statusCodeResult)
        {
            _statusCodeResult = statusCodeResult;
        }

        public bool IsReusable => false;

        public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        {
            //Здесь есть доступ к serviceProvider, можно создавать фильтры, используя его
            return new AntiparserFilter(serviceProvider.GetService<AntiparserAccounter>(),
                serviceProvider.GetRequiredService<IOptionsMonitor<AntiparserOptions>>(),
                _statusCodeResult);
        }
    }
}
