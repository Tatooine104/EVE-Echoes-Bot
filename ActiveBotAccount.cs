using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using static EVEEchoesBot.Program;
using static EVEEchoesBot.Tools;
using System.Diagnostics;
using System.Text.Json;

namespace EVEEchoesBot
{

    public class ActiveBotAccount
    {

#region BOT params

        public AccSettings Settings { get; }
        public IntPtr Hwnd { get; set; }

        public AccountTask CurrentTask { get; set; }

        private CancellationTokenSource? _accountCts;

        public ActiveBotAccount(AccSettings settings)
        {
            // 1. Присваиваем настройки (убедитесь, что имя свойства совпадает: Settings или AccSettings)
            Settings = settings;

            // 2. Формируем путь к файлу состояния для конкретного аккаунта
            _statsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"stats_{settings.Name}.json");

            // 3. Пытаемся загрузить сохраненную статистику и ОЧЕРЕДЬ из файла
            bool isLoaded = TryLoadLastStatsAndQueue();

            // 4. Если файла нет или загрузка не удалась, используем вашу дефолтную логику первого запуска
            if (!isLoaded || CurrentTask == AccountTask.CheckYourOwnState) // или проверка на "Нет задачи"
            {
                string firstTaskStr = settings.FirstTask ?? "Undocking";
                CurrentTask = firstTaskStr switch
                {
                    "Undocking"          => AccountTask.Undocking,
                    "GoToBelt"           => AccountTask.GoToBelt,
                    "Mining"             => AccountTask.Mining,
                    "GoToStation"        => AccountTask.GoToStation,
                    "Unloading"          => AccountTask.Unloading,
                    "CheckSecurity"      => AccountTask.CheckSecurity,
                    "CheckYourOwnState"  => AccountTask.CheckYourOwnState,
                    "SendAliChatWarning" => AccountTask.SendAliChatWarning,
                    _                    => AccountTask.CheckYourOwnState
                };
            }
        }

        // Публичные свойства для чтения статистики (например, для вывода в UI)
        public long TriggerCount => Interlocked.Read(ref _triggerCount);
        public TimeSpan TotalRuntime => TimeSpan.FromSeconds(_accumulatedSeconds) + _runtimeStopwatch.Elapsed;

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
        private readonly Stopwatch _runtimeStopwatch = new();
        private readonly string _statsFilePath;
        private readonly System.Threading.Lock _taskLock = new();
        private List<string> _taskQueue = [];
        // Кэшируем настройки сериализации для повторного использования во всех потоках
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };


