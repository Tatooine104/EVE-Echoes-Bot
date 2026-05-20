using System.Runtime.InteropServices;
using System.Text.Json;

namespace LowMiner
{

    static partial class Program
    {
        #region WinAPI Imports

        /// <summary>
        /// Определяет координаты левого верхнего и правого нижнего углов прямоугольника.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT // ТЕПЕРЬ ЭТО КОРРЕКТНО, ТАК КАК МЫ ВНУТРИ КЛАССА
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// Содержит информацию о размерах и цветовом формате независимого от устройства растрового изображения (DIB).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        #endregion

        #region WinAPI Imports

        // === Поиск и управление окнами ===

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        /// <summary>
        /// Находит дескриптор окна по имени класса или заголовку.
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        /// <summary>
        /// Изменяет позицию и размеры указанного окна.
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "MoveWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

        /// <summary>
        /// Получает габариты окна (включая невидимые области Aero/теней).
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "GetWindowRect")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        /// <summary>
        /// Получает реальные физические атрибуты окна (например, границы без учета теней).
        /// </summary>
        [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static partial int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);


        // === Захват экрана и рендеринг (GDI / Контекст устройства) ===

        /// <summary>
        /// Получает контекст устройства (HDC) для всего экрана или конкретного окна.
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "GetDC")]
        private static partial IntPtr GetDC(IntPtr hWnd);

        /// <summary>
        /// Освобождает контекст устройства (HDC), возвращая память операционной системе.
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "ReleaseDC")]
        private static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        /// <summary>
        /// Создает контекст устройства в памяти (DC), совместимый с указанным устройством.
        /// </summary>
        [LibraryImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")]
        private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

        /// <summary>
        /// Создает растровое изображение (Bitmap), совместимое с указанным контекстом устройства.
        /// </summary>
        [LibraryImport("gdi32.dll", EntryPoint = "CreateCompatibleBitmap")]
        private static partial IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

        /// <summary>
        /// Выбирает объект в указанный контекст устройства (HDC). Новый объект заменяет старый.
        /// </summary>
        [LibraryImport("gdi32.dll", EntryPoint = "SelectObject")]
        private static partial IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        /// <summary>
        /// Копирует визуальное содержимое окна в указанный контекст устройства (HDC).
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "PrintWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        /// <summary>
        /// Получает биты указанного совместимого растрового изображения и копирует их в буфер.
        /// </summary>
        [LibraryImport("gdi32.dll", EntryPoint = "GetDIBits")]
        private static partial int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, byte[] lpvBits, ref BITMAPINFOHEADER lpbmi, uint usage);

        /// <summary>
        /// Удаляет логическое перо, кисть, шрифт, растровое изображение или палитру, освобождая ресурсы.
        /// </summary>
        [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteObject(IntPtr ho);

        /// <summary>
        /// Удаляет указанный контекст устройства (DC) из памяти.
        /// </summary>
        [LibraryImport("gdi32.dll", EntryPoint = "DeleteDC")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteDC(IntPtr hdc);


        // === Фоновая отправка событий (Клик/Ввод) ===

        /// <summary>
        /// Помещает сообщение в очередь сообщений связанного с окном потока без ожидания ответа.
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        #endregion

        #region Constants & Fields

        // 1. Создаем глобальный источник токена отмены
        private static CancellationTokenSource _cts = new CancellationTokenSource();

        // --- Константы мыши (Windows Messages) ---
        private const uint WM_LBUTTONDOWN = 0x0201; // Нажатие левой кнопки мыши
        private const uint WM_LBUTTONUP = 0x0202;   // Отпускание левой кнопки мыши

        // --- Константы графического интерфейса и DWM ---
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9; // Флаг получения реальных границ окна в Win 10/11
        private const uint PW_RENDERFULLCONTENT = 2;       // Флаг полного рендеринга содержимого (DirectX/OpenGL эмуляторы)

        // --- Глобальные утилиты ---
        private static readonly Random _random = new();

        // Глобальный список аккаунтов, для которых размер окна уже был успешно подогнан
        private static System.Collections.Generic.HashSet<string> _resizedAccounts = new();


        #endregion

        #region Bot params

        // 1. Используем bool? со значением null. Теперь начальное состояние «неизвестно».
        private static bool? _isSave = null; 

        public static bool? IsSave
        {
            get => _isSave;
            set
            {
                // Проверяем строгое изменение: старый статус был ТОЧНО true, а новый стал ТOЧНО false
                if (_isSave == true && value == false)
                {
                    _isSave = value; // Обновляем значение на false
                    ConsolePrint("=== ВНИМАНИЕ! Обнаружена опасность!", ConsoleColor.Magenta);
                    AliChatWarning(); // Вызываем оповещение (сработает 1 раз до возврата в true)
                }
                else
                {
                    // Сюда попадают случаи: 
                    // - инициализация (из null в true или false)
                    // - повторные удержания статуса (из false в false, из true в true)
                    // - восстановление системы (из false в true)
                    ConsolePrint("=== В системе безопастно.", ConsoleColor.Green);
                    _isSave = value;
                }
            }
        }

        #endregion

        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
        // - + - + - + - + - |  Основная программа   | - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

        #region Main


static void Main(string[] args)
    {
        ConsolePrint("=== Бот успешно запущен ===", ConsoleColor.Cyan);
        ConsolePrint("--> Нажмите [ESC] в любой момент для остановки скрипта.", ConsoleColor.DarkGray);

        // 2. Запускаем фоновый поток, который слушает клавиатуру
        Thread inputThread = new Thread(ListenForCancelKey) { IsBackground = true };
        inputThread.Start();

        // 3. Основной цикл теперь проверяет, не поступил ли сигнал отмены
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                // Каждый круг заново загружаем настройки из файла
                BotConfig config = ConfigManager.Load();
                WindowSettings? testAccount = config.Accounts.FirstOrDefault();

                if (testAccount == null)
                {
                    ConsolePrint("Ошибка цикла: В конфигурации нет доступных аккаунтов. Ожидание...", ConsoleColor.Red);
                    
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
                        ConsolePrint($"Предупреждение: Неизвестный или пустой режим скрипта '{scriptMode}'.", ConsoleColor.Yellow);
                        break;
                }
            }
            catch (Exception ex)
            {
                ConsolePrint($"Критическая ошибка в главном цикле: {ex.Message}", ConsoleColor.Red);
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
        ConsolePrint("=== Бот успешно остановлен. До свидания! ===", ConsoleColor.Cyan);
    }

    /// <summary>
    /// Метод постоянно работает в фоне и ждет нажатия клавиши Escape
    /// </summary>
    private static void ListenForCancelKey()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            // Проверяем, нажата ли клавиша на клавиатуре
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true); // intercept: true прячет символ Esc из консоли
                if (key.Key == ConsoleKey.Escape)
                {
                    ConsolePrint("\n[INFO] Получен сигнал отмены. Завершаем текущий круг и выходим...", ConsoleColor.Yellow);
                    _cts.Cancel(); // Посылаем сигнал отмены во все методы
                    break;
                }
            }
            Thread.Sleep(100); // Разгружаем процессор
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
        ConsolePrint("Ошибка CheckSecurityStatus: В конфигурации нет доступных аккаунтов.", ConsoleColor.Red);
        IsSave = false; // Нет настроек — система не может считаться безопасной
        return;
    }

    // 2. Получаем дескриптор окна приложения
    nint hWnd = GetWindow(testAccount);

    if (hWnd == IntPtr.Zero)
    {
        ConsolePrint("Ошибка CheckSecurityStatus: Окно целевой программы не найдено.", ConsoleColor.Red);
        IsSave = false;
        return;
    }

    // 3. Делаем снимок экрана и оборачиваем в using для автоматической очистки памяти C++
    OpenCvSharp.Mat? screenshot = CaptureWindow(hWnd);

    if (screenshot == null || screenshot.Empty() || screenshot.Width <= 0 || screenshot.Height <= 0)
    {
        ConsolePrint("Ошибка CheckSecurityStatus: Не удалось сделать скриншот окна.", ConsoleColor.Red);
        IsSave = false;
        screenshot?.Dispose(); // Освобождаем память вручную при ошибке
        return;
    }

    // 4. Логика проверки шаблонов интерфейса
    string[] templates = ["imgLocalCriminal.png", "imgLocalMinus.png", "imgLocalNeutral.png"];
    bool currentStatus = true;

    OpenCvSharp.Rect searchRegion = new OpenCvSharp.Rect(50, 250, 300, 420);

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
        ConsolePrint("=== ВНИМАНИЕ! Обнаружена опасность!", ConsoleColor.Magenta);
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
    ConsolePrint("--> Запуск цепочки кликов AliChatWarning...", ConsoleColor.Yellow);
