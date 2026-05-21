
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
                    Tools.ConsolePrint($"IsSave | ОПАСТНОСТЬ !!! В системе посторонние!", ConsoleColor.Red);
                    //Tools.ConsolePrint($"IsSave | {settings.AccountName} | ОПАСТНОСТЬ !!! В системе посторонние!", ConsoleColor.Red);
                    AliChatWarning(); // Вызываем оповещение
                }
                else
                {
                    Tools.ConsolePrint($"IsSave | В системе нет посторонних!", ConsoleColor.Green);
                    _isSave = value;
                }
            }
        }


        #endregion

        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
        // - + - + - + - + - |  Основная программа   | - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

        #region Main

static void Main()
    {
        Tools.ConsolePrint("=== Бот успешно запущен ===", ConsoleColor.Cyan);
        Tools.ConsolePrint("--> Нажмите [ESC] в любой момент для остановки скрипта.", ConsoleColor.DarkGray);

        // 2. Запускаем фоновый поток, который слушает клавиатуру
        Thread inputThread = new(ListenForCancelKey) { IsBackground = true };

        inputThread.Start();

        // 3. Основной цикл теперь проверяет, не поступил ли сигнал отмены
        while (!_cts.Token.IsCancellationRequested)
        {
            try
                {
                // TODO: Вынести загрузку параметров аккаунтов из мейна
                // TODO: Сделать переменную, хранящую название активного аккаунта
                // TODO: Сделать массив задач для аккаунтов
                // Каждый круг заново загружаем настройки из файла
                BotConfig config = ConfigManager.Load();
                WindowSettings? testAccount = config.Accounts.FirstOrDefault();

                if (testAccount == null)
                {
                    Tools.ConsolePrint("Ошибка цикла: В конфигурации нет доступных аккаунтов. Ожидание...", ConsoleColor.Red);

                    // Вместо жесткого Thread.Sleep используем безопасную задержку с проверкой токена
                    if (_cts.Token.WaitHandle.WaitOne(5000)) break;
                    continue;
                }

                string scriptMode = testAccount.Script ?? "Default";

                switch (scriptMode)
                {
                    case "LocalWatcher":
                        CheckSecurityStatus();
                        break;

                    case "AnotherScript":
                        // RunAnotherScenario();
                        break;

                    default:
                        Tools.ConsolePrint($"Предупреждение: Неизвестный или пустой режим скрипта '{scriptMode}'.", ConsoleColor.Yellow);
                        break;
                }
            }
            catch (Exception ex)
            {
                Tools.ConsolePrint($"Критическая ошибка в главном цикле: {ex.Message}", ConsoleColor.Red);
            }

            // Безопасное ожидание 15 секунд между кругами.
            // Если во время этой паузы нажать ESC — программа прервет ожидание МГНОВЕННО, 
            // а не будет послушно дожидаться окончания 15 секунд.
            if (_cts.Token.WaitHandle.WaitOne(15000))
            {
                break;
            }
        }

        // Выполняется после выхода из цикла while
        Tools.ConsolePrint("=== Бот успешно остановлен. До свидания! ===", ConsoleColor.Cyan);
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
            OpenCvSharp.Mat? screenshot = CaptureWindow(hWnd);

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

                OpenCvSharp.Point? foundPoint = FindTemplateInRegion(screenshot, fullTemplatePath, searchRegion, 0.80);

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
    OpenCvSharp.Mat? currentScreenshot = CaptureWindow(hWnd);

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
    OpenCvSharp.Point? found1 = FindTemplateInRegion(currentScreenshot, imgPath1, searchRegion, 0.85);
    OpenCvSharp.Point? found2 = FindTemplateInRegion(currentScreenshot, imgPath2, searchRegion, 0.85);

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

        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
        // - + - + - + - + - | Остальные методы и функции, используемые в основной программе | - + - + - + - + -
        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -



        #region Others Methods 


/// <summary>
/// Кросплатформенный метод поиска шаблона на кадре по форме.
/// </summary>
/// <param name="screen">Матрица полного скриншота эмулятора.</param>
/// <param name="templatePath">Путь к файлу-шаблону.</param>
/// <param name="searchArea">Область поиска. Если null — поиск по всему кадру.</param>
/// <param name="threshold">Порог точности (0.0 - 1.0).</param>
/// <returns>Точка центра или null.</returns>
public static OpenCvSharp.Point? FindTemplateInRegion(OpenCvSharp.Mat screen, string templatePath, OpenCvSharp.Rect? searchArea = null, double threshold = 0.55)
{
    if (screen?.Empty() is not false) return null;

    // 1. Объявляем переменную. Этот стиль объявления плагины очистки кода не удаляют.
    OpenCvSharp.Mat croppedScreen;

    if (searchArea.HasValue)
    {
        // Если область передана, создаем под-матрицу (ссылку на регион)
        croppedScreen = new OpenCvSharp.Mat(screen, searchArea.Value);
    }
    else
    {
        // Если область не передана, работаем со всем экраном целиком
        croppedScreen = screen;
    }

    // 2. Загружаем файл шаблона с диска
    using var matTemplate = Cv2.ImRead(templatePath, ImreadModes.Color);

    if (matTemplate.Empty())
    {
        Console.WriteLine($"[Ошибка] Не удалось загрузить шаблон: {templatePath}");

        // Освобождаем память под-матрицы перед выходом, если она создавалась
        if (searchArea.HasValue) croppedScreen.Dispose();
        return null;
    }

    // 3. Проверяем, помещается ли шаблон в выбранную область поиска
    if (matTemplate.Width > croppedScreen.Width || matTemplate.Height > croppedScreen.Height)
    {
        Console.WriteLine($"[Ошибка] Шаблон {templatePath} ({matTemplate.Width}x{matTemplate.Height}) больше области поиска ({croppedScreen.Width}x{croppedScreen.Height})!");

        if (searchArea.HasValue) croppedScreen.Dispose();
        return null;
    }

    // 4. Переводим изображения в оттенки серого для повышения скорости и точности поиска
    using Mat grayScreen = new();
    using Mat grayTemplate = new();
    Cv2.CvtColor(croppedScreen, grayScreen, ColorConversionCodes.BGR2GRAY);
    Cv2.CvtColor(matTemplate, grayTemplate, ColorConversionCodes.BGR2GRAY);

    // 5. Выполняем сопоставление шаблонов (Template Matching)
    using Mat result = new();
    Cv2.MatchTemplate(grayScreen, grayTemplate, result, TemplateMatchModes.CCoeffNormed);
    Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

#if DEBUG
    // Данный блок выводится только при запуске проекта в режиме отладки (Debug)
    double matchPercentage = maxVal * 100;
    Console.WriteLine($"   -> Диагностика {Path.GetFileName(templatePath)}: Макс. совпадение = {matchPercentage:F1}%");
#endif

    // 6. Проверяем, превысил ли результат установленный порог точности
    if (maxVal >= threshold)
    {
        int offsetX = searchArea?.X ?? 0;
        int offsetY = searchArea?.Y ?? 0;

        // Вычисляем координаты центра найденного объекта на исходном полном скриншоте
        int centerX = offsetX + maxLoc.X + (matTemplate.Width / 2);
        int centerY = offsetY + maxLoc.Y + (matTemplate.Height / 2);

        // Корректно освобождаем память региона перед возвратом результата
        if (searchArea.HasValue) croppedScreen.Dispose();

        return new OpenCvSharp.Point(centerX, centerY);
    }

    // Освобождаем память региона перед выходом, если объект не был найден
    if (searchArea.HasValue) croppedScreen.Dispose();

    return null;
}






        /// <summary>
        /// Делает скриншот целевого окна и возвращает его напрямую в формате матрицы OpenCV (Mat).
        /// </summary>
        /// <param name="hWnd">Дескриптор окна эмулятора.</param>
        /// <returns>Матрица <see cref="OpenCvSharp.Mat"/> с изображением, или null в случае ошибки.</returns>
        static OpenCvSharp.Mat? CaptureWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return null;

            if (!WinAPI.GetWindowRect(hWnd, out WinAPI.RECT rect)) return null;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0) return null;

            IntPtr hdcWindow = WinAPI.GetDC(hWnd);
            IntPtr hdcMem = WinAPI.CreateCompatibleDC(hdcWindow);
            IntPtr hBitmap = WinAPI.CreateCompatibleBitmap(hdcWindow, width, height);
            IntPtr hOldBmp = WinAPI.SelectObject(hdcMem, hBitmap);

            try
            {
                // ИСПРАВЛЕНО: Добавлен префикс WinAPI.
                WinAPI.PrintWindow(hWnd, hdcMem, WinAPI.PW_RENDERFULLCONTENT);

                // ИСПРАВЛЕНО: Ссылка на структуру теперь указывает на WinAPI.BITMAPINFOHEADER
                WinAPI.BITMAPINFOHEADER bmi = new()
                {
                    biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WinAPI.BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0
                };

                byte[] rawPixels = new byte[width * height * 4];

                // ИСПРАВЛЕНО: Добавлен префикс WinAPI.
                WinAPI.GetDIBits(hdcMem, hBitmap, 0, (uint)height, rawPixels, ref bmi, 0);

                OpenCvSharp.Mat mat = new(height, width, OpenCvSharp.MatType.CV_8UC4);
                System.Runtime.InteropServices.Marshal.Copy(rawPixels, 0, mat.Data, rawPixels.Length);

                return mat;
            }
            finally
            {
                // ИСПРАВЛЕНО: Ко всем вызовам очистки ресурсов GDI добавлены префиксы WinAPI.
                WinAPI.SelectObject(hdcMem, hOldBmp);
                WinAPI.DeleteObject(hBitmap);
                WinAPI.DeleteDC(hdcMem);
                _ = WinAPI.ReleaseDC(hWnd, hdcWindow);
            }
        }


    }

        #endregion

}


