using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using static EVEEchoesBot.Program;
using static EVEEchoesBot.Tools;
using System.Diagnostics;
using System.Text.Json;
using System.Collections.Concurrent;
using static EVEEchoesBot.Logger;

// [v] TODO 2026.05.30 Привести все тексты логгера к единому стилю 

namespace EVEEchoesBot
{

    public class ActiveBotAccount
    {

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region BOT params

        public AccSettings Settings { get; }
        public IntPtr Hwnd { get; set; }

        public AccountTask CurrentTask { get; set; }

        private CancellationTokenSource? _accountCts;

        public ActiveBotAccount(AccSettings settings)
        {
            // 1. Присваиваем настройки
            Settings = settings;

            // 2. Формируем путь к файлу состояния для конкретного аккаунта
            _statsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"stats_{settings.Name}.json");

            // 3. Пытаемся загрузить сохраненную статистику и ОЧЕРЕДЬ из файла
            bool isLoaded = TryLoadLastStatsAndQueue();

            // 4. Если файла нет или загрузка не удалась, накатываем сценарий из поля Script
            if (!isLoaded || CurrentTask == AccountTask.CheckYourOwnState)
            {
                // Берем имя сценария напрямую из вашего конфига ("Script")
                // Если там вдруг пусто, по умолчанию напишем "mining"
                string currentScript = settings.Script ?? "mining";

                // Обращаемся к нашей фабрике и получаем список ["CheckSecurity"]
                List<string> defaultTasks = ScenarioFactory.GetDefaultTasks(currentScript);

                // Закидываем этот список в самый конец нашей пустой очереди
                // (Используем ваш метод EnqueueTasks, который мы писали в самом начале)
                this.EnqueueTasks(defaultTasks, addToFront: false);

                // Достаем самое первое действие для старта из только что наполненной очереди
                // (Метод DequeueNextTask заберет "CheckSecurity", переведет в Enum и запишет в CurrentTask)
                CurrentTask = DequeueNextTask();
            }
        }


        // Публичные свойства для чтения статистики (например, для вывода в UI)
        public long TriggerCount => Interlocked.Read(ref _triggerCount);
        public TimeSpan TotalRuntime => TimeSpan.FromSeconds(_accumulatedSeconds);

        internal string _eveSystem = "???";
        internal string _eveShip = "???";
        internal bool _inSpace = false;

        public string EVESystem
        {
            get { lock (_taskLock) return _eveSystem; }
            set { lock (_taskLock) _eveSystem = value; }
        }

        public string EVEShip
        {
            get { lock (_taskLock) return _eveShip; }
            set { lock (_taskLock) _eveShip = value; }
        }

        // Метод для инкремента из любой части логики этого бота
        public void IncrementTrigger() => Interlocked.Increment(ref _triggerCount);

        private long _triggerCount;
        private double _accumulatedSeconds;
        private readonly string _statsFilePath;
        private readonly System.Threading.Lock _taskLock = new();
        private List<string> _taskQueue = [];
        //private long _triggerCount = 0;
        // Кэшируем настройки сериализации для повторного использования во всех потоках
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        // Флаг для принудительного пропуска первого лога при старте
        private bool _isFirstSecurityCheck = true;

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

        private AccountTask DequeueNextTask()
        {
            // Используем ваш новый быстрый Lock из .NET 9 для потокобезопасности
            lock (_taskLock)
            {
                // Если задач в очереди вообще нет, возвращаем дефолтную проверку состояния
                if (_taskQueue.Count == 0)
                {
                    return AccountTask.CheckYourOwnState;
                }

                // 1. Берем самую первую текстовую задачу из начала списка
                string nextTaskStr = _taskQueue[0];

                // 2. Удаляем её из списка, так как мы её забрали в работу
                _taskQueue.RemoveAt(0);

                // 3. Сразу сохраняем измененную очередь на диск
                SaveStats();

                // 4. Переводим строку (например, "CheckSecurity") в ваш Enum (AccountTask.CheckSecurity)
                if (Enum.TryParse(nextTaskStr, out AccountTask parsedTask))
                {
                    return parsedTask;
                }

                // Если перевод не удался (опечатка в строке), возвращаем дефолт
                return AccountTask.CheckYourOwnState;
            }
        }

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region EnqueueTasks