#endif

    // 1. Первый клик: Открываем меню чатов
    SmartClick(targetWindow, 25, 600);

    // Пауза 500 мс, чтобы интерфейс игры/эмулятора успел обновиться после клика
    Thread.Sleep(500);

    // ================= НАЧАЛО АВТОНОМНОЙ ПРОВЕРКИ НА ВТОРОМ ШАГЕ =================

    // Получаем настройки и дескриптор окна для создания свежего скриншота
    BotConfig config = ConfigManager.Load();
    WindowSettings? testAccount = config.Accounts.FirstOrDefault();

    if (testAccount == null)
    {
        ConsolePrint("Ошибка AliChatWarning: Аккаунты в конфигурации не найдены. Прерывание!", ConsoleColor.Red);
        return;
    }

    nint hWnd = GetWindow(testAccount);
    if (hWnd == IntPtr.Zero)
    {
        ConsolePrint("Ошибка AliChatWarning: Целевое окно программы не найдено. Прерывание!", ConsoleColor.Red);
        return;
    }

    // Делаем актуальный снимок экрана после первого клика (классическое объявление)
    // ЭТУ СТРОКУ БОЛЬШЕ НЕ УДАЛИТ НИ ОДИН ПЛАГИН:
    OpenCvSharp.Mat? currentScreenshot = CaptureWindow(hWnd);

    if (currentScreenshot == null || currentScreenshot.Empty())
    {
        ConsolePrint("Ошибка AliChatWarning: Не удалось сделать свежий скриншот. Прерывание!", ConsoleColor.Red);
        currentScreenshot?.Dispose(); // Освобождаем память перед выходом
        return;
    }

    // Пути к шаблонам локализации чата
    string imgPath1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "imgAliChatENG.png");
    string imgPath2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "imgAliChatRUS.png");

    // Проверяем физическое наличие файлов картинок, чтобы избежать системных ошибок OpenCV
    if (!File.Exists(imgPath1) || !File.Exists(imgPath2))
    {
        ConsolePrint("Ошибка AliChatWarning: Файлы шаблонов чата отсутствуют на диске. Прерывание!", ConsoleColor.Red);
        currentScreenshot.Dispose(); // Освобождаем память перед выходом
        return;
    }

    // Область поиска элементов чата на экране
    OpenCvSharp.Rect searchRegion = new OpenCvSharp.Rect(5, 220, 300, 500);

    // Ищем шаблоны на обновленном кадре
    OpenCvSharp.Point? found1 = FindTemplateInRegion(currentScreenshot, imgPath1, searchRegion, 0.85);
    OpenCvSharp.Point? found2 = FindTemplateInRegion(currentScreenshot, imgPath2, searchRegion, 0.85);

    // Если ОБА изображения НЕ найдены — меню не открылось, прерываем выполнение
    if (!found1.HasValue && !found2.HasValue)
    {
#if DEBUG
        ConsolePrint($"--> [{Path.GetFileName(imgPath1)}] и [{Path.GetFileName(imgPath2)}] не найдены. Прерывание AliChatWarning!", ConsoleColor.Red);
#endif
        currentScreenshot.Dispose(); // Освобождаем память перед выходом
        return;
    }

    // Освобождаем память скриншота, так как дальше он нам больше не нужен (идут только клики)
    currentScreenshot.Dispose();

    // ================= КОНЕЦ АВТОНОМНОЙ ПРОВЕРКИ НА ВТОРОМ ШАГЕ =================

    // 2. Второй клик: Активируем чат альянса
    SmartClick(targetWindow, 50, 450);

    // 3. Третий клик: Открываем меню чата
    SmartClick(targetWindow, 365, 700);

    // 4. Четвертый клик: Открываем меню сообщений
    SmartClick(targetWindow, 1250, 700);

    // 5. Пятый клик: Открываем данные разведки
    SmartClick(targetWindow, 80, 400);

    // 6. Шестой клик: Выбираем сообщение "Scout"
    SmartClick(targetWindow, 300, 600);

    // 7. Седьмой клик: Нажимаем кнопку "Отправить"
    SmartClick(targetWindow, 450, 255);

    // 8. Восьмой клик: Закрыь чаты
    SmartClick(targetWindow, 625, 225);

