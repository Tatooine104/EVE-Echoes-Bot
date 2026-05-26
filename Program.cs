using OpenCvSharp;

namespace EVEEchoesBot
{

// [ ] TODO Проверить все методы и добавить новый метод Logger.Log() 

    static partial class Program
    {

        #region Constants & Fields

        // 1. Создаем глобальный источник токена отмены
        private static readonly CancellationTokenSource _cts = new();

        #endregion

        #region Bot params

        private static bool? _isSave;

        public static bool? IsSave
        {
            get => _isSave;
            set
            {
                // Если значение не изменилось, ничего не делаем (чтобы не спамить в консоль)
                if (_isSave == value) return;

                _isSave = value;

                if (_isSave is false)
                {
                    // Сработает ВСЕГДА, когда статус становится опасным (даже при первом запуске)
                    Logger.Log("ОПАСНОСТЬ!!! В системе посторонние!", LogType.Warning);
                    AliChatWarning();
                }
                else if (_isSave is true)
                {
                    // Сработает, только если в системе действительно чисто
                    Logger.Log("В системе нет посторонних.", LogType.Success);
                }
            }
        }



        #endregion

    // 1. Глобальные переменные для управления состоянием
    public static BotConfig? _config;
    public static WindowSettings? _currentAccount;
    public static readonly Dictionary<string, AccountTask> _accountTasks = [];

    /// <summary>
    /// Глобальный путь к папке Images в корне проекта.
    /// </summary>
    public static string TemplatesDir => Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Images"));


    // Перечисление для задач (что делать боту дальше) 
    // [v] Продумать список возможных действий
    // [v] Добавить действие "Осмотреться"
    public enum AccountTask
    {
        Undocking,        // Выйти из дока
        GoToBelt,         // Отправиться в зону добычи
        Mining,           // Добывать руду
        GoToStation,      // Вернуться на станцию
        Unloading,        // Выгрузить руду на станцию
        CheckSecurity,    // Проверить статус безопастности
        CheckYourOwnState // Проверить текущее состояние
    }

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
// - + - + - + - + - |  Основная программа   | - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

#region Main


    public static void Main()
    {
        Logger.Log("Бот успешно запущен.", LogType.Success);
        Logger.Log("Нажмите [ESC] в любой момент для плавной остановки скрипта.", LogType.Info);

        // Запуск фонового потока для непрерывного отслеживания нажатия управляющих клавиш
        Thread inputThread = new(ListenForCancelKey) { IsBackground = true };
        inputThread.Start();

        // Инициализация параметров диспетчера задач из конфигурации
        InitializeBot();

        if (_config?.Accounts == null)
        {
            Logger.Log("Критическая ошибка: Данные конфигурации не инициализированы. Выход.", LogType.Error);
            return;
        }

        // Главный диспетчерский цикл автоматизации
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                foreach (WindowSettings account in _config.Accounts)
                {
                    if (_cts.Token.IsCancellationRequested)
                    {
                        break;
                    }

                    // Переключаем глобальный контекст логгера на обрабатываемый аккаунт
                    _currentAccount = account;
                    string accountName = account.Name ?? "Unknown";

                    // Извлекаем текущую назначенную задачу из словаря диспетчера
                    if (!_accountTasks.TryGetValue(accountName, out AccountTask currentTask))
                    {
                        Logger.Log("В словаре диспетчера отсутствует задача для данного аккаунта.", LogType.Warning);
                        continue;
                    }

                    // Маршрутизация выполнения в зависимости от текущего состояния автоматизации
                    switch (currentTask)
                    {
                        case AccountTask.Undocking:
                            Logger.Log("Выполняется андок (вылет со станции)...", LogType.Info);
                            // [ ] TODO Написать метод андока 
                            // RunUndockLogic();
                            break;

                        case AccountTask.GoToBelt:
                            Logger.Log("Полет на астероидный белт...", LogType.Info);
                            // [ ] TODO Написать метод варпа на белт
                            // RunWarpToBeltLogic();
                            break;

                        case AccountTask.Mining:
                            Logger.Log("Процесс добычи руды (майнинг)...", LogType.Info);
                            // [ ] TODO Написать метод добычи руды
                            // RunMiningLogic();
                            break;

                        case AccountTask.GoToStation:
                            Logger.Log("Трюм полон. Возврат на станцию (варп)...", LogType.Info);
                            // [ ] TODO Написать метод возврата на станцию
                            // RunWarpToStationLogic();
                            break;

                        case AccountTask.Unloading:
                            Logger.Log("Разгрузка руды на станции в ангар...", LogType.Info);
                            // [ ] TODO Написать метод выгрузки руды на станцию
                            // RunUnloadLogic();
                            break;

                        case AccountTask.CheckSecurity:
                            Logger.Log("Выполняю контроль безопасности системы...", LogType.Info);
                            // [ ] TEST Проверить работопособность 
                            CheckSecurityStatus();
                            break;

                        case AccountTask.CheckYourOwnState:
                            // [ ] TODO Написать метод проверки текущего состояния
                            Logger.Log("Выполняю оценку текущего состояния...", LogType.Info);
                            // CheckYourOwnState();
                            break;

                        default:
                            Logger.Log($"Неизвестное состояние задачи: {currentTask}", LogType.Error);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Критическая ошибка в главном цикле диспетчера: {ex.Message}", LogType.Error);
            }
            finally
            {
                // Сбрасываем контекст логов в конце каждого полного круга
                _currentAccount = null;
            }

            // Безопасное неблокирующее ожидание 15 секунд между кругами автоматизации
            if (_cts.Token.WaitHandle.WaitOne(15000))
            {
                break;
            }
        }

        Logger.Log("Бот остановлен. Сессия завершена.", LogType.Warning);
    }


#endregion

#region Initialize Bot