        public void EnqueueTasks(IEnumerable<string> tasks, bool addToFront = false)
        {
            if (tasks == null) return;

            lock (_taskLock)
            {
                if (addToFront)
                {
                    _taskQueue.InsertRange(0, tasks); // В начало (сохраняя порядок переданных задач)
                }
                else
                {
                    _taskQueue.AddRange(tasks); // В конец
                }

                SaveStats(); // Сохраняем состояние под защитой lock
            }
        }

/*
// Добавляем одну конкретную задачу в конец очереди
EnqueueTasks(["WarpToStation"]);

// Добавляем одну конкретную задачу в начало очереди
EnqueueTasks(["WarpToStation"], true);

// Добавляем цепочку задач
var miningCycle = new List<string>
{
    "Undock",
    "WarpToBelt",
    "MineAsteroids",
    "WarpToStation",
    "Dock"
};
EnqueueTasks(miningCycle);
*/

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region AdvanceToNextTask

        // Главный метод логики: Берет следующую задачу из очереди
        public bool AdvanceToNextTask()
        {
            lock (_taskLock) // Используем переименованный объект синхронизации
            {
                if (_taskQueue.Count > 0)
                {
                    string nextTaskStr = _taskQueue[0]; // Берем первую строку из очереди
                    _taskQueue.RemoveAt(0);             // Сразу удаляем её оттуда

                    // Пытаемся превратить строку в ваш Enum AccountTask
                    if (Enum.TryParse(nextTaskStr, out AccountTask parsedTask))
                    {
                        CurrentTask = parsedTask;
                    }
                    else
                    {
                        // Если в очереди оказалась неизвестная строка, включаем безопасный режим
                        CurrentTask = AccountTask.CheckYourOwnState;
                        Logger.Log($"Неизвестная задача в очереди: '{nextTaskStr}'.", LogType.Warning);
                    }

                    SaveStats(); // Сохраняем обновленную очередь и текущую задачу на диск
                    return true;
                }

                // Если очередь пуста, переводим бота в режим проверки себя/ожидания
                CurrentTask = AccountTask.CheckYourOwnState;
                SaveStats();
                return false;
            }
        }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region TryLoadLastStatsAndQueue