#if DEBUG
    ConsolePrint("--> Цепочка кликов AliChatWarning успешно завершена.", ConsoleColor.Green);
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
        static OpenCvSharp.Mat? CaptureWindow(IntPtr hWnd) // <--- ДОБАВЛЕН ЗНАК '?'
        {
            if (hWnd == IntPtr.Zero) return null;

            if (!GetWindowRect(hWnd, out RECT rect)) return null;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0) return null;

            IntPtr hdcWindow = GetDC(hWnd);
            IntPtr hdcMem = CreateCompatibleDC(hdcWindow);
            IntPtr hBitmap = CreateCompatibleBitmap(hdcWindow, width, height);
            IntPtr hOldBmp = SelectObject(hdcMem, hBitmap);

            try
            {
                PrintWindow(hWnd, hdcMem, PW_RENDERFULLCONTENT);

                BITMAPINFOHEADER bmi = new()
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0
                };

                byte[] rawPixels = new byte[width * height * 4];
                GetDIBits(hdcMem, hBitmap, 0, (uint)height, rawPixels, ref bmi, 0);

                OpenCvSharp.Mat mat = new(height, width, OpenCvSharp.MatType.CV_8UC4);
                Marshal.Copy(rawPixels, 0, mat.Data, rawPixels.Length);

                return mat;
            }
            finally
            {
                SelectObject(hdcMem, hOldBmp);
                DeleteObject(hBitmap);
                DeleteDC(hdcMem);
                _ = ReleaseDC(hWnd, hdcWindow);
            }
        }





        /// <summary>
        /// Изменяет размер окна BlueStacks так, чтобы его рабочая область стала заданной ширины и высоты.
        /// </summary>
        static bool ResizeWindow(IntPtr hWnd, int targetWidth, int targetHeight)
        {
            if (hWnd == IntPtr.Zero) return false;

            int structSize = Marshal.SizeOf<RECT>();
            if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT realWindowRect, structSize) == 0)
            {
                // Панели BlueStacks (верхний заголовок и правое тулбар-меню)
                const int bluestacksToolbarWidth = 33;
                const int bluestacksHeaderHeight = 33;

                // Обычные тонкие рамки изменения размера Windows 10/11
                const int windowsFrameWidth = 2;
                const int windowsFrameHeight = 2;

                // Рассчитываем итоговый полный габарит окна
                int finalWindowWidth = targetWidth + bluestacksToolbarWidth + windowsFrameWidth;
                int finalWindowHeight = targetHeight + bluestacksHeaderHeight + windowsFrameHeight;

                // Меняем размер окна, оставляя его на прежних координатах X и Y
                return MoveWindow(hWnd, realWindowRect.Left, realWindowRect.Top, finalWindowWidth, finalWindowHeight, true);
            }

            return false;
        }