    /// <summary>
    /// Выполняет первоначальную инициализацию бота при старте сессии:
    /// <list type="bullet">
    /// <item><description>Загружает файл конфигурации JSON через <see cref="ConfigManager"/>.</description></item>
    /// <item><description>Проверяет валидность списка игровых аккаунтов.</description></item>
    /// <item><description>Конвертирует строковые названия стартовых задач в перечисление <see cref="AccountTask"/>.</description></item>
    /// <item><description>Формирует глобальный диспетчерский словарь начального состояния автоматизации.</description></item>
    /// </list>
    /// В случае критической ошибки инициирует безопасную отмену токена <c>_cts.Cancel()</c> для остановки всех потоков.
    /// </summary>
    private static void InitializeBot()
    {
        try
        {
            // Загружаем конфигурацию один раз при старте
            _config = ConfigManager.Load();

            if (_config?.Accounts == null || _config.Accounts.Count == 0)
            {
                Logger.Log("В конфигурации нет доступных аккаунтов. Запуск отменен.", LogType.Error);
                _cts.Cancel();
                return;
            }

            // Заполняем словарь начальными задачами на основе конфигурации
            foreach (WindowSettings account in _config.Accounts)
            {
                // Временно привязываем контекст логгера к текущему аккаунту для точной диагностики
                _currentAccount = account;

                string accountName = account.Name ?? "Unknown";
                string firstTaskStr = account.FirstTask ?? "Undocking";

                // Преобразуем строку из конфига в элемент перечисления AccountTask
                AccountTask initialTask = firstTaskStr switch
                {
                    "Undocking"         => AccountTask.Undocking,
                    "GoToBelt"          => AccountTask.GoToBelt,
                    "Mining"            => AccountTask.Mining,
                    "GoToStation"       => AccountTask.GoToStation,
                    "Unloading"         => AccountTask.Unloading,
                    "CheckSecurity"     => AccountTask.CheckSecurity,
                    "CheckYourOwnState" => AccountTask.CheckYourOwnState,
                    _                   => AccountTask.CheckYourOwnState
                };

                // Записываем стартовую задачу в глобальный словарь диспетчера
                _accountTasks[accountName] = initialTask;
            }

            // Сбрасываем системный контекст после завершения цикла инициализации
            _currentAccount = null;

            Logger.Log($"Конфигурация успешно загружена. Аккаунтов в работе: {_config.Accounts.Count}", LogType.Success);
        }
        catch (Exception ex)
        {
            Logger.Log($"Критическая ошибка при инициализации бота: {ex.Message}", LogType.Error);
            _cts.Cancel();
        }
    }

#region ListenForCancelKey