#endregion

        public void EnqueueTasks(IEnumerable<string> tasks)
        {
            lock (_taskLock) // Ипользуем новое имя
            {
                _taskQueue.AddRange(tasks);
            }
            SaveStats();
        }

        // Главный метод логики: Берет следующую задачу из очереди
        public bool AdvanceToNextTask()
        {
            lock (_taskLock) // Используем переименованный объект синхронизации
            {
                if (_taskQueue.Count > 0)
                {
                    string nextTaskStr = _taskQueue[0]; // Берем первую строку из очереди
                    _taskQueue.RemoveAt(0);            // Сразу удаляем её оттуда

                    // Пытаемся превратить строку в ваш Enum AccountTask
                    if (Enum.TryParse(nextTaskStr, out AccountTask parsedTask))
                    {
                        CurrentTask = parsedTask;
                    }
                    else
                    {
                        // Если в очереди оказалась неизвестная строка, включаем безопасный режим
                        CurrentTask = AccountTask.CheckYourOwnState;
                        // Можно добавить лог: Logger.Log($"Неизвестная задача в очереди: {nextTaskStr}", LogType.Warning);
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
                    _accumulatedSeconds = state.RuntimeSeconds;

                    lock (_taskLock) // Защищаем запись в общие переменные потока
                    {
                        // Восстанавливаем список задач из DTO
                        _taskQueue = state.TaskQueue ?? [];

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
                        RuntimeSeconds = TotalRuntime.TotalSeconds,
                        CurrentTask    = CurrentTask.ToString(),
                        TaskQueue      = [.. _taskQueue],
                        EVESystem      = _eveSystem,
                        EVEShip        = _eveShip,
                        LastUpdate     = DateTime.UtcNow
                    };
                }

                // ИСПРАВЛЕНИЕ: Передаем именно _jsonOptions вторым аргументом
                string json = JsonSerializer.Serialize(dto, _jsonOptions);
                File.WriteAllText(_statsFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.Log($"Ошибка сохранения статистики {Settings.Name}: {ex.Message}", LogType.Warning);
            }
        }




#region Start


        public void Start(CancellationToken globalToken)
        {
            _accountCts = CancellationTokenSource.CreateLinkedTokenSource(globalToken);

            // Передаем токен созданной связки вторым параметром в Task.Run
            Task.Run(() => RunLoopAsync(_accountCts.Token), _accountCts.Token);
        }

#endregion

#region Stop

        public void Stop() => _accountCts?.Cancel();

#endregion

#region RunLoopAsync

        private async Task RunLoopAsync(CancellationToken token)
        {
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Поток запущен. Начинаю непрерывный мониторинг безопасности...", LogType.Info);

            // Принудительно стартуем с проверки безопасности
            CurrentTask = AccountTask.CheckSecurity;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    switch (CurrentTask)
                    {
                        case AccountTask.CheckSecurity:
                            // Проверяем безопасность локал-чата
                            bool isSystemPerfectlySafe = await CheckSecurityStatusAsync(token);

                            if (isSystemPerfectlySafe)
                            {
                                // Сработает ТОЛЬКО если найдены все 3 маркера (в системе только свои)
                                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Локал-чат проверен. Всё спокойно.", LogType.Success);
                            }
                            else
                            {
                                // Сработает, если хотя бы один маркер пропал (найден минус или нейтрал)
                                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] ОБНАРУЖЕНА УГРОЗА! Приостанавливаю мониторинг. Перехожу к оповещению альянса.", LogType.Error);

                                // Инициализируем действие: переключаем стейт-машину на задачу отправки варнинга
                                // (Используем стейт CheckYourOwnState как триггер макроса оповещения)
                                CurrentTask = AccountTask.CheckYourOwnState;
                            }
                            break;

                        case AccountTask.SendAliChatWarning:
                            // ВЫПОЛНЯЕМ ЦЕПОЧКУ КЛИКОВ ОПОВЕЩЕНИЯ В АЛЬЯНС-ЧАТ
                            // Ключевое слово 'await' заставит этот поток ЖДАТЬ (около 15 секунд), 
                            // пока бот полностью прокликает все меню. Проверка локала в этот момент полностью остановлена!
                            await RunAliChatWarningAsync(token);

                            // После того, как варнинг успешно отправлен, возвращаем бота обратно на мониторинг локала
                            CurrentTask = AccountTask.CheckSecurity;
                            break;

                        default:
                            CurrentTask = AccountTask.CheckSecurity;
                            break;
                    }

                    // Частота опроса стейт-машины (каждые 5 секунд бот заходит в свой текущий case)
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Ошибка цикла мониторинга: {ex.Message}", LogType.Error);
                    await Task.Delay(5000, token);
                }
            }

            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Поток мониторинга остановлен.", LogType.Info);
        }

#endregion