/// <summary>
/// Находит окно по настройкам из конфигурации и подгоняет его размеры только один раз за сессию.
/// </summary>
/// <param name="settings">Объект настроек окна.</param>
/// <returns>Дескриптор окна (IntPtr.Zero, если окно не найдено).</returns>
static IntPtr GetWindow(WindowSettings settings)
{
    // Поиск дескриптора окна по заголовку из настроек
    IntPtr hWnd = FindWindow(null, settings.WindowTitle);

    if (hWnd != IntPtr.Zero)
    {
        // 1. Проверяем наш глобальный массив (HashSet)
        if (_resizedAccounts.Contains(settings.AccountName))
        {
#if DEBUG
            // В режиме отладки пишем, что аккаунт уже подгонялся ранее
            ConsolePrint($"GetWindow | {settings.AccountName} | Окно уже подгонялось в этой сессии. Шаг пропущен.", ConsoleColor.DarkGray);
#endif
            return hWnd; // МГНОВЕННЫЙ ВЫХОД, ПОЛНЫЙ ПРОПУСК ЛЮБЫХ ПРОВЕРОК И РЕСАЙЗОВ
        }

        if (settings.Size == null)
        {
            ConsolePrint($"GetWindow | Ошибка: В конфиге аккаунта {settings.AccountName} отсутствует блок WindowSettings!", ConsoleColor.Red);
            return hWnd;
        }

        int targetW = settings.Size.TargetWidth;
        int targetH = settings.Size.TargetHeight;

        // 2. Если аккаунта нет в списке, выполняем подгонку размера
        if (ResizeWindow(hWnd, targetW, targetH))
        {
            ConsolePrint($"GetWindow | Аккаунт: {settings.AccountName} | Успех: Окно '{settings.WindowTitle}' подогнано под размер {targetW}x{targetH}", ConsoleColor.Green);
            
            // Запоминаем аккаунт в глобальный список, чтобы больше никогда его не трогать
            _resizedAccounts.Add(settings.AccountName);
            
            // Даем окну 300 мс на применение изменений в Windows
            Thread.Sleep(300);
        }
        else
        {
            ConsolePrint($"GetWindow | Аккаунт: {settings.AccountName} | Ошибка: Не удалось изменить размер окна.", ConsoleColor.Red);
        }
    }
    else
    {
        ConsolePrint($"GetWindow | Ошибка: Окно '{settings.WindowTitle}' для аккаунта {settings.AccountName} не найдено.", ConsoleColor.Red);
    }

    return hWnd;
}



        /// <summary>
        /// Генерирует случайную задержку в миллисекундах на основе заданного диапазона секунд.
        /// </summary>
        /// <param name="minSeconds">Минимальный порог задержки в секундах.</param>
        /// <param name="maxSeconds">Максимальный порог задержки в секундах.</param>
        /// <returns>Целое число миллисекунд для использования в Thread.Sleep.</returns>
        static int GetRandomDelayMs(int minSeconds = 1, int maxSeconds = 7)
        {
            // Рассчитываем случайное значение и переводим в миллисекунды
            return _random.Next(minSeconds, maxSeconds + 1) * 1000;
        }

        /// <summary>
        /// Выполняет имитацию человеческого клика: выдерживает паузу,
        /// добавляет случайное смещение к координатам и нажимает кнопку мыши.
        /// </summary>
        /// <param name="hWnd">Дескриптор окна для отправки сообщения.</param>
        /// <param name="x">Базовая координата X.</param>
        /// <param name="y">Базовая координата Y.</param>
        /// <param name="minSec">Минимальная задержка перед кликом (сек).</param>
        /// <param name="maxSec">Максимальная задержка перед кликом (сек).</param>
        /// <param name="offset">Максимальное отклонение от точки в пикселях (в обе стороны).</param>
        static void SmartClick(IntPtr hWnd, int x, int y, int minSec = 1, int maxSec = 5, int offset = 10)
        {
            // 1. Рандомная задержка перед действием
            int initialDelay = GetRandomDelayMs(minSec, maxSec);
#if DEBUG
            ConsolePrint($"SmartClick | Действие: Ожидание перед кликом {initialDelay} мс...", ConsoleColor.Cyan);
#endif
            Thread.Sleep(initialDelay);

            // 2. Расчет координат с плавающим смещением
            // Используем offset для задания диапазона [-offset, +offset]
            int finalX = x + _random.Next(-offset, offset + 1);
            int finalY = y + _random.Next(-offset, offset + 1);

            // 3. Подготовка данных и отправка клика
            IntPtr lParam = (IntPtr)((finalY << 16) | (finalX & 0xFFFF));

            // Нажатие (wParam 1 = левая кнопка)
            PostMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)1, lParam);

            // Короткая пауза удержания кнопки (30-100 мс) для реалистичности
            Thread.Sleep(_random.Next(30, 101));

            // Отпускание
            PostMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, lParam);

