using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using static EVEEchoesBot.Program;
using static EVEEchoesBot.resources.Tools;
using System.Diagnostics;
using System.Text.Json;
using System.Collections.Concurrent;
using static EVEEchoesBot.resources.Logger;
using EVEEchoesBot.resources;
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
        if (State == BotState.Stopped)
            return "00 д. 00 ч. 00 м. 00 с.";

        // Считаем время текущей сессии (если запущен) + то, что накопилось до пауз
        var currentSessionTime = _startTime.HasValue ? (DateTime.Now - _startTime.Value) : TimeSpan.Zero;
        var total = _accumulatedTime + currentSessionTime;

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
        internal bool _inSpace = false;
        internal bool _isinzone = false;
        internal bool _iswarping = false;
        internal bool _hastarget = false;
        internal bool _weaponryactive = false;
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
        private readonly BehaviorNode _behaviorTree;

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

            // 2. Формируем путь к файлу состояния для конкретного аккаунта
            _statsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"stats_{settings.Name}.json");

            // 3. Пытаемся загрузить сохраненную статистику из файла
            _ = TryLoadLastStatsAndQueue();

            // 4. КОМПИЛЯЦИЯ ДЕРЕВА ПОВЕДЕНИЯ: Навечно привязываем воркер к его ветвящемуся сценарию
            string currentScript = settings.Script ?? "mining";
            _behaviorTree = ScenarioFactory.CreateTree(currentScript);

            // Старая FSM-инициализация очередей удалена. Бот готов к тикам дерева поведения.
        }

        // TODO: Разобраться почему не используется
        /// <summary>
        /// Производит атомарный инкремент счетчика срабатываний триггеров из любой части логики автоматизации бота.
        /// </summary>
        public void IncrementTrigger() => Interlocked.Increment(ref _triggerCount);

    #endregion

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

        // Используем объект синхронизации Lock из .NET 9+
        lock (_taskLock)
        {
            if (addToFront)
            {
                // Вставляем элементы в начало очереди, строго сохраняя их исходную последовательность
                _taskQueue.InsertRange(0, tasks);
            }
            else
            {
                // Стандартное добавление элементов в хвост очереди сценария
                _taskQueue.AddRange(tasks);
            }

            // Синхронизируем измененную очередь с файлом состояния на диске под защитой блокировки
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

                    // Выполняем интерактивный опрос оператора с блокировкой системного потока ввода Console.In
                    lock (Console.In)
                    {
                        // Проверяем и валидируем звездную систему персонажа
                        if (string.IsNullOrEmpty(state.EVESystem) || state.EVESystem == "???")
                        {
                            Console.ResetColor();
                            string sys = "";
                            while (string.IsNullOrWhiteSpace(sys))
                            {
                                Console.Write($"[{state.AccountName}] Введите текущую звездную систему (например, Jita): ");
                                sys = Console.ReadLine()?.Trim() ?? "";
                            }
                            _eveSystem = sys;
                        }
                        else
                        {
                            _eveSystem = state.EVESystem;
                        }

                        // Загружаем дату последнего сбора планетарных ресурсов
                        _planetassembly = state.PlanetAssembly;

                        // Проверяем и валидируем текущий корабль персонажа
                        if (string.IsNullOrEmpty(state.EVEShip) || state.EVEShip == "???")
                        {
                            Console.ResetColor();
                            string ship = "";
                            while (string.IsNullOrWhiteSpace(ship))
                            {
                                Console.Write($"[{state.AccountName}] Введите название корабля (например, Covetor II): ");
                                ship = Console.ReadLine()?.Trim() ?? "";
                            }
                            _eveShip = ship;
                        }
                        else
                        {
                            _eveShip = state.EVEShip;
                        }

                        // Умная проверка локации (космос / станция) без ошибок компиляции и лишних вопросов к пользователю
                        if (state.InSpace.HasValue)
                        {
                            // Если значение успешно прочитано из JSON, берем его и НЕ открываем консоль опроса
                            _inSpace = state.InSpace.Value;
                        }
                        else
                        {
                            // Консольный опрос сработает ТОЛЬКО один раз, если поля в JSON файле еще физически нет
                            Console.ResetColor();
                            Console.Write($"[{state.AccountName}] Корабль сейчас в космосе? (y/n, по умолчанию n): ");
                            string spaceAnswer = Console.ReadLine()?.Trim().ToLower() ?? "";

                            if (spaceAnswer == "y" || spaceAnswer == "yes" || spaceAnswer == "д" || spaceAnswer == "да")
                            {
                                _inSpace = true;
                            }
                            else if (spaceAnswer == "n" || spaceAnswer == "no" || spaceAnswer == "н" || spaceAnswer == "нет")
                            {
                                _inSpace = false;
                            }
                            else
                            {
                                // Если ввели некорректные данные, безопасно приводим bool? к дефолтному false
                                _inSpace = state.InSpace ?? false;
                            }
                        }
                    }
                }

                return true;
            }
        }
        catch (Exception ex)
        {
            // Маршрутизируем сбой десериализации через штатный логгер платформы, чтобы событие улетело в CSV-отчет
            Log($"Ошибка загрузки файла состояния: {ex.Message}", LogType.Error);
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
        Logger.Log($"[Аккаунт {Settings?.Name ?? "ID_" + CurrentTask}] Система изменена вручную на: {systemName}", LogType.Info);
    }

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    public void UpdateShipManually(string shipName)
    {
        lock (_taskLock)
        {
            _eveShip = shipName;
        }
        Logger.Log($"[Аккаунт {Settings?.Name ?? "ID_" + CurrentTask}] Корабль изменен вручную на: {shipName}", LogType.Info);
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
                    IsInMiningZone = _isinzone,
                    HasTarget     = _hastarget,
                    WeaponryActive = _weaponryactive,
                    PlanetAssembly = _planetassembly,
                    CurrentTarget  = _currenttarget?.ToString()
                };
            }

            // Сериализация и дисковая запись выполняются за пределами lock, чтобы не блокировать процессор
            string json = JsonSerializer.Serialize(dto, _jsonOptions);
            File.WriteAllText(_statsFilePath, json);
        }
        catch (Exception ex)
        {
            // Вызов логгера строго в соответствии с сигнатурой вашего бота (message, type)
            Log($"Не удалось сохранить статистику аккаунта '{Settings.Name}': {ex.Message}", LogType.Warning);
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
        // Если уже остановлен — ничего не делаем
        if (State == BotState.Stopped) return;

        State = BotState.Stopped;

        // Плавное гашение асинхронного цикла воркера
        _accountCts?.Cancel();

        // Полный сброс таймеров аптайма (Требование №3)
        _startTime = null;
        _accumulatedTime = TimeSpan.Zero;

        Logger.Log($"[{Settings?.Name}] Поток автоматизации полностью остановлен. Время сброшено.", LogType.Warning);
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    /// <summary>
    /// Кнопка "Пауза": Приостанавливает поток автоматизации, сохраняя набранное время работы.
    /// </summary>
    public void Pause()
    {
        if (State != BotState.Running) return;

        this.State = BotState.Paused;

        // Фиксируем отработанное время в накопитель перед сбросом точки старта
        if (_startTime != null)
        {
            _accumulatedTime += (DateTime.Now - _startTime.Value);
        }

        _startTime = null; // Сбрасываем точку старта, останавливая отсчет

        // Плавное гашение асинхронного цикла воркера
        _accountCts?.Cancel();

        Logger.Log($"[{Settings?.Name}] Поток автоматизации приостановлен (Пауза). Время сохранено.", LogType.Warning);
    }

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    public double RuntimeSeconds
    {
        get
        {
            // Если бот работает прямо сейчас, возвращаем накопленное время + время текущей сессии
            if (State == BotState.Running && _startTime != null)
            {
                return (_accumulatedTime + (DateTime.Now - _startTime.Value)).TotalSeconds;
            }
            // Если бот на паузе или стопе, возвращаем только то, что успели накопить
            return _accumulatedTime.TotalSeconds;
        }
        set
        {
            // Пустой сеттер, если коду где-то нужно принудительно обнулить поле (например в Stop())
            if (value == 0) _accumulatedTime = TimeSpan.Zero;
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
        Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Поток запущен. Начало работы по Дереву поведения: '{Settings.Script ?? "mining"}'.", LogType.Info);

        var sessionStart = System.DateTime.UtcNow;

        // Включаем высокоточный секундомер времени работы для этого окна
        var sessionStopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Фиксируем стартовое значение, которое мы уже успели загрузить из JSON прошлых сессий
        long baseSeconds = (long)_accumulatedSeconds;

        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {

                    // ДИНАМИЧЕСКИЙ РАСЧЕТ ВРЕМЕНИ ДЛЯ ВЕБ-ИНТЕРФЕЙСА (внутри RunLoopAsync)
                    if (this.State == BotState.Running && _startTime != null)
                    {
                        // Текущий аптайм = то, что накопили на прошлых паузах + разница с момента текущего старта
                        TimeSpan currentUptime = _accumulatedTime + (DateTime.Now - _startTime.Value);

                        // Передаем чистые секунды типа double в поле DTO
                        this.RuntimeSeconds = currentUptime.TotalSeconds;
                    }

                    // ОБНОВЛЕНИЕ ВРЕМЕНИ: Прибавляем секунды текущей сессии к базовому времени из файла
                    _accumulatedSeconds = baseSeconds + (sessionStopwatch.ElapsedMilliseconds / 1000);

                    // ========================================================
                    // ГЛАВНЫЙ И ЕДИНСТВЕННЫЙ ЭТАП: ТИК ДЕРЕВА ПОВЕДЕНИЯ
                    // ========================================================
                    // Дерево само выполнит нужные проверки (включая безопасность) и запустит 
                    // соответствующие макросы, вернув статус выполнения (Success / Failure / Running)
                    NodeStatus treeResult = await _behaviorTree.TickAsync(this, token);

                    // ДИНАМИЧЕСКИЙ ТАЙМИНГ ТАКТОВ:
                    // Если дерево находится в состоянии выполнения длительного макроса (Running), 
                    // опрашиваем дерево чаще (каждую секунду), чтобы мгновенно среагировать на угрозу в локале.
                    // Если дерево завершило такт (Success/Failure), делаем стандартную паузу в 5 секунд.
                    int delaySeconds = (treeResult == NodeStatus.Running) ? 1 : 5;

    #if DEBUG
                    // В режиме отладки логируем результат прохода дерева для контроля стабильности узлов
                    if (treeResult == NodeStatus.Running)
                    {
                        Log($"[{Settings.Name}] Дерево выполняет длительную операцию (Running). Следующий чек через {delaySeconds}с.", LogType.Test);
                    }
    #endif

                    // Адаптивная задержка между тактами принятия решений ботом
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                }
                catch (TaskCanceledException)
                {
                    // Перехватываем отмену внутри цикла, чтобы управление перешло во внешний блок catch/finally
                    throw;
                }
                catch (Exception ex)
                {
                    Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Сбой в главном цикле обработки такта дерева: {ex.Message}", LogType.Error);
                    await Task.Delay(5000, token);
                }
            }
        }
        catch (TaskCanceledException)
        {
            Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Получен сигнал остановки аккаунта. Фиксация состояния дерева.", LogType.Info);
        }
        catch (Exception ex)
        {
            Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Критический сбой рабочего потока дерева поведения: {ex.Message}", LogType.Error);
        }
        finally
        {
            // Финальное обновление времени перед сохранением на диск
            sessionStopwatch.Stop();
            _accumulatedSeconds = baseSeconds + (sessionStopwatch.ElapsedMilliseconds / 1000);

            // ГАРАНТИРОВАННОЕ СОХРАНЕНИЕ: Выполнится всегда при закрытии или падении потока
            lock (_taskLock)
            {
                SaveStats();
            }
            int sessionSeconds = (int)(System.DateTime.UtcNow - sessionStart).TotalSeconds;

            Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Состояние сохранено. Поток поведения остановлен. Время работы в сессии (сек): {sessionSeconds}", LogType.Info);
        }
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ForceSaveStats

    /// <summary>
    /// Выполняет немедленное принудительное сохранение текущей статистики и очереди задач на диск.
    /// Используется внешними модулями для экстренной фиксации состояния аккаунта под защитой блокировки.
    /// </summary>
    public void ForceSaveStats()
    {
        // Безопасно блокируем контекст перед вызовом внутренней логики сериализации
        lock (_taskLock)
        {
            SaveStats();
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
        Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Начало выполнения метода.", LogType.Test);

        if (Hwnd == IntPtr.Zero)
        {
            Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Окно целевой программы не найдено.", LogType.Error);
            return SecurityCheckResult.Unknown; // Ошибка -> Осматриваемся
        }

        string pathImg1 = Path.Combine(Program.TemplatesDir, "imgLocalChatHead.png");
        string pathImg2 = Path.Combine(Program.TemplatesDir, "imgLocalChatIcon.png");

        Rect localRegion1 = GameRegions.LocalChat.GetOpenCvRect();
        Rect localRegion2 = GameRegions.LocalChatIcon.GetOpenCvRect();
        string debugDir = Path.GetFullPath(Path.Combine(Program.TemplatesDir, "..", "DebugScreenshots"));

        using Mat? screenshot = Tools.CaptureWindow(Hwnd);
        if (screenshot?.Empty() is not false || screenshot.Width <= 0 || screenshot.Height <= 0)
        {
            Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось выполнить повторный захват окна.", LogType.Error);
            return SecurityCheckResult.Unknown; // Ошибка -> Осматриваемся
        }

        Rect safeRegion1 = Tools.ClampRegion(localRegion1, screenshot.Width, screenshot.Height);
        Rect safeRegion2 = Tools.ClampRegion(localRegion2, screenshot.Width, screenshot.Height);

        if (safeRegion1.Width <= 0 || safeRegion1.Height <= 0 || safeRegion2.Width <= 0 || safeRegion2.Height <= 0)
        {
            Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Область поиска выходит за рамки окна.", LogType.Error);
            return SecurityCheckResult.Unknown; // Ошибка -> Осматриваемся
        }

        // ========================================================
        // ЭТАП 1: Ищем Шапку чата (Развернут ли чат?)
        // ========================================================
        Point? foundImg1 = Tools.FindTemplateInRegion(screenshot, pathImg1, safeRegion1, 0.80);

        if (foundImg1.HasValue)
        {
    #if DEBUG
            try
            {
                using Mat cropped = new(screenshot, safeRegion1);
                Directory.CreateDirectory(debugDir);
                Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatHead_FOUND.png"), cropped);
            }
            catch (Exception ex) {
                Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось сохранить отладочный кадр: {ex.Message}", LogType.Warning);
            }
    #endif
            return RunLocalCheck(screenshot, safeRegion1); // Возвращает Safe или Danger
        }

        // ========================================================
        // ЭТАП 2: Чат свернут, ищем Иконку для разворачивания
        // ========================================================
        Point? foundImg2 = Tools.FindTemplateInRegion(screenshot, pathImg2, safeRegion2, 0.80);

        if (foundImg2.HasValue)
        {
            Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Локальный чат свернут. Обнаружена иконка развертывания.", LogType.Test);

    #if DEBUG
            try
            {
                using Mat cropped = new(screenshot, safeRegion2);
                Directory.CreateDirectory(debugDir);
                Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatIcon_FOUND.png"), cropped);
            }
            catch (Exception ex) {
                Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось сохранить отладочный кадр: {ex.Message}", LogType.Warning);
            }
    #endif

            // ИСПРАВЛЕНО: Заменяем синхронный вызов Tools.SmartClick на наш эталонный асинхронный метод расширения
            await this.ClickPointAsync(foundImg2.Value, token, minSec: 1, maxSec: 2, offset: 2);

            await Task.Delay(3500, token);

            using Mat? freshScreenshot = Tools.CaptureWindow(Hwnd);
            if (freshScreenshot?.Empty() is not false) return SecurityCheckResult.Unknown;

            Rect freshSafeRegion1 = Tools.ClampRegion(localRegion1, freshScreenshot.Width, freshScreenshot.Height);
            Point? retryImg1 = Tools.FindTemplateInRegion(freshScreenshot, pathImg1, freshSafeRegion1, 0.80);

            if (retryImg1.HasValue)
            {
                return RunLocalCheck(freshScreenshot, freshSafeRegion1); // Возвращает Safe или Danger
            }

            Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Интерфейс чата не открылся после клика.", LogType.Warning);
            return SecurityCheckResult.Unknown; // Ошибка открытия интерфейса
        }

        // ========================================================
        // ЭТАП 3: ЖЕЛЕЗНАЯ ТИШИНА (Интерфейс не найден вообще)
        // ========================================================
        Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Шаблоны чата отсутствуют на экране. Смена сессии или загрузка экрана.", LogType.Info);
        return SecurityCheckResult.Unknown; // Полная неопределенность -> Запуск "Осмотрись"
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
    /// <param name="screenshot">Текущая графическая матрица скриншота окна эмулятора <see cref="Mat"/>.</param>
    /// <param name="searchRegion">Прямоугольная область экрана <see cref="Rect"/>, в которой отображаются маркеры чата.</param>
    /// <returns>Возвращает <c>true</c>, если обнаружены все 3 маркера (система чиста); возвращает <c>false</c>, если обнаружена угроза [INDEX].</returns>
    private SecurityCheckResult RunLocalCheck(Mat screenshot, Rect searchRegion)
    {
        Rect safeSearchRegion = Tools.ClampRegion(searchRegion, screenshot.Width, screenshot.Height);

        string[] templates = ["imgLocalCriminal.png", "imgLocalMinus.png", "imgLocalNeutral.png"];
        int foundCount = 0;

        foreach (string templateName in templates)
        {
            string fullTemplatePath = Path.Combine(Program.TemplatesDir, templateName);
            if (!File.Exists(fullTemplatePath)) continue;

            Point? foundPoint = Tools.FindTemplateInRegion(screenshot, fullTemplatePath, safeSearchRegion, 0.88);

            if (foundPoint.HasValue)
            {
                foundCount++;
    #if DEBUG
                try
                {
                    using Mat croppedRegion = new(screenshot, safeSearchRegion);
                    string debugDir = Path.GetFullPath(Path.Combine(Program.TemplatesDir, "..", "DebugScreenshots"));
                    Directory.CreateDirectory(debugDir);
                    string debugPath = Path.Combine(debugDir, $"{Settings.Name}_{Path.GetFileNameWithoutExtension(templateName)}_FOUND.png");
                    Cv2.ImWrite(debugPath, croppedRegion);
                }
                catch (Exception ex)
                {
                    Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось сохранить снимок экрана: {ex.Message}", LogType.Warning);
                }
    #endif
            }
        }

        // ========================================================
        // ИСПРАВЛЕННАЯ СТРЕДЖ-ЛОГИКА ВЕРДИКТОВ:
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
            Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Маркеры безопасности не найдены (0 из 3). Интерфейс смазан или перекрыт. Осматриваемся.", LogType.Warning);
            return SecurityCheckResult.Unknown;
        }

        // 3. РЕАЛЬНАЯ ОПАСНОСТЬ: Найдено 1 или 2 маркера. 
        // Это значит, что интерфейс чата виден ИДЕАЛЬНО, но часть маркеров сместилась/исчезла из-за появления минуса/нейтрала.
        Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] ВНИМАНИЕ: Найдено маркеров безопасности: {foundCount} из 3. Четкая фиксация угрозы!", LogType.Warning);
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
                        Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Стартовая инициализация: система безопасна. Мониторинг запущен.", LogType.Info);
                        return;
                    }

                    Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Стартовая проверка: система СРАЗУ ОПАСНА! Запуск экстренных процедур.", LogType.Warning);
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
                    Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] ВНИМАНИЕ! Первичная фиксация угрозы в системе. Запуск каскадной паники.", LogType.Warning);

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
                            Log($"Ошибка отправки сообщения в чат альянса: {ex.Message}", LogType.Error);
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
                                // ИСПРАВЛЕНО: Другие окна паникуют (уходят в док) ТОЛЬКО если они реально в космосе!
                                if (bot._inSpace)
                                {
                                    bot.ClearTasks();
                                    bot.ExecuteEmergencyResponse(isInitiator: false);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log($"Ошибка паники для окна {bot.Settings.Name}: {ex.Message}", LogType.Error);
                            }
                        }
                    });
                }

                // ИСПРАВЛЕНО: Текущее окно уводим в док ТОЛЬКО если оно реально находится в космосе
                if (this._inSpace && this.CurrentTask != AccountTask.GoToStation)
                {
                    this.ClearTasks();
                    this.ExecuteEmergencyResponse(isInitiator: isFirstAlert);
                }
                else if (!this._inSpace)
                {
                    // Если мы на станции — просто переводим задачу в ожидание/мониторинг, не запуская эвакуацию
                    this.CurrentTask = AccountTask.CheckSecurity;
                    Log($"[{Settings.Name}] Корабль уже находится в безопасности (в доке станции). Эвакуация не требуется.", LogType.Info);
                }
            }
            else if (value is true)
            {
                // Проверяем текущее состояние из синглтона. Если там и так Safe — игнорируем, чтобы не спамить лог.
                if (SystemSafetyManager.GetSystemState(EVESystem).IsSafe is true) return;

                SystemSafetyManager.SetSystemSafe(EVESystem);
                Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Статус системы изменился на БЕЗОПАСНО. Враги покинули систему.", LogType.Info);
            }
        }
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ExecuteEmergencyResponse

    /// <summary>
    /// Формирует и экстренно активирует пакет сценариев эвакуации при обнаружении угрозы в локальной системе.
    /// На основе флага <paramref name="isInitiator"/> определяет необходимость отправки оповещения альянсу
    /// и закидывает собранный список задач в самое начало очереди с наивысшим приоритетом.
    /// </summary>
    /// <param name="isInitiator">Если <c>true</c> — данный аккаунт является первоисточником обнаружения врага и должен отправить варнинг в чат.</param>
    public void ExecuteEmergencyResponse(bool isInitiator)
    {
        // 1. Если корабль находится в космосе, немедленно меняем его глобальную задачу
        if (_inSpace)
        {
            switch (Settings.Script?.ToLower())
            {
                case "localwatcher":
                    // Наблюдателю отварп не нужен, он контролирует локал из безопасности
                    Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Наблюдатель остается на позиции.", LogType.Info);
                    this.CurrentTask = AccountTask.CheckSecurity;
                    break;

                case "lowminer":
                    // ОПТИМИЗИРОВАНО: Переводим шахтера в режим экстренного возврата на станцию
                    Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] 🚨 Сценарий lowminer активирует экстренную эвакуацию корабля!", LogType.Warning);
                    this.ClearTasks(); // Очищаем текущие шахтерские подзадачи тика
                    this.CurrentTask = AccountTask.GoToStation; // Переключаем корень дерева на эвакуацию
                    break;

                default:
                    // Фолбек безопасности для любых других скриптов
                    Log($"[{Settings.Name}] Неизвестный скрипт в космосе при угрозе. Принудительный отварп в док.", LogType.Warning);
                    this.ClearTasks();
                    this.CurrentTask = AccountTask.GoToStation;
                    break;
            }
        }
        else
        {
            // Если мы уже на станции, просто продолжаем сканировать окружение
            this.CurrentTask = AccountTask.CheckSecurity;
            Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Корабль в безопасности (станция/цитадель). Эвакуация не требуется.", LogType.Info);
        }

        // 2. Оповещение альянса через макрос чата (выполняется асинхронно в фоне)
        if (isInitiator)
        {
            Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Этот аккаунт — инициатор паники. Запуск макроса чата...", LogType.Warning);
            Task.Run(async () =>
            {
                try
                {
                    CancellationToken token = _accountCts?.Token ?? Program.GetGlobalToken();
                    await ScenarioFactory.RunAliChatWarningAsync(this, token);
                }
                catch (Exception ex)
                {
                    Log($"Ошибка отправки сообщения в чат альянса: {ex.Message}", LogType.Error);
                }
            });
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
        Log($"[{Settings.Name}] Очередь задач экстренно очищена.", LogType.Info);
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Warp And Dock

    /// <summary>
    /// ДЕЙСТВИЕ: Инициирует варп и автоматический док на домашнюю станцию/цитадель.
    /// </summary>
    public async Task<bool> WarpAndDockToHomeStationAsync(CancellationToken token)
    {
        // Симулируем задержку на сетевой запрос или клик по интерфейсу
        await Task.Delay(100, token);
        Logger.Log($"[ЗАГЛУШКА] {Settings.Name} выполняет команду: Варп и Док на домашнюю станцию.", LogType.Test);

        // Для теста принудительно переводим стейт в док (космос = false)
        _inSpace = false;
        return true;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Check Cargo

    /// <summary>
    /// ДЕЙСТВИЕ: Проверяет текущую заполненность рудного трюма корабля.
    /// </summary>
    public async Task<bool> CheckIsCargoFullAsync(CancellationToken token)
    {
        await Task.Delay(50, token);
        Logger.Log($"[ЗАГЛУШКА] {Settings.Name} проверяет заполненность трюма.", LogType.Test);

        // По умолчанию возвращаем false, чтобы бот не уходил в бесконечный цикл разгрузки на старте
        return false;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Unload Ore

    /// <summary>
    /// ДЕЙСТВИЕ: Переносит всю добытую руду из трюма корабля на склад станции.
    /// </summary>
    public async Task<bool> UnloadOreToHangarAsync(CancellationToken token)
    {
        await Task.Delay(500, token); // Выгрузка обычно занимает чуть больше времени
        Logger.Log($"[ЗАГЛУШКА] {Settings.Name} успешно разгрузил руду на склад станции.", LogType.Test);
        return true;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Undock 

    /// <summary>
    /// ДЕЙСТВИЕ: Производит отстыковку (андок) корабля от станции.
    /// </summary>
    public async Task<bool> UndockFromStationAsync(CancellationToken token)
    {
        await Task.Delay(200, token);
        Logger.Log($"[ЗАГЛУШКА] {Settings.Name} запускает процедуру андока.", LogType.Test);

        // Для теста переводим стейт корабля в космос
        _inSpace = true;
        return true;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Select Belt

    /// <summary>
    /// ДЕЙСТВИЕ: Сканирует овервью или меню игры, выбирает подходящий пояс астероидов.
    /// </summary>
    /// <returns>Возвращает объект (строку) с названием пояса, либо null, если ничего не найдено.</returns>
    public async Task<object?> ScanAndSelectAvailableBeltAsync(CancellationToken token)
    {
        await Task.Delay(150, token);
        const string mockBeltName = "Asteroid Belt Cluster-Alpha";
        Logger.Log($"[ЗАГЛУШКА] {Settings.Name} отсканировал локацию и выбрал: {mockBeltName}.", LogType.Test);
        return mockBeltName;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Warp To Belt

    /// <summary>
    /// ДЕЙСТВИЕ: Инициирует разгон и переход в варп на конкретно выбранный пояс астероидов.
    /// </summary>
    public async Task<bool> WarpToSpecificBeltAsync(object? targetBelt, CancellationToken token)
    {
        await Task.Delay(100, token);
        string beltName = targetBelt?.ToString() ?? "Unknown Belt";
        Logger.Log($"[ЗАГЛУШКА] {Settings.Name} отправлен в варп на точку: {beltName}.", LogType.Test);
        return true;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Try Target Asteroid

    /// <summary>
    /// ДЕЙСТВИЕ: Находит ближайший астероид в овервью космоса и берет его в захват (Lock Target).
    /// </summary>
    public async Task<bool> TryTargetAsteroidAsync(CancellationToken token)
    {
        await Task.Delay(100, token);
        Logger.Log($"[ЗАГЛУШКА] {Settings.Name} захватил астероид в цель.", LogType.Test);
        return true;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Activate Lasers

    /// <summary>
    /// ДЕЙСТВИЕ: Включает буровые/шахтерские лазеры (модули) корабля для начала добычи.
    /// </summary>
    public async Task<bool> ActivateLasersAsync(CancellationToken token)
    {
        await Task.Delay(150, token); // Имитация задержки на клик по модулю
        Logger.Log($"[ЗАГЛУШКА] {Settings.Name} отправил команду на активацию буровых лазеров.", LogType.Test);

        // Здесь в будущем будет выставляться флаг _weaponryactive = true
        return true;
    }

    #endregion

    // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Legacy FSM Methods (Deprecated)

    /// <summary>
    /// Устаревший метод получения плоских списков задач. Оставлен для временной обратной совместимости.
    /// </summary>
    [Obsolete("Используйте метод CreateTree для получения полноценного дерева поведения.")]
    public List<string> GetDefaultTasks(string scenarioName)
    {
        return scenarioName?.ToLower() switch
        {
            "localwatcher" => ["CheckSecurity"],
            _ => ["CheckYourOwnState"]
        };
    }

    #endregion

    /// <summary>
    /// Логика "Осмотрись": выполняет аппаратно-независимые клики для закрытия случайных поп-апов,
    /// окон наград или рекламы, мешающих обзору OCR.
    /// </summary>
    internal async Task ExecuteLookAroundDiagnosticsAsync(CancellationToken token)
    {
        Log($"[{Settings.Name}] Запуск макроса 'Осмотрись': попытка восстановить интерфейс.", LogType.Info);

        try
        {
            // 1. Нажимаем клавишу ESC через ADB, чтобы закрыть любые случайные окна
            // (Параметр KEYCODE_ESCAPE в Android равен 111, либо используйте вашу обертку Tools)
            // Tools.SendKeyEvent(111, Settings.AdbPort); 

            // 2. Делаем небольшую паузу, чтобы интерфейс успел отреагировать
            await Task.Delay(1500, token);

            // 3. Делаем клик по «пустому» безопасному месту экрана, где обычно нет кнопок,
            // чтобы сбросить фокус с возможных зависших элементов интерфейса
            // Tools.SmartClick(100, 100, minSec: 0, maxSec: 1, offset: 0, adbPort: Settings.AdbPort);

            await Task.Delay(1000, token);
        }
        catch (Exception ex)
        {
            Log($"[{Settings.Name}] Ошибка при выполнении диагностики экрана: {ex.Message}", LogType.Error);
        }
    }

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
    /// Задача определить что происходит
    /// </summary>
    LookAround
}

#endregion

