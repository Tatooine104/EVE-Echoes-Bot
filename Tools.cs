//using System.Drawing;
using System.Runtime.InteropServices;
using OpenCvSharp;
using System.Diagnostics;
using System.IO;

// [v] TODO 2026.05.30 Привести все тексты логгера к единому стилю 

namespace EVEEchoesBot
{

// [v] Проверить все методы и добавить новый метод Logger.Log() 

    public static class Tools
    {

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region Globals

        // --- Глобальные утилиты ---
        private static readonly Random _random = new();

        // Используем ConcurrentDictionary вместо HashSet для полной потокобезопасности
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _resizedAccounts = new();


#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region CaptureWindow

        /// <summary>
        /// Делает скриншот целевого окна и возвращает его в формате матрицы OpenCV (Mat).
        /// В режиме отладки (DEBUG) автоматически сохраняет снимок в папку проекта.
        /// </summary>
        /// <param name="hWnd">Дескриптор окна эмулятора.</param>
        /// <returns>Матрица <see cref="Mat"/> с изображением, или null в случае ошибки.</returns>
        public static Mat? CaptureWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                Logger.Log("Неверный дескриптор целевого окна.", LogType.Warning);
                return null;
            }

            if (!WinAPI.GetWindowRect(hWnd, out WinAPI.RECT rect))
            {
                Logger.Log($"Не удалось получить геометрические размеры окна {hWnd}", LogType.Error);
                return null;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0)
            {
                Logger.Log($"Обнаружены некорректные размеры окна: {width}x{height}.", LogType.Warning);
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
                    Logger.Log("Функция захвата окна вернула ошибку при копировании графического буфера.", LogType.Warning);
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

#if DEBUG
                try
                {
                    // Находим путь к папке проекта (на 3 уровня выше bin/Debug/netX.X)
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string projectDir = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\"));

                    // Создаем папку DebugScreenshots внутри проекта
                    //string saveDir = Path.Combine(projectDir, "DebugScreenshots");
                    //Directory.CreateDirectory(saveDir);

                    // Формируем имя файла с временной меткой
                    const string fileName = "debug_screenshot.png";
                    string targetFolder = Path.Combine(projectDir, "DebugScreenshots");
                    string filePath = Path.Combine(targetFolder, fileName);

                    // Сохраняем матрицу на диск
                    Cv2.ImWrite(filePath, mat);
                    Logger.Log($"Снимок экрана сохранен по пути '{fileName}'.", LogType.Test);
                }
                catch (Exception dbgEx)
                {
                    Logger.Log($"Не удалось сохранить снимок экрана на диск: {dbgEx.Message}", LogType.Test);
                }
#endif

                return mat;
            }
            catch (Exception ex)
            {
                Logger.Log($"Критический сбой при захвате экрана: {ex.Message}", LogType.Error);

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
                    Logger.Log("Не удалось освободить графический контекст устройства.", LogType.Warning);
                }
            }
        }