    /// <summary>
    /// Постоянно работает в фоновом потоке, отслеживая нажатия управляющих клавиш:
    /// <list type="bullet">
    /// <item><description><c>ConsoleKey.Escape</c> — инициирует плавную остановку всех процессов бота.</description></item>
    /// <item><description><c>ConsoleKey.F10</c> — запускает мгновенный изолированный тест эмуляции клика драйвером.</description></item>
    /// </list>
    /// </summary>
    private static void ListenForCancelKey()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            if (!Console.KeyAvailable)
            {
                Thread.Sleep(100); // Снижаем нагрузку на процессор до ~0%
                continue;
            }

            // Считываем нажатую клавишу и скрываем её символ из консоли
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            // 1. Плавный выход из программы
            if (key.Key == ConsoleKey.Escape)
            {
                Logger.Log("Получен сигнал отмены. Завершаем текущий круг и выходим...", LogType.Warning);
                _cts.Cancel();
                break;
            }

            // 2. Мгновенный отладочный клик
            if (key.Key == ConsoleKey.F10)
            {
                Logger.Log("Нажата клавиша F10! Запуск экспресс-проверки клика...", LogType.Test);

                try
                {
                    BotConfig config = ConfigManager.Load();
                    WindowSettings? testAccount = config.Accounts.FirstOrDefault();

                    if (testAccount == null)
                    {
                        Logger.Log("Ошибка теста: Аккаунты в файле конфигурации не найдены.", LogType.Error);
                        continue;
                    }

                    // Переключаем глобальный контекст логгера на тестовый аккаунт
                    _currentAccount = testAccount;

                    // Получаем хэндл окна через обновленный менеджер
                    IntPtr hWnd = Tools.GetWindow(testAccount);
                    if (hWnd == IntPtr.Zero)
                    {
                        Logger.Log($"Ошибка теста: Окно '{testAccount.WindowTitle}' не найдено в ОС.", LogType.Error);
                        continue;
                    }

                    // Находим внутреннее окно ввода эмулятора
                    IntPtr inputWindow = WinAPI.GetInputWindow(hWnd);

                    // Координаты для быстрой проверки клика
                    const int testX = 60;
                    const int testY = 680;

                    Logger.Log($"Отправляю тестовый клик в окно {inputWindow} (X: {testX}, Y: {testY})...", LogType.Test);

                    // Вызываем клик через оптимизированный класс эмулятора драйвера
                    Tools.SmartClick(hWnd, testX, testY, minSec: 0, maxSec: 0, offset: 3);

                    Logger.Log("Тестовый клик успешно сгенерирован и отправлен.", LogType.Success);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Сбой при симуляции отладочного клика: {ex.Message}", LogType.Error);
                }
                finally
                {
                    // Сбрасываем контекст логов после окончания теста
                    _currentAccount = null;
                }
            }
        }
    }