#if DEBUG
            ConsolePrint($"SmartClick | Действие: Клик в ({finalX}, {finalY}) со смещением {offset}", ConsoleColor.Yellow);
#endif
        }

        /// <summary>
        /// Выводит форматированное сообщение в консоль с указанным цветом.
        /// </summary>
        /// <param name="message">Текст сообщения.</param>
        /// <param name="color">Цвет текста (по умолчанию серый).</param>
        public static void ConsolePrint(string message, ConsoleColor color = ConsoleColor.Gray)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            Console.ResetColor();
        }



    }

        #endregion

#region CONFIG

    public class BotConfig
    {
        // Список настроек для каждого персонажа/окна
        public List<WindowSettings> Accounts { get; set; } = [];

    }



public class WindowSettings
{
    public string AccountName { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public string? Script { get; set; }

    // Указываем полный путь к родному атрибуту .NET. 
    // Он свяжет тег "WindowSettings" из JSON со свойством "Size" в коде без каких-либо using!
    [System.Text.Json.Serialization.JsonPropertyName("WindowSettings")]
    public TargetSize? Size { get; set; } 
}

public class TargetSize
{
    // Поля внутри JSON-блока WindowSettings
    public int TargetWidth { get; set; }
    public int TargetHeight { get; set; }
}





