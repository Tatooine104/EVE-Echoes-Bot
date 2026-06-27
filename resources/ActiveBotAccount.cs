using OpenCvSharp;
using System.Text.Json;
using EVEEchoesBot.scenarios;
using Point = OpenCvSharp.Point;
using System.Text.RegularExpressions;

namespace EVEEchoesBot.resources;


public partial class ActiveBotAccount
{

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region BOT params

    // Переменные для точного расчета времени работы (аптайма)
    private DateTime? _startTime;
    private TimeSpan _accumulatedTime = TimeSpan.Zero;

    /// <summary>
    /// Возвращает точную строку аптайма аккаунта в формате: "00 д. 00 ч. 00 м. 00 с."
    /// </summary>
    public string GetRuntimeString()
    {
        TimeSpan total;

        // Защищаем чтение состояния сессии от изменений из RunLoopAsync
        // BUG MEDIUM - Использование потенциально неинициализированного или стороннего объекта блокировки. В текущем фрагменте кода поле `_taskLock` используется, но не объявлено. Если оно объявлено в другой partial-части как `private readonly object _taskLock = new();` — всё хорошо. Однако в методе `GetAccountsState` класса `BotAccountManager` мы видели, что чтение свойств защищалось через `lock (Program.ActiveBotsLock)`. Разные локи для одних и тех же полей (`_taskLock` здесь и `ActiveBotsLock` там) приводят к состоянию гонки (Race Condition): Kestrel будет читать несинхронизированные данные прямо в момент их записи из `RunLoopAsync`, что ломает потокобезопасность.
        lock (_taskLock)
        {
            if (State == BotState.Stopped)
                return "00 д. 00 ч. 00 м. 00 с.";

            var currentSessionTime = _startTime.HasValue ? (DateTime.Now - _startTime.Value) : TimeSpan.Zero;
            total = _accumulatedTime + currentSessionTime;
        }

        return $"{total.Days:D2} д. {total.Hours:D2} ч. {total.Minutes:D2} м. {total.Seconds:D2} с.";
    }


    public BotState State { get; private set; } = BotState.Stopped;

    /// <summary>
    /// Конфигурационные настройки текущего игрового аккаунта.
    /// </summary>
    public AccSettings Settings { get; }

    /// <summary>
    /// Дескриптор (Handle) окна эмулятора, привязанного к данному аккаунту.
    /// </summary>
    public IntPtr Hwnd { get; set; }

    public bool PlanetMining { get; set; }

    public bool POS { get; set; }

    /// <summary>
    /// Текущая выполняемая ботом игровая задача.
    /// </summary>
    public AccountTask CurrentTask { get; set; }

    /// <summary>
    /// Публичное свойство для получения общего количества срабатываний триггеров (потокобезопасное чтение).
    /// </summary>
    public long TriggerCount => Interlocked.Read(ref _triggerCount);

    // TODO: Разобраться почему не используется
    /// <summary>
    /// Публичное свойство для получения общего времени работы данного аккаунта.
    /// </summary>
    // BUG LOW - Ошибка компиляции (Undefined Field). Свойство ссылается на поле `_accumulatedSeconds`, которое отсутствует в текущей partial-области. Если оно не объявлено в скрытых частях, проект не соберётся.
    public TimeSpan TotalRuntime => TimeSpan.FromSeconds(_accumulatedSeconds);

    /// <summary>
    /// Потокобезопасное свойство для получения или изменения текущей звездной системы, где находится персонаж.
    /// </summary>
    public string EVESystem
    {
        // BUG MEDIUM - Избыточный lock-оверхед при чтении ссылок. Чтение и запись ссылки на строку в C# являются атомарными операциями. Использование `lock (_taskLock)` на свойствах, которые часто запрашиваются из UI и Kestrel, создает лишнее соперничество потоков (lock contention). Для предотвращения дедлоков и фризов на тиках лучше избавиться от лока здесь, сделав поля `_eveSystem` и `_eveShip` обычными свойствами с атомарным доступом, либо обновлять их через потокобезопасный обмен (`Interlocked.Exchange`).
        get { lock (_taskLock) return _eveSystem; }
        set { lock (_taskLock) _eveSystem = value; }
    }

    /// <summary>
    /// Потокобезопасное свойство для получения или изменения текущего корабля персонажа.
    /// </summary>
    public string EVEShip
    {
        get { lock (_taskLock) return _eveShip; }
        set { lock (_taskLock) _eveShip = value; }
    }


// Внутренние переменные игрового контекста персонажа
    internal string _eveSystem = "???";
    internal string _eveShip = "???";
    internal bool _isUndocking = false;
    internal bool _inSpace = false;
    internal bool _isinminingzone = false;
    internal bool _iswarping = false;
    internal bool _hastarget = false;
    internal bool _weaponryactive = false;
    internal bool? _isfullmain = false;
    internal bool? _isfullore = false;
    internal DateTime? _planetassembly = null;
    internal long _triggerCount;

    #pragma warning disable IDE1006 // Отключаем проверку стиля именования для этого свойства
        /// <summary>
        /// Физические координаты точки выбранного астероидного пояса / объекта в овервью.
        /// Использование префикса подчёркивания согласовано с архитектурной кодовой базой проекта.
        /// </summary>
        internal OpenCvSharp.Point? _currenttarget { get; set; }
    #pragma warning restore IDE1006 // Включаем проверку обратно для остального кода


    // Приватные поля управления потоками, памятью, деревом и файловой системой
    private CancellationTokenSource? _accountCts;
    private double _accumulatedSeconds;
    private readonly string _statsFilePath;
    internal readonly System.Threading.Lock _taskLock = new();
    private List<string> _taskQueue = [];

    /// <summary>
    /// Корневой управляющий узел дерева поведения (Behavior Tree) текущего аккаунта.
    /// </summary>
    private BehaviorNode _behaviorTree;

    /// <summary>
    /// Флаг для принудительного пропуска первого лога проверки безопасности при старте сессии.
    /// </summary>
    private bool _isFirstSecurityCheck = true;

    /// <summary>
    /// Кэшированные настройки JSON-сериализации для оптимизации работы с файлами статов во всех потоках аккаунтов.
    /// </summary>
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="ActiveBotAccount"/> на основе конфигурации аккаунта.
    /// Выполняет восстановление сохраненного состояния и компилирует дерево поведения из фабрики сценариев.
    /// </summary>
    /// <param name="settings">Объект настроек игрового аккаунта <see cref="AccSettings"/>.</param>
    public ActiveBotAccount(AccSettings settings)
    {
        // 1. Присваиваем настройки
        Settings = settings;

        // 2. КОМПИЛЯЦИЯ ДЕРЕВА ПОВЕДЕНИЯ: Инициализируем поле в первую очередь для безопасности Nullable-контекста
        string currentScript = settings.Script ?? "mining";
        _behaviorTree = ScenarioFactory.CreateTree(currentScript);

        // 3. Формируем путь к файлу состояния для конкретного аккаунта
        _statsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"stats_{settings.Name}.json");

