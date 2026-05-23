
namespace EVEEchoesBot
{

// [ ] Проверить все методы и добавить новый метод Logger.Log() 

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
                // Если состояние изменилось с безопасного (true) на опасное (false)
                if (_isSave is true && value is false)
                {
                    _isSave = value;

                    // Используем критический тип лога Red
                    Logger.Log("ОПАСНОСТЬ!!! В системе посторонние!", LogType.Warning);
                    AliChatWarning();
                }
                else
                {
                    // Используем подтверждающий тип лога Green
                    Logger.Log("В системе нет посторонних.", LogType.Success);
                    _isSave = value;
                }
            }
        }


        #endregion

    // 1. Глобальные переменные для управления состоянием
    public static BotConfig? _config;
    public static WindowSettings? _currentAccount;
    public static readonly Dictionary<string, AccountTask> _accountTasks = [];

    // Перечисление для задач (что делать боту дальше) 
    // [x] Продумать список возможных действий
    // [x] Добавить действие "Осмотреться"
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

        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
        // - + - + - + - + - |  Основная программа   | - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

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
                            // RunUndockLogic();
                            break;

                        case AccountTask.GoToBelt:
                            Logger.Log("Полет на астероидный белт...", LogType.Info);
                            // RunWarpToBeltLogic();
                            break;

                        case AccountTask.Mining:
                            Logger.Log("Процесс добычи руды (майнинг)...", LogType.Info);
                            // RunMiningLogic();
                            break;

                        case AccountTask.GoToStation:
                            Logger.Log("Трюм полон. Возврат на станцию (варп)...", LogType.Info);
                            // RunWarpToStationLogic();
                            break;

                        case AccountTask.Unloading:
                            Logger.Log("Разгрузка руды на станции в ангар...", LogType.Info);
                            // RunUnloadLogic();
                            break;

                        case AccountTask.CheckSecurity:
                            Logger.Log("Выполняю контроль безопасности системы...", LogType.Info);
                            CheckSecurityStatus();
                            break;

                        case AccountTask.CheckYourOwnState:
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

        Logger.Log("Бот успешно остановлен. Сессия завершена.", LogType.Success);
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
    // 1. Самостоятельно загружаем настройки и ищем аккаунт
    BotConfig config = ConfigManager.Load();
    WindowSettings? testAccount = config.Accounts.FirstOrDefault();

    if (testAccount == null)
    {
        Logger.Log("Ошибка CheckSecurityStatus: В конфигурации нет доступных аккаунтов.", LogType.Error);
        IsSave = false;
        return;
    }

    // Привязываем контекст логгера к текущему аккаунту
    Program._currentAccount = testAccount;

    // 2. Получаем дескриптор окна приложения
    IntPtr hWnd = Tools.GetWindow(testAccount);
    if (hWnd == IntPtr.Zero)
    {
        Logger.Log("Ошибка CheckSecurityStatus: Окно целевой программы не найдено.", LogType.Error);
        IsSave = false;
        return;
    }

    // Пути к базовым маркерам интерфейса
    string pathImg1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "imgMarker1.png"); // Переименуйте в ваше имя
    string pathImg2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "imgMarker2.png"); // Переименуйте в ваше имя

    // Области поиска (настройте под координаты вашей игры)
    Rect localRegion = new(50, 250, 300, 420);

    while (!_cts.Token.IsCancellationRequested)
    {
        // 3. Делаем снимок экрана
        using Mat? screenshot = Tools.CaptureWindow(hWnd);
        if (screenshot?.Empty() is not false || screenshot.Width <= 0 || screenshot.Height <= 0)
        {
            Logger.Log("Ошибка CheckSecurityStatus: Не удалось сделать скриншот окна.", LogType.Error);
            IsSave = false;
            return;
        }

        // Шаг А: Ищем Изображение 1
        Point? foundImg1 = Tools.FindTemplateInRegion(screenshot, pathImg1, localRegion, 0.80);
        if (foundImg1.HasValue)
        {
            // Нашли изображение 1 -> Переходим сразу к финальной проверке локала
            if (RunLocalCheck(screenshot, localRegion)) return;
            continue; // Если финальная проверка сказала "не нашли ничего", идем на старт цикла
        }

        // Шаг Б: Изображение 1 не найдено, ищем Изображение 2
        Point? foundImg2 = Tools.FindTemplateInRegion(screenshot, pathImg2, localRegion, 0.80);
        if (foundImg2.HasValue)
        {
            // Нашли Изображение 2 -> Кликаем в него и переходим к финальной проверке
            Logger.Log("Обнаружено Изображение 2. Выполняю клик для раскрытия интерфейса.", LogType.Test);
            Tools.SmartClick(hWnd, foundImg2.Value.X, foundImg2.Value.Y, minSec: 0, maxSec: 0, offset: 2);
            Thread.Sleep(500); // Короткая пауза, чтобы интерфейс успел отрисоваться

            // Делаем новый скриншот, так как после клика экран изменился
            using Mat? freshScreenshot = Tools.CaptureWindow(hWnd);
            if (freshScreenshot?.Empty() is not false) continue;

            if (RunLocalCheck(freshScreenshot, localRegion)) return;
            continue;
        }

        // Шаг В: Изображение 2 тоже не нашли -> Ждем 5 секунд и пробуем найти его еще раз
        Logger.Log("Изображение 1 и 2 не найдены. Ожидание 5 секунд для повторной проверки...", LogType.Warning);
        Thread.Sleep(5000);

        using Mat? retryScreenshot = Tools.CaptureWindow(hWnd);
        if (retryScreenshot?.Empty() is not false) continue;

        Point? retryImg2 = Tools.FindTemplateInRegion(retryScreenshot, pathImg2, localRegion, 0.80);
        if (retryImg2.HasValue)
        {
            // Со второй попытки нашли Изображение 2 -> Кликаем и проверяем
            Tools.SmartClick(hWnd, retryImg2.Value.X, retryImg2.Value.Y, minSec: 0, maxSec: 0, offset: 2);
            Thread.Sleep(500);

            using Mat? freshScreenshot2 = Tools.CaptureWindow(hWnd);
            if (freshScreenshot2?.Empty() is not false) continue;

            if (RunLocalCheck(freshScreenshot2, localRegion)) return;
            continue;
        }

        // КРИТИЧЕСКИЙ СТОП: Если после 5 секунд ожидания Изображение 2 так и не появилось
        Logger.Log("КРИТИЧЕСКИЙ СБОЙ: Повторный поиск Изображения 2 не дал результатов. Полная остановка бота!", LogType.Error);
        IsSave = false;
        _cts.Cancel(); // Экстренное завершение работы всего бота
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
    string[] templates = ["imgLocalCriminal.png", "imgLocalMinus.png", "imgLocalNeutral.png"];
    int foundCount = 0;

    foreach (string templateName in templates)
    {
        string fullTemplatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", templateName);
        if (!File.Exists(fullTemplatePath)) continue;

        Point? foundPoint = Tools.FindTemplateInRegion(screenshot, fullTemplatePath, searchRegion, 0.80);
        if (foundPoint.HasValue)
        {
            foundCount++;
        }
    }

    // Обработка результатов согласно вашим правилам:
    if (foundCount == 3)
    {
        // 1. Нашли все три шаблона — БЕЗОПАСНО
        IsSave = true;
        return true;
    }

    if (foundCount > 0 && foundCount < 3)
    {
        // 2. Нашли один или два, но не все — ОПАСНОСТЬ (сработает триггер в свойстве IsSave)
        IsSave = false;
        return true;
    }

    // 3. Не нашли вообще ни одного шаблона (foundCount == 0) — возвращаемся в начало (ищем изображение 1 и т.д.)
    Logger.Log("Ни один из трех шаблонов безопасности не найден на экране. Сброс к началу проверки...", LogType.Warning);
    return false;
}

