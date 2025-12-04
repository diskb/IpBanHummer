using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

#nullable enable

namespace IpBanHammer
{
    public static class BanAccounterExtensions
    {
        /// <summary>
        /// Возвращает реальный ip адрес клиента с учётом заголовка X-Forwarded-For, если защищаемый сервер находится за реверс-прокси сервером.
        /// Учитывает всю возможную цепочку из допустимых реверс-серверов, если их несколько.
        /// </summary>
        /// <remarks>Заголовок X-Forwarded-For формируют реверс-сервера, дописывая айпишник клиента к уже полученному списку в этом заголовке.
        /// Но то, что пришло в этом заголовке ДО НАШЕГО сервера, может сформировать (подделать) и сам юзер. 
        /// Поэтому доверяем заголовку X-Forwarded-For только в том случае, если список из каскада наших доверенных вышестоящих серверов
        /// <paramref name="ipsOfReverseProxy"/> не пустой и фактическое соединение пришло с айпишника прокси из этого списка (нашего сервера).
        /// При этом и берётся только крайний правый айпи X-Forwarded-For (поставленный первым из наших серверов в цепочке). Всё остальное игнорируется.
        /// </remarks>
        /// <param name="httpContext"></param>
        /// <param name="ipsOfReverseProxy">Список допустимых ip-адресов реверс-прокси серверов. Заголовки X-Forwarded-For проверяются только для http
        /// соединений, чьи соединения установлены с IP, входящих в этот список. Если используется цепочка из нескольких реверс-прокси, все их айпишники нужно указать здесь.
        /// Если список не задан и значение <paramref name="addLocalIpAddressToProxyList"/>==false, значения заголовков X-Forwarded-For будут проигнорированы.
        /// </param>
        /// <param name="addLocalIpAddressToProxyList">В редких случаях при неправильной конфигурации сервера в списке прокси также надо указывать айпишник 
        /// и непосредственно защищаемого сервера. Но только в том случае, если сервер сам дописывает свой <b>и клиентский</b> IP в заголовок. Иначе, 
        /// если прокси как таковых нет и в X-Forwarded передан фэйковый ип, то вернётся именно он, фэйковый.</param>
        /// <returns>Строка с Ip-адресом. Если текущий IP Http-соединения является одним из значений <paramref name="ipsOfReverseProxy"/>
        /// и содержит валидный IP в заголовке "X-Forwarded-For", НЕ перечисленный в <paramref name="ipsOfReverseProxy"/>,
        /// то вернётся IP из этого заголовка. Иначе вернётся обычный ip фактического http-соединения или null, если соединение не содержит IP-адреса.</returns>
        public static string? RemoteIpRegardsXForwardedFor(this HttpContext httpContext, IEnumerable<string> ipsOfReverseProxy, bool addLocalIpAddressToProxyList = false)
        {
            //По мотивам https://stackoverflow.com/questions/28664686/how-do-i-get-client-ip-address-in-asp-net-core :
            //Not supported new "Forwarded" header (2014) https://en.wikipedia.org/wiki/X-Forwarded-For

            // X-Forwarded-For (csv list):  Using the First entry in the list seems to work
            // for 99% of cases however it has been suggested that a better (although tedious)
            // approach might be to read each IP from right to left and use the first public IP.
            // http://stackoverflow.com/a/43554000/538763
            //

            if (ipsOfReverseProxy != null)
            {
                if (addLocalIpAddressToProxyList && httpContext.Connection.LocalIpAddress != null) //Может стоит докидывать адрес сервера? Но тогда  
                    ipsOfReverseProxy = ipsOfReverseProxy.Prepend(httpContext.Connection.LocalIpAddress.ToString());

                foreach (var item in ipsOfReverseProxy.Where(w => w != null))
                    if (IPAddress.TryParse(item, out IPAddress? ipOfProxy))
                    {
                        if (ipOfProxy.Equals(httpContext.Connection.RemoteIpAddress)) // Текущее соединение установлено с одного из НАШЕГО доверенного сервера. Надо именно Equals!
                        {
                            //Прокси перечисляют (дописывают) айпишники в X-Forwarded-For через запятую.
                            //При прохождении каскада прокси от клиента к серверу идут слева направо.
                            var ipList = httpContext.GetHeaderValueAs<string>("X-Forwarded-For")?.SplitCsv();
                            return httpContext.SkipCascadeProxy(ipsOfReverseProxy, ipList);
                        }
                    }
            }

            return GetIpFromHttpContext(httpContext);
        }