    public static class ConfigManager
    {
        private const string ConfigPath = "config.json";

        // Кэшируем настройки сериализации один раз для всего приложения
        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true // Полезно, если вы вручную правите JSON
        };


/// <summary>
/// Загружает конфигурацию бота из JSON-файла или создает конфигурацию по умолчанию, если файл отсутствует.
/// </summary>
/// <returns>Объект конфигурации <see cref="BotConfig"/>.</returns>
public static BotConfig Load()
{
    if (!File.Exists(ConfigPath))
    {
        // Создаем дефолтный конфиг, соответствующий вашей новой вложенной структуре
        var defaultConfig = new BotConfig
        {
            Accounts =
            [
                new WindowSettings
                {
                    AccountName = "Miner_V04K0",
                    WindowTitle = "BlueStacks_EVE.01",
                    Script = "LocalWatcher", // Сразу прописываем сценарий по умолчанию
                    
                    // ИСПРАВЛЕНО: Теперь инициализируем вложенный объект Size
                    Size = new TargetSize
                    {
                        TargetWidth = 1280,
                        TargetHeight = 720
                    }
                }
            ]
        };

        Save(defaultConfig);
        return defaultConfig;
    }

    try
    {
        string json = File.ReadAllText(ConfigPath);

        // ИСПРАВЛЕНО: Добавлен оператор '!' после закрывающей круглой скобки метода Deserialize
        return JsonSerializer.Deserialize<BotConfig>(json, _options)! ?? new();
    }
    catch (Exception ex)
    {
        Program.ConsolePrint($"[Ошибка] Не удалось прочитать конфиг: {ex.Message}", ConsoleColor.Red);
        return new();
    }
}


        public static void Save(BotConfig config)
        {
            try
            {
                // Используем те же закэшированные настройки
                string json = JsonSerializer.Serialize(config, _options);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Program.ConsolePrint($"[Ошибка] Не удалось сохранить конфиг: {ex.Message}", ConsoleColor.Red);
            }
        }
    }

#endregion


}