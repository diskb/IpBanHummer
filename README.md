# IpBanHummer
Нехитрая легковесная библиотека для защиты веб-приложений на .NET от интенсивного сканирования (парсинга) ендпоинтов и брутфорса.
## Базовый принцип работы
При обработке запроса формируется идентификатор, определяющий пользователя (например, IP-адрес, юзер-агент или любые другие доступные приложению данные).
Если обнаружено "плохое действие" (например, отправка неправильного логин-пароля или высокая частота обращений к сайту) - 
для этого юзера увеличивается счётчик плохих действий.
При достижении определённого порога плохих действий, идентификатор юзера помечается забаненым на заданное время.
Конкретная логика работа настраивается через опции методов расширения Add..(ActionOrConfigSection) и методы класса BanAccounter.
## Использование автоматической защиты от парсинга
```
//Подключение сервиса:
builder.Services.AddAntiparser();

[ParserProtector] //<-- в контроллере: атрибут на actions или на весь класс контроллера
public IActionResult Index()
{
    return Content("Этот url защищён от частого сканирования");
}
```

## Использование защиты от брутфорсов
```
//Подключение сервиса:
builder.Services.AddBanAccounter<string>();

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
            if (_banHummer.IsBanned(ipKey)) //Юзер забанен. Не делаем проверку логики модели и возвращаем BadRequest
            {
                return BadRequest("Превышено число неправильных вводов. Текущий IP заблокирован");
            }

            if (ModelState.IsValid)
            {
                return View("BruteForceProtectedCorrect", model.Value);
            }

            //Валидация данных не прошла, регистрируем факт "плохого" события.
            //Этот же метод выполнит необходимые проверки и в случае чего забанит идентификатор ipKey.
            _banHummer.RegisterBadAction(ipKey);

            return View();
        }
    }
}
```