        /// <summary>
        /// Извлекает из http-контекста IP адрес.
        /// </summary>
        /// <param name="httpContext"></param>
        /// <returns>Строка с айпишником или null</returns>
        private static string? GetIpFromHttpContext(HttpContext httpContext)
        {
            if (httpContext.Connection.RemoteIpAddress != null) //у Майкрософта бывает и не такое...
                return httpContext.Connection.RemoteIpAddress?.ToString();

            var ip = httpContext.GetHeaderValueAs<string>("REMOTE_ADDR");
            if (!string.IsNullOrEmpty(ip) && IPAddress.TryParse(ip, out IPAddress? itIsIp))
                return itIsIp.ToString();

            return null;
        }

        /// <summary>
        /// Возвращает первый крайний справа ip-шник из списка <paramref name="ipsFromForwardedHeader"/>, который не перечисленн в <paramref name="ipsOfReverseProxy"/>.
        /// (берёт крайний справа айпишник, пропуская айпишники прокси-серверов) Если такое условие невыполнимо, вернётся ip-адрес соединения.
        /// </summary>
        /// <param name="httpContext"></param>
        /// <param name="ipsOfReverseProxy">Каскад реверс-прокси серверов для игнорирования IP-шников из списка <paramref name="ipsFromForwardedHeader"/></param>
        /// <param name="ipsFromForwardedHeader">Возможные варианты IP-шников из заголовка X-Forwarded-For. Читается справа налево.
        /// Значения могут быть с портом в формате 192.168.0.5:12345</param>
        /// <returns>Строка с IP-адресом</returns>
        private static string? SkipCascadeProxy(this HttpContext httpContext, IEnumerable<string> ipsOfReverseProxy, IEnumerable<string>? ipsFromForwardedHeader)
        {
            var lastIp = ipsFromForwardedHeader?
                .LastOrDefault()?
                .Split(':')? //ip-шник быть может указан и с портом.
                .FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(lastIp) && IPAddress.TryParse(lastIp, out _))
            {
                if (ipsOfReverseProxy.Contains(lastIp)) //этот айпишник в заголовке принадлежит допустимому реверс-прокси. Откинем его и продолжим поиск
                    return httpContext.SkipCascadeProxy(ipsOfReverseProxy, ipsFromForwardedHeader?.SkipLast(1));
                else
                    return lastIp;
            }
            return GetIpFromHttpContext(httpContext);
        }

        /// <summary>
        /// Преобразует значения http-заголовка с именем <paramref name="headerName"/> к типу <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="httpContext"></param>
        /// <param name="headerName"></param>
        /// <returns></returns>
        private static T? GetHeaderValueAs<T>(this HttpContext httpContext, string headerName)
        {
            if (httpContext.Request.Headers.TryGetValue(headerName, out var values))
            {
                string rawValues = values.ToString();   // writes out as Csv when there are multiple.

                if (!string.IsNullOrWhiteSpace(rawValues))
                    return (T)Convert.ChangeType(values.ToString(), typeof(T));
            }
            return default;
        }

        /// <summary>
        /// Разбирает строку в CSV-формате (разделитель запятая) в список, игнорируя последнюю запятую и триммируя каждое из полученных значений.
        /// </summary>
        /// <param name="csvList">Строка с разделителем запятая</param>
        /// <param name="nullOrWhitespaceInputReturnsNull">Если false, в любом случае вернётся список даже при нулевой входной строке</param>
        /// <returns></returns>
        private static List<string>? SplitCsv(this string csvList, bool nullOrWhitespaceInputReturnsNull = false)
        {
            if (string.IsNullOrWhiteSpace(csvList))
                return nullOrWhitespaceInputReturnsNull ? null : new List<string>();

            return csvList
                .TrimEnd(',')
                .Split(',')
                .AsEnumerable()
                .Select(s => s.Trim())
                .ToList();
        }
    }
}