        private bool TryLoadLastStatsAndQueue()
        {
            if (!File.Exists(_statsFilePath)) return false;

            try
            {
                string json = File.ReadAllText(_statsFilePath);
                var state = JsonSerializer.Deserialize<AccountStateDto>(json);

                if (state != null)
                {
                    _triggerCount = state.Triggers;
                    _accumulatedSeconds = (double)state.RuntimeSeconds;

                    lock (_taskLock)
                    {
                        // Восстанавливаем список задач
                        _taskQueue = state.TaskQueue?.ToList() ?? [];

                        // Конвертируем строку задачи в Enum
                        if (Enum.TryParse(state.CurrentTask, out AccountTask savedTask))
                        {
                            CurrentTask = savedTask;
                        }
                        else
                        {
                            CurrentTask = AccountTask.CheckYourOwnState;
                        }

                        // ИНТЕРАКТИВНЫЙ ОПРОС
                        lock (Console.In)
                        {
                            // Проверяем систему
                            if (string.IsNullOrEmpty(state.EVESystem) || state.EVESystem == "???")
                            {
                                Console.ResetColor();
                                string sys = "";
                                while (string.IsNullOrWhiteSpace(sys))
                                {
                                    Console.Write($"[{state.AccountName}] Введите текущую звездную систему (например, UB-UQZ): ");
                                    sys = Console.ReadLine()?.Trim() ?? "";
                                }
                                _eveSystem = sys;
                            }
                            else
                            {
                                _eveSystem = state.EVESystem;
                            }

                            // Проверяем корабль
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

                            // ========================================================
                            // ИСПРАВЛЕНИЕ: Умная проверка локации без ошибки компиляции
                            // ========================================================
                            if (state.InSpace.HasValue)
                            {
                                // Если значение успешно прочитано из JSON, берем его и НЕ открываем консоль
                                _inSpace = state.InSpace.Value;
                            }
                            else
                            {
                                // Консольный опрос сработает ТОЛЬКО один раз, если поля в JSON файле еще нет
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
                                    // Если ввели некорректные данные, безопасно приводим bool? к bool
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
                Console.WriteLine($"[Stats] Ошибка загрузки состояния: {ex.Message}");
            }

            return false;
        }




#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region SaveStats

        // Сохранение статистики на диск
        public void SaveStats()
        {
            try
            {
                AccountStateDto dto;

                lock (_taskLock)
                {
                    dto = new AccountStateDto
                    {
                        AccountName    = Settings.Name,
                        Triggers       = TriggerCount,

                        // ИСПРАВЛЕНИЕ: Явно приводим double к типу long
                        // Также убедитесь, что вы используете правильное поле времени 
                        // (например, (long)TotalRuntime.TotalSeconds или просто переменную _accumulatedSeconds)
                        RuntimeSeconds = _accumulatedSeconds,

                        CurrentTask    = CurrentTask.ToString(),
                        TaskQueue      = [.. _taskQueue],
                        EVESystem      = _eveSystem,
                        EVEShip        = _eveShip,
                        LastUpdate     = DateTime.UtcNow,
                        InSpace        = _inSpace
                    };
                }

                // Сериализация и запись файла выполняются за пределами lock,
                // не замораживая работу других потоков бота
                string json = JsonSerializer.Serialize(dto, _jsonOptions);
                File.WriteAllText(_statsFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.Log($"Не удалось сохранить статистику аккаунта '{Settings.Name}': {ex.Message}", LogType.Warning);
            }
        }


#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region Start


        public void Start(CancellationToken globalToken)
        {
            _accountCts = CancellationTokenSource.CreateLinkedTokenSource(globalToken);

            // Передаем токен созданной связки вторым параметром в Task.Run
            Task.Run(async () => await RunLoopAsync(_accountCts.Token), _accountCts.Token);
        }


#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region Stop

        public void Stop() => _accountCts?.Cancel();

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region RunLoopAsync

        private async Task RunLoopAsync(CancellationToken token)
        {
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Поток запущен. Начало работы по сценарию: '{Settings.Script ?? "mining"}'.", LogType.Info);

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
                        // ОБНОВЛЕНИЕ ВРЕМЕНИ: Прибавляем секунды текущей сессии к базовому времени из файла
                        _accumulatedSeconds = baseSeconds + (sessionStopwatch.ElapsedMilliseconds / 1000);

                        // ========================================================
                        // ЭТАП 1: ГЛОБАЛЬНЫЙ ДВУХЭТАПНЫЙ МОНИТОРИНГ БЕЗОПАСНОСТИ
                        // ========================================================
                        // Вызываем метод проверки. Он внутри себя обновит IsSaveLocal 
                        // и, если обнаружен враг, очистит очередь и добавит экстренные задачи.
                        await CheckSecurityStatusAsync(token);

                        // Блокировка "if (!isEverythingSafe)" удалена! 
                        // Поток больше не замерзает здесь во время опасности, позволяя выполнять задачи.

                        // ========================================================
                        // ЭТАП 2: УПРАВЛЕНИЕ БЕСКОНЕЧНОЙ ОЧЕРЕДЬЮ СЦЕНАРИЯ
                        // ========================================================
                        bool isQueueEmpty = false;
                        lock (_taskLock)
                        {
                            isQueueEmpty = _taskQueue.Count == 0;
                        }

                        // Перезапускаем рутинный сценарий ТОЛЬКО если бот находится в простое, 
                        // в очереди пусто И СИСТЕМА ДЕЙСТВИТЕЛЬНО БЕЗОПАСНА (IsSaveLocal is true).
                        if (CurrentTask == AccountTask.CheckYourOwnState && isQueueEmpty && IsSaveLocal is true)
                        {
                            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Сценарий '{Settings.Script ?? "mining"}' завершил цикл. Перезапуск.", LogType.Test);

                            string currentScript = Settings.Script ?? "mining";
                            List<string> defaultTasks = ScenarioFactory.GetDefaultTasks(currentScript);

                            this.EnqueueTasks(defaultTasks, addToFront: false);

                            // Сразу переходим на следующий виток, чтобы взять первую задачу из свежей очереди
                            continue;
                        }

                        // ЗАЩИТА ПРИ ОПАСНОСТИ: Если очередь пуста, но в системе враг (IsSaveLocal is false),
                        // значит бот уже выполнил эвакуацию и отправил чат-варнинг. Просто спим в безопасности.
                        if (isQueueEmpty && IsSaveLocal is false)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5), token);
                            continue;
                        }

                        // Достаем следующую экстренную или плановую задачу из очереди
                        CurrentTask = DequeueNextTask();

                        // ========================================================
                        // ЭТАП 3: ВЫПОЛНЕНИЕ ТЕКУЩЕЙ ЗАДАЧИ
                        // ========================================================
                        switch (CurrentTask)
                        {
                            case AccountTask.CheckSecurity:
                                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Плановый цикл мониторинга завершен.", LogType.Test);
                                break;

                            case AccountTask.SendAliChatWarning:
                                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Запуск макроса оповещения альянса.", LogType.Warning);
                                await RunAliChatWarningAsync(token);
                                break;

                            case AccountTask.GoToStation:
                                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Экстренная эвакуация: возвращаемся на станцию.", LogType.Warning);
                                // Как только бот успешно докнулся во время эвакуации, сбрасываем флаг:
                                _inSpace = false;
                                break;

                            case AccountTask.Undocking:
                                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Выход из дока станции.", LogType.Info);
                                // Как только бот прогрузился в космосе после андока, поднимаем флаг:
                                _inSpace = true;
                                break;

                            case AccountTask.Mining:
                                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Начало добычи руды.", LogType.Info);
                                break;

                            default:
                                // Логируем непредвиденные задачи, чтобы не терять управление
                                if (CurrentTask != AccountTask.CheckYourOwnState)
                                {
                                    Logger.Log($"[{Settings.Name}] Получена необработанная задача: {CurrentTask}", LogType.Warning);
                                }
                                break;
                        }

                        await Task.Delay(TimeSpan.FromSeconds(5), token);
                    }
                    catch (TaskCanceledException)
                    {
                        // Перехватываем отмену внутри цикла, чтобы управление перешло во внешний блок catch/finally
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Сбой в главном цикле обработки: {ex.Message}", LogType.Error);
                        await Task.Delay(5000, token);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Получен сигнал остановки. Фиксация состояния.", LogType.Info);
            }
            catch (Exception ex)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Критический сбой рабочего потока: {ex.Message}", LogType.Error);
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

                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Состояние сохранено на диск. Рабочий поток остановлен. Время работы в сессии (сек): {sessionSeconds}", LogType.Info);
            }
        }


#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

public void ForceSaveStats()
{
    lock (_taskLock)
    {
        SaveStats();
    }
}

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region CheckSecurityStatus 

