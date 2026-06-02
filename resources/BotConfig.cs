using System.Text.Json;
using System.Text.Json.Serialization;
using static EVEEchoesBot.resources.Logger;
using System.Runtime.InteropServices;
using System.Text;

namespace EVEEchoesBot.resources;

// [v] Проверить все методы и добавить новый метод Logger.Log()
// [v] TODO 2026.05.30 Сделать вызов окна ввода при создании дефолтного конфига 
// [v] TODO 2026.05.30 Привести все тексты логгера к единому стилю  
// [v] TODO 2026.05.30 Добавить класс сохранения статистики по ботам (отдельно для каждого акка stat_accountname.json)
// [v] TODO 2026.05.30 Перенести EVESystem и EVEShip в файл статистики. 

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region BotConfig

/// <summary>
/// Главный корневой класс глобальной конфигурации бота, считываемый из файла appsettings.json.
/// </summary>
public class BotConfig
{
    /// <summary>
    /// Список индивидуальных настроек для каждого подключенного игрового аккаунта/окна эмулятора.
    /// </summary>
    public List<AccSettings> Accounts { get; set; } = [];
}

#endregion

#region AccSettings

/// <summary>
/// Класс конфигурационных настроек для конкретного игрового аккаунта EVE Echoes.
/// </summary>
public class AccSettings
{
    /// <summary>Уникальное имя или логин аккаунта (используется для формирования имен файлов статов).</summary>
    public string Name { get; set; } = "";

    /// <summary>Точный заголовок окна Windows (Title) для поиска дескриптора через WinAPI.</summary>
    public string WindowTitle { get; set; } = "";

    /// <summary>Тип или название используемого эмулятора (например, BlueStacks, LDPlayer).</summary>
    public string Emulator { get; set; } = "";

    /// <summary>Имя активного сценария автоматизации для этого аккаунта (например, "localwatcher", "mining").</summary>
    public string Script { get; set; } = "";

    /// <summary>Флаг активации подмодуля сбора ресурсов планетарной добычи.</summary>
    public bool PlanetMining { get; set; }

    /// <summary>Флаг активации подмодуля обслуживания корпоративной или личной структуры (ПОС).</summary>
    public bool POS { get; set; }

    /// <summary>Сетевой порт для отправки аппаратно-независимых команд клика и ввода через интерфейс ADB.</summary>
    public int AdbPort { get; set; }

    /// <summary>
    /// Параметры целевого физического размера окна, маппируемые из JSON-блока "AccSettings".
    /// </summary>
    [JsonPropertyName("AccSettings")]
    public TargetSize? Size { get; set; }
}

#endregion

#region TargetSize

/// <summary>
/// Описывает целевые геометрические размеры рабочей области окна эмулятора.
/// </summary>
public class TargetSize
{
    /// <summary>Желаемая чистая ширина рабочей области в пикселях.</summary>
    public int TargetWidth { get; set; }

    /// <summary>Желаемая чистая высота рабочей области в пикселях.</summary>
    public int TargetHeight { get; set; }
}

#endregion

#region AccountStateDto

// [ ] TODO 2026.05.30 Добавить параметр В космосе/В доке, статус безопасности, и подумать что еще нужно  

/// <summary>
/// Объект переноса данных (DTO) для сохранения и восстановления полной статистики и очереди задач аккаунта между сессиями.
/// </summary>
public class AccountStateDto
{
    /// <summary>Имя игрового аккаунта.</summary>
    public string AccountName { get; set; } = "";

    /// <summary>Общее накопленное количество срабатываний триггеров безопасности/активности.</summary>
    public long Triggers { get; set; }

    /// <summary>Общее суммарное время работы бота по данному аккаунту в секундах.</summary>
    public double RuntimeSeconds { get; set; }

    /// <summary>Строковое представление текущей активной задачи на момент сохранения.</summary>
    public string CurrentTask { get; set; } = "";

    /// <summary>Снапшот оставшейся очереди задач сценария для непрерывного возобновления работы.</summary>
    public IList<string> TaskQueue { get; set; } = [];

    /// <summary>Временная метка последней синхронизации данных с диском (локальное время ПК).</summary>
    public DateTime LastUpdate { get; set; }

    /// <summary>Текущая звездная система, в которой бот зафиксировал персонажа.</summary>
    public string EVESystem { get; set; } = "";

    /// <summary>Текущий корабль, на котором летает персонаж.</summary>
    public string EVEShip { get; set; } = "";

    /// <summary>
    /// Флаг локации персонажа.
    /// <para>Значение <c>true</c> — корабль находится в открытом космосе.</para>
    /// <para>Значение <c>false</c> — корабль придокан к станции/цитадели.</para>
    /// <para>Значение <c>null</c> — статус еще не определен (требуется опрос).</para>
    /// </summary>
    public bool? InSpace { get; set; }

    /// <summary>
    /// Имя или идентификатор конкретного астероидного пояса / аномалии, 
    /// выбранной на Шаге 3 для совершения варпа. Очищается при прилете.
    /// </summary>
    public string? CurrentTarget { get; set; }

