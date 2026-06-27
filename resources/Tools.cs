//using System.Drawing;
using System.Runtime.InteropServices;
using OpenCvSharp;
using System.Diagnostics;
using System.IO;
using Point = OpenCvSharp.Point;

namespace EVEEchoesBot.resources;

public static class Tools
{

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Globals

    // /// <summary>
    // /// Глобальный генератор случайных чисел для симуляции задержек и действий пользователя.
    // /// </summary>
    // // BUG MEDIUM - Потенциальная проблема с многопоточностью при генерации случайных чисел. Класс `System.Random` по умолчанию НЕ является потокобезопасным. Если несколько фоновых потоков одновременно вызовут `_random.Next()` внутри `Tools.SmartClick` для расчета случайного смещения `offset` или секунд задержки, внутреннее состояние генератора может разрушиться, из-за чего он начнет бесконечно возвращать `0`. Это приведет к полной потере человекоподобного рандома (клики пойдут в одну точку). Для .NET 9+ правильнее использовать потокобезопасный `Random.Shared.Next()`.
    // private static readonly Random _random = new();

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
    /// <returns>Матрица <see cref="Mat"/> с изображением в формате BGRA (4 канала), или <c>null</c> в случае ошибки.</returns>// ИСПРАВЛЕНО HIGH: Переводим метод в async Task и пробрасываем CancellationToken из дерева
public static async Task<Mat?> CaptureWindowAsync(IntPtr hWnd, System.Threading.SemaphoreSlim? gdiSemaphore = null, CancellationToken token = default)
{
    if (hWnd == IntPtr.Zero) return null;

    IntPtr hdcWindow = IntPtr.Zero;
    IntPtr hdcMem = IntPtr.Zero;
    IntPtr hBitmap = IntPtr.Zero;
    IntPtr hOldBmp = IntPtr.Zero;
    Mat? mat = null;

    try
    {
        // ИСПРАВЛЕНО HIGH: Захватываем замок асинхронно с поддержкой токена отмены дерева!
        // Если такт дерева отменится по таймауту, этот вызов мгновенно выбросит OperationCanceledException
        // и поток ОПТИМАЛЬНО вернется в пул, вообще не создавая дедлоков и очередей заклинивания!
        if (gdiSemaphore != null)
        {
            await gdiSemaphore.WaitAsync(token).ConfigureAwait(false);
        }

        if (!WinAPI.GetClientRect(hWnd, out WinAPI.RECT rect)) return null;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return null;

        hdcWindow = WinAPI.GetDC(hWnd);
        hdcMem = WinAPI.CreateCompatibleDC(hdcWindow);
        hBitmap = WinAPI.CreateCompatibleBitmap(hdcWindow, width, height);
        hOldBmp = WinAPI.SelectObject(hdcMem, hBitmap);

        const uint SRCCOPY = 0x00CC0020;
        if (!WinAPI.BitBlt(hdcMem, 0, 0, width, height, hdcWindow, 0, 0, SRCCOPY)) return null;

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

        return mat;
    }
    catch
    {
        mat?.Dispose();
        return null;
    }
    finally
    {
        // Очищаем нативные дескрипторы
        if (hdcMem != IntPtr.Zero)
        {
            if (hOldBmp != IntPtr.Zero) WinAPI.SelectObject(hdcMem, hOldBmp);
            WinAPI.DeleteDC(hdcMem);
        }
        if (hBitmap != IntPtr.Zero) WinAPI.DeleteObject(hBitmap);
        if (hdcWindow != IntPtr.Zero)
        {
            _ = WinAPI.ReleaseDC(hWnd, hdcWindow);
        }

        // Гарантированно отпускаем замок семафора
        gdiSemaphore?.Release();
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
            // BUG HIGH - Колоссальная дисковая утечка и тормоза (I/O Bottleneck). Метод `Cv2.ImRead` вызывается при каждом поиске шаблона в дереве поведения (каждый тик бота). Чтение файлов картинок с SSD/HDD по нескольку раз в секунду намертво забивает дисковую подсистему ОС, превращая асинхронный цикл в черепаху. Если диск перегружен, метод `ImRead` начинает выполняться секундами, вызывая жесткие микрофризы и иллюзию "зависания" скрипта намертво сразу после старта. Картинки-шаблоны ОБЯЗАНЫ загружаться в память один раз при старте приложения (например, в Dictionary<string, Mat>) и использоваться оттуда в виде готовых `Mat` объектов.
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

    // // ПОПРАВКА HIGH - Инфраструктура для мгновенного кэширования шаблонов OpenCV в ОЗУ
    // private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Mat> _cachedTemplates = new();

    // /// <summary>
    // /// Потокобезопасный метод получения матрицы шаблона из кэша оперативной памяти.
    // /// Если картинки в памяти еще нет — она загружается один раз и сохраняется на всю сессию.
    // /// </summary>
    // private static Mat GetOrCreateTemplate(string templatePath)
    // {
    //     return _cachedTemplates.GetOrAdd(templatePath, path =>
    //     {
    //         Mat mat = Cv2.ImRead(path, ImreadModes.Color);
    //         if (mat.Empty())
    //         {
    //             throw new FileNotFoundException($"[КЭШ ОБРАЗОВ] Критическая ошибка! Не удалось загрузить шаблон: {path}");
    //         }
    //         return mat;
    //     });
    // }

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    /// <summary>
    /// Кроссплатформенный метод поиска всех совпадений изображения-шаблона в заданной области кадра.
    /// Выполняет сопоставление признаков в градациях серого и фильтрует дубликаты в пределах размеров шаблона.
    /// </summary>
    /// <param name="screen">Матрица полного скриншота эмулятора (в формате BGR/BGRA).</param>
    /// <param name="templatePath">Абсолютный или относительный путь к графическому файлу-шаблону.</param>
    /// <param name="searchArea">Прямоугольная область ограничения поиска. Если <c>null</c> — сканируется весь кадр.</param>
    /// <param name="threshold">Порог точности совпадения от 0.0 до 1.0. По умолчанию: 0.55.</param>
    /// <param name="maxResults">Максимальное количество возвращаемых точек, чтобы избежать зацикливания. По умолчанию: 20.</param>
    /// <returns>Список точек <see cref="Point"/> центров найденных объектов в координатах исходного кадра, или пустой список при отсутствии совпадений.</returns>
    public static List<Point> FindAllTemplatesInRegion(
        Mat screen,
        string templatePath,
        Rect? searchArea = null,
        double threshold = 0.55,
        int maxResults = 20)
    {
        List<Point> foundPoints = [];

        if (screen?.Empty() is not false) return foundPoints;

        // Вырезаем область поиска, если она задана, иначе работаем с полным экраном
        Mat croppedScreen = searchArea.HasValue
            ? new Mat(screen, searchArea.Value)
            : screen;

        try
        {
            // BUG HIGH - Повторение критической дисковой утечки и просадки производительности (I/O Bottleneck). Метод `Cv2.ImRead` вызывается при каждом множественном поиске шаблонов. При параллельной работе нескольких ботов постоянное чтение файлов с SSD/HDD на каждом тике парализует дисковую подсистему ОС, превращая асинхронные задержки в жесткие зависания потоков. Проблему решает внедрение статического кэша `GetOrCreateTemplate(templatePath)`, как мы спроектировали шагом ранее.
            using var matTemplate = Cv2.ImRead(templatePath, ImreadModes.Color);
            if (matTemplate.Empty())
            {
                Logger.Log($"Не удалось загрузить файл шаблона по пути '{templatePath}'.", LogType.Error);
                return foundPoints;
            }

            if (matTemplate.Width > croppedScreen.Width || matTemplate.Height > croppedScreen.Height)
            {
                Logger.Log($"Файл шаблона '{Path.GetFileName(templatePath)}' ({matTemplate.Width}x{matTemplate.Height}) превышает размеры области поиска ({croppedScreen.Width}x{croppedScreen.Height}).", LogType.Warning);
                return foundPoints;
            }

            // Переводим изображения в оттенки серого для ускорения вычислений
            using Mat grayScreen = new();
            using Mat grayTemplate = new();
            Cv2.CvtColor(croppedScreen, grayScreen, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(matTemplate, grayTemplate, ColorConversionCodes.BGR2GRAY);

            // Выполняем нормированное сопоставление
            using Mat result = new();
            Cv2.MatchTemplate(grayScreen, grayTemplate, result, TemplateMatchModes.CCoeffNormed);

            int offsetX = searchArea?.X ?? 0;
            int offsetY = searchArea?.Y ?? 0;

            // Цикл извлечения локальных максимумов (Non-Maximum Suppression)
            for (int i = 0; i < maxResults; i++)
            {
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out Point maxLoc);

                // Если максимальное совпадение ниже порога — выходим из цикла, совпадений больше нет
                if (maxVal < threshold)
                    break;

                // Вычисляем точку центра найденного объекта в координатах полного экрана
                int centerX = offsetX + maxLoc.X + (matTemplate.Width / 2);
                int centerY = offsetY + maxLoc.Y + (matTemplate.Height / 2);
                foundPoints.Add(new Point(centerX, centerY));

#if DEBUG
                Logger.Log($"Множественный поиск '{Path.GetFileName(templatePath)}' [#{i + 1}], совпадение: {maxVal * 100:F1}%, центр: {centerX}х{centerY}.", LogType.Test);
#endif

                // Стираем (зануляем) область вокруг найденного максимума в матрице результатов,
                // чтобы не находить один и тот же объект на соседних пикселях. Стираем в радиусе размера шаблона.
                int startX = Math.Max(0, maxLoc.X - (matTemplate.Width / 2));
                int startY = Math.Max(0, maxLoc.Y - (matTemplate.Height / 2));

                // BUG MEDIUM - Потенциальный выход за границы матрицы (IndexOutOfRangeException / OpenCvSharpException). При расчете `endX` и `endY` используется деление сторон шаблона пополам, но не проверяется, не превышают ли финальные координаты `result.Cols` и `result.Rows`. Несмотря на использование `Math.Min`, если `roiToErase` сформируется с некорректным размером из-за округления, вызов `new Mat(result, roiToErase)` выбросит исключение прямо посреди цикла детекции, обрушив итерацию сценария. Безопаснее использовать метод `Tools.ClampRegion` или жестко валидировать ширину и высоту Rect.
                int endX = Math.Min(result.Cols, maxLoc.X + (matTemplate.Width / 2));
                int endY = Math.Min(result.Rows, maxLoc.Y + (matTemplate.Height / 2));

                Rect roiToErase = new(startX, startY, endX - startX, endY - startY);
                using Mat eraseRoi = new(result, roiToErase);
                eraseRoi.SetTo(Scalar.All(0)); // Заполняем нулями, так как ищем значения близкие к 1.0
            }

            return foundPoints;
        }
        catch (Exception ex)
        {
            Logger.Log($"Сбой при множественном сопоставлении шаблона '{Path.GetFileName(templatePath)}': {ex.Message}", LogType.Error);
            return foundPoints;
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
        // 1. Безопасная симуляция паузы перед кликом
        if (minSec > 0 || maxSec > 0)
        {
            int delay = GetRandomDelayMs(minSec, maxSec);
            Thread.Sleep(delay);
        }

        // 2. КОРРЕКЦИЯ ОКНА WINDOWS: компенсируем 31 пиксель стандартной рамки
        if (applyWinHeaderCorrection)
        {
            y -= 31;
        }

        // 3. ПОТОКОБЕЗОПАСНЫЙ РАНДОМ (Используем .NET 9+ Random.Shared взамен старого поля _random)
        int finalX = x + Random.Shared.Next(-offset, offset + 1);
        int finalY = y + Random.Shared.Next(-offset, offset + 1);

        string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "adb.exe");

        if (!File.Exists(adbPath))
        {
            Logger.Log($"Файл 'adb.exe' не найден по пути '{adbPath}'.", LogType.Error);
            return;
        }

        string deviceTarget = "127.0.0.1:" + adbPort;

        try
        {
            // --- ШАГ 1: БЕЗОПАСНЫЙ CONNECT С ТАЙМАУТОМ ---
            using (var procConnect = Process.Start(new ProcessStartInfo
            {
                FileName = adbPath,
                Arguments = "connect " + deviceTarget,
                CreateNoWindow = true,
                UseShellExecute = false
            }))
            {
                // ИСПРАВЛЕНО: Применили оператор условного доступа ?. взамен ручной проверки на null
                if (procConnect?.WaitForExit(4000) is false)
                {
                    Logger.Log("[ADB System] Превышен таймаут ожидания команды connect для " + deviceTarget, LogType.Warning);
                    procConnect.Kill();
                }
            }

            // --- ШАГ 2: БЕЗОПАСНАЯ ОТПРАВКА КЛИКА (TAP) С ТАЙМАУТОМ ---
            string argsTap = "-s " + deviceTarget + " shell input tap " + finalX + " " + finalY;

            using (var procTap = Process.Start(new ProcessStartInfo
            {
                FileName = adbPath,
                Arguments = argsTap,
                CreateNoWindow = true,
                UseShellExecute = false
            }))
            {
                // ИСПРАВЛЕНО: Применили оператор условного доступа ?. взамен ручной проверки на null
                if (procTap?.WaitForExit(5000) is false)
                {
                    Logger.Log("[ADB System] Превышен таймаут выполнения клика input tap для " + deviceTarget, LogType.Warning);
                    procTap.Kill();
                    return;
                }
            }


    #if DEBUG
            Logger.Log($"[ADB] Клик успешно отправлен на '{deviceTarget}': координаты (X={finalX}, Y={finalY}).", LogType.Test);
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
        return Random.Shared.Next(minMs, maxMs + 1);
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

        // BUG HIGH - Падение или скрытый сбой из-за расхождения имен свойств (NullReferenceException). В JSON-конфигурации, которую мы зафиксировали в начале, блок размеров называется "WindowSettings" (`"WindowSettings": { "TargetWidth": 1280... }`). Однако в коде ты обращаешься к свойству `settings.Size`. Если в классе `AccSettings` свойство не имеет атрибута переименования вроде `[JsonPropertyName("WindowSettings")]`, то `settings.Size` гарантированно вернет `null`. Бот запишет ошибку в лог и выйдет, вернув `hWnd`, но окно ОСТАНЕТСЯ НЕПОДГОТОВЛЕННЫМ (размеры не изменятся под 1280x720). В итоге OpenCV-координаты и клики поползут, из-за чего дерево поведения не сможет найти ни одного шаблона на экране и уйдет в бесконечное ожидание/клин.
        if (settings.Size == null)
        {
            Logger.Log($"[{settings.Name}] В файле конфигурации отсутствует блок настроек размеров 'AccSettings'.", LogType.Error);
            return hWnd;
        }

        int targetW = settings.Size.TargetWidth;
        int targetH = settings.Size.TargetHeight;

        // Пытаемся изменить размеры окна под стандарты бота
        // BUG HIGH - Скрытая блокировка (Deadlock) WinAPI потока. Метод `ResizeWindow` внутри себя наверняка вызывает WinAPI функции вроде `SetWindowPos` или `MoveWindow`. Если этот метод вызывается из фонового потока воркера, а окно эмулятора BlueStacks в этот момент занято обработкой графики или зависло, вызов `SetWindowPos` без специальных флагов асинхронности (вроде SWP_ASYNCWINDOWPOS) может намертво заблокировать вызывающий поток C#, ожидая ответа от оконной процедуры эмулятора.
        if (ResizeWindow(hWnd, targetW, targetH))
        {
            Logger.Log($"[{settings.Name}] Размеры окна скорректированы под разрешение {targetW}x{targetH}.", LogType.Test);

            // Безопасно добавляем имя аккаунта в потокобезопасный словарь-кэш (0 — минимальная byte-заглушка)
            _resizedAccounts.TryAdd(settings.Name, 0);

            // Небольшая задержка, чтобы ОС успела применить новые размеры окна до первого скриншота
            // BUG MEDIUM - Синхронный Sleep потока из пула. Замораживает поток выполнения на 300мс. Опять же, лучше избегать синхронных Thread.Sleep в Task-воркерах.
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