        private async Task<bool> CheckSecurityStatusAsync(CancellationToken token)
        {
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Начало выполнения метода.", LogType.Test);

            if (Hwnd == IntPtr.Zero)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Окно целевой программы не найдено.", LogType.Error);
                return false;
            }

            string pathImg1 = Path.Combine(Program.TemplatesDir, "imgLocalChatHead.png");
            string pathImg2 = Path.Combine(Program.TemplatesDir, "imgLocalChatIcon.png");

            Rect localRegion1 = new(5, 5, 500, 750);
            Rect localRegion2 = new(5, 650, 100, 120);

            string debugDir = Path.GetFullPath(Path.Combine(Program.TemplatesDir, "..", "DebugScreenshots"));

            using Mat? screenshot = Tools.CaptureWindow(Hwnd);
            if (screenshot?.Empty() is not false || screenshot.Width <= 0 || screenshot.Height <= 0)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось выполнить повторный захват окна. Прерывание выполнения.", LogType.Error);
                return false;
            }

            Rect safeRegion1 = Tools.ClampRegion(localRegion1, screenshot.Width, screenshot.Height);
            Rect safeRegion2 = Tools.ClampRegion(localRegion2, screenshot.Width, screenshot.Height);

            if (safeRegion1.Width <= 0 || safeRegion1.Height <= 0 || safeRegion2.Width <= 0 || safeRegion2.Height <= 0)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Область поиска выходит за рамки окна.", LogType.Error);
                return false;
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
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось сохранить отладочный кадр: {ex.Message}", LogType.Warning);
                }
        #endif
                // Чат открыт — запускаем глубокую проверку пилотов
                return RunLocalCheck(screenshot, safeRegion1);
            }

            // ========================================================
            // ЭТАП 2: Чат свернут, ищем Иконку для разворачивания
            // ========================================================
            Point? foundImg2 = Tools.FindTemplateInRegion(screenshot, pathImg2, safeRegion2, 0.80);

            if (foundImg2.HasValue)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Локальный чат свернут. Обнаружена иконка развертывания.", LogType.Test);

        #if DEBUG
                try
                {
                    using Mat cropped = new(screenshot, safeRegion2);
                    Directory.CreateDirectory(debugDir);
                    Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatIcon_FOUND.png"), cropped);
                }
                catch (Exception ex) {
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось сохранить отладочный кадр: {ex.Message}", LogType.Warning);
                }
        #endif

                // КОРРЕКЦИЯ ШАПКИ: Вычитаем 31 пиксель из координаты Y,
                // чтобы компенсировать заголовок окна Windows при клике через ADB в эмулятор
                int adbX = foundImg2.Value.X;
                int adbY = foundImg2.Value.Y - 31;

                Tools.SmartClick(adbX, adbY, minSec: 1, maxSec: 2, offset: 2, adbPort: Settings.AdbPort);
                await Task.Delay(3500, token); // Честное ожидание проигрывания анимации интерфейса

                using Mat? freshScreenshot = Tools.CaptureWindow(Hwnd);
                if (freshScreenshot?.Empty() is not false) return false;

                Rect freshSafeRegion1 = Tools.ClampRegion(localRegion1, freshScreenshot.Width, freshScreenshot.Height);
                Point? retryImg1 = Tools.FindTemplateInRegion(freshScreenshot, pathImg1, freshSafeRegion1, 0.80);

                if (retryImg1.HasValue)
                {
                    // Успешно развернули — глубоко проверяем локал
                    return RunLocalCheck(freshScreenshot, freshSafeRegion1);
                }

                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Интерфейс чата не открылся. Повторная попытка клика.", LogType.Warning);
                return false;
            }

            // ========================================================
            // ЭТАП 3: ЖЕЛЕЗНАЯ ТИШИНА (Интерфейс не найден вообще)
            // ========================================================
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Шаблоны чата отсутствуют на экране. Смена сессии или загрузка.", LogType.Info);
            return false;
        }




