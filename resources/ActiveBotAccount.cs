using OpenCvSharp;
using System.Text.Json;
using EVEEchoesBot.scenarios;
using Point = OpenCvSharp.Point;
using System.Text.RegularExpressions;


// [v] TODO 2026.05.30 Привести все тексты логгера к единому стилю 

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
    public TimeSpan TotalRuntime => TimeSpan.FromSeconds(_accumulatedSeconds);

    /// <summary>
    /// Потокобезопасное свойство для получения или изменения текущей звездной системы, где находится персонаж.
    /// </summary>
    public string EVESystem
    {
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
    #pragma warning disable IDE1006 // Отключаем проверку стиля именования
    internal object? _currenttarget { get; set; }
    #pragma warning restore IDE1006 // Включаем обратно для остального кода

    // Приватные поля управления потоками, памятью, деревом и файловой системой
    private CancellationTokenSource? _accountCts;
    private double _accumulatedSeconds;
    private readonly string _statsFilePath;
    private readonly System.Threading.Lock _taskLock = new();
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
        _statsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{settings.Name}_stats.json");

        // 4. Пытаемся загрузить сохраненную статистику из файла
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
            lock (_taskLock)
            {
                dto = new AccountStateDto
                {
                    AccountName    = Settings.Name,
                    Triggers       = TriggerCount,
                    RuntimeSeconds = _accumulatedSeconds,
                    CurrentTask    = CurrentTask.ToString(),
                    TaskQueue      = [.. _taskQueue],
                    EVESystem      = _eveSystem,
                    EVEShip        = _eveShip,

                    // Используем локальное время персонального компьютера вместо UTC для удобства чтения логов
                    LastUpdate     = DateTime.Now,

                    InSpace        = _inSpace,
                    IsWarping      = _iswarping,
                    IsInMiningZone = _isinminingzone,
                    HasTarget      = _hastarget,
                    WeaponryActive = _weaponryactive,
                    PlanetAssembly = _planetassembly,
                    CurrentTarget  = _currenttarget?.ToString(),
                    IsFullMain     = _isfullmain,
                    IsFullOre      = _isfullore
                };
            }

            // Сериализация и дисковая запись выполняются за пределами lock, чтобы не блокировать процессор
            string json = JsonSerializer.Serialize(dto, _jsonOptions);
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

    /// <summary>
    /// Инициализирует и запускает асинхронный рабочий цикл автоматизации для текущего игрового аккаунта.
    /// Создает связанный токен отмены на основе глобального токена приложения для поддержки каскадной остановки.
    /// </summary>
    /// <param name="globalToken">Глобальный токен отмены приложения (<see cref="CancellationToken"/>), сигнализирующий о закрытии бота.</param>
    public void Start(CancellationToken globalToken)
    {
        // 1. Взводим правильный статус для веб-панели
        this.State = BotState.Running;

        // 2. Если запускаемся впервые или после Стопа — фиксируем точку отсчета
        if (_startTime == null)
        {
            _startTime = DateTime.Now;
        }

        // Гарантированно очищаем ресурсы старого токена перед выделением новой памяти
        _accountCts?.Dispose();

        // Создаем сквозную связку токенов
        _accountCts = CancellationTokenSource.CreateLinkedTokenSource(globalToken);

        // Передаем токен созданной связки вторым параметром в Task.Run
        Task.Run(async () => await RunLoopAsync(_accountCts.Token), _accountCts.Token);
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
                    // ========================================================
                    // 1. СИНХРОНИЗАЦИЯ ДАННЫХ ДЛЯ ВЕБ-ИНТЕРФЕЙСА
                    // ========================================================
                    lock (_taskLock)
                    {
                        if (this.State == BotState.Running && _startTime != null)
                        {
                            // Аптайм текущей сессии для DTO
                            TimeSpan currentUptime = _accumulatedTime + (DateTime.Now - _startTime.Value);
                            this.RuntimeSeconds = currentUptime.TotalSeconds;
                        }

                        // Накапливаем секунды с использованием double, чтобы избежать погрешностей деления
                        _accumulatedSeconds = baseSeconds + sessionStopwatch.Elapsed.TotalSeconds;
                    }

                    // ========================================================
                    // 2. ВЫПОЛНЕНИЕ ТАКТА ДЕРЕВА ПОВЕДЕНИЯ
                    // ========================================================
                    // Кэшируем ссылку на случай, если веб-поток подменит её через SwitchScenario во время тика
                    var currentTree = _behaviorTree;

                    NodeStatus treeResult = await currentTree.TickAsync(this, token);

                    // ========================================================
                    // 3. РАСЧЕТ АДАПТИВНОГО ТАЙМИНГА И ОЖИДАНИЕ
                    // ========================================================
                    // Опрашиваем чаще (1с) если: макрос выполняется ИЛИ бот в космосе ИЛИ активны пушки
                    bool isHighActivity = (treeResult == NodeStatus.Running) || this._inSpace || this._weaponryactive;
                    int delaySeconds = isHighActivity ? 1 : 5;

    #if DEBUG
                    if (treeResult == NodeStatus.Running)
                    {
                        Logger.Log($"[{Settings.Name}] Дерево выполняет длительную операцию (Running). Следующий чек через {delaySeconds}с.", LogType.Test);
                    }
    #endif
                    // Использование упрощенного using без фигурных скобок
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, _delayCts.Token);

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), linkedCts.Token);
                    }
                    catch (TaskCanceledException) when (token.IsCancellationRequested)
                    {
                        // Фильтр сработал: отмена пришла от глобального токена остановки приложения/бота.
                        // Пробрасываем наверх во внешний цикл для чистого завершения RunLoopAsync.
                        throw;
                    }
                    catch (TaskCanceledException)
                    {
                        // Сюда мы попадаем, ТОЛЬКО если token.IsCancellationRequested == false.
                        // Значит, отмена пришла от локального _delayCts.Token (вызван SwitchScenario).
                        Logger.Log($"[{Settings.Name}] Пауза прервана командой из UI. Переключение на новый сценарий...", LogType.Info);
                    }
                    finally
                    {
                        lock (_scenarioLock)
                        {
                            _delayCts.Dispose();
                            _delayCts = new();
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    // Пробрасываем во внешний блок для корректного завершения работы
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Сбой в главном цикле обработки такта дерева: {ex.Message}", LogType.Error);
                    await Task.Delay(5000, token); // Защитная пауза при ошибках логики дерева
                }
            }
        }
        catch (TaskCanceledException)
        {
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Получен сигнал остановки аккаунта. Фиксация состояния дерева.", LogType.Info);
        }
        catch (Exception ex)
        {
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Критический сбой рабочего потока дерева поведения: {ex.Message}", LogType.Error);
        }
        finally
        {
            // ========================================================
            // 4. ФИНАЛИЗАЦИЯ И ГАРАНТИРОВАННОЕ СОХРАНЕНИЕ СТАТИСТИКИ
            // ========================================================
            sessionStopwatch.Stop();

            lock (_taskLock)
            {
                _accumulatedSeconds = baseSeconds + (long)sessionStopwatch.Elapsed.TotalSeconds;
                SaveStats();
            }

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
        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Начало выполнения метода детекции угрозы.", LogType.Test);

        if (Hwnd == IntPtr.Zero)
        {
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Окно целевой программы не найдено.", LogType.Error);
            return SecurityCheckResult.Unknown;
        }

        string pathImg1 = Path.Combine(Program.TemplatesDir, "imgLocalChatHead.png");
        string pathImg2 = Path.Combine(Program.TemplatesDir, "imgLocalChatIcon.png");

        Rect localRegion1 = GameRegions.LocalChat.GetOpenCvRect();
        Rect localRegion2 = GameRegions.LocalChatIcon.GetOpenCvRect();
        string debugDir = Path.Combine(Program.TemplatesDir, "..", "DebugScreenshots");

        Mat? screenshot = null;
        try
        {
            // ========================================================
            // ЗАХВАТ ЭКРАНА С ЗАЩИТОЙ GDI WINAPI
            // ========================================================
            await Program.GdiSemaphore.WaitAsync(token);
            try
            {
                screenshot = await Task.Run(() => Tools.CaptureWindow(Hwnd), token);
            }
            finally
            {
                Program.GdiSemaphore.Release();
            }

            if (screenshot?.Empty() ?? true)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось выполнить повторный захват окна.", LogType.Error);
                return SecurityCheckResult.Unknown;
            }

            // Выносим расчет безопасных регионов в Task.Run для разгрузки вызывающего потока
            var currentSnap = screenshot;
            var (safeRegion1, safeRegion2) = await Task.Run(() => (
                Tools.ClampRegion(localRegion1, currentSnap.Width, currentSnap.Height),
                Tools.ClampRegion(localRegion2, currentSnap.Width, currentSnap.Height)
            ), token);

            if (safeRegion1.Width <= 0 || safeRegion1.Height <= 0 || safeRegion2.Width <= 0 || safeRegion2.Height <= 0)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Область поиска выходит за рамки окна.", LogType.Error);
                return SecurityCheckResult.Unknown;
            }

            // ========================================================
            // ЭТАП 1: ЧАТ РАЗВЕРНУТ (Ищем Шапку чата)
            // ========================================================
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
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось сохранить отладочный кадр: {ex.Message}", LogType.Warning);
                }
    #endif
                return RunLocalCheck(currentSnap, safeRegion1);
            }

            // ========================================================
            // ЭТАП 2: ЧАТ СВЕРНУТ (Ищем иконку для разворачивания)
            // ========================================================
            Point? foundImg2 = await Task.Run(() => Tools.FindTemplateInRegion(currentSnap, pathImg2, safeRegion2, 0.80), token);

            if (foundImg2.HasValue)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Локальный чат свернут. Обнаружена иконка развертывания.", LogType.Test);

    #if DEBUG
                try
                {
                    using Mat cropped = new(currentSnap, safeRegion2);
                    Directory.CreateDirectory(debugDir);
                    Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatIcon_FOUND.png"), cropped);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось сохранить отладочный кадр: {ex.Message}", LogType.Warning);
                }
    #endif

                // Асинхронный безопасный клик по координатам иконки
                await this.ClickPointAsync(foundImg2.Value, token, minSec: 1, maxSec: 2, offset: 2);
                await Task.Delay(3500, token);

                // Повторный защищенный захват экрана после клика развертывания
                Mat? freshScreenshot = null;
                try
                {
                    await Program.GdiSemaphore.WaitAsync(token);
                    try
                    {
                        freshScreenshot = await Task.Run(() => Tools.CaptureWindow(Hwnd), token);
                    }
                    finally
                    {
                        Program.GdiSemaphore.Release();
                    }

                    if (freshScreenshot?.Empty() ?? true) return SecurityCheckResult.Unknown;

                    var currentFresh = freshScreenshot;
                    Rect freshSafeRegion1 = await Task.Run(() => Tools.ClampRegion(localRegion1, currentFresh.Width, currentFresh.Height), token);
                    Point? retryImg1 = await Task.Run(() => Tools.FindTemplateInRegion(currentFresh, pathImg1, freshSafeRegion1, 0.80), token);

                    if (retryImg1.HasValue)
                    {
                        return RunLocalCheck(currentFresh, freshSafeRegion1);
                    }
                }
                finally
                {
                    freshScreenshot?.Dispose(); // Гарантированная утилизация второго скриншота
                }

                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Интерфейс чата не открылся после клика.", LogType.Warning);
                return SecurityCheckResult.Unknown;
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Пробрасываем корректную отмену в RunLoopAsync
        }
        catch (Exception ex)
        {
            Logger.Log($"[{Settings.Name}] Критический сбой модуля проверки безопасности: {ex.Message}", LogType.Error);
            return SecurityCheckResult.Unknown;
        }
        finally
        {
            screenshot?.Dispose(); // Гарантированная очистка базового Mat при любом исходе метода
        }

        // ========================================================
        // ЭТАП 3: ПОЛНАЯ НЕОПРЕДЕЛЕННОСТЬ
        // ========================================================
        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Шаблоны чата отсутствуют на экране. Смена сессии или загрузка.", LogType.Info);
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
    private static readonly string[] SecurityTemplates = ["imgLocalCriminal.png", "imgLocalMinus.png", "imgLocalNeutral.png"];

    private SecurityCheckResult RunLocalCheck(Mat screenshot, Rect searchRegion)
    {
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
                    using Mat croppedRegion = new(screenshot, searchRegion);
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

            // Защищаем внутренние флаги бота от гонок
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
                    // При опасности на старте не делаем return, идем обрабатывать угрозу локально
                }
            }

            // ========================================================
            // ОБРАБОТКА ИЗМЕНЕНИЯ СТАТУСА (УГРОЗА)
            // ========================================================
            if (value is false)
            {
                bool isFirstAlert = SystemSafetyManager.TrySetSystemDanger(EVESystem);

                if (isFirstAlert)
                {
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] ВНИМАНИЕ! Первичная фиксация угрозы в системе. Запуск каскадной паники.", LogType.Warning);

                    // СТРОГО ОДНОКРАТНАЯ ОТПРАВКА УВЕДОМЛЕНИЯ В ЧАТ
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

                    // Рассылаем панику остальным окнам в этой же системе
                    Task.Run(() =>
                    {
                        var botsToPanic = Program.GetActiveBots().Where(b => b != this && b.EVESystem == this.EVESystem).ToList();

                    foreach (var bot in botsToPanic)
                    {
                        try
                        {
                            if (bot._inSpace && bot.CurrentTask != AccountTask.GoToStation)
                            {
                                bot.ClearTasks();

                                // Запускаем эвакуацию соседа в пуле потоков без блокировки текущего цикла
                                var globalToken = Program.GetGlobalToken();
                                _ = Task.Run(async () => await bot.ExecuteEmergencyResponseAsync(isInitiator: false, globalToken));
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"Ошибка паники для окна {bot.Settings.Name}: {ex.Message}", LogType.Error);
                        }
                    }

                    });
                }

                if (this._inSpace && this.CurrentTask != AccountTask.GoToStation)
                {
                    this.ClearTasks();

                    // Вызываем асинхронный метод из синхронного контекста в режиме "выстрелил-и-забыл"
                    // Используем глобальный токен из Program
                    var globalToken = Program.GetGlobalToken();
                    _ = Task.Run(async () => await this.ExecuteEmergencyResponseAsync(isInitiator: isFirstAlert, globalToken));
                }


                else if (!this._inSpace)
                {
                    // Если мы на станции — просто переводим задачу в ожидание/мониторинг, не запуская эвакуацию
                    this.CurrentTask = AccountTask.CheckSecurity;
                    Logger.Log($"[{Settings.Name}] Корабль уже находится в безопасности (в доке станции). Эвакуация не требуется.", LogType.Info);
                }
            }
            else if (value is true)
            {
                // Проверяем текущее состояние из синглтона. Если там и так Safe — игнорируем, чтобы не спамить лог.
                if (SystemSafetyManager.GetSystemState(EVESystem).IsSafe is true) return;

                SystemSafetyManager.SetSystemSafe(EVESystem);
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Статус системы изменился на БЕЗОПАСНО. Враги покинули систему.", LogType.Info);
            }
        }
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ExecuteEmergencyResponse

    /// <summary>
    /// Экстренная реакция на угрозу. Сначала спасает корабль, затем координирует союзников и пишет в чат.
    /// </summary>
    public async Task ExecuteEmergencyResponseAsync(
        bool isInitiator,
        CancellationToken token) // Убрали лишний параметр manager!
    {
        // ========================================================
        // ПРАВИЛО 1: НЕМЕДЛЕННАЯ ЭВАКУАЦИЯ
        // ========================================================
        lock (_taskLock)
        {
            if (_inSpace && this.CurrentTask != AccountTask.GoToStation)
            {
                switch (Settings.Script?.ToLower())
                {
                    case "lowminer":
                        Logger.Log($"[{Settings.Name}|{EVESystem}] 🚨 УГРОЗА! Инициирована экстренная эвакуация на станцию!", LogType.Warning);
                        this.ClearTasks();
                        this.CurrentTask = AccountTask.GoToStation;
                        break;

                    case "localwatcher":
                        Logger.Log($"[{Settings.Name}|{EVESystem}] Наблюдатель зафиксировал угрозу, но остается на позиции в доке.", LogType.Info);
                        this.CurrentTask = AccountTask.CheckSecurity;
                        break;

                    default:
                        this.ClearTasks();
                        this.CurrentTask = AccountTask.GoToStation;
                        break;
                }
            }
        }

        await Task.Delay(500, token);

        // ========================================================
        // ПРАВИЛО 2: ОПОВЕЩЕНИЕ ВСЕХ СВОИХ БОТОВ В ЭТОЙ ЖЕ СИСТЕМЕ
        // ========================================================
        if (isInitiator)
        {
            Logger.Log($"[{Settings.Name}|{EVESystem}] Рассылка сигнала тревоги остальным ботам в системе...", LogType.Warning);

            // ИСПРАВЛЕНО: Вызываем наш новый потокобезопасный метод из Program напрямую!
            var companionBots = Program.GetActiveBots();

            foreach (var companion in companionBots)
            {
                if (companion != this &&
                    companion.EVESystem == this.EVESystem &&
                    companion.CurrentTask != AccountTask.GoToStation)
                {
                    Logger.Log($"[{Settings.Name}] -> Отправка команды паники для {companion.Settings.Name}...", LogType.Info);

                    _ = companion.ExecuteEmergencyResponseAsync(isInitiator: false, token);
                }
            }
        }

        // ========================================================
        // ПРАВИЛО 3: ОТПРАВКА СООБЩЕНИЯ В ЧАТ (Только для инициатора)
        // ========================================================
        if (isInitiator)
        {
            Logger.Log($"[{Settings.Name}|{EVESystem}] Этот аккаунт — обнаружил угрозу первым. Запуск макроса чата альянса.", LogType.Warning);

            try
            {
                await ScenarioFactory.RunAliChatWarningAsync(this, token);
            }
            catch (OperationCanceledException)
            {
                Logger.Log($"[{Settings.Name}] Макрос чата прерван отменой потока.", LogType.Warning);
            }
            catch (Exception ex)
            {
                Logger.Log($"Ошибка отправки сообщения в чат альянса: {ex.Message}", LogType.Error);
            }
        }
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

            // Сбрасываем текущую задачу в состояние покоя, чтобы главный цикл RunLoopAsync понял, что нужно переключиться
            CurrentTask = AccountTask.CheckYourOwnState;
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

