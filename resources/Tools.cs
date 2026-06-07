//using System.Drawing;
using System.Runtime.InteropServices;
using OpenCvSharp;
using System.Diagnostics;
using System.IO;
using Point = OpenCvSharp.Point;

namespace EVEEchoesBot.resources;


// [v] Проверить все методы и добавить новый метод Logger.Log() 
// [v] TODO 2026.05.30 Привести все тексты логгера к единому стилю 

public static class Tools
{

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Globals

    /// <summary>
    /// Глобальный генератор случайных чисел для симуляции задержек и действий пользователя.
    /// </summary>
    private static readonly Random _random = new();

    /// <summary>
    /// Потокобезопасное множество аккаунтов, для которых уже был изменен размер игрового окна.
    /// Использует ConcurrentDictionary вместо HashSet для предотвращения конфликтов при параллельной работе воркеров.
    /// </summary>
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _resizedAccounts = new();

    #endregion


// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region CaptureWindow

    /// <summary>
    /// Делает скриншот целевого окна и возвращает его в формате матрицы OpenCV (Mat).
    /// В режиме отладки (DEBUG) автоматически сохраняет снимок в папку проекта.
    /// </summary>
    /// <param name="hWnd">Дескриптор (Handle) целевого окна эмулятора.</param>
    /// <returns>Матрица <see cref="Mat"/> с изображением в формате BGRA (4 канала), или <c>null</c> в случае ошибки.</returns>
    public static Mat? CaptureWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            Logger.Log("Неверный дескриптор целевого окна.", LogType.Warning);
            return null;
        }

        // ИСПРАВЛЕНИЕ №1: Заменили GetWindowRect на GetClientRect!
        // Теперь rect содержит чистые размеры внутренней рабочей области Android эмулятора.
        if (!WinAPI.GetClientRect(hWnd, out WinAPI.RECT rect))
        {
            Logger.Log($"Не удалось получить геометрические клиентские размеры окна {hWnd}", LogType.Error);
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
            // ИСПРАВЛЕНИЕ №2: Передаем 0 вместо WinAPI.PW_RENDERFULLCONTENT
            // Это заставляет PrintWindow копировать ТОЛЬКО клиентскую область игры,
            // полностью отрезая внешнюю рамку, заголовок Windows и боковые кнопки.
            if (!WinAPI.PrintWindow(hWnd, hdcMem, 0))
            {
                Logger.Log("Функция захвата окна вернула ошибку при копировании графического буфера.", LogType.Warning);
            }

            // --- ВЕСЬ ВАШ ОСТАЛЬНОЙ КОД СТРУКТУРЫ BITMAPINFOHEADER И MARSHAL.COPY ОСТАЕТСЯ БЕЗ ИЗМЕНЕНИЙ ---
            WinAPI.BITMAPINFOHEADER bmi = new()
            {
                biSize = (uint)Marshal.SizeOf<WinAPI.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };

            byte[] rawPixels = new byte[width * height * 4];
            WinAPI.GetDIBits(hdcMem, hBitmap, 0, (uint)height, rawPixels, ref bmi, 0);

            mat = new Mat(height, width, MatType.CV_8UC4);
            Marshal.Copy(rawPixels, 0, mat.Data, rawPixels.Length);

    #if DEBUG
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string projectDir = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\"));
                string targetFolder = Path.Combine(projectDir, "DebugScreenshots");
                if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

                const string fileName = "debug_screenshot.png";
                Cv2.ImWrite(Path.Combine(targetFolder, fileName), mat);
                Logger.Log($"Чистый снимок клиентской области сохранен по пути '{fileName}'.", LogType.Test);
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
            mat?.Dispose();
            return null;
        }
        finally
        {
            WinAPI.SelectObject(hdcMem, hOldBmp);
            WinAPI.DeleteObject(hBitmap);
            WinAPI.DeleteDC(hdcMem);

            if (WinAPI.ReleaseDC(hWnd, hdcWindow) == 0)
            {
                Logger.Log("Не удалось освободить графический контекст устройства.", LogType.Warning);
            }
        }
    }



    #endregion


// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region FindTemplateInRegion

    /// <summary>
    /// Кроссплатформенный метод поиска изображения-шаблона в заданной области кадра.
    /// Выполняет сопоставление признаков в градациях серого с учетом смещения координат области поиска.
    /// </summary>
    /// <param name="screen">Матрица полного скриншота эмулятора (в формате BGR/BGRA).</param>
    /// <param name="templatePath">Абсолютный или относительный путь к графическому файлу-шаблону.</param>
    /// <param name="searchArea">Прямоугольная область ограничения поиска. Если <c>null</c> — сканируется весь кадр.</param>
    /// <param name="threshold">Порог точности совпадения от 0.0 (любое сходство) до 1.0 (идентичность). По умолчанию: 0.55.</param>
    /// <returns>Точка <see cref="Point"/> центра найденного объекта в координатах исходного кадра, или <c>null</c>, если объект не найден или произошла ошибка.</returns>
    public static Point? FindTemplateInRegion(
        Mat screen,
        string templatePath,
        Rect? searchArea = null,
        double threshold = 0.55)
    {
        if (screen?.Empty() is not false) return null;

        // Вырезаем область поиска, если она задана, иначе работаем с полным экраном
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

            // Переводим изображения в оттенки серого для ускорения вычислений
            using Mat grayScreen = new();
            using Mat grayTemplate = new();
            Cv2.CvtColor(croppedScreen, grayScreen, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(matTemplate, grayTemplate, ColorConversionCodes.BGR2GRAY);

            // Выполняем нормированное сопоставление коэффициентов корреляции (стандарт для ботов)
            using Mat result = new();
            Cv2.MatchTemplate(grayScreen, grayTemplate, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out Point maxLoc);

            // Проверяем, превысил ли результат установленный порог точности
            if (maxVal >= threshold)
            {
                // Рассчитываем глобальное смещение относительно полного экрана
                int offsetX = searchArea?.X ?? 0;
                int offsetY = searchArea?.Y ?? 0;

                // Вычисляем точку центра найденного объекта
                int centerX = offsetX + maxLoc.X + (matTemplate.Width / 2);
                int centerY = offsetY + maxLoc.Y + (matTemplate.Height / 2);

    #if DEBUG
                Logger.Log($"Поиск '{Path.GetFileName(templatePath)}', совпадение: {maxVal * 100:F1}%, локация: {maxLoc.X}х{maxLoc.Y}, центр: {centerX}х{centerY}.", LogType.Test);
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
            // Обязательно освобождаем память вырезанной подматрицы, чтобы не было утечек в цикле бота
            if (searchArea.HasValue)
            {
                croppedScreen.Dispose();
            }
        }
    }

    #endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ClampRegion

    /// <summary>
    /// Корректирует прямоугольную область (Rect) под фактические границы и размеры изображения.
    /// Предотвращает аварийное завершение работы OpenCV (индексы вне диапазона) при попытке вырезать область за краями кадра.
    /// </summary>
    /// <param name="region">Исходная прямоугольная область, требующая валидации.</param>
    /// <param name="maxWidth">Максимально допустимая ширина (обычно ширина полного скриншота эмулятора).</param>
    /// <param name="maxHeight">Максимально допустимая высота (обычно высота полного скриншота эмулятора).</param>
    /// <returns>Новый скорректированный объект <see cref="Rect"/>, гарантированно находящийся внутри границ кадра.</returns>
    public static Rect ClampRegion(Rect region, int maxWidth, int maxHeight)
    {
        // Ограничиваем начальные координаты X и Y, чтобы они не выходили за рамки [0 ... max-1]
        int x = Math.Max(0, Math.Min(region.X, maxWidth - 1));
        int y = Math.Max(0, Math.Min(region.Y, maxHeight - 1));

        // Корректируем ширину и высоту с учетом сдвига координат, чтобы область не усекалась некорректно
        int width = Math.Min(region.Width, maxWidth - x);
        int height = Math.Min(region.Height, maxHeight - y);

        return new Rect(x, y, width, height);
    }

    #endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region SmartClick

    /// <summary>
    /// Выполняет клик по экрану Android-устройства/эмулятора с помощью утилиты ADB.
    /// Включает симуляцию поведения человека: рандомизацию задержки, случайное смещение пикселей (анти-бан),
    /// автоматическое масштабирование под внутреннее разрешение Android и компенсацию заголовка окон Windows.
    /// </summary>
    /// <param name="x">Исходная координата X (обычно полученная из OpenCV).</param>
    /// <param name="y">Исходная координата Y (обычно полученная из OpenCV).</param>
    /// <param name="minSec">Минимальное время случайной задержки перед кликом (в секундах). По умолчанию: 1.</param>
    /// <param name="maxSec">Максимальное время случайной задержки перед кликом (в секундах). По умолчанию: 5.</param>
    /// <param name="offset">Радиус случайного разброса пикселей от центра клика для защиты от антикликеров. По умолчанию: 10.</param>
    /// <param name="adbPort">Сетевой порт для подключения к конкретному эмулятору по ADB. По умолчанию: 5565.</param>
    /// <param name="applyWinHeaderCorrection">Если <c>true</c>, компенсирует высоту стандартного заголовка окна Windows (-31px по оси Y). По умолчанию: <c>true</c>.</param>
    public static void SmartClick(
        int x,
        int y,
        int minSec = 1,
        int maxSec = 5,
        int offset = 10,
        int adbPort = 5565,
        bool applyWinHeaderCorrection = true)
    {
        // Симуляция паузы перед кликом
        if (minSec > 0 || maxSec > 0)
        {
            Thread.Sleep(GetRandomDelayMs(minSec, maxSec));
        }

        // КОРРЕКЦИЯ ОКНА WINDOWS: компенсируем 31 пиксель стандартной рамки/заголовка окна
        if (applyWinHeaderCorrection)
        {
            y -= 31;
        }

        // Рандомизация координат в пределах заданного смещения
        int finalX = x + _random.Next(-offset, offset + 1);
        int finalY = y + _random.Next(-offset, offset + 1);

        string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "adb.exe");


        if (!File.Exists(adbPath))
        {
            Logger.Log($"Файл 'adb.exe' не найден по пути '{adbPath}'.", LogType.Error);
            return;
        }

        string deviceTarget = $"127.0.0.1:{adbPort}";
        string argsConnect = $"connect {deviceTarget}";

        try
        {
            // 1. Подключение к ADB-интерфейсу эмулятора
            ProcessStartInfo psiConnect = new(adbPath, argsConnect) { CreateNoWindow = true, UseShellExecute = false };
            Process.Start(psiConnect)?.WaitForExit();

            // 2. УЗНАЕМ РЕАЛЬНОЕ РАЗРЕШЕНИЕ ЭМУЛЯТОРА ИЗНУТРИ ANDROID
            ProcessStartInfo psiSize = new(adbPath, $"-s {deviceTarget} shell wm size")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };

            var procSize = Process.Start(psiSize);
            string outputSize = procSize?.StandardOutput.ReadToEnd() ?? "";
            procSize?.WaitForExit();

            // Если разрешение отличается от стандартного 1280x720, динамически пересчитываем пропорции координат
            if (outputSize.Contains(':') && outputSize.Contains('x'))
            {
                string sizeStr = outputSize.Split(':')[1].Trim();
                string[] wAndH = sizeStr.Split('x');
                if (wAndH.Length == 2 && int.TryParse(wAndH[0], out int internalW) && int.TryParse(wAndH[1], out int internalH))
                {
                    if (internalW != 1280 && internalW > 0)
                    {
                        finalX = (int)(finalX * ((double)internalW / 1280.0));
                        finalY = (int)(finalY * ((double)internalH / 720.0));
                    }
                }
            }

            // 3. ОТПРАВЛЯЕМ КОМАНДУ НАЖАТИЯ (TAP)
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


// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region GetRandomDelayMs

    /// <summary>
    /// Генерирует случайную задержку в миллисекундах на основе заданного диапазона в секундах.
    /// Конвертация границ в миллисекунды происходит до генерации случайного числа, что обеспечивает высокую точность разброса.
    /// </summary>
    /// <param name="minSeconds">Минимальный порог задержки в секундах. По умолчанию: 1.</param>
    /// <param name="maxSeconds">Максимальный порог задержки в секундах. По умолчанию: 7.</param>
    /// <returns>Случайное количество миллисекунд (целое число), готовое для использования в методах ожидания вроде <see cref="Thread.Sleep(int)"/>.</returns>
    public static int GetRandomDelayMs(int minSeconds = 1, int maxSeconds = 7)
    {
        // Защита от инверсии параметров (если случайно перепутали min и max местами при вызове)
        if (minSeconds > maxSeconds)
        {
            (minSeconds, maxSeconds) = (maxSeconds, minSeconds);
        }

        // Переводим границы диапазона из секунд в миллисекунды
        int minMs = minSeconds * 1000;
        int maxMs = maxSeconds * 1000;

        // Возвращаем случайное число в миллисекундах с точностью до 1 мс (включая верхнюю границу)
        return _random.Next(minMs, maxMs + 1);
    }

    #endregion


// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region GetWindow

    /// <summary>
    /// Находит дескриптор (Handle) целевого окна эмулятора по его заголовку.
    /// Если окно обнаружено впервые в текущей сессии, автоматически изменяет его геометрические размеры
    /// под целевое разрешение и кэширует состояние для предотвращения повторной коррекции.
    /// </summary>
    /// <param name="settings">Объект конфигурации аккаунта <see cref="AccSettings"/>, содержащий заголовок окна и параметры целевого размера.</param>
    /// <returns>Дескриптор окна <see cref="IntPtr"/>, или <see cref="IntPtr.Zero"/>, если окно не было найдено.</returns>
    public static IntPtr GetWindow(AccSettings settings)
    {
        // Ищем окно эмулятора по названию в Windows
        IntPtr hWnd = WinAPI.FindWindow(null, settings.WindowTitle);

        if (hWnd == IntPtr.Zero)
        {
            Logger.Log($"[{settings.Name}] Окно '{settings.WindowTitle}' не найдено.", LogType.Error);
            return IntPtr.Zero;
        }

        // Проверяем, настраивались ли размеры этого окна ранее в текущей сессии
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

        // Пытаемся изменить размеры окна под стандарты бота
        if (ResizeWindow(hWnd, targetW, targetH))
        {
            Logger.Log($"[{settings.Name}] Размеры окна скорректированы под разрешение {targetW}x{targetH}.", LogType.Test);

            // Безопасно добавляем имя аккаунта в потокобезопасный словарь-кэш (0 — минимальная byte-заглушка)
            _resizedAccounts.TryAdd(settings.Name, 0);

            // Небольшая задержка, чтобы ОС успела применить новые размеры окна до первого скриншота
            Thread.Sleep(300);
        }
        else
        {
            Logger.Log($"[{settings.Name}] Не удалось изменить геометрические размеры окна эмулятора.", LogType.Warning);
        }

        return hWnd;
    }

    #endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region ResizeWindow

    /// <summary>
    /// Изменяет геометрические размеры окна BlueStacks таким образом, чтобы его полезная рабочая область
    /// соответствовала строго заданным значениям ширины и высоты.
    /// Автоматически учитывает габариты бокового тулбара, заголовка приложения и невидимых аэро-рамок Windows 10/11.
    /// </summary>
    /// <param name="hWnd">Дескриптор (Handle) целевого окна эмулятора.</param>
    /// <param name="targetWidth">Желаемая чистая ширина рабочей области внутри эмулятора.</param>
    /// <param name="targetHeight">Желаемая чистая высота рабочей области внутри эмулятора.</param>
    /// <returns><c>true</c>, если размер окна успешно изменен операционной системой; иначе <c>false</c>.</returns>
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

        // Рассчитываем итоговый полный габарит окна для API MoveWindow
        int finalWindowWidth = targetWidth + BluestacksToolbarWidth + WindowsFrameWidth;
        int finalWindowHeight = targetHeight + BluestacksHeaderHeight + WindowsFrameHeight;

        // Изменяем размер окна, сохраняя его текущую позицию на экране пользователя
        bool success = WinAPI.MoveWindow(hWnd, currentRect.Left, currentRect.Top, finalWindowWidth, finalWindowHeight, true);

        if (!success)
        {
            Logger.Log("Функция изменения геометрии окна вернула ошибку.", LogType.Warning);
        }

        return success;
    }

    #endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

}