#region CheckSecurityStatus 


        private async Task<bool> CheckSecurityStatusAsync(CancellationToken token)
        {
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Запуск метода.", LogType.Test);

            if (Hwnd == IntPtr.Zero)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Окно целевой программы не найдено.", LogType.Error);
                return false;
            }

            // Использование путей к шаблонам (убедитесь, что Program.TemplatesDir доступен)
            string pathImg1 = Path.Combine(Program.TemplatesDir, "imgLocalChatHead.png");
            string pathImg2 = Path.Combine(Program.TemplatesDir, "imgLocalChatIcon.png");

            // Области поиска
            Rect localRegion1 = new(5, 5, 500, 720);
            Rect localRegion2 = new(5, 650, 100, 120);

            string debugDir = Path.GetFullPath(Path.Combine(Program.TemplatesDir, "..", "DebugScreenshots"));

            using Mat? screenshot = Tools.CaptureWindow(Hwnd);
            if (screenshot?.Empty() is not false || screenshot.Width <= 0 || screenshot.Height <= 0)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось сделать скриншот окна.", LogType.Error);
                return false;
            }

            Rect safeRegion1 = Tools.ClampRegion(localRegion1, screenshot.Width, screenshot.Height);
            Rect safeRegion2 = Tools.ClampRegion(localRegion2, screenshot.Width, screenshot.Height);

            if (safeRegion1.Width <= 0 || safeRegion1.Height <= 0 || safeRegion2.Width <= 0 || safeRegion2.Height <= 0)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Заданные Rect выходят за рамки окна! Размер скриншота: {screenshot.Width}x{screenshot.Height}", LogType.Error);
                return false;
            }

            // ==========================================
            // ЭТАП 1: Ищем Шапку чата (Развернун ли чат?)
            // ==========================================
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
                catch (Exception ex) { Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
        #endif

                // ИСПРАВЛЕНО: Прямо возвращаем результат глубокой проверки (true если все 3 маркера на месте, false если меньше)
                return RunLocalCheck(screenshot, safeRegion1);
            }

        #if DEBUG
            try
            {
                using Mat cropped = new(screenshot, safeRegion1);
                Directory.CreateDirectory(debugDir);
                Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatHead_NOT_FOUND.png"), cropped);
            }
            catch (Exception ex) { Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
        #endif

            // ==========================================
            // ЭТАП 2: Чат свернут, ищем Иконку для разворачивания
            // ==========================================
            Point? foundImg2 = Tools.FindTemplateInRegion(screenshot, pathImg2, safeRegion2, 0.80);

            if (foundImg2.HasValue)
            {
                Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Чат свернут. Найдена иконка {Path.GetFileName(pathImg2)}. Пробую развернуть...", LogType.Test);

        #if DEBUG
                try
                {
                    using Mat cropped = new(screenshot, safeRegion2);
                    Directory.CreateDirectory(debugDir);
                    Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatIcon_FOUND.png"), cropped);
                }
                catch (Exception ex) { Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
        #endif

                // Кликаем по иконке через ADB, передавая порт этого бота
                Tools.SmartClick(foundImg2.Value.X, foundImg2.Value.Y, minSec: 0, maxSec: 0, offset: 2, adbPort: Settings.AdbPort);

                // Даем игре время проиграть анимацию развертывания чата
                await Task.Delay(3000, token);

                using Mat? freshScreenshot = Tools.CaptureWindow(Hwnd);
                if (freshScreenshot?.Empty() is not false) return false;

                Rect freshSafeRegion1 = Tools.ClampRegion(localRegion1, freshScreenshot.Width, freshScreenshot.Height);
                Point? retryImg1 = Tools.FindTemplateInRegion(freshScreenshot, pathImg1, freshSafeRegion1, 0.80);

                if (retryImg1.HasValue)
                {
                    // Чат успешно открылся, возвращаем РЕАЛЬНЫЙ результат проверки локала
                    return RunLocalCheck(freshScreenshot, freshSafeRegion1);
                }
                else
                {
        #if DEBUG
                    try
                    {
                        using Mat cropped = new(freshScreenshot, freshSafeRegion1);
                        Directory.CreateDirectory(debugDir);
                        Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatHead_AFTER_CLICK_NOT_FOUND.png"), cropped);
                    }
                    catch (Exception ex) { Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
        #endif
                }

                // Если кликнули, но чат так и не открылся — система не безопасна
                return false;
            }

        #if DEBUG
            try
            {
                using Mat cropped = new(screenshot, safeRegion2);
                Directory.CreateDirectory(debugDir);
                Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatIcon_NOT_FOUND.png"), cropped);
            }
            catch (Exception ex) { Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
        #endif

            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Шаблоны чата не найдены. Безопасность под угрозой!", LogType.Error);
            return false;
        }


#endregion

#region RunLocalCheck

        private bool RunLocalCheck(Mat screenshot, Rect searchRegion)
        {
            Rect safeSearchRegion = ClampRegion(searchRegion, screenshot.Width, screenshot.Height);

            // Список трех обязательных маркеров, которые должны быть ВСЕГДА для статуса "Безопасно"
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
                        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Ошибка сохранения скриншота: {ex.Message}", LogType.Warning);
                    }
        #endif
                }
            }

            // 1. ИДЕАЛЬНАЯ БЕЗОПАСНОСТЬ: Найдены строго все 3 маркера
            if (foundCount == 3)
            {
                this.IsSaveLocal = true;
                return true; // БЕЗОПАСНО, в системе только свои
            }

            // 2. ОПАСНОСТЬ: Часть маркеров пропала (хотя бы один отсутствует, но интерфейс частично виден)
            if (foundCount > 0 && foundCount < 3)
            {
                this.IsSaveLocal = false; // Включает тревогу и ставит статус ОПАСНОСТЬ
                return false; // НЕ БЕЗОПАСНО, уходим в панику
            }

            // 3. СБОЙ ИНТЕРФЕЙСА: foundCount == 0 (Ни один маркер не найден, чат перекрылся или закрылся)
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Маркеры безопасности полностью пропали с экрана. Интерфейс не готов!", LogType.Warning);

            // При полном пропадании интерфейса мы тоже обязаны вернуть false, 
            // чтобы бот не думал, что всё безопасно, а повторил попытку анализа или развернул чат заново.
            return false;
        }