        // 4. Пытаемся загрузить сохраненную статистику из файла
        // BUG HIGH - Смертельный Fire-and-Forget вызов во время конструирования объекта. Метод `TryLoadLastStatsAndQueue()` запускается асинхронно без какого-либо ожидания. Это приводит к жесткому Race Condition: конструктор завершает работу, поток Main добавляет бота в систему, а параллельный поток Kestrel через секундный пуллинг начинает вызывать `GetAccountsState()`, пытаясь прочитать `_taskQueue` или настройки. Если метод `TryLoadLastStatsAndQueue` внутри себя обращается к файловой системе или забивает очередь, в то время как другие потоки уже работают с экземпляром класса, это ломает внутренние структуры данных или выбрасывает InvalidOperationException, вводя логический автомат бота в ступор на первом же тике. Асинхронные методы ЗАПРЕЩЕНО вызывать в конструкторах без синхронизации или перевода их в чисто синхронный вид.
        _ = TryLoadLastStatsAndQueue();

        // Старая FSM-инициализация очередей удалена. Бот готов к тикам дерева поведения.
    }

    // TODO: Разобраться почему не используется
    /// <summary>
    /// Производит атомарный инкремент счетчика срабатываний триггеров из любой части логики автоматизации бота.
    /// </summary>
    public void IncrementTrigger() => Interlocked.Increment(ref _triggerCount);

    // Использование нового высокоэффективного типа Lock из C# 13 / .NET 9
    private readonly System.Threading.Lock _scenarioLock = new();

    // Упрощенное создание экземпляра через целевой тип new()
    private CancellationTokenSource _delayCts = new();


    #pragma warning disable IDE1006 // Отключаем проверку стиля именования для этого свойства
    /// <summary>
    /// Хранит последнее целое количество часов ожидания планетарки, которое было выведено в лог.
    /// Позволяет выводить дебаг-строку строго один раз в час вместо каждого тика дерева.
    /// </summary>
    internal int _lastLoggedPlanetHours { get; set; } = -1;
    #pragma warning restore IDE1006 // Включаем проверку обратно для остального кода

    /// <summary>
    /// Индивидуальный семафор аккаунта для защиты графического контекста конкретного окна эмулятора.
    /// Предотвращает перекрестные дедлоки между разными окнами ботов при параллельной работе.
    /// </summary>
    internal readonly System.Threading.SemaphoreSlim AccountGdiSemaphore = new(1, 1);

    #endregion

public void SwitchScenario(string newScenarioName)
    {
        lock (_scenarioLock)
        {
            Logger.Log($"[{Settings.Name}] Запрос на горячую смену сценария на: '{newScenarioName}'", LogType.Info);

            // 1. Запрашиваем у фабрики новое дерево поведения
            // Передаем newScenarioName, если фолбек — соберет дефолтное дерево
            var newTree = ScenarioFactory.CreateTree(newScenarioName);

            // 2. Меняем ссылку на дерево под локом
            _behaviorTree = newTree;
            Settings.Script = newScenarioName;

            // 3. МГНОВЕННО БУДИМ БОТА: отменяем только активный токен
            // BUG HIGH - Потенциальная вечная блокировка смены сценариев (ObjectDisposedException / Deadlock). Если в основном рабочем цикле `RunLoopAsync` после срабатывания отмены токена `_delayCts` происходит его утилизация (`_delayCts.Dispose()`) и пересоздание без жесткой синхронизации с `_scenarioLock`, то данный метод выбросит исключение ObjectDisposedException прямо посреди критической секции. Хуже того, если в цикле ожидания тика `Task.Delay` не обрабатывается отмена токена должным образом, бот проигнорирует команду, а стейт дерева останется в неопределенном состоянии. Также смена ссылки на `_behaviorTree` происходит под `_scenarioLock`, но сам игровой цикл `RunLoopAsync` при обходе дерева, скорее всего, этот лок НЕ захватывает (или использует `_taskLock`). Это классическое состояние гонки (Race Condition), ломающее проход по узлам дерева.
            if (!_delayCts.IsCancellationRequested)
            {
                _delayCts.Cancel();
            }

        }
    }


    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region EnqueueTasks

    /// <summary>
    /// Потокобезопасно добавляет пачку новых задач в очередь аккаунта и сохраняет измененное состояние на диск.
    /// </summary>
    /// <param name="tasks">Коллекция строковых идентификаторов задач для добавления.</param>
    /// <param name="addToFront">
    /// Если <c>true</c>, задачи вставляются в самое начало очереди (с высоким приоритетом, сохраняя свой исходный порядок).
    /// Если <c>false</c>, задачи приписываются в самый конец текущей очереди. По умолчанию: <c>false</c>.
    /// </param>
public void EnqueueTasks(IEnumerable<string> tasks, bool addToFront = false)
    {
        if (tasks == null) return;

        // Материализуем коллекцию в памяти, защищая InsertRange от сбоев итератора
        var materializedTasks = tasks.ToList();
        if (materializedTasks.Count == 0) return;

        lock (_taskLock)
        {
            if (addToFront)
            {
                // Используем стабильный материализованный список
                _taskQueue.InsertRange(0, materializedTasks);
            }
            else
            {
                _taskQueue.AddRange(materializedTasks);
            }

            // BUG HIGH - Скрытая дисковая блокировка и зацикливание шагов. Метод `SaveStats()` вызывается синхронно внутри `lock (_taskLock)` на КАЖДОЕ добавление задач. В архитектуре Дерева Поведения (BT) узлы могут проверять условия и перестраивать очередь задач по нескольку раз за один единственный тик. Из-за этого бот начинает долбить по жесткому диску (SSD/HDD), пытаясь перезаписать JSON-файл статистики прямо посреди выполнения игрового шага. Поток воркера банально зависает на I/O-операциях внутри лока, не успевая вовремя вернуть статус в дерево поведения или пропустить тик анимации. Запись статистики на диск должна быть строго асинхронной (Fire-and-Forget или через фоновую очередь) и вынесена за пределы критических секций логики бота.
            SaveStats();
        }
    }


    #endregion


    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region TryLoadLastStatsAndQueue

    /// <summary>
    /// Пытается загрузить сохраненное состояние аккаунта из файла JSON.
    /// Восстанавливает статистику триггеров, время работы, текущую задачу и состав очереди задач.
    /// При отсутствии в файле актуальных данных о звездной системе, корабле или локации, инициирует
    /// безопасный интерактивный опрос оператора через консоль ввода.
    /// </summary>
    /// <returns>Возвращает <c>true</c>, если файл состояния существовал и был успешно прочитан; иначе <c>false</c>.</returns>
