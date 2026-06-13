using OpenCvSharp;

namespace EVEEchoesBot.resources;

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

#region GameUI

    /// <summary>
    /// Перечисление элементов графического интерфейса игры EVE Echoes с упакованными координатами клика.
    /// Каждое значение сформировано по математическому правилу сжатия векторов: <c>ИмяЭлемента = (X * 10000) + Y</c> [INDEX].
    /// </summary>
    public enum GameUI
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

        CharMenu =   500070, // Проверено
        Fitting  =  1850240, // Проверено
        XButton  = 12300065, // Проверено
        FirstPlanet = 1300190, // Проверено
        PlanetTimer = 11900145, // Проверено
        ResList = 10600400,
        ConfirmButton = 11650565, // Проверено
        EyeIconClose = 12400435,
        EyeIconOpen  = 9250435,
        FastMenu1 = 250115,
        // FastMenu2 = ?
        // FastMenu3 = ?
        // FastMenu4 = ?
        CollapseStation = 2450105,
        SelectAll = 9850645,
        MoveTo = 1150150,
        ItemHangar = 4000165,
        UndockButton = 12300250,
        CoreInSpace = 6450660
        // ? = ?

    }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

public enum GameRegions : long
{
    /// <summary>Область локального чата (X=5, Y=5, W=640, H=780)</summary>
    LocalChat     = 5L | (5L   << 16) | (650L << 32) | (800L << 48),

    /// <summary>Область иконки локального чата (X=5, Y=650, W=100, H=120)</summary>
    LocalChatIcon = 5L | (650L << 16) | (100L << 32) | (120L << 48),

    /// <summary>Область лейбов чатов (X=1, Y=5, W=150, H=750)</summary>
    ChatsLabels  = 1L | (5L << 16) | (150L << 32) | (750L << 48),

    /// <summary>Область обзора локального чата (X=10, Y=80, W=400, H=600)</summary>
    //LocalChatArea = 10L | (80L << 16) | (400L << 32) | (600L << 48)

    /// <summary>Область кнопок "Control" / "Undock" (X=1070, Y=185, W=210, H=100)</summary>
    ControlUndock = 1070L | (220L << 16) | (210L << 32) | (100L << 48),

    SystemName = 110L | (50L  << 16) | (140L << 32) | (25L << 48),
    ShipName   = 6L   | (235L << 16) | (300L << 32) | (50L << 48),

    FastMenu = 0L | (120L << 16) | (300L << 32) | (80L << 48),

    MainMenu = 5L | (120L << 16) | (775L << 32) | (700L << 48),
    PlanetList = 5L | (110L << 16) | (245L << 32) | (670L << 48),
    ResourceList = 830L | (100L << 16) | (450L << 32) | (630L << 48),

    EyeIconClose = 1190L | (390L << 16) | (90L << 32) | (90L << 48),
    EyeIconOpen  =  880L | (390L << 16) | (90L << 32) | (90L << 48)

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
