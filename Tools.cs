//using System.Drawing;
using System.Runtime.InteropServices;
using OpenCvSharp;


namespace EVEEchoesBot
{

// [v] Проверить все методы и добавить новый метод Logger.Log() 

    public static class Tools
    {

#region Globals

        // --- Глобальные утилиты ---
        private static readonly Random _random = new();

        // Глобальный список аккаунтов, для которых размер окна уже был успешно подогнан
        public static readonly System.Collections.Generic.HashSet<string> _resizedAccounts = [];

#endregion

#region CaptureWindow

        /// <summary>
        /// Делает скриншот целевого окна и возвращает его в формате матрицы OpenCV (Mat).
        /// </summary>
        /// <param name="hWnd">Дескриптор окна эмулятора.</param>
        /// <returns>Матрица <see cref="Mat"/> с изображением, или null в случае ошибки.</returns>
        public static Mat? CaptureWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                Logger.Log("Неверный дескриптор окна (IntPtr.Zero)", LogType.Warning);
                return null;
            }

            if (!WinAPI.GetWindowRect(hWnd, out WinAPI.RECT rect))
            {
                Logger.Log($"Не удалось получить размеры окна для hWnd: {hWnd}", LogType.Error);
                return null;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0)
            {
                Logger.Log($"Некорректные размеры окна: {width}x{height}", LogType.Warning);
                return null;
            }

            // Инициализация контекстов устройств (GDI)
            IntPtr hdcWindow = WinAPI.GetDC(hWnd);
            IntPtr hdcMem = WinAPI.CreateCompatibleDC(hdcWindow);
            IntPtr hBitmap = WinAPI.CreateCompatibleBitmap(hdcWindow, width, height);
            IntPtr hOldBmp = WinAPI.SelectObject(hdcMem, hBitmap);

            Mat? mat = null;

            try
            {
                // Рендеринг содержимого окна в контекст памяти
                if (!WinAPI.PrintWindow(hWnd, hdcMem, WinAPI.PW_RENDERFULLCONTENT))
                {
                    Logger.Log("WinAPI.PrintWindow вернул ошибку при захвате кадра", LogType.Warning);
                }

                // Настройка структуры BITMAPINFOHEADER (отрицательная высота переворачивает изображение правильно)
                WinAPI.BITMAPINFOHEADER bmi = new()
                {
                    biSize = (uint)Marshal.SizeOf<WinAPI.BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0
                };

                // Извлечение пикселей в массив
                byte[] rawPixels = new byte[width * height * 4];
                WinAPI.GetDIBits(hdcMem, hBitmap, 0, (uint)height, rawPixels, ref bmi, 0);

                // Создание матрицы OpenCV (BGRA, 4 канала) и заполнение данными
                mat = new Mat(height, width, MatType.CV_8UC4);
                Marshal.Copy(rawPixels, 0, mat.Data, rawPixels.Length);

                return mat;
            }
            catch (Exception ex)
            {
                Logger.Log($"Критическая ошибка при создании скриншота: {ex.Message}", LogType.Error);

                // Защита от утечки памяти: если матрица была создана, но упал Marshal.Copy
                mat?.Dispose();
                return null;
            }
            finally
            {
                // Корректное и безопасное освобождение всех ресурсов GDI
                WinAPI.SelectObject(hdcMem, hOldBmp);
                WinAPI.DeleteObject(hBitmap);
                WinAPI.DeleteDC(hdcMem);

                // Проверяем успешность освобождения контекста устройства (DC)
                if (WinAPI.ReleaseDC(hWnd, hdcWindow) == 0)
                {
                    // Используем Logger.Log без указания аккаунта, он определится автоматически
                    Logger.Log($"Не удалось освободить контекст устройства (ReleaseDC) для hWnd: {hWnd}", LogType.Warning);
                }
            }
        }

#endregion

