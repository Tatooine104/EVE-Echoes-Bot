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

        // --- Константы мыши (Windows Messages) ---
        private const uint WM_LBUTTONDOWN = 0x0201; // Нажатие левой кнопки мыши
        private const uint WM_LBUTTONUP = 0x0202;   // Отпускание левой кнопки мыши

        // --- Константы графического интерфейса и DWM ---
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9; // Флаг получения реальных границ окна в Win 10/11
        private const uint PW_RENDERFULLCONTENT = 2;       // Флаг полного рендеринга содержимого (DirectX/OpenGL эмуляторы)

        // --- Глобальные утилиты ---
        private static readonly Random _random = new();

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
                    AliChatWarning(); // Вызываем оповещение (сработает 1 раз до возврата в true)
                }
                else
                {
                    // Сюда попадают случаи: 
                    // - инициализация (из null в true или false)
                    // - повторные удержания статуса (из false в false, из true в true)
                    // - восстановление системы (из false в true)
                    _isSave = value;
                }
            }
        }

        #endregion

        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
        // - + - + - + - + - |  Основная программа   | - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

        #region Main


        /*static void Main()
        {
            // Фиктивная строка для предотвращения автоудаления using в VS Code
            _ = typeof(OpenCvSharp.Mat);

            Console.Title = "Тестирование статуса безопасности (IsSave)";
            ConsolePrint("Запуск теста комплексного поиска шаблонов...", ConsoleColor.Cyan);

            // 1. Загружаем настройки из файла
            BotConfig config = ConfigManager.Load();

            // Берем первый доступный аккаунт для теста
            WindowSettings? testAccount = config.Accounts.FirstOrDefault();

            if (testAccount == null)
            {
                ConsolePrint("[Ошибка] В конфигурации нет доступных аккаунтов!", ConsoleColor.Red);
                Console.ReadKey();
                return;
            }

            // 2. Находим и масштабируем окно под 1280x720
            IntPtr hWnd = GetWindow(testAccount);

            if (hWnd != IntPtr.Zero)
            {
                ConsolePrint($"Окно '{testAccount.WindowTitle}' успешно найдено и подготовлено.", ConsoleColor.Gray);
                ConsolePrint("Ожидание 1 секунды для стабильной перерисовки...", ConsoleColor.DarkGray);
                System.Threading.Thread.Sleep(1000);

                // 3. Делаем кроссплатформенный скриншот окна
                ConsolePrint("Выполняю захват экрана...", ConsoleColor.Gray);
                using OpenCvSharp.Mat? screenshot = CaptureWindow(hWnd);

                if (screenshot?.Empty() is false)
                {
                    ConsolePrint("\n--- Запуск анализа экрана ---", ConsoleColor.Gray);

                    // 4. Вызываем новый метод проверки
                    CheckSecurityStatus(screenshot);

                    // 5. Финальная визуальная проверка глобальной переменной
                    ConsolePrint("\n--- Проверка глобального состояния бота ---", ConsoleColor.Gray);
                    if (IsSave)
                    {
                        ConsolePrint($"[СТАТУС ИЗ ГЛОБАЛЬНОЙ ПЕРЕМЕННОЙ] IsSave = {IsSave}. Все элементы найдены. Бот может продолжать работу.", ConsoleColor.Green);
                    }
                    else
                    {
                        ConsolePrint($"[СТАТУС ИЗ ГЛОБАЛЬНОЙ ПЕРЕМЕННОЙ] IsSave = {IsSave}. Обнаружен дефицит элементов! Бот должен среагировать.", ConsoleColor.Magenta);
                    }
                }
                else
                {
                    ConsolePrint("[Ошибка] Метод CaptureWindow вернул пустую матрицу. Проверьте эмулятор.", ConsoleColor.Red);
                }
            }

            //ConsolePrint("\nТест завершен. Нажми любую клавишу...");
            //Console.ReadKey();
        }*/