#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region RunLocalCheck

        private bool RunLocalCheck(Mat screenshot, Rect searchRegion)
        {
            Rect safeSearchRegion = Tools.ClampRegion(searchRegion, screenshot.Width, screenshot.Height);

            string[] templates = ["imgLocalCriminal.png", "imgLocalMinus.png", "imgLocalNeutral.png"];
            int foundCount = 0;

            foreach (string templateName in templates)
            {
                string fullTemplatePath = Path.Combine(Program.TemplatesDir, templateName);
                if (!File.Exists(fullTemplatePath)) continue;

                // Порог 0.85-0.90 оптимален для иконок стендингов, чтобы отсечь фантомные пиксели чата
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
                        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось сохранить снимок экрана: {ex.Message}", LogType.Warning);
                    }
        #endif
                }
            }

            // ========================================================
            // ЖЕЛЕЗНАЯ ЛОГИКА МАРКЕРОВ СТЕНДИНГА:
            // ========================================================

            // 1. ИДЕАЛЬНАЯ БЕЗОПАСНОСТЬ: Найдена вся тройка маркеров (Criminal, Minus, Neutral на месте)
            if (foundCount == 3)
            {
                this.IsSaveLocal = true; // Сообщаем системе, что всё чисто
                return true;
            }

            // 2. ОПАСНОСТЬ: Хотя бы один маркер пропал (или пропали ВСЕ, так как интерфейс перекрыт списком врагов)
            // Раз мы зашли сюда, значит foundCount равен 0, 1 или 2. Система НЕ в безопасности!
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] ВНИМАНИЕ: Найдено маркеров безопасности: {foundCount} из 3. Фиксация угрозы!", LogType.Warning);
            
            this.IsSaveLocal = false; // Взводит тревогу для всей сетки окон аккаунтов!
            return false;
        }