#region FindTemplateInRegion

        /// <summary>
        /// Кросплатформенный метод поиска шаблона на кадре по форме.
        /// </summary>
        /// <param name="screen">Матрица полного скриншота эмулятора.</param>
        /// <param name="templatePath">Путь к файлу-шаблону.</param>
        /// <param name="searchArea">Область поиска. Если null — поиск по всему кадру.</param>
        /// <param name="threshold">Порог точности (0.0 - 1.0).</param>
        /// <returns>Точка центра или null.</returns>
        public static Point? FindTemplateInRegion(
            Mat screen,
            string templatePath,
            Rect? searchArea = null,
            double threshold = 0.55)
        {
            if (screen?.Empty() is not false) return null;

            // Определяем рабочую область (весь экран или ROI под-матрица)
            Mat croppedScreen = searchArea.HasValue
                ? new Mat(screen, searchArea.Value)
                : screen;

            try
            {
                // Загружаем файл шаблона с диска
                using var matTemplate = Cv2.ImRead(templatePath, ImreadModes.Color);
                if (matTemplate.Empty())
                {
                    Logger.Log($"Не удалось загрузить шаблон: {templatePath}", LogType.Error);
                    return null;
                }

                // Проверяем, помещается ли шаблон в выбранную область поиска
                if (matTemplate.Width > croppedScreen.Width || matTemplate.Height > croppedScreen.Height)
                {
                    Logger.Log($"Шаблон {Path.GetFileName(templatePath)} ({matTemplate.Width}x{matTemplate.Height}) больше области поиска ({croppedScreen.Width}x{croppedScreen.Height})!", LogType.Warning);
                    return null;
                }

                // Переводим изображения в оттенки серого для повышения скорости и точности поиска
                using Mat grayScreen = new();
                using Mat grayTemplate = new();
                Cv2.CvtColor(croppedScreen, grayScreen, ColorConversionCodes.BGR2GRAY);
                Cv2.CvtColor(matTemplate, grayTemplate, ColorConversionCodes.BGR2GRAY);

                // Выполняем сопоставление шаблонов (Template Matching)
                using Mat result = new();
                Cv2.MatchTemplate(grayScreen, grayTemplate, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out Point maxLoc);

    #if DEBUG
                // Вывод отладочной информации через новый Test лог
                Logger.Log($"Диагностика {Path.GetFileName(templatePath)}: Макс. совпадение = {maxVal * 100:F1}%", LogType.Test);
    #endif

                // Проверяем, превысил ли результат установленный порог точности
                if (maxVal >= threshold)
                {
                    int offsetX = searchArea?.X ?? 0;
                    int offsetY = searchArea?.Y ?? 0;

                    // Вычисляем координаты центра найденного объекта на исходном полном скриншоте
                    int centerX = offsetX + maxLoc.X + (matTemplate.Width / 2);
                    int centerY = offsetY + maxLoc.Y + (matTemplate.Height / 2);

                    return new Point(centerX, centerY);
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.Log($"Ошибка при поиске шаблона {Path.GetFileName(templatePath)}: {ex.Message}", LogType.Error);
                return null;
            }
            finally
            {
                // Гарантированное освобождение памяти под-матрицы в одном месте без дублирования
                if (searchArea.HasValue)
                {
                    croppedScreen.Dispose();
                }
            }
        }

#endregion

#region SmartClick

        /// <summary>
        /// Аппаратно эмулирует клик мыши на уровне драйвера Windows с помощью SendInput.
        /// Оптимизирован для молниеносного и чёткого нажатия с защитой от антикликеров.
        /// </summary>
        /// <param name="hWnd">   Дескриптор окна эмулятора.                                       </param>
        /// <param name="x">      Координата X относительно окна.                                  </param>
        /// <param name="y">      Координата Y относительно окна.                                  </param>
        /// <param name="minSec"> Минимальное время случайной задержки перед кликом (в секундах).  </param>
        /// <param name="maxSec"> Максимальное время случайной задержки перед кликом (в секундах). </param>
        /// <param name="offset"> Радиус случайного разброса пикселей от центра клика.             </param>
        public static void SmartClick(
            IntPtr hWnd,
            int x,
            int y,
            int minSec = 1,
            int maxSec = 5,
            int offset = 10)
        {
            if (hWnd == IntPtr.Zero)
            {
                Logger.Log("Передан пустой дескриптор окна (IntPtr.Zero). Кликер отменен.", LogType.Warning);
                return;
            }

            // 1. Рандомная задержка перед действием (анти-детект система)
            if (minSec > 0 || maxSec > 0)
            {
                int initialDelay = GetRandomDelayMs(minSec, maxSec);
                Thread.Sleep(initialDelay);
            }

            // 2. Расчет координат внутри окна со случайным микро-смещением (имитация руки человека)
            int finalX = x + _random.Next(-offset, offset + 1);
            int finalY = y + _random.Next(-offset, offset + 1);

            // 3. Выводим окно эмулятора на передний план (SendInput работает только с активным окном)
            WinAPI.SetForegroundWindow(hWnd);

            // 4. Переводим относительные координаты окна в реальные экранные пиксели
            if (!WinAPI.GetWindowRect(hWnd, out WinAPI.RECT rect))
            {
                Logger.Log($"Не удалось получить координаты границ окна для hWnd: {hWnd}", LogType.Error);
                return;
            }

            int screenX = rect.Left + finalX;
            int screenY = rect.Top + finalY;

            // 5. Мгновенно перемещаем курсор в точку клика
            WinAPI.SetCursorPos(screenX, screenY);

            // Константы для Windows API SendInput
            const uint INPUT_MOUSE = 0;
            const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
            const uint MOUSEEVENTF_LEFTUP = 0x0004;

            // Инициализируем массив ввода для оптимизации выделения памяти
            WinAPI.INPUT[] inputs = new WinAPI.INPUT[2];

            // СТРУКТУРА №1: Нажатие левой кнопки мыши
            inputs[0] = new WinAPI.INPUT { type = INPUT_MOUSE };
            inputs[0].mi.dwFlags = MOUSEEVENTF_LEFTDOWN;

            // СТРУКТУРА №2: Отпускание левой кнопки мыши
            inputs[1] = new WinAPI.INPUT { type = INPUT_MOUSE };
            inputs[1].mi.dwFlags = MOUSEEVENTF_LEFTUP;

            int inputSize = Marshal.SizeOf<WinAPI.INPUT>();

            // 6. ВЫПОЛНЯЕМ КЛИК
            // Шаг А: Зажимаем кнопку мыши
            WinAPI.SendInput(1, [inputs[0]], inputSize);

            // Анатомическая пауза удержания кнопки (20-40 мс), чтобы игровой движок EVE Echoes гарантированно зафиксировал нажатие
            Thread.Sleep(_random.Next(20, 41));

            // Шаг Б: Отпускаем кнопку мыши
            WinAPI.SendInput(1, [inputs[1]], inputSize);

    #if DEBUG
            Logger.Log($"Выполнен клик по координатам экрана: ({screenX}, {screenY})", LogType.Test);
    #endif
        }

#endregion

#region GetRandomDelayMs

        /// <summary>
        /// Генерирует случайную задержку в миллисекундах на основе заданного диапазона секунд.
        /// Перевод в миллисекунды происходит до генерации, что обеспечивает истинный случайный разброс.
        /// </summary>
        /// <param name="minSeconds">Минимальный порог задержки в секундах.</param>
        /// <param name="maxSeconds">Максимальный порог задержки в секундах.</param>
        /// <returns>Случайное количество миллисекунд для использования в Thread.Sleep.</returns>
        public static int GetRandomDelayMs(int minSeconds = 1, int maxSeconds = 7)
        {
            // Защита от некорректных параметров (если перепутали min и max местами)
            if (minSeconds > maxSeconds)
            {
                (minSeconds, maxSeconds) = (maxSeconds, minSeconds);
            }

            // Переводим границы диапазона в миллисекунды
            int minMs = minSeconds * 1000;
            int maxMs = maxSeconds * 1000;

            // Возвращаем случайное число с точностью до 1 миллисекунды
            return _random.Next(minMs, maxMs + 1);
        }

#endregion



#region GetWindow

public static IntPtr GetWindow(WindowSettings settings)
{
    // Привязываем контекст логгера к текущему обрабатываемому аккаунту
    Program._currentAccount = settings;

    IntPtr hWnd = WinAPI.FindWindow(null, settings.WindowTitle);

    if (hWnd == IntPtr.Zero)
    {
        Logger.Log($"Окно '{settings.WindowTitle}' не найдено.", LogType.Error);
        return IntPtr.Zero;
    }

    if (_resizedAccounts.Contains(settings.Name))
    {
#if DEBUG
        Logger.Log("Окно уже подгонялось в этой сессии. Шаг пропущен.", LogType.Test);
#endif
        return hWnd;
    }

    if (settings.Size == null)
    {
        Logger.Log("В конфигурации отсутствует блок WindowSettings (Size)!", LogType.Error);
        return hWnd;
    }

    int targetW = settings.Size.TargetWidth;
    int targetH = settings.Size.TargetHeight;

    if (ResizeWindow(hWnd, targetW, targetH))
    {
        Logger.Log($"Окно подогнано под размер {targetW}x{targetH}", LogType.Test);
        _resizedAccounts.Add(settings.Name);
        Thread.Sleep(300);
    }
    else
    {
        Logger.Log("Не удалось изменить размер окна эмулятора.", LogType.Warning);
    }

    return hWnd;
}



#endregion


#region ResizeWindow

        /// <summary>
        /// Изменяет размер окна BlueStacks так, чтобы его рабочая область стала заданной ширины и высоты.
        /// Автоматически учитывает габариты бокового тулбара, заголовка и невидимых рамок Windows 10/11.
        /// </summary>
        /// <param name="hWnd">Дескриптор окна эмулятора.</param>
        /// <param name="targetWidth">Целевая ширина рабочей области.</param>
        /// <param name="targetHeight">Целевая высота рабочей области.</param>
        /// <returns>True, если размер окна успешно изменен; иначе false.</returns>
        public static bool ResizeWindow(IntPtr hWnd, int targetWidth, int targetHeight)
        {
            if (hWnd == IntPtr.Zero) return false;

            int structSize = Marshal.SizeOf<WinAPI.RECT>();
            WinAPI.RECT currentRect = new();

            // Пытаемся получить точные физические границы окна с учетом невидимых рамок Windows 10/11
            int dwmResult = WinAPI.DwmGetWindowAttribute(hWnd, WinAPI.DWMWA_EXTENDED_FRAME_BOUNDS, out currentRect, structSize);

            // ФОЛБЕК-СИСТЕМА: Если DWM вернул ошибку (не 0), откатываемся на классический GetWindowRect
            if (dwmResult != 0)
            {
                if (!WinAPI.GetWindowRect(hWnd, out currentRect))
                {
                    Logger.Log($"Не удалось определить текущие координаты окна для hWnd: {hWnd}", LogType.Error);
                    return false;
                }
            }

            // Константы интерфейса BlueStacks (верхний заголовок и правое тулбар-меню)
            const int BluestacksToolbarWidth = 33;
            const int BluestacksHeaderHeight = 33;

            // Стандартные рамки изменения размера окон ОС Windows
            const int WindowsFrameWidth = 2;
            const int WindowsFrameHeight = 2;

            // Рассчитываем итоговый полный габарит окна для MoveWindow
            int finalWindowWidth = targetWidth + BluestacksToolbarWidth + WindowsFrameWidth;
            int finalWindowHeight = targetHeight + BluestacksHeaderHeight + WindowsFrameHeight;

            // Изменяем размер окна, сохраняя его текущую позицию на экране
            bool success = WinAPI.MoveWindow(hWnd, currentRect.Left, currentRect.Top, finalWindowWidth, finalWindowHeight, true);

            if (!success)
            {
                Logger.Log($"WinAPI.MoveWindow вернул ошибку при изменении геометрии окна {hWnd}", LogType.Warning);
            }

            return success;
        }

#endregion

    }
}