static void Main(string[] args)
{
    // 1. Загружаем настройки из файла
    BotConfig config = ConfigManager.Load();

    // Берем первый доступный аккаунт для теста
    WindowSettings? testAccount = config.Accounts.FirstOrDefault();

    // Проверяем, что аккаунт успешно загружен
    if (testAccount == null)
    {
        ConsolePrint("Ошибка: testAccount равен null! В конфиге нет аккаунтов.", ConsoleColor.Red);
        return;
    }

    // 2. Получаем дескриптор (hWnd) нужного окна
    nint hWnd = GetWindow(testAccount);

    // Проверяем, что окно найдено
    if (hWnd == IntPtr.Zero)
    {
        ConsolePrint("Ошибка: Окно целевой программы не найдено.", ConsoleColor.Red);
        return;
    }

    // 3. Вызываем метод захвата экрана
    using Mat? screenshot = CaptureWindow(hWnd);

    // 4. Проверяем, что скриншот успешно получен (не null) и не пустой
    if (screenshot != null && screenshot.Width > 0 && screenshot.Height > 0)
    {
        ConsolePrint($"Скриншот успешно получен! Размер: {screenshot.Width}x{screenshot.Height}", ConsoleColor.Green);

        // ================= НАЧАЛО ПРОВЕРКИ ДВУХ ИЗОБРАЖЕНИЙ =================
        
        // Задаем пути к двум картинкам, которые нужно найти
        string imgPath1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "imgAliChatRUS.png");
        string imgPath2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "imgAliChatENG.png");

        // Проверяем физическое существование файлов картинок на диске
        if (!File.Exists(imgPath1) || !File.Exists(imgPath2))
        {
            ConsolePrint("Ошибка: Один или оба файла шаблонов отсутствуют в папке images!", ConsoleColor.Red);
            return;
        }

        // Задаем область поиска (координаты, которые использовались ранее)
        OpenCvSharp.Rect searchRegion = new OpenCvSharp.Rect(0, 20, 500, 700);
        // Ищем первое изображение
        OpenCvSharp.Point? found1 = FindTemplateInRegion(screenshot, imgPath1, searchRegion, 0.75);
        // Ищем второе изображение
        OpenCvSharp.Point? found2 = FindTemplateInRegion(screenshot, imgPath2, searchRegion, 0.75);

        // Логика проверки: найдены ли ОБА изображения
        if (found1.HasValue && found2.HasValue)
        {
            ConsolePrint("Успех: Оба изображения успешно найдены на скриншоте!", ConsoleColor.Green);
            ConsolePrint($"Картинка {Path.GetFileName(imgPath1)} в точке: {found1.Value.X}, {found1.Value.Y}", ConsoleColor.Gray);
            ConsolePrint($"Картинка {Path.GetFileName(imgPath2)} в точке: {found2.Value.X}, {found2.Value.Y}", ConsoleColor.Gray);
        }
        else
        {
            ConsolePrint("Внимание: Одно или оба изображения отсутствуют на экране.", ConsoleColor.Yellow);
            
            // Имена файлов подтягиваются динамически из переменных через интерполяцию строк $""
            if (!found1.HasValue) 
                ConsolePrint($"-> Не найдено: {Path.GetFileName(imgPath1)}", ConsoleColor.DarkYellow);
                
            if (!found2.HasValue) 
                ConsolePrint($"-> Не найдено: {Path.GetFileName(imgPath2)}", ConsoleColor.DarkYellow);
        }

        // ================= КОНЕЦ ПРОВЕРКИ ДВУХ ИЗОБРАЖЕНИЙ =================

        // (Опционально) Для теста сохраняем скриншот на диск
        Cv2.ImWrite("debug_screenshot.png", screenshot);
    }
    else
    {
        ConsolePrint("Ошибка: Не удалось сделать скриншот окна.", ConsoleColor.Red);
    }
}




        #endregion

        #region EVE Echoes Methods 

        /// <summary>
        /// Проверяет экран на наличие обязательных элементов интерфейса.
        /// Устанавливает IsSave = true только если найдены все три шаблона.
        /// </summary>
        /// <param name="screenshot">Текущий снимок экрана эмулятора.</param>
        static void CheckSecurityStatus(OpenCvSharp.Mat screenshot)
        {
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

                OpenCvSharp.Point? foundPoint = FindTemplateInRegion(screenshot, fullTemplatePath, searchRegion, 0.85);

                if (!foundPoint.HasValue)
                {
                    currentStatus = false;
                }
            }

            // При присвоении сработает логика внутри свойства set { ... }
            IsSave = currentStatus;

            if (IsSave == false)
            {
                ConsolePrint("=== ВНИМАНИЕ! Обнаружена опасность!", ConsoleColor.Magenta);
            }
        }