#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

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

            Mat croppedScreen = searchArea.HasValue
                ? new Mat(screen, searchArea.Value)
                : screen;

            try
            {
                using var matTemplate = Cv2.ImRead(templatePath, ImreadModes.Color);
                if (matTemplate.Empty())
                {
                    Logger.Log($"Не удалось загрузить файл шаблона по пути '{templatePath}'.", LogType.Error);
                    return null;
                }

                if (matTemplate.Width > croppedScreen.Width || matTemplate.Height > croppedScreen.Height)
                {
                    Logger.Log($"Файл шаблона '{Path.GetFileName(templatePath)}' ({matTemplate.Width}x{matTemplate.Height}) превышает размеры области поиска ({croppedScreen.Width}x{croppedScreen.Height}).", LogType.Warning);
                    return null;
                }

                using Mat grayScreen = new();
                using Mat grayTemplate = new();
                Cv2.CvtColor(croppedScreen, grayScreen, ColorConversionCodes.BGR2GRAY);
                Cv2.CvtColor(matTemplate, grayTemplate, ColorConversionCodes.BGR2GRAY);

                using Mat result = new();
                Cv2.MatchTemplate(grayScreen, grayTemplate, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out Point maxLoc);

                // Проверяем, превысил ли результат установленный порог точности
                if (maxVal >= threshold)
                {
                    int offsetX = searchArea?.X ?? 0;
                    int offsetY = searchArea?.Y ?? 0;

                    int centerX = offsetX + maxLoc.X + (matTemplate.Width / 2);
                    int centerY = offsetY + maxLoc.Y + (matTemplate.Height / 2);

#if DEBUG
                    // Пишем в лог ТОЛЬКО если картинка действительно прошла порог и мы её возвращаем
                    Logger.Log($"Анализ шаблона '{Path.GetFileName(templatePath)}': совпадение = {maxVal * 100:F1}%, локация (X={maxLoc.X}, Y={maxLoc.Y}), центр (X={centerX}, Y={centerY}).", LogType.Test);
#endif
                    return new Point(centerX, centerY);
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.Log($"Сбой при сопоставлении шаблона '{Path.GetFileName(templatePath)}': {ex.Message}", LogType.Error);
                return null;
            }
            finally
            {
                if (searchArea.HasValue)
                {
                    croppedScreen.Dispose();
                }
            }
        }


#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region ClampRegion

        /// <summary>
        /// Корректирует прямоугольник под фактические размеры изображения, предотвращая вылет OpenCV.
        /// </summary>
        public static Rect ClampRegion(Rect region, int maxWidth, int maxHeight)
        {
            int x = Math.Max(0, Math.Min(region.X, maxWidth - 1));
            int y = Math.Max(0, Math.Min(region.Y, maxHeight - 1));

            int width = Math.Min(region.Width, maxWidth - x);
            int height = Math.Min(region.Height, maxHeight - y);

            return new Rect(x, y, width, height);
        }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region SmartClick

        /// <summary>
        /// Аппаратно эмулирует клик мыши на уровне драйвера Windows с помощью SendInput.
        /// Оптимизирован для молниеносного и чёткого нажатия с защитой от антикликеров.
        /// </summary>
        /// <param name="x">      Координата X относительно окна.                                  </param>
        /// <param name="y">      Координата Y относительно окна.                                  </param>
        /// <param name="minSec"> Минимальное время случайной задержки перед кликом (в секундах).  </param>
        /// <param name="maxSec"> Максимальное время случайной задержки перед кликом (в секундах). </param>
        /// <param name="offset"> Радиус случайного разброса пикселей от центра клика.             </param>

            public static void SmartClick(
                int x,
                int y,
                int minSec = 1,
                int maxSec = 5,
                int offset = 10,
                int adbPort = 5565)
            {
                if (minSec > 0 || maxSec > 0)
                {
                    Thread.Sleep(GetRandomDelayMs(minSec, maxSec));
                }

                int finalX = x + _random.Next(-offset, offset + 1);
                int finalY = y + _random.Next(-offset, offset + 1);

                string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe");

                if (!File.Exists(adbPath))
                {
                    Logger.Log($"Файл 'adb.exe' не найден по пути '{adbPath}'.", LogType.Error);
                    return;
                }

                string deviceTarget = $"127.0.0.1:{adbPort}";
                string argsConnect = $"connect {deviceTarget}";

                try
                {
                    // 1. Коннект к порту
                    ProcessStartInfo psiConnect = new(adbPath, argsConnect) { CreateNoWindow = true, UseShellExecute = false };
                    Process.Start(psiConnect)?.WaitForExit();

                    // 2. УЗНАЕМ РЕАЛЬНОЕ РАЗРЕШЕНИЕ ЭМУЛЯТОРА ИЗНУТРИ ANDROID
                    // Иногда BlueStacks снаружи имеет 1280x720, а внутри Android считает себя 1920x1080 (из-за DPI)
                    ProcessStartInfo psiSize = new(adbPath, $"-s {deviceTarget} shell wm size")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    };
                    var procSize = Process.Start(psiSize);
                    string outputSize = procSize?.StandardOutput.ReadToEnd() ?? "";
                    procSize?.WaitForExit();

                    // Если Android выдал кастомный размер (например, "Physical size: 1920x1080")
                    if (outputSize.Contains(':') && outputSize.Contains('x'))
                    {
                        string sizeStr = outputSize.Split(':')[1].Trim(); // "1920x1080"
                        string[] wAndH = sizeStr.Split('x');
                        if (wAndH.Length == 2 && int.TryParse(wAndH[0], out int internalW) && int.TryParse(wAndH[1], out int internalH))
                        {
                            // Если внутреннее разрешение не совпадает со стандартным 1280x720, пропорционально масштабируем клик
                            if (internalW != 1280 && internalW > 0)
                            {
                                finalX = (int)(finalX * ((double)internalW / 1280.0));
                                finalY = (int)(finalY * ((double)internalH / 720.0));
                            }
                        }
                    }

                    // 3. ОТПРАВЛЯЕМ КЛИК ЧЕРЕЗ АЛЬТЕРНАТИВНЫЙ СИНТАКСИС (input text / input keyevent / input tap)
                    // Мы склеим стандартный input tap и принудительную активацию окна
                    string argsTap = $"-s {deviceTarget} shell input tap {finalX} {finalY}";

                    ProcessStartInfo psiTap = new(adbPath, argsTap) { CreateNoWindow = true, UseShellExecute = false };
                    Process.Start(psiTap)?.WaitForExit();

            #if DEBUG
                    Logger.Log($"Отправка команды клика на устройство '{deviceTarget}': координаты (X={finalX}, Y={finalY}).", LogType.Test);
            #endif
                }
                catch (Exception ex)
                {
                    Logger.Log($"Сбой при отправке команды клика через ADB: {ex.Message}", LogType.Error);
                }
            }



