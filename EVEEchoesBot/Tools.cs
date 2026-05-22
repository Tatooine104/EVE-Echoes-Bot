namespace EVEEchoesBot
{

    public static class Tools
    {


        // --- Глобальные утилиты ---
        private static readonly Random _random = new();

        // Глобальный список аккаунтов, для которых размер окна уже был успешно подогнан
        public static readonly System.Collections.Generic.HashSet<string> _resizedAccounts = [];

        /// <summary>
        /// Выводит форматированное сообщение в консоль с указанным цветом.
        /// </summary>
        /// <param name="message">Текст сообщения.</param>
        /// <param name="color">Цвет текста (по умолчанию серый).</param>
        public static void ConsolePrint(string message, ConsoleColor color = ConsoleColor.Gray)
        {
            // [ ] Сделать разделение логики по флагу DEBUG: Консоль / Лог
            // [ ] Сделать метод логирования
            // [ ] Сделать типы: Info / Success / Warning / Error           
            // [ ] Добавить в лог название аккаунта 
            // [ ] Добавить в лог название метода откуда вызван лог

            /*
            Начиная с C# 5.0, в язык добавлены специальные атрибуты [Caller...]. 
            Компилятор сам на этапе сборки подставляет в них данные. 
            Это работает мгновенно и вообще не нагружает процессор. 
            Для этого в параметры метода нужно добавить необязательные переменные со специальными атрибутами из пространства System.Runtime.CompilerServices:
            using System;
            using System.Runtime.CompilerServices;

            private static void CheckSecurityStatus(
                [CallerMemberName] string callerMethod = "", // Имя метода, который вызвал
                [CallerFilePath] string callerFile = "",     // Полный путь к файлу
                [CallerLineNumber] int callerLine = 0)        // Номер строки кода
            {
                // Метод выполняет свою работу, но теперь знает, откуда его дернули
                Tools.ConsolePrint($"Метод CheckSecurityStatus вызван из: {callerMethod} (Строка: {callerLine})", ConsoleColor.DarkGray);
                
                // Ваша обычная логика...
            }
            */

            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            Console.ResetColor();
        }


        /// <summary>
        /// Делает скриншот целевого окна и возвращает его напрямую в формате матрицы OpenCV (Mat).
        /// </summary>
        /// <param name="hWnd">Дескриптор окна эмулятора.</param>
        /// <returns>Матрица <see cref="OpenCvSharp.Mat"/> с изображением, или null в случае ошибки.</returns>
        public static OpenCvSharp.Mat? CaptureWindow(IntPtr hWnd)
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
        /// Аппаратно эмулирует клик мыши на уровне драйвера Windows с помощью SendInput.
        /// Оптимизирован для молниеносного и чёткого нажатия.
        /// </summary>
        public static void SmartClick(IntPtr hWnd, int x, int y, int minSec = 1, int maxSec = 5, int offset = 10)
        {
            if (hWnd == IntPtr.Zero) return;

            // 1. Рандомная задержка перед действием (если параметры 0, выполнится мгновенно)
            if (minSec > 0 || maxSec > 0)
            {
                int initialDelay = GetRandomDelayMs(minSec, maxSec);
                Thread.Sleep(initialDelay);
            }

            // 2. Расчет координат внутри окна со случайным смещением
            int finalX = x + _random.Next(-offset, offset + 1);
            int finalY = y + _random.Next(-offset, offset + 1);

            // 3. Выводим окно эмулятора на передний план
            WinAPI.SetForegroundWindow(hWnd);

            // 4. Переводим относительные координаты окна в реальные экранные пиксели
            if (!WinAPI.GetWindowRect(hWnd, out WinAPI.RECT rect))
            {
                Tools.ConsolePrint("SmartClick | Ошибка: Не удалось получить координаты границ окна.", ConsoleColor.Red);
                return;
            }

            int screenX = rect.Left + finalX;
            int screenY = rect.Top + finalY;

            // 5. Мгновенно перемещаем курсор в точку клика
            WinAPI.SetCursorPos(screenX, screenY);

            // Константы для SendInput
            const uint INPUT_MOUSE = 0;
            const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
            const uint MOUSEEVENTF_LEFTUP = 0x0004;

            // СТРУКТУРА №1: Нажатие левой кнопки
            WinAPI.INPUT inputDown = new() { type = INPUT_MOUSE };
            inputDown.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;

            // СТРУКТУРА №2: Отпускание левой кнопки
            WinAPI.INPUT inputUp = new() { type = INPUT_MOUSE };
            inputUp.mi.dwFlags = MOUSEEVENTF_LEFTUP;

            // 6. ВЫПОЛНЯЕМ КЛИК: отправляем массивы из 1 элемента по отдельности
            // Шаг А: Мгновенно зажимаем кнопку
            WinAPI.SendInput(1, [inputDown], System.Runtime.InteropServices.Marshal.SizeOf<WinAPI.INPUT>());

            // Маленькая реалистичная пауза удержания кнопки (всего 20-40 миллисекунд), чтобы игра успела зафиксировать клик
            Thread.Sleep(_random.Next(20, 41));

            // Шаг Б: Мгновенно отпускаем кнопку
            WinAPI.SendInput(1, [inputUp], System.Runtime.InteropServices.Marshal.SizeOf<WinAPI.INPUT>());

#if DEBUG
            Tools.ConsolePrint($"SmartClick | [ДРАЙВЕР] Быстрый клик отправлен в ({screenX}, {screenY})", ConsoleColor.Yellow);
#endif
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
        /// Находит окно по настройкам из конфигурации и подгоняет его размеры только один раз за сессию.
        /// </summary>
        /// <param name="settings">Объект настроек окна.</param>
        /// <returns>Дескриптор окна (IntPtr.Zero, если окно не найдено).</returns>
        public static IntPtr GetWindow(WindowSettings settings)
        {
            // ИСПРАВЛЕНО: Добавлен префикс WinAPI к вызову FindWindow
            IntPtr hWnd = WinAPI.FindWindow(null, settings.WindowTitle);

            if (hWnd != IntPtr.Zero)
            {
                // 1. Проверяем наш глобальный массив (HashSet)
                if (_resizedAccounts.Contains(settings.Name))
                {
#if DEBUG
                    // В режиме отладки пишем, что аккаунт уже подгонялся ранее
                    Tools.ConsolePrint($"GetWindow | {settings.Name} | Окно уже подгонялось в этой сессии. Шаг пропущен.", ConsoleColor.DarkGray);
#endif
                    return hWnd; // МГНОВЕННЫЙ ВЫХОД, ПОЛНЫЙ ПРОПУСК ЛЮБЫХ ПРОВЕРОК И РЕСАЙЗОВ
                }

                if (settings.Size == null)
                {
                    Tools.ConsolePrint($"GetWindow | Ошибка: В конфиге аккаунта {settings.Name} отсутствует блок WindowSettings!", ConsoleColor.Red);
                    return hWnd;
                }

                int targetW = settings.Size.TargetWidth;
                int targetH = settings.Size.TargetHeight;

                // 2. Если аккаунта нет в списке, выполняем подгонку размера
                if (ResizeWindow(hWnd, targetW, targetH))
                {
                    Tools.ConsolePrint($"GetWindow | Аккаунт: {settings.Name} | Успех: Окно '{settings.WindowTitle}' подогнано под размер {targetW}x{targetH}", ConsoleColor.Green);

                    // Запоминаем аккаунт в глобальный список, чтобы больше никогда его не трогать
                    _resizedAccounts.Add(settings.Name);

                    // Даем окну 300 мс на применение изменений в Windows
                    Thread.Sleep(300);
                }
                else
                {
                    Tools.ConsolePrint($"GetWindow | Аккаунт: {settings.Name} | Ошибка: Не удалось изменить размер окна.", ConsoleColor.Red);
                }
            }
            else
            {
                Tools.ConsolePrint($"GetWindow | Ошибка: Окно '{settings.WindowTitle}' для аккаунта {settings.Name} не найдено.", ConsoleColor.Red);
            }

            return hWnd;
        }

        /// <summary>
        /// Изменяет размер окна BlueStacks так, чтобы его рабочая область стала заданной ширины и высоты.
        /// </summary>
        /// <param name="hWnd">Дескриптор окна эмулятора.</param>
        /// <param name="targetWidth">Целевая ширина рабочей области.</param>
        /// <param name="targetHeight">Целевая высота рабочей области.</param>
        /// <returns>True, если размер окна успешно изменен, иначе false.</returns>
        static bool ResizeWindow(IntPtr hWnd, int targetWidth, int targetHeight)
        {
            if (hWnd == IntPtr.Zero) return false;

            // ИСПРАВЛЕНО: Указываем структуру из класса WinAPI
            int structSize = System.Runtime.InteropServices.Marshal.SizeOf<WinAPI.RECT>();

            // ИСПРАВЛЕНО: Добавлен префикс WinAPI. и указана структура WinAPI.RECT
            if (WinAPI.DwmGetWindowAttribute(hWnd, WinAPI.DWMWA_EXTENDED_FRAME_BOUNDS, out WinAPI.RECT realWindowRect, structSize) == 0)
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

                // ИСПРАВЛЕНО: Добавлен префикс WinAPI. для вызова MoveWindow
                // Меняем размер окна, оставляя его на прежних координатах X и Y
                return WinAPI.MoveWindow(hWnd, realWindowRect.Left, realWindowRect.Top, finalWindowWidth, finalWindowHeight, true);
            }

            return false;
        }

    }
}