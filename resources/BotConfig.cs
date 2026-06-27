using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;

namespace EVEEchoesBot.resources;

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

#region BotConfig

/// <summary>
/// Главный корневой класс глобальной конфигурации бота, считываемый из файла.
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
    /// Параметры целевого физического размера окна.
    /// </summary>
    // BUG HIGH - Найдено несовпадение с физическим JSON-файлом конфигурации! В самом первом JSON-файле, который ты прислал для запоминания, этот блок называется `"WindowSettings"`:
    // `"WindowSettings": { "TargetWidth": 1280, "TargetHeight": 720 }`
    // Однако здесь в атрибуте указано `[JsonPropertyName("Size")]`. Из-за этого при чтении конфигурации свойство `Size` гарантированно останется NULL. Как результат, в методе `Tools.GetWindow` срабатывает условие `if (settings.Size == null)`, которое полностью отменяет ресайз окна эмулятора! Окно не сбрасывается в 1280x720, OpenCV ищет картинки не по тем пикселям, возвращает null, и дерево поведения бесконечно долбит первый попавшийся шаг, думая, что клик не прошел. Чтобы это исправить, измените атрибут на `[JsonPropertyName("WindowSettings")]`.
    [JsonPropertyName("Size")] // Исправлено: маппим на корректное имя поля в JSON
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

// [v] TODO 2026.05.30 Добавить параметр В космосе/В доке, статус безопасности, и подумать что еще нужно  

/// <summary>
/// Объект переноса данных (DTO) для сохранения и восстановления полной статистики и очереди задач аккаунта между сессиями.
/// </summary>
public class AccountStateDto
{
    /// <summary>Имя игрового аккаунта.</summary>
    public string AccountName { get; set; } = "";

    // Поле для автоматического вывода любого сценария в UI
    public string Script { get; set; } = string.Empty;

    /// <summary>Общее накопленное количество срабатываний триггеров безопасности/активности.</summary>
    public long Triggers { get; set; }

    /// <summary>Общее суммарное время работы бота по данному аккаунту в секундах.</summary>
    public double RuntimeSeconds { get; set; }

    /// <summary>Строковое представление текущей активной задачи на момент сохранения.</summary>
    public string CurrentTask { get; set; } = "";

    /// <summary>Снапшот оставшейся очереди задач сценария для непрерывного возобновления работы.</summary>
    public string[] TaskQueue { get; set; } = [];


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
    /// Физические координаты точки выбранного астероидного пояса / объекта в овервью,
    /// по которому был совершен клик для разгона. Очищается при прилете.
    /// </summary>
    // Вариант 1: Изменяем тип на OpenCvSharp.Point?
    public OpenCvSharp.Point? CurrentTarget { get; set; }


    public bool? IsInMiningZone { get; set; }

    public bool? IsWarping { get; set; }

    public bool? HasTarget { get; set; }

    public bool? WeaponryActive { get; set; }

    /// <summary>Дата и время последнего  сбора планетарных ресурсов.</summary>
    public DateTime? PlanetAssembly { get; set;}

    public bool? IsFullMain { get; set; }
    public bool? IsFullOre { get; set; }

}


#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

// Облегченный контейнер для передачи данных на веб-страницу
public class BotWebResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string State { get; set; } = "Stopped";
    public string Runtime { get; set; } = ""; // UI сам подставит нули, если бот не запущен
    public string EmulatorTitle { get; set; } = "";
    // Вшиваем ваш реальный стейт аккаунта для средней части экрана
    public AccountStateDto? ExtendedState { get; set; }
}

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

public class BotAccountManager
{

    public bool SetAccountSystem(int id, string systemName)
    {
        var bots = Program.GetActiveBots();
        // Проверяем, что индекс (id) входит в границы списка ботов
        if (id >= 0 && id < bots.Count)
        {
            bots[id].UpdateSystemManually(systemName);
            return true;
        }
        return false;
    }