private bool TryLoadLastStatsAndQueue()
    {
        if (!File.Exists(_statsFilePath)) return false;

        try
        {
            string json = File.ReadAllText(_statsFilePath);
            var state = JsonSerializer.Deserialize<AccountStateDto>(json, _jsonOptions);

            if (state != null)
            {
                _triggerCount = state.Triggers;
                _accumulatedSeconds = (double)state.RuntimeSeconds;

                // Используем объект синхронизации Lock из .NET 9+ для потокобезопасного наполнения контекста
                lock (_taskLock)
                {
                    // Восстанавливаем список задач из очереди
                    _taskQueue = state.TaskQueue?.ToList() ?? [];

                    // Конвертируем сохраненную строку задачи в строго типизированный Enum AccountTask
                    if (Enum.TryParse(state.CurrentTask, out AccountTask savedTask))
                    {
                        CurrentTask = savedTask;
                    }
                    else
                    {
                        CurrentTask = AccountTask.CheckYourOwnState;
                    }

                    // Убрали опасную блокировку консоли. Данные восстанавливаются напрямую из DTO.
                    _eveSystem = state.EVESystem;
                    _eveShip = state.EVEShip;
                    _isfullmain = state.IsFullMain;
                    _isfullore = state.IsFullOre;
                    _planetassembly = state.PlanetAssembly;

                    // Если значения нет, выставляем безопасный дефолт (false - станция), OpenCV обновит его на первом тике
                    _inSpace = state.InSpace ?? false;

                }

                return true;
            }
        }
        catch (Exception ex)
        {
            // Маршрутизируем сбой десериализации через штатный логгер платформы, чтобы событие улетело в CSV-отчет
            Logger.Log($"Ошибка загрузки файла состояния: {ex.Message}", LogType.Error);
        }

        return false;
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    public void UpdateSystemManually(string systemName)
    {
        lock (_taskLock)
        {
            _eveSystem = systemName;
        }
        Logger.Log($"[Аккаунт {Settings?.Name ?? $"ID_{CurrentTask}"}] Система изменена вручную на: {systemName}", LogType.Info);

    }

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    public void UpdateShipManually(string shipName)
    {
        lock (_taskLock)
        {
            _eveShip = shipName;
        }
        Logger.Log($"[Аккаунт {Settings?.Name ?? $"ID_{CurrentTask}"}] Корабль изменен вручную на: {shipName}", LogType.Info);

    }


    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region SaveStats

    /// <summary>
    /// Синхронизирует текущее состояние, статистику и состав очереди задач аккаунта с диском.
    /// Формирует объект переноса данных (DTO) под защитой блокировки, после чего выполняет
    /// сериализацию и запись в JSON-файл в неблокирующем потоке.
    /// </summary>
    public void SaveStats()
    {
        try
        {
            AccountStateDto dto;

            // Быстро собираем срез данных под защитой объекта синхронизации Lock из .NET 9+
            // Быстро собираем срез данных под защитой объекта синхронизации Lock из .NET 9+
            lock (_taskLock)
            {
                // Если бот еще не запущен (время сессии 0), выводим имя самого сценария,
                string displayTask = $"{Settings.Script} | {CurrentTask}";

                dto = new AccountStateDto
                {
                    AccountName    = Settings.Name,
                    Script         = Settings.Script,
                    Triggers       = TriggerCount,
                    RuntimeSeconds = _accumulatedSeconds,
                    CurrentTask    = CurrentTask.ToString(),
                    TaskQueue      = [.. _taskQueue],
                    EVESystem      = _eveSystem,
                    EVEShip        = _eveShip,
                    LastUpdate     = DateTime.Now,

                    InSpace        = _inSpace,
                    IsWarping      = _iswarping,
                    IsInMiningZone = _isinminingzone,
                    HasTarget      = _hastarget,
                    WeaponryActive = _weaponryactive,
                    PlanetAssembly = _planetassembly,

                    // ИСПРАВЛЕНО: Убран .ToString(). Передаем чистый Point? напрямую в Point? свойства DTO
                    CurrentTarget  = _currenttarget,

                    IsFullMain     = _isfullmain,
                    IsFullOre      = _isfullore
                };
            }


            // Сериализация и дисковая запись выполняются за пределами lock, чтобы не блокировать процессор
            string json = JsonSerializer.Serialize(dto, _jsonOptions);
            // BUG HIGH - Скрытая дисковая блокировка потока воркера (I/O Bottleneck). Хотя ты абсолютно правильно вынес сериализацию и метод `File.WriteAllText` за пределы критической секции `lock (_taskLock)`, сам по себе этот вызов остается СИНХРОННЫМ. Метод `SaveStats` вызывается внутри логики добавления задач `EnqueueTasks`, которая работает на текущем тике Дерева Поведения. Если операционная система в этот момент занята (или SSD перегружен нативным С++ от OCR/OpenCV), поток воркера застынет на строчке `File.WriteAllText` на несколько сотен миллисекунд. Из-за этого нарушаются тайминги адаптивных тиков `RunLoopAsync`, игра успевает уйти по анимации вперед, зрение бота считывает устаревший кадр на следующем шаге, условия BT ломаются и бот начинает гонять одно и то же действие по кругу. Запись на диск должна быть асинхронной: `await File.WriteAllTextAsync(_statsFilePath, json);`, а сам метод `SaveStats` должен быть переведен в `async Task`.
            File.WriteAllText(_statsFilePath, json);
        }
        catch (Exception ex)
        {
            // Вызов логгера строго в соответствии с сигнатурой вашего бота (message, type)
            Logger.Log($"Не удалось сохранить статистику аккаунта '{Settings.Name}': {ex.Message}", LogType.Warning);
        }
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Start

    private readonly System.Threading.Lock _startLock = new();

    /// <summary>
    /// Запускает логический воркер аккаунта в изолированном фоновом потоке.
    /// Полностью изолирован от перекрестных блокировок с веб-интерфейсом Kestrel.
    /// </summary>
    public void Start(CancellationToken token)
    {
        bool shouldStart = false;
        CancellationToken workerToken = CancellationToken.None;

        // КРИТИЧЕСКАЯ СЕКЦИЯ: Быстро под локом настраиваем токены и стейт, и МГНОВЕННО выходим из лока!
        lock (_startLock)
        {
            if (this.State is BotState.Running && _accountCts?.IsCancellationRequested is false)
            {
                return; // Бот уже работает, выходим
            }

            this.State = BotState.Running;
            _startTime ??= DateTime.Now;

            if (_accountCts is not null)
            {
                try
                {
                    _accountCts.Cancel();
                    _accountCts.Dispose();
                }
                catch { /* Подавляем сбои очистки */ }
            }

            // BUG HIGH - Каскадная отмена и логический паралич дерева поведения. Метод `CancellationTokenSource.CreateLinkedTokenSource(token)` связывает новый источник токенов со старым статическим токеном `Program.GetGlobalToken()`. Вспоминаем баг из `BotAccountManager.HandleCommand`: при нажатии кнопки "Старт" для ЛЮБОГО бота там жестко вызывается метод `Program.ResetGlobalToken()`. Пересоздание токена отменяет старый глобальный токен. Это автоматически стриггерит сигнал отмены через `workerToken` во ВСЕХ связанных ботах. Сценарий получает сигнал `IsCancellationRequested`, ломает логику обхода узлов Дерева Поведения (BT) на первом же тике, узлы действий выбрасывают `OperationCanceledException` или зависают в невалидном стейте, и бот начинает гонять одно и то же стартовое действие по кругу, не имея возможности переключиться дальше. Токены отмены должны быть полностью изолированы.
            _accountCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            workerToken = _accountCts.Token;
            shouldStart = true;
        } // ВОТ ТУТ ЛОК СТАРТА ГАРАНТИРОВАННО ОСВОБОЖДЕН!

        // Запуск фонового потока выполняем СТРОГО ЗА ПРЕДЕЛАМИ критической секции lock!
        // Теперь RunLoopAsync запустится в чистом поле, и его внутренний lock(_taskLock) 
        // никогда не пересечется с логикой метода Start. Дедлок физически невозможен.
        if (shouldStart)
        {
            Logger.Log($"[{Settings.Name}|{_eveSystem}|{_eveShip}] Инициирую запуск логического воркера RunLoopAsync.", LogType.Info);
            // BUG MEDIUM - Избыточный стейт-машинный оверхед. Конструкция `async () => await ...` порождает лишний скрытый класс во время генерации IL-кода. С учетом `Task.Run` правильнее писать: `Task.Run(() => RunLoopAsync(workerToken), workerToken);`.
            Task.Run(async () => await RunLoopAsync(workerToken).ConfigureAwait(false), workerToken);
        }
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Stop

    /// <summary>
    /// Инициирует безопасную остановку рабочего цикла автоматизации текущего аккаунта.
    /// Вызывает отмену связанного токена, позволяя асинхронному потоку завершить текущий виток и сохранить статы на диск.
    /// </summary>
    public void Stop()
    {
        // BUG HIGH - Потенциальный Lock Contention Deadlock (Блокировка потока остановки). Метод `Stop` вызывается из внешних потоков (веб-интерфейс Kestrel или трей приложения). Он захватывает лок `lock (_taskLock)`. Внутри этого же лока вызывается `_accountCts?.Cancel()`. Вызов `Cancel()` каскадно активирует отмену во всех асинхронных задачах текущего бота. Если игровой цикл `RunLoopAsync` прямо в этот момент выполняет тяжелое действие и тоже удерживает `_taskLock` (или застрял внутри него), метод `Cancel()` заблокирует поток Kestrel или UI-поток трея, ожидая освобождения критической секции. Метод `Cancel()` должен вызываться ДО захода в критическую секцию `lock (_taskLock)`, чтобы мгновенно прервать рабочий цикл.
        lock (_taskLock)
        {
            if (State == BotState.Stopped) return;

            State = BotState.Stopped;

            // Плавное гашение асинхронного цикла воркера и очистка памяти
            _accountCts?.Cancel();
            _accountCts?.Dispose();
            _accountCts = null;

            // Полный сброс таймеров аптайма под защитой лока
            _startTime = null;
            _accumulatedTime = TimeSpan.Zero;
        }

        // BUG LOW - Потенциальный NullReferenceException в строке логирования. Конструкция `Settings?.Name` защищает от падения, если `Settings` равен null, но если это произойдет, вызов `Settings?.Name` вернет пустую строку, а логгер попытается вывести `[]`. На логику зацикливания шагов это не влияет.
        Logger.Log($"[{Settings?.Name}] Поток автоматизации полностью остановлен. Время сброшено.", LogType.Warning);
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Pause

    /// <summary>
    /// Кнопка "Пауза": Приостанавливает поток автоматизации, сохраняя набранное время работы.
    /// </summary>
    public void Pause()
    {
        // BUG HIGH - Повторение смертельного тупика иерархии блокировок (Lock Contention Deadlock). Метод `Pause` вызывается из потока Minimal API при нажатии кнопки в браузере. Он захватывает `lock (_taskLock)` и внутри него пытается вызвать `_accountCts?.Cancel()`. Если игровой цикл `RunLoopAsync` в этот момент застрял внутри критической секции `_taskLock` (например, выполняет тяжелое действие, опрашивает зрение или завис на дисковой записи `SaveStats`), метод `Cancel()` заблокирует поток веб-сервера Kestrel. Внешнее управление ботом полностью отвалится. Вызов `_accountCts?.Cancel()` ОБЯЗАН находиться ДО входа в критическую секцию `lock (_taskLock)`.
        lock (_taskLock)
        {
            if (State != BotState.Running) return;

            this.State = BotState.Paused;

            // Потокобезопасно фиксируем отработанное время в накопитель
            if (_startTime != null)
            {
                _accumulatedTime += (DateTime.Now - _startTime.Value);
            }

            _startTime = null; // Сбрасываем точку старта, останавливая отсчет

            // Плавное гашение асинхронного цикла воркера
            _accountCts?.Cancel();
        }

        Logger.Log($"[{Settings?.Name}] Поток автоматизации приостановлен (Пауза). Время сохранено.", LogType.Warning);
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    public double RuntimeSeconds
    {
        get
        {
            lock (_taskLock)
            {
                // Защищенное чтение: исключает получение разорванного стейта времени в веб-панели
                if (State == BotState.Running && _startTime != null)
                {
                    return (_accumulatedTime + (DateTime.Now - _startTime.Value)).TotalSeconds;
                }
                return _accumulatedTime.TotalSeconds;
            }
        }
        set
        {
            lock (_taskLock)
            {
                // BUG MEDIUM - Логическая ошибка сброса времени сессии (State Loss). Setter свойства `RuntimeSeconds` используется сериализатором (или при сбросе статов) для восстановления накопленного времени. Однако условие `if (value == 0)` приводит к тому, что любое положительное сохраненное число (например, 45000 секунд аптайма из stats.json) будет просто ПРОИГНОРИРОВАНО. Время бота никогда не восстановится из файла и обнулится при перезапуске. Правильный код должен присваивать значение: `_accumulatedTime = TimeSpan.FromSeconds(value);`. На зацикливание дерева поведения это не влияет, но ломает веб-статистику.
                if (value == 0) _accumulatedTime = TimeSpan.Zero;
            }
        }
    }



    #region RunLoopAsync

    /// <summary>
    /// Главный асинхронный рабочий цикл (Runtime Loop) автоматизации игрового аккаунта.
    /// Переведен на архитектуру Дерева поведения (Behavior Tree). На каждом такте (Tick) производит
    /// высокоточный расчет таймингов сессии и делегирует принятие решений и выполнение макросов корневому узлу дерева.
    /// </summary>
    /// <param name="token">Токен отмены операции <see cref="CancellationToken"/>, привязанный к текущему аккаунту.</param>
    /// <returns>Асинхронная задача <see cref="Task"/>, управляющая жизненным циклом потока воркера.</returns>
private async Task RunLoopAsync(CancellationToken token)
    {
        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Поток запущен. Начало работы по Дереву поведения: '{Settings.Script ?? "mining"}'.", LogType.Info);

        var sessionStart = System.DateTime.Now;
        var sessionStopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Фиксируем стартовое значение секунд из лога прошлых сессий
        long baseSeconds = (long)_accumulatedSeconds;

        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    lock (_taskLock)
                    {
                        if (this.State is BotState.Running && _startTime is not null)
                        {
                            TimeSpan currentUptime = _accumulatedTime + (DateTime.Now - _startTime.Value);
                            this.RuntimeSeconds = currentUptime.TotalSeconds;
                        }

                        // Накапливаем секунды с использованием double, чтобы избежать погрешностей деления
                        _accumulatedSeconds = baseSeconds + sessionStopwatch.Elapsed.TotalSeconds;
                    }

                    var currentTree = _behaviorTree;

                    if (currentTree is null)
                    {
                        Logger.Log($"[{Settings.Name}] Ошибка: Дерево _behaviorTree не инициализировано.", LogType.Error);
                        await Task.Delay(2000, token);
                        continue;
                    }

                    NodeStatus treeResult;

                    // Создаем защитный токен с таймаутом на выполнение ВСЕГО дерева (3 секунды)
                    using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                    using (var linkedCtsForTick = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token))
                    {
                        try
                        {
                            // ИСПРАВЛЕНО: Применяем .ConfigureAwait(false), чтобы полностью отвязать дерево 
                            // от SynchronizationContext главного потока Windows Forms. Это уберет дедлоки на логах!
                            // BUG HIGH — Главный логический затык найден! Дерево поведения (Behavior Tree) по своей фундаментальной архитектуре ОБЯЗАНО быть полностью синхронным (вычисляться мгновенно за доли миллисекунд), либо хранить внутренний стейт шага. Ты вызываешь `await currentTree.TickAsync(...)` КАЖДЫЙ ТИК ЦИКЛА (раз в 1 или 5 секунд). Если узел-Действие (Action Node) внутри себя выполняет асинхронный `await ClickToAsync(...)` и возвращает статус `NodeStatus.Running`, дерево прерывает выполнение и возвращает наружу статус `Running`. На СЛЕДУЮЩЕМ ТИКЕ цикла `while` ты заново вызываешь `currentTree.TickAsync()`. Дерево начинает обход С САМОГО НАЧАЛА (с корня), а не с того узла, который вернул `Running`! Корневой селектор заново проверяет первое условие, оно совпадает, и бот опять заходит в ПЕРВОЕ действие сценария, генерируя бесконечный повтор одного и того же шага по кругу. Деревья поведения не должны перезапускаться с нуля, пока текущее действие возвращает `Running`, либо логика узлов должна опираться на инкапсулированные индексы или проверку стейта в игре!
                            treeResult = await currentTree.TickAsync(this, linkedCtsForTick.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                        {
                            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] КРИТИЧЕСКИЙ ЗАВИС КЛИЕНТА: Дерево поведения застыло дольше чем на 3 секунды! Проверьте методы ветки безопасности.", LogType.Warning);
                            await Task.Delay(3000, token);
                            continue;
                        }
                    }

                    // BUG MEDIUM — Логическая ловушка адаптивных тиков. Условие `bool isHighActivity = (treeResult is NodeStatus.Running) || this._inSpace || this._weaponryactive;` приводит к тому, что если бот ушел в космос (`_inSpace = true`), цикл переключается на жесткий режим "1 тик в секунду". Так как дерево на каждом тике обходится с нуля, бот в космосе начинает долбить по игре командами ADB каждую секунду без остановки. Это вызывает дикие анимационные лаги в эмуляторе, зрение OpenCV не успевает зафиксировать смену картинки (ведь игра еще обрабатывает прошлый клик), и бот зацикливается на одном действии.
                    bool isHighActivity = (treeResult is NodeStatus.Running) || this._inSpace || this._weaponryactive;
                    int delaySeconds = isHighActivity ? 1 : 5;

    #if DEBUG
                    if (treeResult is NodeStatus.Running)
                    {
                        Logger.Log($"[{Settings.Name}] Дерево выполняет длительную операцию (Running). Следующий чек через {delaySeconds}с.", LogType.Test);
                    }
    #endif
                    // Кэшируем ссылку локального источника прерывания под атомарной заменой
                    CancellationTokenSource currentDelayCts;
                    lock (_taskLock) {currentDelayCts = _delayCts;}

                    // Использование упрощенного using без фигурных скобок для связки токенов отмены
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, currentDelayCts.Token);

                    try {await Task.Delay(TimeSpan.FromSeconds(delaySeconds), linkedCts.Token).ConfigureAwait(false);}
                    catch (TaskCanceledException) when (token.IsCancellationRequested) {throw;}
                    catch (TaskCanceledException)
                    {Logger.Log($"[{Settings.Name}] Пауза прервана командой из UI. Переключение на новый сценарий...", LogType.Info);}
                    finally
                    {
                        // BUG HIGH — Утечка памяти и гонка источников отмены. Каждую секунду в `finally` выполняется `_delayCts = new CancellationTokenSource();`. Старый экземпляр `_delayCts`, ссылка на который была заменена, НИКОГДА не диспозится, так как в блоке ниже очищается `currentDelayCts`, но из-за отсутствия синхронизации в пуле потоков новые CTS плодятся сотнями в минуту. Это вызывает утечку системных дескрипторов Windows (Handles Leak) и, как следствие, падение производительности рантайма через пару минут работы.
                        lock (_taskLock){_delayCts = new CancellationTokenSource();}
                        try { currentDelayCts.Dispose(); }
                        catch (ObjectDisposedException) { /* Игнорируем гонки удаления */ }
                    }
                }
                catch (TaskCanceledException){throw;}
                catch (Exception ex)
                {
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Сбой в главном цикле обработки такта дерева: {ex.Message}", LogType.Error);
                    await Task.Delay(5000, token).ConfigureAwait(false); 
                }
            }
        }
        catch (TaskCanceledException){Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Получен сигнал остановки аккаунта. Фиксация состояния дерева.", LogType.Info);}
        catch (Exception ex){Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Критический сбой рабочего потока дерева поведения: {ex.Message}", LogType.Error);}
        finally
        {
            sessionStopwatch.Stop();
            lock (_taskLock){_accumulatedSeconds = baseSeconds + sessionStopwatch.Elapsed.TotalSeconds;}
            SaveStats();
            int sessionSeconds = (int)(System.DateTime.Now - sessionStart).TotalSeconds;
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Состояние сохранено. Поток поведения остановлен. Время работы в сессии (сек): {sessionSeconds}", LogType.Info);
        }
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CheckSecurityStatus 

    /// <summary>
    /// Асинхронно анализирует состояние локального чата эмулятора для обеспечения безопасности аккаунта.
    /// Проверяет, развернуто ли окно чата. Если чат свернут, находит иконку развертывания, совершает клик через ADB,
    /// ожидает анимацию и инициирует глубокое сканирование списка пилотов на наличие враждебных статусов.
    /// </summary>
    /// <param name="token">Токен отмены операции <see cref="CancellationToken"/> для текущего рабочего потока.</param>
    /// <returns>Возвращает <c>true</c>, если система безопасности успешно проанализировала локал и подтвердила отсутствие угроз; иначе <c>false</c>.</returns>
internal async Task<SecurityCheckResult> CheckSecurityStatusAsync(CancellationToken token)
    {
#if DEBUG
        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Запуск computer-зрения: сканирование локал-чата...", LogType.Test);
#endif

        if (Hwnd == IntPtr.Zero)
        {
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Окно целевой программы не найдено.", LogType.Error);
            return SecurityCheckResult.Unknown;
        }

        string pathImg1 = Path.Combine(Program.TemplatesDir, "imgLocalChatHead.png");
        string pathImg2 = Path.Combine(Program.TemplatesDir, "imgLocalChatIcon.png");
        string debugDir = Path.Combine(Program.TemplatesDir, "..", "DebugScreenshots");

        var (screenshot, safeRegion1) = await this.PrepareScreenshotRegionAsync(GameRegions.LocalChat, token);
        if (screenshot == null) return SecurityCheckResult.Unknown;

        using var screenshotScope = screenshot; // Стрикт-утилизация unmanaged-памяти C++
        var currentSnap = screenshot;

        Point? foundImg1 = await Task.Run(() => Tools.FindTemplateInRegion(currentSnap, pathImg1, safeRegion1, 0.80), token);

        if (foundImg1.HasValue)
        {
#if DEBUG
            try
            {
                using Mat cropped = new(currentSnap, safeRegion1);
                Directory.CreateDirectory(debugDir);
                Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatHead_FOUND.png"), cropped);
            }
            catch (Exception ex)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось сохранить отладочный кадр шапки: {ex.Message}", LogType.Warning);
            }
#endif
            return RunLocalCheck(currentSnap, safeRegion1);
        }

        Rect localRegion2 = GameRegions.MainMenu.GetOpenCvRect(); // Ваша базовая область иконки чата
        Rect safeRegion2 = await Task.Run(() => Tools.ClampRegion(localRegion2, currentSnap.Width, currentSnap.Height), token);

        if (safeRegion2.Width > 0 && safeRegion2.Height > 0)
        {
            Point? foundImg2 = await Task.Run(() => Tools.FindTemplateInRegion(currentSnap, pathImg2, safeRegion2, 0.80), token);

            if (foundImg2.HasValue)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Локальный чат свернут. Обнаружена иконка. Разворачиваю...", LogType.Warning);

#if DEBUG
                try
                {
                    using Mat cropped = new(currentSnap, safeRegion2);
                    Directory.CreateDirectory(debugDir);
                    Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatIcon_FOUND.png"), cropped);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось сохранить отладочный кадр иконки: {ex.Message}", LogType.Warning);
                }
#endif

                await this.ClickPointAsync(foundImg2.Value, token, minSec: 1, maxSec: 2, offset: 2);

                for (int retry = 1; retry <= 3; retry++)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(1500, token);

                    var (freshScreenshot, freshSafeRegion1) = await this.PrepareScreenshotRegionAsync(GameRegions.LocalChat, token);
                    if (freshScreenshot != null)
                    {
                        using var freshScope = freshScreenshot;
                        Point? retryImg1 = await Task.Run(() => Tools.FindTemplateInRegion(freshScreenshot, pathImg1, freshSafeRegion1, 0.80), token);

                        if (retryImg1.HasValue)
                        {
                            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Чат успешно развернут на попытке {retry}/3.", LogType.Success);
                            return RunLocalCheck(freshScreenshot, freshSafeRegion1);
                        }
                    }
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Интерфейс чата еще подгружается. Попытка {retry}/3...", LogType.Test);
                }

                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Критическая ошибка: Чат не открылся после серии кликов.", LogType.Error);
                return SecurityCheckResult.Unknown;
            }
        }
        
        // BUG HIGH — Причина полной тишины в логах "localwatcher". Посмотри на этот кусок кода: если чат развернут (ЭТАП 1), он вызывает `RunLocalCheck`. Если чат свернут (ЭТАП 2), он пытается его развернуть. Но что если на экране открыто окно дока станции, склад, меню фитинга или трюма, которое ПОЛНОСТЬСТЬЮ ПЕРЕКРЫВАЕТ интерфейс игры? 
        // В этом случае условия `foundImg1.HasValue` и `foundImg2.HasValue` гарантированно вернут `false`. Бот пролетает мимо обоих этапов, пишет ОДИН лог "ВНИМАНИЕ: Шаблоны чата не найдены" и возвращает `SecurityCheckResult.Unknown`.
        // Затем в методе `AnalyzeScreenAndUpdateStateAsync` этот `Unknown` перехватывается, выставляет `bot.CurrentTask = AccountTask.LookAround` и возвращает `NodeStatus.Failure`.
        // Stateless-дерево `localwatcher` рушится, `RunLoopAsync` через 5 секунд (так как вернулся Failure) заново тикает дерево с нуля. Бот снова заходит сюда, снова ничего не находит, снова пишет одну и ту же строку "Шаблоны чата не найдены" и возвращает Failure.
        // Бот не завис физически, он циклится. Твоя консоль забита этой строкой "Зрение бота ослепло", либо ты её пропустил, потому что в `RunLoopAsync` при статусе `Failure` задержка составляет 5 секунд, и лог размывается. Экран эмулятора перекрыт интерфейсом станции. Чтобы чат стал виден, бот обязан сначала принудительно нажать кнопку «Закрыть» (XButton) или сбросить фокус, если он находится на станции и текущая задача `LookAround`.
        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] ВНИМАНИЕ: Шаблоны чата (ни шапка, ни иконка) не найдены. Зрение бота ослепло.", LogType.Warning);
        return SecurityCheckResult.Unknown;
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region RunLocalCheck

    /// <summary>
    /// Выполняет глубокую проверку безопасности локальной звездной системы (сканирование локал-чата).
    /// Ищет три эталонных маркера фильтров стендингов (Criminal, Minus, Neutral) в заданной области экрана [INDEX, INDEX].
    /// Наличие всех трех маркеров гарантирует отсутствие посторонних пилотов; исчезновение хотя бы одного из них
    /// свидетельствует о появлении потенциальной угрозы в локале и переводит аккаунт в режим тревоги [INDEX].
    /// </summary>
    /// <returns>Возвращает <c>true</c>, если обнаружены все 3 маркера (система чиста); возвращает <c>false</c>, если обнаружена угроза [INDEX].</returns>
    // Выносим массив имен файлов в статические поля класса для экономии памяти
    // Массив остается статическим на уровне partial-класса аккаунта
    private static readonly string[] SecurityTemplates = ["imgLocalCriminal.png", "imgLocalMinus.png", "imgLocalNeutral.png"];

    private SecurityCheckResult RunLocalCheck(Mat screenshot, Rect searchRegion)
    {
        // Исправлено: страхуем отладочный блок от выхода за рамки матрицы, если safeRegion2 лагнул
        Rect debugSafeRegion = Tools.ClampRegion(searchRegion, screenshot.Width, screenshot.Height);

        // Регион searchRegion уже проверен в родительском методе, ClampRegion больше не нужен
        int foundCount = 0;
        string debugDir = Path.Combine(Program.TemplatesDir, "..", "DebugScreenshots");

        // Использование ReadOnlySpan исключает выделение памяти в куче при каждом такте
        ReadOnlySpan<string> templates = SecurityTemplates;

        foreach (string templateName in templates)
        {
            string fullTemplatePath = Path.Combine(Program.TemplatesDir, templateName);
            if (!File.Exists(fullTemplatePath)) continue;

            Point? foundPoint = Tools.FindTemplateInRegion(screenshot, fullTemplatePath, searchRegion, 0.88);

            if (foundPoint.HasValue)
            {
                foundCount++;
    #if DEBUG
                try
                {
                    using Mat croppedRegion = new(screenshot, debugSafeRegion);
                    Directory.CreateDirectory(debugDir);
                    string debugPath = Path.Combine(debugDir, $"{Settings.Name}_{Path.GetFileNameWithoutExtension(templateName)}_FOUND.png");
                    Cv2.ImWrite(debugPath, croppedRegion);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось сохранить снимок экрана: {ex.Message}", LogType.Warning);
                }
    #endif
            }
        }

        // ========================================================
        // СТРОГОЕ СОБЛЮДЕНИЕ ТВОЕЙ ЛОГИКИ ВЕРДИКТОВ:
        // ========================================================

        // 1. ИДЕАЛЬНАЯ БЕЗОПАСНОСТЬ: Найдена вся тройка маркеров
        if (foundCount == 3)
        {
            return SecurityCheckResult.Safe;
        }

        // 2. СБОЙ OCR / ПЕРЕКРЫТИЕ: Не найдено вообще ничего (0 из 3).
        // Чат вроде бы открыт, но маркеры пропали полностью. Даем боту шанс "Осмотреться".
        if (foundCount == 0)
        {
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Маркеры безопасности не найдены (0 из 3). Интерфейс смазан или перекрыт. Осматриваемся.", LogType.Warning);
            return SecurityCheckResult.Unknown;
        }

        // 3. РЕАЛЬНАЯ ОПАСНОСТЬ: Найдено 1 или 2 маркера. 
        // Это значит, что интерфейс чата виден ИДЕАЛЬНО, но часть маркеров сместилась/исчезла из-за появления минуса/нейтрала.
        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] ВНИМАНИЕ: Найдено маркеров безопасности: {foundCount} из 3. Четкая фиксация угрозы!", LogType.Warning);
        return SecurityCheckResult.Danger;
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region _isSaveLocal

    private readonly System.Threading.Lock _localStateLock = new();


    /// <summary>
    /// Глобальное свойство безопасности звездной системы текущего аккаунта.
    /// <para>Чтение (get): Возвращает актуальный статус безопасности из синглтона <see cref="SystemSafetyManager"/>.</para>
    /// <para>Запись (set): Потокобезопасно обрабатывает изменение статуса, реализует "защиту старта"
    /// и координирует каскадную панику (очистку очередей и запуск эвакуации) для всех окон в этой же системе.</para>
    /// </summary>
    public bool? IsSaveLocal
    {
        get => SystemSafetyManager.GetSystemState(EVESystem).IsSafe;
        set
        {
            if (string.IsNullOrEmpty(EVESystem) || EVESystem == "Неизвестно" || value == null) return;

            // 1. СТАРТОВАЯ ИНИЦИАЛИЗАЦИЯ СИСТЕМЫ (Отрабатывает ровно 1 раз за сессию)
            lock (_localStateLock)
            {
                if (_isFirstSecurityCheck)
                {
                    _isFirstSecurityCheck = false;
                    if (value is true)
                    {
                        SystemSafetyManager.SetSystemSafe(EVESystem);
                        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Стартовая инициализация: система безопасна. Мониторинг запущен.", LogType.Info);
                        return;
                    }
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Стартовая проверка: система СРАЗУ ОПАСНА! Запуск экстренных процедур.", LogType.Warning);
                }
            }

            // ========================================================
            // ОБРАБОТКА ФИКСАЦИИ УГРОЗЫ (value == false)
            // ========================================================
            if (value is false)
            {
                // Атомарно выставляем глобальный статус опасности через менеджер.
                // TrySetSystemDanger вернет true ТОЛЬКО первому боту, который зафиксировал минус!
                bool isFirstAlert = SystemSafetyManager.TrySetSystemDanger(EVESystem);

                if (isFirstAlert)
                {
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] ВНИМАНИЕ! Первичная фиксация угрозы в системе. Запуск каскадной паники.", LogType.Warning);

                    // АЛЬЯНС-ОПОВЕЩЕНИЕ: Строго однократно в фоне
                    Task.Run(async () =>
                    {
                        try
                        {
                            CancellationToken token = _accountCts?.Token ?? Program.GetGlobalToken();
                            await ScenarioFactory.RunAliChatWarningAsync(this, token);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"Ошибка отправки сообщения в чат альянса: {ex.Message}", LogType.Error);
                        }
                    });

                    // КАСКАДНАЯ ЭВАКУАЦИЯ СОСЕДЕЙ: Будим только тех, кто еще не эвакуируется
                    Task.Run(() =>
                    {
                        var neighborBots = Program.GetActiveBots()
                            .Where(b => b != this && b.EVESystem == this.EVESystem)
                            .ToList();

                        foreach (var bot in neighborBots)
                        {
                            // Защищаем соседа от дублирующих команд эвакуации через его личный lock
                            lock (bot._taskLock)
                            {
                                // Если сосед в космосе и ЕЩЕ НЕ летит на станцию — даем команду
                                if (bot._inSpace && bot._iswarping is false && bot.CurrentTask != AccountTask.GoToStation)
                                {
                                    Logger.Log($"[Паника] Отправляю приказ на отварп соседу: {bot.Settings.Name}", LogType.Warning);
                                    
                                    bot._iswarping = true; // Выставляем флаг варпа, чтобы заблокировать повторные тики
                                    bot._taskQueue.Clear(); // Потокобезопасно чистим его личную очередь
                                    bot.CurrentTask = AccountTask.GoToStation;

                                    var globalToken = Program.GetGlobalToken();
                                    _ = Task.Run(async () => await bot.ExecuteEmergencyResponseAsync(isInitiator: false, globalToken));
                                }
                            }
                        }
                    });
                }

                // ЭВАКУАЦИЯ СЕБЯ (Текущий бот)
                lock (_taskLock)
                {
                    if (this._inSpace)
                    {
                        // КРИТИЧЕСКИЙ БАРЬЕР: Если мы УЖЕ в процессе варпа/отварпа на станцию,
                        // полностью игнорируем тик, предотвращая бесконечный цикл заклинивания!
                        if (this._iswarping is true || this.CurrentTask == AccountTask.GoToStation) 
                            return;

                        Logger.Log($"[{Settings.Name}] Инициатор паники уходит на эвакуацию в док.", LogType.Warning);
                        this._iswarping = true; // Запираем вход для следующих секундных тиков цикла
                        this._taskQueue.Clear(); // Чистим задачи строго под локальным _taskLock
                        this.CurrentTask = AccountTask.GoToStation;

                        var globalToken = Program.GetGlobalToken();
                        _ = Task.Run(async () => await this.ExecuteEmergencyResponseAsync(isInitiator: isFirstAlert, globalToken));
                    }
                    else
                    {
                        // Если мы уже на станции — просто переводим задачу в мониторинг, не запуская отварп
                        if (this.CurrentTask != AccountTask.CheckSecurity)
                        {
                            this.CurrentTask = AccountTask.CheckSecurity;
                            Logger.Log($"[{Settings.Name}] Корабль уже на станции в безопасности. Ждем смены статуса на Безопасно.", LogType.Info);
                        }
                    }
                }
            }
            // ========================================================
            // ОБРАБОТКА СБРОСА ОПАСНОСТИ НА "БЕЗОПАСНО" (value == true)
            // ========================================================
            else if (value is true)
            {
                // Если в синглтоне система уже помечена как Safe, пропускаем лог, чтобы не спамить экран
                if (SystemSafetyManager.GetSystemState(EVESystem).IsSafe is true) return;

                lock (_taskLock)
                {
                    SystemSafetyManager.SetSystemSafe(EVESystem);
                    
                    // СБРАСЫВАЕМ ЗАЩИТНЫЕ ФЛАГИ: разрешаем боту снова летать
                    this._iswarping = false; 
                    
                    // Переключаем текущую задачу обратно в проверку штатного состояния, 
                    // чтобы дерево поведения поняло: опасность прошла, можно собирать новую очередь задач
                    this.CurrentTask = AccountTask.CheckYourOwnState; 
                }

                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Статус системы изменился на БЕЗОПАСНО. Враги ушли. Возвращаемся к работе.", LogType.Success);
            }
        }
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ExecuteEmergencyResponse

    /// <summary>
    /// Экстренная реакция на угрозу. Сначала спасает корабль, затем координирует союзников и пишет в чат.
    /// </summary>