#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

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

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region GetWindow

        public static IntPtr GetWindow(AccSettings settings)
        {
            IntPtr hWnd = WinAPI.FindWindow(null, settings.WindowTitle);

            if (hWnd == IntPtr.Zero)
            {
                Logger.Log($"[{settings.Name}] Окно '{settings.WindowTitle}' не найдено.", LogType.Error);
                return IntPtr.Zero;
            }

            // Проверяем, есть ли уже ключ в словаре
            if (_resizedAccounts.ContainsKey(settings.Name))
            {
        #if DEBUG
                Logger.Log($"[{settings.Name}] Окно уже настроено в текущей сессии. Коррекция размеров пропущена.", LogType.Test);
        #endif
                return hWnd;
            }

            if (settings.Size == null)
            {
                Logger.Log($"[{settings.Name}] В файле конфигурации отсутствует блок настроек размеров 'AccSettings'.", LogType.Error);
                return hWnd;
            }

            int targetW = settings.Size.TargetWidth;
            int targetH = settings.Size.TargetHeight;

            if (ResizeWindow(hWnd, targetW, targetH))
            {
                Logger.Log($"[{settings.Name}] Размеры окна скорректированы под разрешение {targetW}x{targetH}.", LogType.Test);

                // Безопасно добавляем имя аккаунта в словарь (0 — просто заглушка)
                _resizedAccounts.TryAdd(settings.Name, 0);

                Thread.Sleep(300);
            }
            else
            {
                Logger.Log($"[{settings.Name}] Не удалось изменить геометрические размеры окна эмулятора.", LogType.Warning);
            }

            return hWnd;
        }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

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
                    Logger.Log($"Не удалось определить текущие координаты целевого окна: {hWnd}", LogType.Error);
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
                Logger.Log("Функция изменения геометрии окна вернула ошибку.", LogType.Warning);
            }

            return success;
        }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

    }
}