    public bool? IsInMiningZone { get; set; }

    public bool? IsWarping { get; set; }

    public bool? HasTarget { get; set; }

    public bool? WeaponryActive { get; set; }

}

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region ConfigManager

/// <summary>
/// Статический менеджер для работы с глобальной конфигурацией бот-платформы.
/// Обеспечивает потокобезопасное чтение, валидацию и кэширование параметров из файла конфигурации.
/// </summary>
public static class ConfigManager
{
    /// <summary>
    /// Имя и путь к файлу глобальной конфигурации приложения. По умолчанию: "config.json".
    /// </summary>
    private const string ConfigPath = "config.json";

    /// <summary>
    /// Кэшированные настройки JSON-сериализации, оптимизированные для всего приложения.
    /// Включают регистронезависимое чтение свойств и красивое форматирование с отступами.
    /// </summary>
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region BotConfig Load

/// <summary>
/// Загружает глобальную конфигурацию бота из JSON-файла.
/// Если файл отсутствует на диске, автоматически генерирует, сериализует и сохраняет конфигурацию по умолчанию.
/// В случае повреждения структуры файла возвращает инициализированный пустой объект для предотвращения падения приложения.
/// </summary>
/// <returns>Полностью заполненный объект конфигурации <see cref="BotConfig"/>.</returns>
public static BotConfig Load()
{
    // Если файл конфигурации отсутствует, создаем и сохраняем дефолтный шаблон для удобства оператора
    if (!File.Exists(ConfigPath))
    {
        BotConfig defaultConfig = CreateDefaultConfig();
        Save(defaultConfig);

        Logger.Log($"Создан файл конфигурации по умолчанию по пути '{ConfigPath}'.", LogType.Info);
        return defaultConfig;
    }

    try
    {
        string json = File.ReadAllText(ConfigPath);
        BotConfig? config = JsonSerializer.Deserialize<BotConfig>(json, _options);

        if (config == null)
        {
            Logger.Log("Файл конфигурации пуст или поврежден. Инициализирован новый объект.", LogType.Warning);
            return new BotConfig();
        }

        return config;
    }
    catch (Exception ex)
    {
        // Маршрутизируем сбой через вашу штатную систему логирования бота (message, type)
        Logger.Log($"Не удалось прочитать или десериализовать файл конфигурации: {ex.Message}", LogType.Error);
        return new BotConfig();
    }
}

#endregion


// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region CreateDefaultConfig

/// <summary>
/// Генерирует базовую структуру конфигурации для первой сессии работы приложения.
/// Разворачивает интерактивное CLI-меню опроса оператора в консоли, выполняет автоматический сбор 
/// заголовков активных окон Windows и собирает готовый объект настроек по умолчанию.
/// </summary>
/// <returns>Полностью заполненный дефолтный объект конфигурации <see cref="BotConfig"/>.</returns>
private static BotConfig CreateDefaultConfig()
{
    // Очищаем накопившийся буфер потока ввода консоли, чтобы избежать ложных срабатываний
    while (Console.KeyAvailable) 
    {
        Console.ReadKey(true);
    }

    Console.ResetColor();
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("=== ПЕРВЫЙ ЗАПУСК: ИНТЕРАКТИВНАЯ НАСТРОЙКА БОТА ===");
    Console.ResetColor();

    // 1. Интерактивный запрос уникального имени персонажа
    string name = string.Empty;
    while (string.IsNullOrWhiteSpace(name))
    {
        Console.Write("Введите имя вашего персонажа (для логов): ");
        name = Console.ReadLine()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("⚠️ Имя не может быть пустым!");
            Console.ResetColor();
        }
    }

    Console.WriteLine("\nСканирую запущенные окна эмуляторов...");

    // 2. Получаем список всех видимых окон и отсекаем системные утилиты ОС и сам процесс бота
    var allWindows = WindowEnumerator.GetVisibleWindowTitles()
        .Where(t => !t.Equals("Program Manager", StringComparison.OrdinalIgnoreCase) &&
                    !t.Equals("Settings", StringComparison.OrdinalIgnoreCase) &&
                    !t.Contains(AppDomain.CurrentDomain.FriendlyName))
        .Distinct()
        .ToList();

    string windowTitle = string.Empty;

