# IpBanHummer
Нехитрая легковесная библиотека для защиты веб-приложений на .NET от интенсивного сканирования (парсинга) ендпоинтов и брутфорса.
## Использование автоматической защиты от парсинга
```
//Подключение:
builder.services.AddAntiparser();

[ParserProtector] //<-- в контроллере: атрибут на actions или на весь класс контроллера
public IActionResult Index()
{
    return Content("Этот url защищён от частого сканирования");
}
```

## Использование защиты от брутфорсов
```
//Подключение
builder.AddBanAccounter<string>();

//В контролере:
using IpBanHammer;

namespace Sample.Controllers
{
    public class HomeController : Controller
    {
        private readonly BanAccounter<string> _banHummer;

        public HomeController(BanAccounter<string> banHummer)
        {
            _banHummer = banHummer;
        }

        [HttpPost]
        public IActionResult BruteForceProtected(SimpleFormModel model)
        {
            var ipKey = HttpContext.Connection.RemoteIpAddress?.ToString();
            if (_banHummer.IsBanned(ipKey)) //юзер забанен. Возвращаемся
            {
                return BadRequest("Превышено число неправильных вводов. Текущий IP заблокирован");
            }

            if (ModelState.IsValid) //Переданные данные корректны
            {
                return View("BruteForceProtectedCorrect", model.Value);
            }

            //Валидация данных не прошла, регистрируем факт "плохого" события.
            _banHummer.RegisterBadAction(ipKey);
            return View();
        }
    }
}
```
