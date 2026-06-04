using OpenCvSharp;

namespace EVEEchoesBot.resources;

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

#region GameUi

    /// <summary>
    /// Перечисление элементов графического интерфейса игры EVE Echoes с упакованными координатами клика.
    /// Каждое значение сформировано по математическому правилу сжатия векторов: <c>ИмяЭлемента = (X * 10000) + Y</c> [INDEX].
    /// </summary>
    public enum GameUi
    {
        // 1. Взаимодействие с окнами и базовым интерфейсом игры

        /// <summary>Иконка развертывания общей панели игровых чатов.</summary>
        ChatsInterface = 250625,

        /// <summary>Точка безопасности чуть ниже и правее геометрического центра окна эмулятора для сброса фокуса меню.</summary>
        WindowCenter = 8000250,

        // 2. Навигация по вкладкам и каналам связи

        /// <summary>Вкладка прямого канала связи альянса.</summary>
        ChatTabAli = 500450,

        // 3. Индивидуальная цепочка шагов макроса автоматического оповещения

        /// <summary>Кнопка активации текстового меню ввода в чат.</summary>
        ChatInputMenu = 3650700,

        /// <summary>Кнопка перехода в оверлей шаблонов быстрого ввода фраз.</summary>
        ChatFastInput = 11900685,

        /// <summary>Вкладка "Inform" для прикрепления автоматических данных разведки системы.</summary>
        ChatInform = 800400,

        /// <summary>Выбор предустановленного статус-сообщения "Scout" в списке быстрых команд.</summary>
        ChatMessScout = 3000600,

        /// <summary>Финальная кнопка "Send" для отправки сформированного пакета данных в активный канал.</summary>
        ChatButtSend = 4450695,

        CharMenu = 500040,
        Fitting = 1850210

    }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

public enum GameRegions : long
{
    /// <summary>Пример: Область локального чата (X=5, Y=5, W=640, H=780)</summary>
    LocalChat     = 5L | (5L   << 16) | (640L << 32) | (780L << 48),

    /// <summary>Пример: Область иконки локального чата (X=5, Y=650, W=100, H=120)</summary>
    LocalChatIcon = 5L | (650L << 16) | (100L << 32) | (120L << 48),

    /// <summary>Пример: Область лейбов чатов (X=1, Y=5, W=150, H=750)</summary>
    ChatsLabels  = 1L | (5L << 16) | (150L << 32) | (750L << 48),

    /// <summary>Пример: Область обзора локального чата (X=10, Y=80, W=400, H=600)</summary>
    //LocalChatArea = 10L | (80L << 16) | (400L << 32) | (600L << 48)

    SystemName = 110L | (15L  << 16) | (140L << 32) | (30L << 48),
    ShipName   = 6L   | (215L << 16) | (300L << 32) | (50L << 48)
}

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

public static class EnumExtensions
{
    public static Rect GetRect(this GameRegions region)
    {
        long val = (long)region;

        return new Rect(
            (int)(val & 0xFFFF),
            (int)((val >> 16) & 0xFFFF),
            (int)((val >> 32) & 0xFFFF),
            (int)((val >> 48) & 0xFFFF)
        );
    }

    public static OpenCvSharp.Rect GetOpenCvRect(this GameRegions region)
    {
        var sysRect = region.GetRect(); // Вызываем существующий метод
        return new OpenCvSharp.Rect(sysRect.X, sysRect.Y, sysRect.Width, sysRect.Height);
    }


}

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

/// <summary>
/// Глобальные состояния жизненного цикла потока автоматизации.
/// </summary>
public enum BotState
{
    Stopped,
    Running,
    Paused
}

public enum SecurityCheckResult
{
    Safe,       // В локале чисто
    Danger,     // Обнаружен враг/минус
    Unknown     // Экран перекрыт, чат не найден, сбой OCR (Нужно осмотреться)
}