#endregion

#region AliChatWarning

/// <summary>
/// Выполняет цепочку из 8 кликов для автоматической отправки предупреждения в чат альянса.
/// На втором шаге производит валидацию экрана через OpenCV, проверяя, открылось ли меню чатов.
/// </summary>
private static void AliChatWarning()
{
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
    Tools.SmartClick(hWnd, 25, 600);
    Thread.Sleep(500); // Ожидаем анимацию открытия интерфейса

    // ================= НАЧАЛО АВТОНОМНОЙ ПРОВЕРКИ ИНТЕРФЕЙСА =================

    // Делаем снимок экрана после первого клика
    Mat? currentScreenshot = Tools.CaptureWindow(hWnd);

    if (currentScreenshot?.Empty() is not false)
{
        Logger.Log("Не удалось сделать свежий скриншот экрана эмулятора. Прерывание!", LogType.Error);
        currentScreenshot?.Dispose();
        return;
    }

    try
    {
        // Пути к шаблонам локализации чата
        string imgPath1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "imgAliChatENG.png");
        string imgPath2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "imgAliChatRUS.png");

        if (!File.Exists(imgPath1) || !File.Exists(imgPath2))
        {
            Logger.Log("Файлы шаблонов чата (ENG/RUS) отсутствуют на диске. Прерывание!", LogType.Error);
            return;
        }

        // Ограничиваем область поиска для ускорения работы алгоритма
        Rect searchRegion = new(5, 220, 300, 500);

        // Ищем языковые маркеры меню чата
        Point? foundEng = Tools.FindTemplateInRegion(currentScreenshot, imgPath1, searchRegion, 0.85);
        Point? foundRus = Tools.FindTemplateInRegion(currentScreenshot, imgPath2, searchRegion, 0.85);

        // Если ни один шаблон не распознан — меню не открылось, прекращаем операцию
        if (!foundEng.HasValue && !foundRus.HasValue)
        {
#if DEBUG
            Logger.Log("Шаблоны чата не обнаружены. Интерфейс не готов. Прерывание!", LogType.Error);
#endif
            return;
        }
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
    Tools.SmartClick(hWnd, 50, 450);

    // 3. Третий клик: Открываем меню ввода чата
    Tools.SmartClick(hWnd, 365, 700);

    // 4. Четвертый клик: Открываем меню быстрых сообщений
    Tools.SmartClick(hWnd, 1250, 700);

    // 5. Пятый клик: Открываем вкладку данных разведки
    Tools.SmartClick(hWnd, 80, 400);

    // 6. Шестой клик: Выбираем статус-сообщение "Scout"
    Tools.SmartClick(hWnd, 300, 600);

    // 7. Седьмой клик: Нажимаем кнопку "Отправить"
    Tools.SmartClick(hWnd, 450, 255);

    // 8. Восьмой клик: Закрываем интерфейс чатов
    Tools.SmartClick(hWnd, 625, 225);

    Logger.Log("Цепочка кликов экстренного оповещения AliChatWarning успешно выполнена.", LogType.Success);

    // Сбрасываем контекст логгера
    _currentAccount = null;
}

#endregion

    }
}