#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region AliChatWarning

        private async Task RunAliChatWarningAsync(CancellationToken token)
        {
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Начало выполнения макроса оповещения альянса.", LogType.Test);

            if (Hwnd == IntPtr.Zero)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Целевое окно программы не найдено. Прерывание выполнения.", LogType.Error);
                return;
            }

            string pathAli = Path.Combine(Program.TemplatesDir, "imgAliChatENG.png");
            string pathCorp = Path.Combine(Program.TemplatesDir, "imgCorpChatENG.png");

            if (!File.Exists(pathAli) || !File.Exists(pathCorp))
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Файлы шаблонов чата отсутствуют на диске. Прерывание выполнения.", LogType.Error);
                return;
            }

            Rect searchRegion = new(5, 220, 300, 500);
            Point? foundChat = null;
            bool isCorpChat = false;
            Mat? screenshot = null;

            // ========================================================
            // ЭТАП 1: ЦИКЛ ОТКРЫТИЯ ИНТЕРФЕЙСА ЧАТА (ДО 2-Х ПОПЫТОК)
            // ========================================================
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                this.ClickTo(GameUi.ChatsInterface);
                await Task.Delay(attempt == 1 ? 3500 : 4000, token); // На второй попытке даем чуть больше времени

                screenshot?.Dispose();
                screenshot = Tools.CaptureWindow(Hwnd);

                if (screenshot?.Empty() is not false || screenshot.Width <= 0 || screenshot.Height <= 0)
                {
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось выполнить повторный захват окна. Прерывание выполнения.", LogType.Error);
                    screenshot?.Dispose();
                    return;
                }

                Rect safeRegion = Tools.ClampRegion(searchRegion, screenshot.Width, screenshot.Height);
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Поиск маркеров языка интерфейса чата (Попытка {attempt}).", LogType.Info);

                // Сначала ищем приоритетный чат альянса
                foundChat = Tools.FindTemplateInRegion(screenshot, pathAli, safeRegion, 0.85);
                isCorpChat = false;

                // Если чат альянса не найден — ищем резервный чат корпорации
                if (!foundChat.HasValue)
                {
                    foundChat = Tools.FindTemplateInRegion(screenshot, pathCorp, safeRegion, 0.85);
                    isCorpChat = true;
                }

                // Если хоть какой-то чат успешно обнаружен — прерываем цикл попыток
                if (foundChat.HasValue) break;

                if (attempt == 1)
                {
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Интерфейс чата не открылся. Повторная попытка клика.", LogType.Warning);
                }
            }

            // Если после двух попыток маркеры так и не появились
            if (!foundChat.HasValue)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Шаблоны чата не обнаружены после повторного клика. Проверьте координаты 'ChatsInterface' в 'GameUi'.", LogType.Error);
        #if DEBUG
                try
                {
                    Rect safeRegion = Tools.ClampRegion(searchRegion, screenshot!.Width, screenshot.Height);
                    using Mat cropped = new(screenshot, safeRegion);
                    string debugDir = Path.GetFullPath(Path.Combine(Program.TemplatesDir, "..", "DebugScreenshots"));
                    Directory.CreateDirectory(debugDir);
                    Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgAliChat_NOT_FOUND.png"), cropped);
                }
                catch (Exception ex) {
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось сохранить отладочный кадр: {ex.Message}", LogType.Warning);
                }
        #endif
                screenshot?.Dispose();
                return;
            }

            // ========================================================
            // ЭТАП 2: КЛИК ПО НАЙДЕННОМУ ЧАТУ
            // ========================================================
            try
            {
                string chatTypeStr = isCorpChat ? "корпорации" : "альянса";
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Обнаружен интерфейс {chatTypeStr} чата в точке (X={foundChat.Value.X}, Y={foundChat.Value.Y}).", LogType.Info);

                // ЖЕСТКАЯ КОРРЕКЦИЯ ДЛЯ ЭМУЛЯТОРА: Вычитаем 25 пикселей из координаты Y,
                // чтобы компенсировать рамку заголовка окна при отправке клика через ADB
                int adbX = foundChat.Value.X;
                int adbY = foundChat.Value.Y - 25;

                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Отправка фонового клика по скорректированным координатам (X={adbX}, Y={adbY}).", LogType.Info);
                
                // Передаем скорректированные adbX и adbY в ваш оригинальный SmartClick
                Tools.SmartClick(adbX, adbY, minSec: 1, maxSec: 3, offset: 3, adbPort: Settings.AdbPort);
            }
            catch (Exception ex)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Критический сбой анализа экрана: {ex.Message}", LogType.Error);
                screenshot?.Dispose();
                return;
            }
            finally
            {
                screenshot?.Dispose(); // Освобождаем память
            }

            await Task.Delay(2000, token);

            // ========================================================
            // ЭТАП 3: ОПТИМИЗИРОВАННАЯ ЦЕПОЧКА ОТПРАВКИ МАКРОСА
            // ========================================================
            // Массив шагов и индивидуальных пауз после каждого из них
            var macroSteps = new (GameUi Element, int DelayMs)[]
            {
                (GameUi.ChatInputMenu, 1200), // Открываем меню ввода чата
                (GameUi.ChatFastInput, 1200), // Открываем меню быстрых сообщений
                (GameUi.ChatInform,    1200), // Открываем вкладку данных разведки
                (GameUi.ChatMessScout, 1200), // Выбираем статус-сообщение "Scout"
                (GameUi.WindowCenter,  1500), // Закрываем область ввода
                (GameUi.ChatButtSend,  2000), // Нажимаем кнопку "Отправить"
                (GameUi.WindowCenter,  0)     // Закрываем общий интерфейс чатов
            };

            // Деконструкция кортежа прямо в объявлении цикла foreach
            foreach (var (element, delayMs) in macroSteps)
            {
                this.ClickTo(element);
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, token);
                }
            }

            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Выполнение цепочки кликов оповещения альянса завершено.", LogType.Success);
        }


