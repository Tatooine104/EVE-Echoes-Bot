
namespace EVEEchoesBot
{

    static partial class Program
    {

        #region Constants & Fields

        // 1. Создаем глобальный источник токена отмены
        private static readonly CancellationTokenSource _cts = new();

        #endregion

        #region Bot params

        private static bool? _isSave = null;

        public static bool? IsSave
        {
            get => _isSave;
            set
            {
                // ИСПРАВЛЕНО: Заменили '== true' на 'is true', а '== false' на 'is false'
                if (_isSave is true && value is false)
                {
                    _isSave = value; // Обновляем значение на false
                    Tools.ConsolePrint("IsSave | ОПАСТНОСТЬ !!! В системе посторонние!", ConsoleColor.Red);
                    //Tools.ConsolePrint($"IsSave | {settings.AccountName} | ОПАСТНОСТЬ !!! В системе посторонние!", ConsoleColor.Red);
                    AliChatWarning(); // Вызываем оповещение
                }
                else
                {
                    Tools.ConsolePrint("IsSave | В системе нет посторонних!", ConsoleColor.Green);
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
    public enum AccountTask
    {
        Undocking,
        GoToBelt,
        Mining,
        GoToStation,
        Unloading
    }

        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
        // - + - + - + - + - |  Основная программа   | - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

        #region Main

        static void Main()
    {
        Tools.ConsolePrint("=== Бот успешно запущен ===", ConsoleColor.Cyan);
        Tools.ConsolePrint("--> Нажмите [ESC] в любой момент для остановки скрипта.", ConsoleColor.DarkGray);

        // Фоновый поток для отслеживания нажатия ESC
        Thread inputThread = new(ListenForCancelKey) { IsBackground = true };
        inputThread.Start();

        // 2. Инициализация параметров вне главного цикла
        InitializeBot();

        if (_config?.Accounts == null)
        {
            Tools.ConsolePrint("Критическая ошибка: Данные конфигурации не инициализированы. Выход.", ConsoleColor.Red);
            return;
        }

        // 3. Основной цикл
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                // Итерируемся по всем аккаунтам из конфигурации
                foreach (var account in _config.Accounts)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    // Устанавливаем текущий активный аккаунт
                    _currentAccount = account;

                    // Получаем имя текущего аккаунта (замените AccountName на реальное поле, если нужно)
                    string accountName = _currentAccount.Name ?? "Unknown";
                    Tools.ConsolePrint($"\n[Аккаунт: {accountName}] Обработка...", ConsoleColor.Blue);

                    // Получаем текущую задачу для этого аккаунта
                    AccountTask currentTask = _accountTasks[accountName];

                    // Выполняем логику в зависимости от текущей задачи бота
                    switch (currentTask)
                    {
                        case AccountTask.Undocking:
                            Tools.ConsolePrint($"[{accountName}] Выполняется андок (вылет со станции)...", ConsoleColor.Cyan);
                            // RunUndockLogic();
                            break;

                        case AccountTask.GoToBelt:
                            Tools.ConsolePrint($"[{accountName}] Полет на астероидный белт...", ConsoleColor.Yellow);
                            // RunWarpToBeltLogic();
                            break;

                        case AccountTask.Mining:
                            Tools.ConsolePrint($"[{accountName}] Процесс добычи руды (майнинг)...", ConsoleColor.Green);
                            // RunMiningLogic();
                            break;

                        case AccountTask.GoToStation:
                            Tools.ConsolePrint($"[{accountName}] Трюм полон. Возврат на станцию (варп)...", ConsoleColor.Magenta);
                            // RunWarpToStationLogic();
                            break;

                        case AccountTask.Unloading:
                            Tools.ConsolePrint($"[{accountName}] Разгрузка руды на станции в ангар...", ConsoleColor.DarkCyan);
                            // RunUnloadLogic();
                            break;

                        default:
                            Tools.ConsolePrint($"Предупреждение: Неизвестное состояние задачи для {accountName}.", ConsoleColor.Red);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.ConsolePrint($"Критическая ошибка в главном цикле: {ex.Message}", ConsoleColor.Red);
            }

            // Безопасное ожидание 15 секунд между кругами
            if (_cts.Token.WaitHandle.WaitOne(15000))
            {
                break;
            }
        }

        Tools.ConsolePrint("=== Бот успешно остановлен ===", ConsoleColor.Cyan);
    }

#endregion

#region Initialize Bot

    // Метод для инициализации данных
    private static void InitializeBot()
    {
        try
        {
            // Загружаем конфиг один раз при старте
            _config = ConfigManager.Load();

            if (_config?.Accounts == null || _config.Accounts.Count == 0)
            {
                Tools.ConsolePrint("Критическая ошибка: В конфигурации нет доступных аккаунтов.", ConsoleColor.Red);
                _cts.Cancel();
                return;
            }

            // Заполняем массив (словарь) начальными задачами на основе Script из конфига
            foreach (var account in _config.Accounts)
            {
                // Безопасно получаем имя аккаунта (замените AccountName на ваше реальное поле)
                string accountName = account.Name ?? "Unknown";

                // Считываем стартовую задачу из конфига. Если там пусто, используем Undocking по умолчанию.
                string firstTaskStr = account.FirstTask ?? "Undocking";

                // Преобразуем строку из конфига в элемент перечисления AccountTask
                AccountTask initialTask = firstTaskStr switch
                {
                    "Undocking"   => AccountTask.Undocking,
                    "GoToBelt"    => AccountTask.GoToBelt,
                    "Mining"      => AccountTask.Mining,
                    "GoToStation" => AccountTask.GoToStation,
                    "Unloading"   => AccountTask.Unloading,
                    _             => AccountTask.Undocking // Защита от опечаток в конфиге
                };

                // Записываем стартовую задачу в словарь
                _accountTasks[accountName] = initialTask;
            }

            Tools.ConsolePrint($"Конфигурация загружена. Аккаунтов в работе: {_config.Accounts.Count}", ConsoleColor.Green);
        }
        catch (Exception ex)
        {
            Tools.ConsolePrint($"Ошибка при инициализации бота: {ex.Message}", ConsoleColor.Red);
            _cts.Cancel();
        }
    }


/// <summary>
/// Метод постоянно работает в фоне: ждет ESC для выхода или F10 для мгновенного теста клика.
/// </summary>
private static void ListenForCancelKey()
{
    while (!_cts.Token.IsCancellationRequested)
    {
        // Проверяем, нажата ли какая-либо клавиша
        if (Console.KeyAvailable)
        {
            // Считываем клавишу и прячем её символ из консоли (intercept: true)
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            // 1. Если нажат ESC — плавно останавливаем бота
            if (key.Key == ConsoleKey.Escape)
            {
                Tools.ConsolePrint("\n[INFO] Получен сигнал отмены. Завершаем текущий круг и выходим...", ConsoleColor.Yellow);
                _cts.Cancel(); // Посылаем сигнал отмены
                break;
            }

            // 2. Если нажат F10 — выполняем мгновенный тестовый клик по координатам
            if (key.Key == ConsoleKey.F10)
            {
                Tools.ConsolePrint("\n[ТЕСТ] Нажата клавиша F10! Запуск проверки клика...", ConsoleColor.Cyan);

                try
                {
                    // Быстро подгружаем настройки текущего аккаунта
                    BotConfig config = ConfigManager.Load();
                    WindowSettings? testAccount = config.Accounts.FirstOrDefault();

                    if (testAccount == null)
                    {
                        Tools.ConsolePrint("[ТЕСТ] Ошибка: Аккаунты в конфигурации не найдены.", ConsoleColor.Red);
                        continue;
                    }

                    // Получаем окно (без ресайза, так как кэш в HashSet его пропустит)
                    nint hWnd = Tools.GetWindow(testAccount);

                    if (hWnd == IntPtr.Zero)
                    {
                        Tools.ConsolePrint($"[ТЕСТ] Ошибка: Окно '{testAccount.WindowTitle}' не найдено.", ConsoleColor.Red);
                        continue;
                    }

                    // Находим внутреннее окно ввода эмулятора (SubWin/RenderWindow)
                    IntPtr inputWindow = WinAPI.GetInputWindow(hWnd);

                    // Укажите здесь ТЕ КООРДИНАТЫ, которые вы хотите проверить
                    const int testX = 60;
                    const int testY = 680;

                    Tools.ConsolePrint($"[ТЕСТ] Отправляю SmartClick в окно {inputWindow} по координатам ({testX}, {testY})...", ConsoleColor.Yellow);

                    // Вызываем клик. minSec и maxSec ставим в 0, чтобы кликнуло мгновенно без пауз
                    Tools.SmartClick(hWnd, testX, testY, minSec: 0, maxSec: 0, offset: 3);

                    Tools.ConsolePrint("[ТЕСТ] Клик успешно отправлен в эмулятор!", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    Tools.ConsolePrint($"[ТЕСТ] Ошибка при симуляции клика: {ex.Message}", ConsoleColor.Red);
                }
            }
        }

        Thread.Sleep(100); // Разгружаем процессор, чтобы поток не ел 100% ядра
    }
}


        #endregion

        // ============================================================================================

        #region EVE Echoes Methods 

        /// <summary>
        /// Автономно загружает конфиг, находит окно, делает скриншот и проверяет безопасность локала.
        /// </summary>
        static void CheckSecurityStatus()
        {
            // 1. Самостоятельно загружаем настройки и ищем аккаунт
            BotConfig config = ConfigManager.Load();
            WindowSettings? testAccount = config.Accounts.FirstOrDefault();

            if (testAccount == null)
            {
                Tools.ConsolePrint("Ошибка CheckSecurityStatus: В конфигурации нет доступных аккаунтов.", ConsoleColor.Red);
                IsSave = false; // Нет настроек — система не может считаться безопасной
                return;
            }

            // 2. Получаем дескриптор окна приложения
            nint hWnd = Tools.GetWindow(testAccount);

            if (hWnd == IntPtr.Zero)
            {
                Tools.ConsolePrint("Ошибка CheckSecurityStatus: Окно целевой программы не найдено.", ConsoleColor.Red);
                IsSave = false;
                return;
            }

            // 3. Делаем снимок экрана и оборачиваем в using для автоматической очистки памяти C++
            OpenCvSharp.Mat? screenshot = Tools.CaptureWindow(hWnd);

            if (screenshot?.Empty() is not false || screenshot.Width <= 0 || screenshot.Height <= 0)
            {
                Tools.ConsolePrint("Ошибка CheckSecurityStatus: Не удалось сделать скриншот окна.", ConsoleColor.Red);
                IsSave = false;
                screenshot?.Dispose(); // Освобождаем память вручную при ошибке
                return;
            }

            // 4. Логика проверки шаблонов интерфейса
            string[] templates = ["imgLocalCriminal.png", "imgLocalMinus.png", "imgLocalNeutral.png"];
            bool currentStatus = true;

            OpenCvSharp.Rect searchRegion = new(50, 250, 300, 420);

            foreach (string templateName in templates)
            {
                string fullTemplatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", templateName);

                if (!File.Exists(fullTemplatePath))
                {
                    currentStatus = false;
                    continue;
                }

                OpenCvSharp.Point? foundPoint = Tools.FindTemplateInRegion(screenshot, fullTemplatePath, searchRegion, 0.80);

                if (!foundPoint.HasValue)
                {
                    currentStatus = false;
                }
            }

            // При присвоении сработает логика внутри свойства set { ... }
            IsSave = currentStatus;

            /*if (IsSave == false)
            {
                Tools.ConsolePrint("=== ВНИМАНИЕ! Обнаружена опасность!", ConsoleColor.Magenta);
            }*/
        }


/// <summary>
/// Выполняет цепочку из 7 кликов для отправки предупреждения в чат.
/// На втором шаге проверяет, открылось ли меню чатов на русском или английском языке.
/// </summary>
static void AliChatWarning()
{
    IntPtr targetWindow = IntPtr.Zero;

#if DEBUG
    Tools.ConsolePrint("--> Запуск цепочки кликов AliChatWarning...", ConsoleColor.Yellow);
#endif

    // 1. Первый клик: Открываем меню чатов
    Tools.SmartClick(targetWindow, 25, 600);

    // Пауза 500 мс, чтобы интерфейс игры/эмулятора успел обновиться после клика
    Thread.Sleep(500);

    // ================= НАЧАЛО АВТОНОМНОЙ ПРОВЕРКИ НА ВТОРОМ ШАГЕ =================

    // Получаем настройки и дескриптор окна для создания свежего скриншота
    BotConfig config = ConfigManager.Load();
    WindowSettings? testAccount = config.Accounts.FirstOrDefault();

    if (testAccount == null)
    {
        Tools.ConsolePrint("Ошибка AliChatWarning: Аккаунты в конфигурации не найдены. Прерывание!", ConsoleColor.Red);
        return;
    }

    nint hWnd = Tools.GetWindow(testAccount);
    if (hWnd == IntPtr.Zero)
    {
        Tools.ConsolePrint("Ошибка AliChatWarning: Целевое окно программы не найдено. Прерывание!", ConsoleColor.Red);
        return;
    }

    // Делаем актуальный снимок экрана после первого клика (классическое объявление)
    OpenCvSharp.Mat? currentScreenshot = Tools.CaptureWindow(hWnd);

    // ИСПРАВЛЕНО: Длинное условие заменено на современный и безопасный условный доступ ?.
    if (currentScreenshot?.Empty() is not false)
    {
        Tools.ConsolePrint("Ошибка AliChatWarning: Не удалось сделать свежий скриншот. Прерывание!", ConsoleColor.Red);
        currentScreenshot?.Dispose(); // Освобождаем память, если объект был создан, но оказался пустым
        return;
    }

    // Пути к шаблонам локализации чата
    string imgPath1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "imgAliChatENG.png");
    string imgPath2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "imgAliChatRUS.png");

    // Проверяем физическое наличие файлов картинок, чтобы избежать системных ошибок OpenCV
    if (!File.Exists(imgPath1) || !File.Exists(imgPath2))
    {
        Tools.ConsolePrint("Ошибка AliChatWarning: Файлы шаблонов чата отсутствуют на диске. Прерывание!", ConsoleColor.Red);
        currentScreenshot.Dispose(); // Освобождаем память перед выходом
        return;
    }

    // Область поиска элементов чата на экране
    OpenCvSharp.Rect searchRegion = new(5, 220, 300, 500);

    // Ищем шаблоны на обновленном кадре
    OpenCvSharp.Point? found1 = Tools.FindTemplateInRegion(currentScreenshot, imgPath1, searchRegion, 0.85);
    OpenCvSharp.Point? found2 = Tools.FindTemplateInRegion(currentScreenshot, imgPath2, searchRegion, 0.85);

    // Если ОБА изображения НЕ найдены — меню не открылось, прерываем выполнение
    if (!found1.HasValue && !found2.HasValue)
    {
#if DEBUG
        Tools.ConsolePrint($"--> [{Path.GetFileName(imgPath1)}] и [{Path.GetFileName(imgPath2)}] не найдены. Прерывание AliChatWarning!", ConsoleColor.Red);
#endif
        currentScreenshot.Dispose(); // Освобождаем память перед выходом
        return;
    }

    // Освобождаем память скриншота, так как дальше он нам больше не нужен (идут только клики)
    currentScreenshot.Dispose();

    // ================= КОНЕЦ АВТОНОМНОЙ ПРОВЕРКИ НА ВТОРОМ ШАГЕ =================

    // 2. Второй клик: Активируем чат альянса
    Tools.SmartClick(targetWindow, 50, 450);

    // 3. Третий клик: Открываем меню чата
    Tools.SmartClick(targetWindow, 365, 700);

    // 4. Четвертый клик: Открываем меню сообщений
    Tools.SmartClick(targetWindow, 1250, 700);

    // 5. Пятый клик: Открываем данные разведки
    Tools.SmartClick(targetWindow, 80, 400);

    // 6. Шестой клик: Выбираем сообщение "Scout"
    Tools.SmartClick(targetWindow, 300, 600);

    // 7. Седьмой клик: Нажимаем кнопку "Отправить"
    Tools.SmartClick(targetWindow, 450, 255);

    // 8. Восьмой клик: Закрыь чаты
    Tools.SmartClick(targetWindow, 625, 225);

#if DEBUG
    Tools.ConsolePrint("--> Цепочка кликов AliChatWarning успешно завершена.", ConsoleColor.Green);
#endif
}

#endregion

    }
}