    if (allWindows.Count == 0)
    {
        // ФОЛБЕК-СИСТЕМА: Если окон автоматически не найдено, переключаем терминал на ручной ввод заголовка
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("⚠️ Не удалось автоматически найти активные окна.");
        Console.ResetColor();

        while (string.IsNullOrWhiteSpace(windowTitle))
        {
            Console.Write("Введите название окна эмулятора вручную: ");
            windowTitle = Console.ReadLine()?.Trim() ?? string.Empty;
        }
    }
    else
    {
        // Выводим красивый структурированный нумерованный список обнаруженных окон BlueStacks/LDPlayer
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Найденные открытые окна:");
        for (int i = 0; i < allWindows.Count; i++)
        {
            Console.WriteLine($"  [{i + 1}] {allWindows[i]}");
        }
        Console.ResetColor();

        int selectedIndex = -1;
        while (selectedIndex < 0 || selectedIndex >= allWindows.Count)
        {
            Console.Write($"Выберите номер вашего эмулятора (1-{allWindows.Count}): ");
            string input = Console.ReadLine() ?? string.Empty;

            if (int.TryParse(input, out int num) && num >= 1 && num <= allWindows.Count)
            {
                selectedIndex = num - 1;
                windowTitle = allWindows[selectedIndex];
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("⚠️ Неверный выбор! Введите число из списка.");
                Console.ResetColor();
            }
        }
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"\n✅ Профиль настроен! Персонаж: '{name}', Окно: '{windowTitle}'");
    Console.ResetColor();
    Console.WriteLine();

    // Возвращаем объект конфигурации с использованием современных коллекционных выражений C# 12+
    return new BotConfig
    {
        Accounts =
        [
            new AccSettings
            {
                Name = name,
                WindowTitle = windowTitle,
                Emulator = "BlueStacks",
                Script = "LocalWatcher",
                PlanetMining = false,
                POS = false,
                AdbPort = 5565,
                Size = new TargetSize
                {
                    TargetWidth = 1280,
                    TargetHeight = 720
                }
            }
        ]
    };
}

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region Save

/// <summary>
/// Синхронизирует текущее состояние объекта конфигурации бота с диском, выполняя запись в JSON-формате.
/// Предотвращает попытки сохранения неинициализированных данных (null) и маршрутизирует ошибки записи через общую систему отчетов.
/// </summary>
/// <param name="config">Объект глобальной конфигурации <see cref="BotConfig"/> для сериализации и записи.</param>
public static void Save(BotConfig config)
{
    if (config == null)
    {
        Logger.Log("Попытка сохранения пустого объекта конфигурации. Действие отменено.", LogType.Warning);
        return;
    }

    try
    {
        string json = JsonSerializer.Serialize(config, _options);
        File.WriteAllText(ConfigPath, json);

#if DEBUG
        // Выводим аналитическую информацию об успешном сохранении файлов конфигураций только в режиме отладки
        Logger.Log($"Конфигурация успешно сохранена в файл '{ConfigPath}'.", LogType.Test);
#endif
    }
    catch (Exception ex)
    {
        // Переведено на вашу единую систему логирования ошибок для записи сбоев ввода-вывода (IO) в CSV-отчет
        Logger.Log($"Не удалось сохранить конфигурацию в файл '{ConfigPath}': {ex.Message}", LogType.Error);
    }
}

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

}

#region WindowEnumerator

/// <summary>
/// Класс перечисления активных окон операционной системы Windows.
/// Используется при первом запуске бота для автоматического поиска и составления списка заголовков эмуляторов.
/// </summary>
public static partial class WindowEnumerator
{
    /// <summary>
    /// Описывает сигнатуру обратного вызова (делегата) для системной функции перечисления окон Windows.
    /// </summary>
    /// <param name="hWnd">Дескриптор (Handle) текущего перечисляемого окна.</param>
    /// <param name="lParam">Дополнительный параметр, переданный в вызывающую функцию.</param>
    /// <returns>Возвращает <c>true</c> для продолжения перечисления, или <c>false</c> для остановки.</returns>
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    /// <summary>
    /// Перечисляет все окна верхнего уровня на экране, передавая дескриптор каждого окна функции обратного вызова.
    /// </summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>
    /// Копирует текст заголовка указанного окна в буфер символов. 
    /// Использует Юникод-версию (GetWindowTextW) для корректной поддержки кириллицы и спецсимволов.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowText(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

    /// <summary>
    /// Определяет видимость указанного окна на рабочем столе пользователя.
    /// </summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    /// <summary>
    /// Перечисляет операционную систему Windows и возвращает список заголовков всех активных видимых окон.
    /// </summary>
    /// <returns>Список <see cref="List{T}"/> строк, содержащий заголовки открытых окон приложений.</returns>
    public static List<string> GetVisibleWindowTitles()
    {
        List<string> titles = [];
        char[] buffer = new char[256];

        // Запуск нативного перечисления окон Windows верхнего уровня
        EnumWindows((hWnd, lParam) =>
        {
            // Намеренно глушим предупреждение компилятора: явно показываем, что параметр lParam проигнорирован
            _ = lParam;

            // Если окно физически отображается на экране
            if (IsWindowVisible(hWnd))
            {
                int length = GetWindowText(hWnd, buffer, buffer.Length);
                if (length > 0)
                {
                    // Извлекаем чистую строку из буфера символов без лишних пробелов по краям
                    string title = new string(buffer, 0, length).Trim();
                    if (!string.IsNullOrEmpty(title))
                    {
                        titles.Add(title);
                    }
                }
            }
            return true; // Возвращаем true, чтобы продолжить итерацию по остальным окнам в ОС
        }, IntPtr.Zero);

        return titles;
    }
}

#endregion


