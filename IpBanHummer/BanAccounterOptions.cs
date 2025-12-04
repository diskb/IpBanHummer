using System;

#nullable enable

namespace IpBanHammer
{
    /// <summary>
    /// Опции для сервиса <see cref="BanAccounter{TKey}"/>
    /// </summary>
    /// <typeparam name="TKey">Тип ключа для идентификации сущностей в коллекции банов</typeparam>
    public class BanAccounterOptions<TKey>
    {
        /// <summary>
        /// Если true, то сервис не регистрирует плохие действия и при проверке наличия бана метод <see cref="BanAccounter{TKey}.IsBanned"/> всегда возвращает false
        /// </summary>
        public bool Disabled { get; set; }

        /// <summary>
        /// Предельное число сработок плохих действий, после которого значение ключа <typeparamref name="TKey"/> отправляется в бан.
        /// </summary>
        public int BadCountLimit { get; set; } = 5;

        /// <summary>
        /// Время для бана. Также это время жизни по умолчанию для значения ключа <typeparamref name="TKey"/> в объекте 
        /// <see cref="BanAccounter{TKey}"/>, если к этому ключу не обращаться. Если в течение этого времени не происходит регистраций плохих действий, 
        /// значение счётчика плохих событий обнуляется и объект считается незабаненным. Счётчик обнуляется также и в момент бана.
        /// </summary>
        public TimeSpan BanTime { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>Множитель времени бана <see cref="BanTime"/> для хранения информации об объекте бана в кэше. Если данные об 
        /// интересуемом объекте не регистрируются и не запрашиваются на протяжении времени <see cref="BanTime"/> умноженного на это значение,
        /// объект удаляется из кэша и сохранённые сведения о нём обнуляются. По умолчанию 1.</summary>
        public double BanTimeCacheStoreMultiplier { get; set; } = 1;

        /// <summary>
        /// Уменьшить в это число раз <b>текущее</b> предельное значение <see cref="BadCountLimit"/> для проверки следующего инцидента для ключа в случае бана. 
        /// Актуально, если <see cref="BanTimeCacheStoreMultiplier"/> больше единицы или к значению ключа делаются запросы (срок кэша больше времени бана). По умолчанию 1.
        /// Учитывается, если больше единицы. Устанавливать, если нужна особая жёсткость работы банхаммера и экспоненциальное уменьшение допустимого
        /// предельного количества плохих действий.
        /// </summary>
        public double LimitBadMultiplierForNextIncident { get; set; } = 1;

        /// <summary>
        /// Если true, метод <see cref="BanAccounter{TKey}.RegisterBadAction(TKey)"/> не будет регистрировать и банить элементы,
        /// у которых значение <typeparamref name="TKey"/> == null, а метод проверки <see cref="BanAccounter{TKey}.IsBanned(TKey)"/>
        /// всегда возвращает false, когда <typeparamref name="TKey"/> == null. По умолчанию true.
        /// </summary>
        public bool IgnoreNullKeys { get; set; } = true;
    }
}