    public bool SetAccountShip(int id, string shipName)
    {
        var bots = Program.GetActiveBots();
        // Проверяем, что индекс (id) входит в границы списка ботов
        if (id >= 0 && id < bots.Count)
        {
            bots[id].UpdateShipManually(shipName);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Возвращает активный аккаунт бота по его числовому идентификатору (индексу).
    /// </summary>
    public ActiveBotAccount? GetAccountById(int id)
    {
        var botsList = Program.GetActiveBots();

        if (id >= 0 && id < botsList.Count)
        {
            return botsList[id];
        }

        return null; // Теперь компилятор знает, что возвращение null здесь легально
    }



    /// <summary>
    /// Формирует актуальный снимок состояния всех ботов для отправки в веб-интерфейс.
    /// </summary>
    /// <summary>
    /// Формирует актуальный снимок состояния всех ботов для отправки в веб-интерфейс.
    /// </summary>
    // BUG LOW - Дублирование XML-комментария <summary> над методом GetAccountsState. На работу не влияет, но засоряет код.
    public List<BotWebResponseDto> GetAccountsState()
    {
        var bots = Program.GetActiveBots();

        // BUG MEDIUM - Использование неэффективного и небезопасного объекта блокировки внутри LINQ. Вызов `lock (Program.ActiveBotsLock)` внутри итератора `Select` для чтения полей конкретного бота — это избыточный оверхед. Во-первых, `Program.ActiveBotsLock` предназначен для защиты целостности самого *списка* `_activeBots`, а не внутренних полей каждого отдельного класса бота. Во-вторых, секундный веб-опрос дергает этот лок из пула потоков Kestrel и конкурирует с методом `Main`. При этом сами свойства вроде `bot._eveSystem` или `bot._iswarping` внутри `RunLoopAsync` изменяются БЕЗ этого лока. Намертво это логику не циклирует, но создает ложные блокировки (contention) между веб-сервером и потоком инициализации. Поля стейта внутри бота должны защищаться его собственным экземплярным объектом синхронизации (например, `_taskLock`, как упоминалось в архитектурном мемо).
        return [.. bots.Select((bot, index) => {
            AccountStateDto extended;

            // Защищаем чтение состояния от конкурентной записи из RunLoopAsync
            lock (Program.ActiveBotsLock)
            {
                extended = new AccountStateDto
                {
                    AccountName = bot.Settings?.Name ?? $"Account_{index + 1}",
                    Script = bot.Settings?.Script ?? "Unknown",
                    CurrentTask = bot.CurrentTask.ToString(),
                    RuntimeSeconds = bot.RuntimeSeconds,
                    EVESystem = bot._eveSystem,
                    EVEShip = bot._eveShip,
                    InSpace = bot._inSpace,

                    // ИСПРАВЛЕНО: Убран .ToString(). Передаем чистый Point? напрямую в Point? свойства DTO
                    CurrentTarget = bot._currenttarget,

                    IsInMiningZone = bot._isinminingzone,
                    IsWarping = bot._iswarping,
                    HasTarget = bot._hastarget,
                    WeaponryActive = bot._weaponryactive
                };
            }

            // Теперь создаем и возвращаем верхний уровень DTO для веб-панели
            return new BotWebResponseDto
            {
                Id = index,
                Name = extended.AccountName,
                State = bot.State.ToString(),
                Runtime = bot.GetRuntimeString(),
                EmulatorTitle = bot.Settings?.WindowTitle ?? $"LDPlayer-{index + 1}",
                ExtendedState = extended
            };
        })];
    }


    /// <summary>
    /// Маршрутизирует команды управления от кнопок браузера к конкретному боту.
    /// </summary>
    public void HandleCommand(int id, string action)
    {
        var bots = Program.GetActiveBots();
        if (id < 0 || id >= bots.Count) return;

        var targetBot = bots[id];

        switch (action.ToLower())
        {
            case "start":
                // BUG HIGH - Смертельная гонка и сброс токена для ВСЕХ ботов при старте ОДНОГО. Когда ты нажимаешь кнопку "Старт" для БОТА №2, метод HandleCommand вызывает `Program.ResetGlobalToken()`. Этот метод пересоздает ОДИН ОБЩИЙ `CancellationTokenSource` для всего приложения. Если в этот момент БОТ №1 уже успешно работал, его токен отмены становится инвалидным или генерирует сигнал отмены (`IsCancellationRequested`). В итоге, запуск любого нового бота ломает, сбрасывает или вводит в бесконечный логический ступор выполнение `RunLoopAsync` у всех остальных параллельно запущенных аккаунтов. Токен отмены ОБЯЗАН быть строго экземплярным для каждого класса `ActiveBotAccount`, а не глобальным статическим на уровне всего `Program`.
                Program.ResetGlobalToken();
                targetBot.Start(Program.GetGlobalToken());
                break;
            case "pause":
                targetBot.Pause();
                break;
            case "stop":
                targetBot.Stop();
                break;
        }
    }
}


// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

#region ConfigManager

/// <summary>
/// Статический менеджер для работы с глобальной конфигурацией бот-платформы.
/// Обеспечивает потокобезопасное чтение, валидацию и кэширование параметров из файла конфигурации.
/// </summary>
public static class ConfigManager
{
    /// <summary>
    /// Имя и путь к файлу глобальной конфигурации приложения. По умолчанию: "config.json" .
    /// </summary>
    const string ConfigPath = "config.json";

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

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

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
            // Гарантируем чтение кириллицы без повреждения символов
            string json = File.ReadAllText(ConfigPath, System.Text.Encoding.UTF8);
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

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CreateDefaultConfig

    /// <summary>
    /// Генерирует базовую дефолтную структуру конфигурации для первого запуска приложения,
    /// если файл настроек отсутствует на диске. Позволяет системе безопасно инициализироваться
    /// и запустить веб-интерфейс для дальнейшей настройки.
    /// </summary>
    /// <returns>Базовый объект конфигурации <see cref="BotConfig"/> с демонстрационным шаблоном.</returns>
    private static BotConfig CreateDefaultConfig()
    {
        // Пытаемся автоматически найти хотя бы одно окно эмулятора для подстановки в шаблон
        string defaultWindowTitle = "Задайте окно в веб-интерфейсе";

        try
        {
            var titles = WindowEnumerator.GetVisibleWindowTitles();

            // Ищем окно, имя которого содержит типичные названия эмуляторов
            var activeWindow = titles.FirstOrDefault(t => t.Contains("LDPlayer", StringComparison.OrdinalIgnoreCase) ||
                                                         t.Contains("BlueStacks", StringComparison.OrdinalIgnoreCase))
                               ?? titles.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t) &&
                                                            !t.Equals("Program Manager", StringComparison.OrdinalIgnoreCase) &&
                                                            !t.Contains(AppDomain.CurrentDomain.FriendlyName));


            if (!string.IsNullOrWhiteSpace(activeWindow))
            {
                defaultWindowTitle = activeWindow;
            }
        }
        catch (Exception ex)
        {
            // Логируем ошибку сканирования окон в системный лог, не ломая запуск приложения
            Logger.Log($"[System] Не удалось выполнить пред-сканирование окон Windows: {ex.Message}", LogType.Warning);
        }

        // Возвращаем чистый дефолтный шаблон. Пользователь настроит его через браузер.
        return new BotConfig
        {
            Accounts =
            [
                new AccSettings
                {
                    Name = "Новый Персонаж",
                    WindowTitle = defaultWindowTitle,
                    Emulator = "LDPlayer", // Популярный дефолт для EVE Echoes
                    Script = "LocalWatcher",
                    PlanetMining = false,
                    POS = false,
                    AdbPort = 5565, // Стандартный первый порт ADB
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

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

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

            // Защищаем файл от одновременной записи из разных HTTP-потоков веб-панели
            // BUG MEDIUM - Захват глобального лока `Program.ActiveBotsLock` для обычной записи JSON-файла на диск — это избыточный lock contention. Этот лок спроектирован для защиты системного списка живых ботов в памяти, а не для синхронизации I/O диска. Если в этот момент Kestrel через HTTP-поток начнет сохранять конфиг, а Main будет в цикле инициализировать ботов, возникнет микрофриз всей системы. Для сохранения файлов конфигурации правильнее использовать свой локальный `private static readonly object _fileConfigLock = new();`.
            lock (Program.ActiveBotsLock)
            {
                // Для надежности используем явную UTF-8 кодировку, как и при чтении
                File.WriteAllText(ConfigPath, json, System.Text.Encoding.UTF8);
            }

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

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

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

        // Использование локальной функции вместо переменной-делегата
        bool FilterWindow(IntPtr hWnd, IntPtr lParam)
        {
            _ = lParam;

            if (IsWindowVisible(hWnd))
            {
                int length = GetWindowText(hWnd, buffer, buffer.Length);
                if (length > 0)
                {
                    string title = new string(buffer, 0, length).Trim();
                    if (!string.IsNullOrEmpty(title))
                    {
                        // BUG HIGH - Скрытое состояние гонки и InvalidOperationException в многопоточном режиме. Локальная функция `FilterWindow` выступает в качестве callback-метода для нативного WinAPI-метода `EnumWindows`. Если в процессе работы этого перечисления (которое выполняется на вызывающем потоке) другой асинхронный воркер или HTTP-поток Kestrel параллельно дернет метод `ConfigManager.CreateDefaultConfig()`, который обращается к этому же методу, несколько потоков начнут конкурентно писать элементы в один и тот же экземпляр `List<string> titles`. Так как `List<T>` не является потокобезопасным, это приведет к разрушению внутренних индексов массива, порче памяти или случайным вылетам приложения. Добавление элементов в `titles` внутри callback должно быть защищено локальным локом или использовать `ConcurrentBag<string>`.
                        titles.Add(title);
                    }
                }
            }
            return true;
        }

        // Передаем имя локальной функции напрямую в метод API
        EnumWindows(new(FilterWindow), IntPtr.Zero);

        return titles;
    }
}


#endregion


