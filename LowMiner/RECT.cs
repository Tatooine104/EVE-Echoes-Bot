using System.Runtime.InteropServices;
using System.Text.Json;

namespace LowMiner
{

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    static partial class Program
    {

        // Для LibraryImport лучше использовать private, а для вызова снаружи — публичную обертку
        [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr FindWindow(
            string? lpClassName,
            string? lpWindowName);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // Константы для Windows сообщений
        const uint WM_LBUTTONDOWN = 0x0201; // Нажатие левой кнопки мыши
        const uint WM_LBUTTONUP = 0x0202;   // Отпускание левой кнопки мыши

        const string TargetWindow = "(MEmu W.01)";

        private static readonly Random _random = new();

        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
        // - + - + - + - + - |  Основная программа   | - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

        static void Main()
        {
            Console.Title = "EVE Echoes Bot Controller";
            ConsolePrint("Запуск LowMiner...", ConsoleColor.Cyan);
            // const string TargetWindow = "(MEmu W.01)";

            // 1. Загружаем настройки из файла
            BotConfig config = ConfigManager.Load();

            // 3. Работаем с прочитанными данными
            foreach (var account in config.Accounts)
            {
                IntPtr hWnd = GetWindow(account.WindowTitle);

                if (hWnd != IntPtr.Zero)
                {
                    ConsolePrint($"Аккаунт '{account.AccountName}' готов к работе.", ConsoleColor.Green);
                    // Теперь можно использовать account.MinDelay, account.MaxDelay и т.д.
                }
            }

            /*
                        IntPtr hWnd = GetWindow(TargetWindow);

                        if (hWnd != IntPtr.Zero)
                        {
                            ConsolePrint("Бот готов. Выполняю тестовый клик через 2 секунды...");
                            Console.WriteLine("Бот готов. Выполняю тестовый клик через 2 секунды...");
                            System.Threading.Thread.Sleep(2000); // Пауза, чтобы вы успели переключиться на эмулятор

                            // Пример: клик в точку 100x100 внутри окна эмулятора
                            SmartClick(hWnd, 40, 600);
                        }
            */
            ConsolePrint("\nНажми любую клавишу для завершения...");
            Console.ReadKey();
        }

        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
        // - + - + - + - + - | Остальные методы и функции, используемые в основной программе | - + - + - + - + -
        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        // Константа для получения реальных границ окна в Windows 10/11
        const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        /// <summary>
        /// Изменяет размер окна BlueStacks так, чтобы его рабочая область стала заданной ширины и высоты.
        /// </summary>
        /// <param name="hWnd">Дескриптор окна.</param>
        /// <param name="targetWidth">Желаемая ширина видимой области.</param>
        /// <param name="targetHeight">Желаемая высота видимой области.</param>
        /// <returns>True, если размер успешно изменен.</returns>
        static bool ResizeWindow(IntPtr hWnd, int targetWidth, int targetHeight)
        {
            if (hWnd == IntPtr.Zero) return false;

            int structSize = Marshal.SizeOf(typeof(RECT));
            if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT realWindowRect, structSize) == 0)
            {
                // Панели BlueStacks (верхний заголовок и правое тулбар-меню)
                int bluestacksToolbarWidth = 33;
                int bluestacksHeaderHeight = 33;

                // Обычные тонкие рамки изменения размера Windows 10/11
                int windowsFrameWidth = 2;
                int windowsFrameHeight = 2;

                // Рассчитываем итоговый полный габарит окна
                int finalWindowWidth = targetWidth + bluestacksToolbarWidth + windowsFrameWidth;
                int finalWindowHeight = targetHeight + bluestacksHeaderHeight + windowsFrameHeight;

                // Меняем размер окна, оставляя его на прежних координатах X и Y
                return MoveWindow(hWnd, realWindowRect.Left, realWindowRect.Top, finalWindowWidth, finalWindowHeight, true);
            }

            return false;
        }


        /// <summary>
        /// Находит дескриптор окна по его заголовку и подгоняет его видимую область под 1280x720.
        /// </summary>
        /// <param name="windowName">Точное имя заголовка окна.</param>
        /// <returns>Дескриптор окна (IntPtr.Zero, если окно не найдено).</returns>
        static IntPtr GetWindow(string windowName)
        {
            // Поиск дескриптора окна через WinAPI
            IntPtr hWnd = FindWindow(null, windowName);

            if (hWnd != IntPtr.Zero)
            {
                int targetWidth = 1280;
                int targetHeight = 720;

                if (ResizeWindow(hWnd, targetWidth, targetHeight))
                {
                    ConsolePrint($"GetWindow | Успех: Окно '{windowName}' подогнано под размер {targetWidth}x{targetHeight}", ConsoleColor.Green);
                }
                else
                {
                    ConsolePrint($"GetWindow | Ошибка: Не удалось изменить размер окна '{windowName}'.", ConsoleColor.Red);
                }
            }
            else
            {
                ConsolePrint($"GetWindow | Ошибка: Окно '{windowName}' не найдено.", ConsoleColor.Red);
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

    public class BotConfig
    {
        // Список настроек для каждого персонажа/окна
        public List<WindowSettings> Accounts { get; set; } = [];
    }

    public class WindowSettings
    {
        public string AccountName { get; set; } = "Miner_V04K0"; // Для вашего удобства
        public string WindowTitle { get; set; } = "(BlueStacks_EVE.01)";    // Заголовок окна
        // Можно добавить специфические координаты для каждого окна, 
        // если они разного размера
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

    public static BotConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaultConfig = new BotConfig 
            { 
                Accounts = [ new() { AccountName = "Основной", WindowTitle = "MEmu" } ] 
            };
            Save(defaultConfig);
            return defaultConfig;
        }

        try
        {
            string json = File.ReadAllText(ConfigPath);
            // Используем кэшированные настройки
            return JsonSerializer.Deserialize<BotConfig>(json, _options) ?? new();
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