static void AliChatWarning()
{

// TODO: Доделать

    // IntPtr.Zero используется, если клики идут по всему экрану. 
    // Если нужно кликать в конкретное окно, замените IntPtr.Zero на дескриптор этого окна.
    IntPtr targetWindow = IntPtr.Zero; 

    ConsolePrint("--> Запуск цепочки кликов AliChatWarning...", ConsoleColor.Yellow);

    // 1. Первый клик (пример: открытие окна чата)
    SmartClick(targetWindow, 25, 600);

    // 2. Второй клик (пример: выбор текстового поля)
    SmartClick(targetWindow, 150, 250);

    // 3. Третий клик
    SmartClick(targetWindow, 200, 300);

    // 4. Четвертый клик
    SmartClick(targetWindow, 250, 350);

    // 5. Пятый клик
    SmartClick(targetWindow, 300, 400);

    // 6. Шестой клик
    SmartClick(targetWindow, 350, 450);

    // 7. Седьмой клик (пример: кнопка отправки или закрытия)
    SmartClick(targetWindow, 400, 500);

    //ConsolePrint("--> Цепочка кликов AliChatWarning успешно завершена.", ConsoleColor.Green);
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

            using var croppedScreen = searchArea.HasValue ? new Mat(screen!, searchArea.Value) : screen!.Clone();
            using var matTemplate = Cv2.ImRead(templatePath, ImreadModes.Color);

            if (matTemplate.Empty())
            {
                Console.WriteLine($"[Ошибка] Не удалось загрузить шаблон: {templatePath}");
                return null;
            }

            if (matTemplate.Width > croppedScreen.Width || matTemplate.Height > croppedScreen.Height)
            {
                Console.WriteLine($"[Ошибка] Шаблон {templatePath} ({matTemplate.Width}x{matTemplate.Height}) больше области поиска ({croppedScreen.Width}x{croppedScreen.Height})!");
                return null;
            }

            using Mat grayScreen = new();
            using Mat grayTemplate = new();
            Cv2.CvtColor(croppedScreen, grayScreen, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(matTemplate, grayTemplate, ColorConversionCodes.BGR2GRAY);

            using Mat result = new();
            Cv2.MatchTemplate(grayScreen, grayTemplate, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

#if DEBUG
            // Этот блок выполнится ТОЛЬКО в режиме Debug
            double matchPercentage = maxVal * 100;
            Console.WriteLine($"   -> Диагностика {templatePath}: Макс. совпадение = {matchPercentage:F1}%");
#endif

            if (maxVal >= threshold)
            {
                int offsetX = searchArea?.X ?? 0;
                int offsetY = searchArea?.Y ?? 0;

                int centerX = offsetX + maxLoc.X + (matTemplate.Width / 2);
                int centerY = offsetY + maxLoc.Y + (matTemplate.Height / 2);

                return new OpenCvSharp.Point(centerX, centerY);
            }

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
        /// Находит окно по настройкам из конфигурации и подгоняет его видимую область под указанные размеры.
        /// </summary>
        /// <param name="settings">Объект настроек окна.</param>
        /// <returns>Дескриптор окна (IntPtr.Zero, если окно не найдено).</returns>
        static IntPtr GetWindow(WindowSettings settings) // <--- ПРОВЕРЬТЕ ЭТУ СТРОКУ
        {
            // Поиск дескриптора окна по заголовку из настроек
            IntPtr hWnd = FindWindow(null, settings.WindowTitle);

            if (hWnd != IntPtr.Zero)
            {
                // Изменяем размер, используя свойства переданного объекта settings
                if (ResizeWindow(hWnd, settings.TargetWidth, settings.TargetHeight))
                {
                    ConsolePrint($"GetWindow | Аккаунт: {settings.AccountName} | Успех: Окно '{settings.WindowTitle}' подогнано под размер {settings.TargetWidth}x{settings.TargetHeight}", ConsoleColor.Green);
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

    public class BotConfig
    {
        // Список настроек для каждого персонажа/окна
        public List<WindowSettings> Accounts { get; set; } = [];
    }



    /// <summary>
    /// Конфигурационные настройки для управления окном эмулятора.
    /// </summary>
    public class WindowSettings
    {
        public string AccountName { get; set; } = "Miner_V04K0";
        public string WindowTitle { get; set; } = "(BlueStacks_EVE.01)";

        // ПРОВЕРЬТЕ ЭТИ ДВЕ СТРОКИ: буквы T, W, H должны быть заглавными!
        public int TargetWidth { get; set; } = 1280;
        public int TargetHeight { get; set; } = 720;
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
                // Создаем дефолтный конфиг, соответствующий вашему новому стандарту
                var defaultConfig = new BotConfig
                {
                    Accounts =
                    [
                        new WindowSettings
                    {
                        AccountName = "Miner_V04K0",
                        WindowTitle = "BlueStacks_EVE.01",
                        TargetWidth = 1280,
                        TargetHeight = 720
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

}