#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region _isSaveLocal

        public bool? IsSaveLocal
        {
            get => SystemSafetyManager.GetSystemState(EVESystem).IsSafe;
            set
            {
                if (string.IsNullOrEmpty(EVESystem) || EVESystem == "Неизвестно" || value == null) return;

                var systemState = SystemSafetyManager.GetSystemState(EVESystem);
                bool? currentStatus = systemState.IsSafe;

                // ========================================================
                // ИСПРАВЛЕННАЯ ЗАЩИТА СТАРТА: 
                // ========================================================
                if (_isFirstSecurityCheck)
                {
                    _isFirstSecurityCheck = false; // Сбрасываем флаг первой проверки

                    if (value is true)
                    {
                        // Если при старте всё чисто — просто фиксируем и молча выходим
                        systemState.SetSafe();
                        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Стартовая инициализация: система безопасна. Мониторинг запущен.", LogType.Info);
                        return; 
                    }
                    
                    // Если же при старте СРАЗУ обнаружена опасность (value is false),
                    // мы НЕ делаем return! Мы разрешаем коду пройти ниже, чтобы 
                    // бот сразу же отработал экстренный сценарий и отправил чат-варнинг!
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Стартовая проверка: система СРАЗУ ОПАСНА! Запуск экстренных процедур.", LogType.Warning);
                }
                else
                {
                    // Для всех последующих проверок: если статус в памяти совпадает с новым — игнорируем
                    if (currentStatus == value) return;
                }

                // ========================================================
                // РЕАКЦИЯ НА РЕАЛЬНОЕ ИЗМЕНЕНИЕ СТАТУСА (Или на опасность при старте)
                // ========================================================
                if (value is false)
                {
                    bool shouldSendAllianceAlert = systemState.SetDanger();
                    _triggerCount++;

                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] ВНИМАНИЕ! Фиксация угрозы в системе.", LogType.Warning);

                    if (shouldSendAllianceAlert)
                    {
                        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Отправка оповещения: в системе обнаружен противник.", LogType.Error);
                    }

                    // Рассылаем сигналы остальным окнам
                    var botsInSystem = Program._activeBots.ToList();
                    foreach (var bot in botsInSystem)
                    {
                        if (bot == this) continue;

                        if (bot.EVESystem == this.EVESystem)
                        {
                            bot.ClearTasks();
                            bot.ExecuteEmergencyResponse(isInitiator: false);
                        }
                    }

                    // Назначаем панику себе
                    this.ClearTasks();
                    this.ExecuteEmergencyResponse(isInitiator: true);
                }
                else if (value is true)
                {
                    systemState.SetSafe();
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Статус системы изменился на БЕЗОПАСНО. Враги покинули систему.", LogType.Info);
                }
            }
        }



        public void ExecuteEmergencyResponse(bool isInitiator)
        {
            List<string> emergencyTasks = [];

            // 1. Если корабль в космосе, наполняем список согласно его сценарию
            if (_inSpace)
            {
                switch (Settings.Script?.ToLower())
                {
                    case "localwatcher":
                        // Наблюдателю отварп не нужен, он остается в космосе (например, в клоке)
                        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] [Сценарий: localwatcher] Корабль остается на позиции наблюдения.", LogType.Info);
                        break;

                    // Сюда в будущем добавятся новые сценарии (mining, combat и т.д.)

                    default:
                        // Поведение по умолчанию для нереализованных скриптов — пока ничего не делаем
                        break;
                }
            }
            else
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Корабль в безопасности (станция/цитадель). Эвакуация не требуется.", LogType.Info);
            }

            // 2. Строго ПОСЛЕ задач эвакуации добавляем оповещение альянса (если это инициатор)
            if (isInitiator)
            {
                emergencyTasks.Add("SendAliChatWarning");
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Этот аккаунт обнаружил угрозу. Задача оповещения добавлена в очередь.", LogType.Warning);
            }

            // 3. Отправляем собранные задачи в начало пустой очереди
            if (emergencyTasks.Count > 0)
            {
                this.EnqueueTasks(emergencyTasks, addToFront: true);
            }
        }


        public void ClearTasks()
        {
            lock (_taskLock)
            {
                _taskQueue.Clear();
                // Сбрасываем текущую задачу в состояние покоя, 
                // чтобы главный цикл RunLoopAsync понял, что нужно переключиться
                CurrentTask = AccountTask.CheckYourOwnState; 
            }
            Logger.Log($"[{Settings.Name}] Очередь задач экстренно очищена.", LogType.Info);
        }

    }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +


