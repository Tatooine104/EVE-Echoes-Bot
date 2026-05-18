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

        //const string TargetWindow = "(MEmu W.01)";

        private static readonly Random _random = new();

        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
        // - + - + - + - + - |  Основная программа   | - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
        // - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

        static void Main()
        {
            Console.Title = "EVE Echoes Bot Controller";
            ConsolePrint("Запуск LowMiner...", ConsoleColor.Cyan);

            // 1. Загружаем настройки из файла
            BotConfig config = ConfigManager.Load();

            // 2. Работаем с прочитанными данными
            foreach (var account in config.Accounts)
            {
                // ПРАВКА: Передаем объект аккаунта целиком, а не только строку заголовка
                IntPtr hWnd = GetWindow(account);

                if (hWnd != IntPtr.Zero)
                {
                    ConsolePrint($"Аккаунт '{account.AccountName}' готов к работе.", ConsoleColor.Green);
                    // Теперь можно использовать account.MinDelay, account.MaxDelay и т.д.
                }
            }

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
                    ConsolePrint($"GetWindow | Аккаунт: {settings.AccountName} | Успех: Окно '{settings.WindowTitle}' подогнано под Android-экран {settings.TargetWidth}x{settings.TargetHeight}", ConsoleColor.Green);
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
                        WindowTitle = "(BlueStacks_EVE.01)",
                        // ИСПРАВЛЕНО: Пишем имена свойств напрямую и с заглавной буквы
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
            // Десериализуем JSON, новые поля подтянутся автоматически
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