#endregion

#region AliChatWarning

private async Task RunAliChatWarningAsync(CancellationToken token)
{
    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Запуск метода AliChatWarning.", LogType.Test);

    if (Hwnd == IntPtr.Zero)
    {
        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Целевое окно программы не найдено. Прерывание цепочки!", LogType.Error);
        return;
    }

#if DEBUG
    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Запуск цепочки кликов оповещения альянса...", LogType.Test);
#endif

    // 1. Первый клик: Пробуем открыть меню чатов
    this.ClickTo(GameUi.ChatsInterface);
    await Task.Delay(3500, token); // Даем время на отрисовку интерфейса

    Mat? currentScreenshot = Tools.CaptureWindow(Hwnd);
    if (currentScreenshot?.Empty() is not false || currentScreenshot.Width <= 0 || currentScreenshot.Height <= 0)
    {
        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось сделать свежий скриншот экрана эмулятора. Прерывание!", LogType.Error);
        currentScreenshot?.Dispose();
        return;
    }

    string imgPath1 = Path.Combine(Program.TemplatesDir, "imgAliChatENG.png");
    string imgPath2 = Path.Combine(Program.TemplatesDir, "imgAliChatRUS.png");

    if (!File.Exists(imgPath1) || !File.Exists(imgPath2))
    {
        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Файлы шаблонов чата (ENG/RUS) отсутствуют на диске. Прерывание!", LogType.Error);
        currentScreenshot.Dispose();
        return;
    }

    Rect searchRegion = new(5, 220, 300, 500);
    Rect safeSearchRegion = Tools.ClampRegion(searchRegion, currentScreenshot.Width, currentScreenshot.Height);

    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Ищу маркеры языков меню чата...", LogType.Info);
    Point? foundEng = Tools.FindTemplateInRegion(currentScreenshot, imgPath1, safeSearchRegion, 0.85);
    Point? foundRus = Tools.FindTemplateInRegion(currentScreenshot, imgPath2, safeSearchRegion, 0.85);

    // ==========================================
    // ПОПЫТКА №2: Если чат не открылся с первого раза
    // ==========================================
    if (!foundEng.HasValue && !foundRus.HasValue)
    {
        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Чат не открылся с первой попытки. Пробую кликнуть повторно...", LogType.Warning);
        currentScreenshot.Dispose(); // Освобождаем старый неудачный кадр

        this.ClickTo(GameUi.ChatsInterface);
        await Task.Delay(4000, token); // Даем чуть больше времени на анимацию

        currentScreenshot = Tools.CaptureWindow(Hwnd);
        if (currentScreenshot?.Empty() is not false)
        {
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Не удалось сделать повторный скриншот. Прерывание!", LogType.Error);
            currentScreenshot?.Dispose();
            return;
        }

        foundEng = Tools.FindTemplateInRegion(currentScreenshot, imgPath1, safeSearchRegion, 0.85);
        foundRus = Tools.FindTemplateInRegion(currentScreenshot, imgPath2, safeSearchRegion, 0.85);
    }

    // Если даже после второго клика ничего не найдено — значит точно не совпадают координаты ChatsInterface
    if (!foundEng.HasValue && !foundRus.HasValue)
    {
        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] КРИТ: Шаблоны чата не обнаружены даже после повторного клика. Проверьте координаты ChatsInterface в GameUi!", LogType.Error);

#if DEBUG
        try
        {
            using Mat cropped = new(currentScreenshot, safeSearchRegion);
            string debugDir = Path.GetFullPath(Path.Combine(Program.TemplatesDir, "..", "DebugScreenshots"));
            Directory.CreateDirectory(debugDir);
            Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgAliChat_NOT_FOUND.png"), cropped);
        }
        catch (Exception ex) { Logger.Log($"[{Settings.Name}] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
#endif
        currentScreenshot.Dispose();
        return;
    }

    try
    {
        Point targetPoint = new();

        if (foundEng.HasValue)
        {
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] УСПЕХ: Найден ENG чат в точке X={foundEng.Value.X}, Y={foundEng.Value.Y}", LogType.Info);
            targetPoint = new Point(foundEng.Value.X, foundEng.Value.Y);
        }
        else if (foundRus.HasValue)
        {
            Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] УСПЕХ: Найден RUS чат в точке X={foundRus.Value.X}, Y={foundRus.Value.Y}", LogType.Info);
            targetPoint = new Point(foundRus.Value.X, foundRus.Value.Y);
        }

        // 2. Второй клик: Кликаем по динамическим координатам через ADB, передавая порт этого бота
        Logger.Log($"[{Settings.Name}][Клик] Отправка фонового клика по динамическим координатам: X={targetPoint.X}, Y={targetPoint.Y}", LogType.Info);
        Tools.SmartClick(targetPoint.X, targetPoint.Y, minSec: 1, maxSec: 3, offset: 3, adbPort: Settings.AdbPort);
    }
    catch (Exception ex)
    {
        Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Критический сбой анализа экрана: {ex.Message}", LogType.Error);
        return;
    }
    finally
    {
        currentScreenshot.Dispose(); // Гарантированно очищаем память матрицы скриншота
    }

    // Ждем выполнения анимации выбора чата альянса
    await Task.Delay(2000, token);

    // 3. Третий клик: Открываем меню ввода чата
    this.ClickTo(GameUi.ChatInputMenu);
    await Task.Delay(1200, token);

    // 4. Четвертый клик: Открываем меню быстрых сообщений
    this.ClickTo(GameUi.ChatFastInput);
    await Task.Delay(1200, token);

    // 5. Пятый клик: Открываем вкладку данных разведки
    this.ClickTo(GameUi.ChatInform);
    await Task.Delay(1200, token);

    // 6. Шестой клик: Выбираем статус-сообщение "Scout"
    this.ClickTo(GameUi.ChatMessScout);
    await Task.Delay(1200, token);

    // 7. Седьмой клик: Закрываем область ввода
    this.ClickTo(GameUi.WindowCenter);
    await Task.Delay(1500, token);

    // 8. Восьмой клик: Нажимаем кнопку "Отправить"
    this.ClickTo(GameUi.ChatButtSend);
    await Task.Delay(2000, token);

    // 9. Девятый клик: Закрываем общий интерфейс чатов
    this.ClickTo(GameUi.WindowCenter);

    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] Цепочка кликов экстренного оповещения AliChatWarning успешно выполнена.", LogType.Success);
}


#endregion

#region _isSaveLocal

        private bool? _isSaveLocal;

        public bool? IsSaveLocal
        {
            get => _isSaveLocal;
            set
            {
                if (_isSaveLocal == value) return;
                _isSaveLocal = value;

                if (_isSaveLocal is false)
                {
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] ОПАСНОСТЬ!!! В системе посторонние!", LogType.Warning);
                }
                else if (_isSaveLocal is true)
                {
                    Logger.Log($"[{Settings.Name}|{EVESystem}|{EVEShip}] В системе нет посторонних.", LogType.Success);
                }
            }
        }


    }

#endregion

#region AccountTask

    // Перечисление для задач (что делать боту дальше) 
    // [v] Продумать список возможных действий
    // [v] Добавить действие "Осмотреться"
    public enum AccountTask
    {
        Undocking,         // Выйти из дока
        GoToBelt,          // Отправиться в зону добычи
        Mining,            // Добывать руду
        GoToStation,       // Вернуться на станцию
        Unloading,         // Выгрузить руду на станцию
        CheckSecurity,     // Проверить статус безопастности
        CheckYourOwnState, // Проверить текущее состояние
        SendAliChatWarning
    }

#endregion

}