using IpBanHammer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sample.Models;
using System.Diagnostics;

namespace Sample.Controllers
{
    public class HomeController : Controller
    {
        private readonly BanAccounter<string> _banHummer;
        private readonly IOptionsMonitor<BanAccounterOptions<string>> _bruteForceOptions;

        public HomeController(BanAccounter<string> banHummer
            , IOptionsMonitor<BanAccounterOptions<string>> bruteForceOptions)
        {
            _banHummer = banHummer;
            _bruteForceOptions = bruteForceOptions;
        }

        public IActionResult Index()
        {
            return View();
        }

        [ParserProtector] //<-- Автоматическая защита от парсинга. Можно навешивать на методы или контроллеры.
                          //Учитываются все обращения к ендпоинтам с этим атрибутом.
        public IActionResult ParserProtected()
        {
            return View();
        }

        /// <summary>
        /// Это тоже автоматически защищаемый ендпоинт. Сработка защиты на <see cref="ParserProtected"/> приведёт 
        /// также к блокировке доступа к <see cref="ParserProtected2"/> и наоборот.
        /// </summary>
        /// <returns></returns>
        [ParserProtector]
        public IActionResult ParserProtected2()
        {
            return Content("Этот url защищён от частого сканирования");
        }

        //А этот ендпоинт не защищён от парсинга: не учитывает обращения и не учитывает блокировки, наложенные в других ендпоинтах.
        public IActionResult ParserUnProtected()
        {
            return Content("Этот url никак не защищён от частого сканирования");
        }

        [HttpGet]
        public IActionResult BruteForceProtected()
        {
            return View();
        }

        [HttpPost]
        public IActionResult BruteForceProtected(SimpleFormModel model)
        {
            //Формируем ключ-идентификатор для юзера. Это могут быть любые достижимые данные: айпишник, юзерагент, имя метода контроллера и т.д.
            var ipKey = $"IP: {HttpContext.Connection.RemoteIpAddress?.ToString()}, UA: {HttpContext.Request.Headers["User-Agent"]}";

            if (_banHummer.IsBanned(ipKey)) //ключ юзера забанен. Возвращаемся
            {
                return BadRequest($"Превышено число неправильных вводов. Текущий юзерагент на этом IP" +
                    $" заблокирован на {_bruteForceOptions.CurrentValue.BanTime}. {ipKey}");
            }

            if (ModelState.IsValid) //Переданные данные корректны
            {
                _banHummer.Unban(ipKey); //<-- Если надо разбанить или полностью забыть о накопленной статистике юзера
                return View("BruteForceProtectedCorrect", model.Value);
            }

            //Валидация данных не прошла. Регистрируем факт "плохого" события. Метод увеличит счётчик попыток,
            //выполнит другие проверки и при необходимости поместит ключ в бан.
            _banHummer.RegisterBadAction(ipKey);

            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