public static class SystemSafetyManager
{
    // Хранит статус безопасности для каждой системы EVE Online
    private static readonly ConcurrentDictionary<string, SystemSafetyState> _systems = new();

    public static SystemSafetyState GetSystemState(string eveSystem)
    {
        if (string.IsNullOrEmpty(eveSystem)) eveSystem = "Неизвестно";
        return _systems.GetOrAdd(eveSystem, _ => new SystemSafetyState());
    }
}

        public class SystemSafetyState
        {
            // Используем новый строго типизированный объект блокировки из C# 13
            private readonly Lock _lock = new();
            private bool? _isSafe = true;
            private bool _allianceAlertSent = false;

            public bool? IsSafe
            {
                get { lock (_lock) return _isSafe; }
            }

            public bool SetDanger()
            {
                lock (_lock) // Синтаксис lock остается прежним, но под капотом работает эффективнее
                {
                    _isSafe = false;
                    if (!_allianceAlertSent)
                    {
                        _allianceAlertSent = true;
                        return true;
                    }
                    return false;
                }
            }

            public void SetSafe()
            {
                lock (_lock)
                {
                    _isSafe = true;
                    _allianceAlertSent = false;
                }
            }

        }


// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region AccountTask

    // Перечисление для задач (что делать боту дальше) 
    // [v] Продумать список возможных действий
    // [v] Добавить действие "Осмотреться"
    public enum AccountTask
    {
        Undocking,         // Выйти из дока
        GoToBelt,          // Отправиться в зону добычи
        GoToMoon,          // Отправиться копать луну
        GoToCondensed,     // Отправиться копать сжатку
        Mining,            // Добывать руду
        GoToStation,       // Вернуться на станцию
        Unloading,         // Выгрузить руду на станцию
        CheckSecurity,     // Проверить статус безопастности
        CheckYourOwnState, // Проверить текущее состояние
        SendAliChatWarning
    }

#endregion

}