public async Task ExecuteEmergencyResponseAsync(bool isInitiator, CancellationToken token)
{
    lock (_taskLock)
    {
        if (_inSpace && this.CurrentTask != AccountTask.GoToStation)
        {
            switch (Settings.Script?.ToLower())
            {
                case "lowminer":
                    Logger.Log($"[{Settings.Name}|{EVESystem}] 🚨 УГРОЗА! Начинаю физическую эвакуацию корабля на станцию!", LogType.Warning);
                    this.ClearTasks();
                    this.CurrentTask = AccountTask.GoToStation;
                    break;

                case "localwatcher":
                    Logger.Log($"[{Settings.Name}|{EVESystem}] Наблюдатель зафиксировал угрозу. Позиция в доке удерживается.", LogType.Info);
                    this.CurrentTask = AccountTask.CheckSecurity;
                    break;

                default:
                    this.ClearTasks();
                    this.CurrentTask = AccountTask.GoToStation;
                    break;
            }
        }
    }

    // ТУТ ДАЛЕЕ ДОЛЖЕН ИДТИ ТВОЙ ФИЗИЧЕСКИЙ ВЫЗОВ ДЕЙСТВИЯ ОТВАРПА (например, клики по овервью)
    // Который выполнится строго один раз благодаря блокировке флагов в IsSaveLocal!
}





    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Clear Tasks

    /// <summary>
    /// Производит экстренную потокобезопасную очистку текущей очереди макросов аккаунта.
    /// Сбрасывает текущую задачу в состояние покоя, заставляя главный цикл воркера мгновенно среагировать на новые директивы.
    /// </summary>
    public void ClearTasks()
    {
        // Используем объект синхронизации Lock из .NET 9+
        lock (_taskLock)
        {
            _taskQueue.Clear();

            // BUG HIGH — Источник бесконечной логической петли! Метод `ClearTasks()` вызывается во время паники. Сброс `CurrentTask` в `AccountTask.CheckYourOwnState` внутри этого метода полностью ломает логику эвакуации. Смотри: в сеттере `IsSaveLocal` или методе `ExecuteEmergencyResponseAsync` ты жестко выставляешь `CurrentTask = AccountTask.GoToStation`, чтобы Дерево Поведения поняло — нужно лететь на станцию. Но внутри этих же методов параллельно вызывается `this.ClearTasks()`. Метод `ClearTasks()` заходит в этот блок и ТУТ ЖЕ НАМЕРТВО ПЕРЕЗАПИСЫВАЕТ `CurrentTask` обратно в `CheckYourOwnState`. В итоге на следующем тике `RunLoopAsync` дерево вместо отварпа видит статус «Проверь свое состояние», запускает штатный мирный скрипт с нуля, зрение снова фиксирует врага, снова вызывает панику, снова чистит задачи и опять сбрасывает стейт. Бот бесконечно гоняет по кругу проверку безопасности и не может начать физический отварп! Метод `ClearTasks()` должен ТОЛЬКО чистить очередь `_taskQueue`, но не имеет права трогать `CurrentTask`.
            _taskQueue.Clear();
            
            // УДАЛИТЬ СТРОКУ НИЖЕ:
            // CurrentTask = AccountTask.CheckYourOwnState;
        }
        Logger.Log($"[{Settings.Name}] Очередь задач экстренно очищена.", LogType.Info);
    }


    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
}

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

#region AccountTask

/// <summary>
/// Перечисление конечных состояний и задач (FSM) автоматизации игрового процесса.
/// Определяет конкретное действие, выполняемое асинхронным потоком бота в текущий момент времени.
/// </summary>
public enum AccountTask
{
    /// <summary>
    /// Эвакуация или плановый возврат: запуск процесса дока (Dock) на станцию или цитадель.
    /// </summary>
    GoToStation,

    /// <summary>
    /// Сканирование локального чата, проверка фильтров стендингов и маркеров безопасности системы.
    /// </summary>
    CheckSecurity,

    /// <summary>
    /// Базовый режим простоя / ожидания: проверка текущих параметров корабля, интерфейса и разворачивание сценариев рутины.
    /// </summary>
    CheckYourOwnState,

    /// <summary>
    /// Задача определить, что происходит на экране (анализ интерфейса при полной неопределенности).
    /// </summary>
    LookAround
}

#endregion