#endregion

        #endregion

        // ============================================================================================

        #region CheckSecurityStatus 

        /// <summary>
        /// Автономно загружает конфиг, находит окно, делает скриншот и проверяет безопасность локала.
        /// Реализует сложную многоуровневую логику поиска маркеров интерфейса с возможностью клика и жесткого стопа бота.
        /// </summary>
        static void CheckSecurityStatus()
        {
            Logger.Log("[Диагностика] Запуск метода CheckSecurityStatus.", LogType.Info);

            BotConfig config = ConfigManager.Load();
            WindowSettings? testAccount = config.Accounts.FirstOrDefault();

            if (testAccount == null)
            {
                Logger.Log("Ошибка CheckSecurityStatus: В конфигурации нет доступных аккаунтов.", LogType.Error);
                IsSave = false;
                return;
            }

            Program._currentAccount = testAccount;

            IntPtr hWnd = Tools.GetWindow(testAccount);
            if (hWnd == IntPtr.Zero)
            {
                Logger.Log("Ошибка CheckSecurityStatus: Окно целевой программы не найдено.", LogType.Error);
                IsSave = false;
                return;
            }

            // Использование глобального свойства для путей к шаблонам
            string pathImg1 = Path.Combine(TemplatesDir, "imgLocalChatHead.png");
            string pathImg2 = Path.Combine(TemplatesDir, "imgLocalChatIcon.png");

            // [ ] TODO Области поиска (настройте под координаты вашей игры)
            Rect localRegion1 = new(5, 5, 500, 715);
            Rect localRegion2 = new(5, 650, 100, 120);

            // Путь к папке отладки вычисляется на одном уровне с папкой Images
            string debugDir = Path.GetFullPath(Path.Combine(TemplatesDir, "..", "DebugScreenshots"));

            while (!_cts.Token.IsCancellationRequested)
            {
                using Mat? screenshot = Tools.CaptureWindow(hWnd);
                if (screenshot?.Empty() is not false || screenshot.Width <= 0 || screenshot.Height <= 0)
                {
                    Logger.Log("Ошибка CheckSecurityStatus: Не удалось сделать скриншот окна.", LogType.Error);
                    IsSave = false;
                    return;
                }

                // КОРРЕКЦИЯ ГРАНИЦ: Защита от вылета OpenCV (срезаем регионы, если они вылезают за границы скриншота)
                Rect safeRegion1 = ClampRegion(localRegion1, screenshot.Width, screenshot.Height);
                Rect safeRegion2 = ClampRegion(localRegion2, screenshot.Width, screenshot.Height);

                // Если регионы получились нулевыми или некорректными — логируем проблему размеров
                if (safeRegion1.Width <= 0 || safeRegion1.Height <= 0 || safeRegion2.Width <= 0 || safeRegion2.Height <= 0)
                {
                    Logger.Log($"[КРИТИЧЕСКИЙ СБОЙ] Заданные Rect выходят за рамки окна! Размер скриншота: {screenshot.Width}x{screenshot.Height}", LogType.Error);
                    IsSave = false;
                    return;
                }

                // ==========================================
                // ЭТАП 1: Ищем Изображение 1 (Шапка чата)
                // ==========================================
                Point? foundImg1 = Tools.FindTemplateInRegion(screenshot, pathImg1, safeRegion1, 0.80);

                if (foundImg1.HasValue)
                {
                    Logger.Log($"[УСПЕХ] Изображение 1 НАЙДЕНО в точке X={foundImg1.Value.X}, Y={foundImg1.Value.Y}.", LogType.Info);

#if DEBUG
                    try
                    {
                        using Mat cropped = new(screenshot, safeRegion1);
                        Directory.CreateDirectory(debugDir);
                        Cv2.ImWrite(Path.Combine(debugDir, "imgLocalChatHead_FOUND.png"), cropped);
                    }
                    catch (Exception ex) { Logger.Log($"[Debug] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
#endif

                    if (RunLocalCheck(screenshot, safeRegion1)) return;
                    continue;
                }

#if DEBUG
                try
                {
                    using Mat cropped = new(screenshot, safeRegion1);
                    Directory.CreateDirectory(debugDir);
                    Cv2.ImWrite(Path.Combine(debugDir, "imgLocalChatHead_NOT_FOUND.png"), cropped);
                }
                catch (Exception ex) { Logger.Log($"Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
#endif

                // ==========================================
                // ЭТАП 2: Ищем Изображение 2 (Иконка)
                // ==========================================
                Point? foundImg2 = Tools.FindTemplateInRegion(screenshot, pathImg2, safeRegion2, 0.80);

                if (foundImg2.HasValue)
                {
                    Logger.Log($"{pathImg2} НАЙДЕНО. Выполняю клик.", LogType.Test);

#if DEBUG
                    try
                    {
                        using Mat cropped = new(screenshot, safeRegion2);
                        Directory.CreateDirectory(debugDir);
                        Cv2.ImWrite(Path.Combine(debugDir, "imgLocalChatIcon_FOUND.png"), cropped);
                    }
                    catch (Exception ex) { Logger.Log($"[Debug] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
#endif

                    Tools.SmartClick(hWnd, foundImg2.Value.X, foundImg2.Value.Y, minSec: 0, maxSec: 0, offset: 2);
                    Thread.Sleep(3000);

                    using Mat? freshScreenshot = Tools.CaptureWindow(hWnd);
                    if (freshScreenshot?.Empty() is not false) continue;

                    // Обновляем безопасный регион для нового скриншота на случай изменения размеров окна
                    Rect freshSafeRegion1 = ClampRegion(localRegion1, freshScreenshot.Width, freshScreenshot.Height);

                    Point? retryImg1 = Tools.FindTemplateInRegion(freshScreenshot, pathImg1, freshSafeRegion1, 0.80);

                    if (retryImg1.HasValue)
                    {
                        if (RunLocalCheck(freshScreenshot, freshSafeRegion1)) return;
                    }
                    else
                    {
#if DEBUG
                        try
                        {
                            using Mat cropped = new(freshScreenshot, freshSafeRegion1);
                            Directory.CreateDirectory(debugDir);
                            Cv2.ImWrite(Path.Combine(debugDir, "imgLocalChatHead_AFTER_CLICK_NOT_FOUND.png"), cropped);
                        }
                        catch (Exception ex) { Logger.Log($"[Debug] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
#endif
                    }

                    continue;
                }

#if DEBUG
                try
                {
                    using Mat cropped = new(screenshot, safeRegion2);
                    Directory.CreateDirectory(debugDir);
                    Cv2.ImWrite(Path.Combine(debugDir, "imgLocalChatIcon_NOT_FOUND.png"), cropped);
                }
                catch (Exception ex) { Logger.Log($"[Debug] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
#endif

                Logger.Log("КРИТИЧЕСКИЙ СБОЙ: Изображение 1 и Изображение 2 не найдены. Полная остановка бота!", LogType.Error);
                IsSave = false;
                _cts.Cancel();
                return;
            }
        }





#endregion

#region RunLocalCheck

        /// <summary>
        /// Выполняет финальную проверку трех шаблонов безопасности на указанном кадре.
        /// </summary>
        /// <returns>True — если статус безопасности окончательно определен (Safe/Danger); False — если не нашли вообще ничего и нужно вернуться в начало.</returns>
        private static bool RunLocalCheck(Mat screenshot, Rect searchRegion)
        {
            // На всякий случай повторно защищаем регион внутри метода
            Rect safeSearchRegion = ClampRegion(searchRegion, screenshot.Width, screenshot.Height);

            string[] templates = ["imgLocalCriminal.png", "imgLocalMinus.png", "imgLocalNeutral.png"];
            int foundCount = 0;

            foreach (string templateName in templates)
            {
                // Используем единое глобальное свойство для поиска шаблонов
                string fullTemplatePath = Path.Combine(TemplatesDir, templateName);
                if (!File.Exists(fullTemplatePath)) continue;

                Point? foundPoint = Tools.FindTemplateInRegion(screenshot, fullTemplatePath, safeSearchRegion, 0.80);

                if (foundPoint.HasValue)
                {
                    foundCount++;
#if DEBUG
                    try
                    {
                        using Mat croppedRegion = new(screenshot, safeSearchRegion);
                        // Путь к папке отладки строим относительно глобальной папки шаблонов
                        string debugDir = Path.GetFullPath(Path.Combine(TemplatesDir, "..", "DebugScreenshots"));
                        Directory.CreateDirectory(debugDir);
                        string debugPath = Path.Combine(debugDir, $"{Path.GetFileNameWithoutExtension(templateName)}_FOUND.png");
                        Cv2.ImWrite(debugPath, croppedRegion);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[Debug] Ошибка сохранения скриншота успеха для {templateName}: {ex.Message}", LogType.Warning);
                    }
#endif
                }
                else
                {
#if DEBUG
                    try
                    {
                        using Mat croppedRegion = new(screenshot, safeSearchRegion);
                        string debugDir = Path.GetFullPath(Path.Combine(TemplatesDir, "..", "DebugScreenshots"));
                        Directory.CreateDirectory(debugDir);
                        string debugPath = Path.Combine(debugDir, $"{Path.GetFileNameWithoutExtension(templateName)}_NOT_FOUND.png");
                        Cv2.ImWrite(debugPath, croppedRegion);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[Debug] Ошибка сохранения скриншота неудачи для {templateName}: {ex.Message}", LogType.Warning);
                    }
#endif
                }
            }

            if (foundCount == 3)
            {
                IsSave = true;
                return true;
            }

            if (foundCount > 0 && foundCount < 3)
            {
                IsSave = false;
                return true;
            }

            Logger.Log("Ни один из трех шаблонов безопасности не найден на экране. Сброс к началу проверки...", LogType.Warning);
            return false;
        }




#endregion

#region ClampRegion

        /// <summary>
        /// Корректирует прямоугольник под фактические размеры изображения, предотвращая вылет OpenCV.
        /// </summary>
        private static Rect ClampRegion(Rect region, int maxWidth, int maxHeight)
        {
            int x = Math.Max(0, Math.Min(region.X, maxWidth - 1));
            int y = Math.Max(0, Math.Min(region.Y, maxHeight - 1));

            int width = Math.Min(region.Width, maxWidth - x);
            int height = Math.Min(region.Height, maxHeight - y);

            return new Rect(x, y, width, height);
        }

#endregion

#region AliChatWarning

        /// <summary>
        /// Выполняет цепочку из 8 кликов для автоматической отправки предупреждения в чат альянса.
        /// На втором шаге производит валидацию экрана через OpenCV, проверяя, открылось ли меню чатов.
        /// </summary>
        // [ ] TODO Добавить всем кликам +5 по Y для компенсации рамки (кроме кнопки "Отправить") 
        private static void AliChatWarning()
        {
            Logger.Log("[Диагностика] Запуск метода AliChatWarning.", LogType.Info);

            // Загружаем конфигурацию для определения целевого окна
            BotConfig config = ConfigManager.Load();
            WindowSettings? activeAccount = config.Accounts.FirstOrDefault();

            if (activeAccount == null)
            {
                Logger.Log("Аккаунты в конфигурации не найдены. Прерывание цепочки!", LogType.Error);
                return;
            }

            // Привязываем контекст логгера к текущему аккаунту
            _currentAccount = activeAccount;

            // Получаем дескриптор окна эмулятора
            IntPtr hWnd = Tools.GetWindow(activeAccount);
            if (hWnd == IntPtr.Zero)
            {
                Logger.Log("Целевое окно программы не найдено. Прерывание цепочки!", LogType.Error);
                return;
            }

#if DEBUG
            Logger.Log("Запуск цепочки кликов оповещения альянса...", LogType.Test);
#endif

            // 1. Первый клик: Открываем меню чатов
            Tools.SmartClick(hWnd, 25, 600, 1, 3, 3);
            Thread.Sleep(3000); // Ожидаем анимацию открытия интерфейса

            // ================= НАЧАЛО АВТОНОМНОЙ ПРОВЕРКИ ИНТЕРФЕЙСА =================

            // Делаем снимок экрана после первого клика
            Mat? currentScreenshot = Tools.CaptureWindow(hWnd);

            if (currentScreenshot?.Empty() is not false || currentScreenshot.Width <= 0 || currentScreenshot.Height <= 0)
            {
                Logger.Log("Не удалось сделать свежий скриншот экрана эмулятора. Прерывание!", LogType.Error);
                currentScreenshot?.Dispose();
                return;
            }

            try
            {
                // Используем глобальное свойство для путей
                string imgPath1 = Path.Combine(TemplatesDir, "imgAliChatENG.png");
                string imgPath2 = Path.Combine(TemplatesDir, "imgAliChatRUS.png");

                Logger.Log($"[Диагностика чата] Путь ENG: {imgPath1}", LogType.Info);
                Logger.Log($"[Диагностика чата] Путь RUS: {imgPath2}", LogType.Info);

                if (!File.Exists(imgPath1) || !File.Exists(imgPath2))
                {
                    Logger.Log("[КРИТИЧЕСКАЯ ОШИБКА] Файлы шаблонов чата (ENG/RUS) отсутствуют на диске. Прерывание!", LogType.Error);
                    return;
                }

                // Ограничиваем область поиска для ускорения работы алгоритма
                Rect searchRegion = new(5, 220, 300, 500);

                // Защита от вылета OpenCV (выход за границы картинки)
                Rect safeSearchRegion = ClampRegion(searchRegion, currentScreenshot.Width, currentScreenshot.Height);

                // Ищем языковые маркеры меню чата
                Logger.Log("[Диагностика чата] Ищу маркеры языков меню чата...", LogType.Info);
                Point? foundEng = Tools.FindTemplateInRegion(currentScreenshot, imgPath1, safeSearchRegion, 0.85);
                Point? foundRus = Tools.FindTemplateInRegion(currentScreenshot, imgPath2, safeSearchRegion, 0.85);

                // Если ни один шаблон не распознан — меню не открылось, прекращаем операцию
                if (!foundEng.HasValue && !foundRus.HasValue)
                {
                    Logger.Log("Шаблоны чата не обнаружены. Интерфейс не готов. Прерывание!", LogType.Error);

#if DEBUG
                    // Сохраняем область, где бот пытался найти маркеры чата, для отладки координат
                    try
                    {
                        using Mat cropped = new(currentScreenshot, safeSearchRegion);
                        string debugDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\DebugScreenshots"));
                        Directory.CreateDirectory(debugDir);
                        Cv2.ImWrite(Path.Combine(debugDir, "imgAliChat_NOT_FOUND.png"), cropped);
                        Logger.Log($"[Debug] Область поиска чата сохранена: {Path.Combine(debugDir, "imgAliChat_NOT_FOUND.png")}", LogType.Info);
                    }
                    catch (Exception ex) { Logger.Log($"[Debug] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
#endif
                    return;
                }

                if (foundEng.HasValue) Logger.Log($"[Диагностика чата] УСПЕХ: Найден ENG чат в точке X={foundEng.Value.X}, Y={foundEng.Value.Y}", LogType.Info);
                if (foundRus.HasValue) Logger.Log($"[Диагностика чата] УСПЕХ: Найден RUS чат в точке X={foundRus.Value.X}, Y={foundRus.Value.Y}", LogType.Info);
            }
            catch (Exception ex)
            {
                Logger.Log($"Критический сбой анализа экрана: {ex.Message}", LogType.Error);
                return;
            }
            finally
            {
                // Гарантированно освобождаем неуправляемую память OpenCV
                currentScreenshot.Dispose();
            }

            // ================= КОНЕЦ АВТОНОМНОЙ ПРОВЕРКИ ИНТЕРФЕЙСА =================

            // 2. Второй клик: Активируем чат альянса
            Tools.SmartClick(hWnd, 50, 450, 1, 3, 3);

            // 3. Третий клик: Открываем меню ввода чата
            Tools.SmartClick(hWnd, 365, 700, 1, 3, 3);

            // 4. Четвертый клик: Открываем меню быстрых сообщений
            Tools.SmartClick(hWnd, 1250, 700, 1, 3, 3);

            // 5. Пятый клик: Открываем вкладку данных разведки
            Tools.SmartClick(hWnd, 80, 400, 1, 3, 3);

            // 6. Шестой клик: Выбираем статус-сообщение "Scout"
            Tools.SmartClick(hWnd, 300, 600, 1, 3, 3);

            // 7. Седьмой клик: Закрываем область ввода
            Tools.SmartClick(hWnd, 800, 250, 1, 3, 10);

            // 8. Восьмой клик: Нажимаем кнопку "Отправить"
            Tools.SmartClick(hWnd, 450, 690, 3, 5, 2);

            // 9. Девятый клик: Закрываем интерфейс чатов
            Tools.SmartClick(hWnd, 625, 225, 1, 3, 3);

            Logger.Log("Цепочка кликов экстренного оповещения AliChatWarning успешно выполнена.", LogType.Success);

            // Сбрасываем контекст логгера
            _currentAccount = null;
        }


#endregion

    }
}


