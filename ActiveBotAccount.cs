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

        private string _eveSystem = "Неизвестно";
        private string _eveShip = "Неизвестно";

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

                    lock (_taskLock) // Теперь отступ правильный и логичный
                    {
                        // Восстанавливаем список задач из DTO обратно в чистый List<string>
                        _taskQueue = state.TaskQueue?.ToList() ?? [];

                        // Восстанавливаем систему и корабль. Если в JSON пусто, пишем "Неизвестно"
                        _eveSystem = !string.IsNullOrEmpty(state.EVESystem) ? state.EVESystem : "Неизвестно";
                        _eveShip = !string.IsNullOrEmpty(state.EVEShip) ? state.EVEShip : "Неизвестно";

                        // Конвертируем строку задачи обратно в ваш Enum
                        if (Enum.TryParse(state.CurrentTask, out AccountTask savedTask))
                        {
                            CurrentTask = savedTask;
                        }
                        else
                        {
                            CurrentTask = AccountTask.CheckYourOwnState;
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
                        LastUpdate     = DateTime.UtcNow
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
                        bool isEverythingSafe = await CheckSecurityStatusAsync(token);

                        if (!isEverythingSafe)
                        {
                            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Система небезопасна или интерфейс разрушен. Ожидание эвакуации.", LogType.Warning);
                        }

                        // ========================================================
                        // ЭТАП 2: УПРАВЛЕНИЕ БЕСКОНЕЧНОЙ ОЧЕРЕДЬЮ СЦЕНАРИЯ
                        // ========================================================
                        bool isQueueEmpty = false;
                        lock (_taskLock)
                        {
                            isQueueEmpty = _taskQueue.Count == 0;
                        }

                        // Перезапускаем сценарий ТОЛЬКО если бот находится в простое И в очереди пусто
                        if (CurrentTask == AccountTask.CheckYourOwnState && isQueueEmpty)
                        {
                            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Сценарий '{Settings.Script ?? "mining"}' завершил цикл. Перезапуск.", LogType.Test);

                            string currentScript = Settings.Script ?? "mining";
                            List<string> defaultTasks = ScenarioFactory.GetDefaultTasks(currentScript);

                            this.EnqueueTasks(defaultTasks, addToFront: false);

                            // Сразу переходим на следующий виток, чтобы взять первую задачу из свежей очереди
                            continue;
                        }

                        // Достаем следующую задачу из очереди
                        CurrentTask = DequeueNextTask();

                        // ========================================================
                        // ЭТАП 3: ВЫПОЛНЕНИЕ ТЕКУЩЕЙ ЗАДАЧИ
                        // ========================================================
                        switch (CurrentTask)
                        {
                            case AccountTask.CheckSecurity:
                                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Плановый цикл мониторинга завершен.", LogType.Info);
                                break;

                            case AccountTask.SendAliChatWarning:
                                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Запуск макроса оповещения альянса.", LogType.Error);
                                await RunAliChatWarningAsync(token);
                                break;

                            case AccountTask.GoToStation:
                                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Экстренная эвакуация: возвращаемся на станцию.", LogType.Warning);
                                break;

                            case AccountTask.Undocking:
                                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Выход из дока станции.", LogType.Info);
                                break;

                            case AccountTask.Mining:
                                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Начало добычи руды.", LogType.Info);
                                break;

                            default:
                                break;
                        }

                        await Task.Delay(TimeSpan.FromSeconds(5), token);
                    }
                    catch (TaskCanceledException)
                    {
                        // Перехватываем отмену внутри цикла, чтобы управление перешло в внешний блок catch/finally
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
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Состояние сохранено на диск. Рабочий поток остановлен. Время работы (сек): {_accumulatedSeconds}", LogType.Info);
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

            Rect localRegion1 = new(5, 5, 500, 720);
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
            Rect safeSearchRegion = ClampRegion(searchRegion, screenshot.Width, screenshot.Height);

            string[] templates = ["imgLocalCriminal.png", "imgLocalMinus.png", "imgLocalNeutral.png"];
            int foundCount = 0;

            foreach (string templateName in templates)
            {
                string fullTemplatePath = Path.Combine(Program.TemplatesDir, templateName);
                if (!File.Exists(fullTemplatePath)) continue;

                Point? foundPoint = Tools.FindTemplateInRegion(screenshot, fullTemplatePath, safeSearchRegion, 0.80);

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
                else
                {
                    // ОПТИМИЗАЦИЯ: Если хотя бы одна обязательная картинка интерфейса пропала,
                    // мы уже гарантированно не наберем 3 балла. Прерываем поиск ради экономии CPU.
                    if (foundCount > 0) break;
                }
            }

            // 1. ИДЕАЛЬНАЯ БЕЗОПАСНОСТЬ: Найдена вся тройка маркеров интерфейса
            if (foundCount == 3)
            {
                this.IsSaveLocal = true; // Синхронно сообщает системе, что всё чисто
                return true;
            }

            // 2. ОПАСНОСТЬ: Какая-то часть интерфейса пропала (например, один из маркеров скрылся из-за появления "минуса")
            if (foundCount > 0 && foundCount < 3)
            {
                this.IsSaveLocal = false; // ВЗВОДИТ ТРЕВОГУ для всех окон в системе через наш Program._activeBots!
                return false;
            }

            // 3. СБОЙ ИНТЕРФЕЙСА: foundCount == 0 (Чат вообще закрыт, свернут или перекрыт другим окном)
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Интерфейс локального чата не найден. Отсутствуют маркеры безопасности.", LogType.Warning);

            // КРИТИЧЕСКИ ВАЖНО: При закрытом чате мы тоже обязаны выставить false (опасность) для текущего бота,
            // чтобы он не вздумал продолжать копку/хакинг вслепую, пока не откроет чат обратно.
            this.IsSaveLocal = false;
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

                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Отправка фонового клика по координатам (X={foundChat.Value.X}, Y={foundChat.Value.Y}).", LogType.Info);
                Tools.SmartClick(foundChat.Value.X, foundChat.Value.Y, minSec: 1, maxSec: 3, offset: 3, adbPort: Settings.AdbPort);
            }
            catch (Exception ex)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Критический сбой анализа экрана: {ex.Message}", LogType.Error);
                screenshot?.Dispose();
                return;
            }
            finally
            {
                screenshot?.Dispose(); // Железно чистим оперативную память от картинки
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

            foreach (var step in macroSteps)
            {
                this.ClickTo(step.Element);
                if (step.DelayMs > 0)
                {
                    await Task.Delay(step.DelayMs, token);
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
                if (string.IsNullOrEmpty(EVESystem) || EVESystem == "Неизвестно") return;

                var systemState = SystemSafetyManager.GetSystemState(EVESystem);
                bool? currentStatus = systemState.IsSafe;

                if (currentStatus == value) return;

                if (value is false)
                {
                    bool shouldSendAllianceAlert = systemState.SetDanger();
                    _triggerCount++;
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Обнаружена опасность. В системе присутствуют посторонние пилоты.", LogType.Warning);

                    if (shouldSendAllianceAlert)
                    {
                        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Отправка оповещения: в системе обнаружен противник.", LogType.Error);
                    }

                    var botsInSystem = Program._activeBots.ToList();

                    // Раздаем команду эвакуации ВСЕМ ОСТАЛЬНЫМ ботам в этой же системе
                    foreach (var bot in botsInSystem)
                    {
                        // КРИТИЧЕСКИ ВАЖНО: Себе задачу через цикл не добавляем, чтобы не было зацикливания
                        if (bot == this) continue;

                        if (bot.EVESystem == this.EVESystem)
                        {
                            bot.EnqueueTasks(["WarpToStation", "Dock"], addToFront: true);
                            Logger.Log($"[{bot.Settings.Name}|{bot.EVESystem}|{bot.EVEShip}] Получен экстренный сигнал тревоги. Запуск отварпа.", LogType.Warning);
                        }
                    }

                    // Себе добавляем задачу отдельно и один раз
                    this.EnqueueTasks(["WarpToStation", "Dock"], addToFront: true);
                }
                else if (value is true)
                {
                    systemState.SetSafe();
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Проверка завершена. В системе отсутствуют посторонние пилоты.", LogType.Success);
                }
            }
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