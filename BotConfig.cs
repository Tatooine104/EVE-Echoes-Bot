using System.Text.Json;
using System.Text.Json.Serialization;
using static EVEEchoesBot.Logger;
using System.Runtime.InteropServices;
using System.Text;


// [v] TODO 2026.05.30 Привести все тексты логгера к единому стилю 
// [v] TODO 2026.05.30 Сделать вызов окна ввода при создании дефолтного конфига 

// ЗАМЕТКА: Убедитесь, что это пространство имен (namespace) совпадает с вашим основным файлом!
namespace EVEEchoesBot
{

// [v] Проверить все методы и добавить новый метод Logger.Log()
// [v] TODO 2026.05.30 Привести все тексты логгера к единому стилю  
// [v] TODO 2026.05.30 Добавить класс сохранения статистики по ботам (отдельно для каждого акка stat_accountname.json)
// [v] TODO 2026.05.30 Перенести EVESystem и EVEShip в файл статистики. 

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

    // Главный класс конфигурации
    public class BotConfig
    {
        public List<AccSettings> Accounts { get; set; } = [];
    }

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

    // Класс настроек конкретного аккаунта (ЗАМЕНИТЕ СВОЙ СТАРЫЙ НА ЭТОТ)
    public class AccSettings
    {
        public string Name        { get; set; } = "";
        public string WindowTitle { get; set; } = "";
        public string Emulator    { get; set; } = "";
        public string Script      { get; set; } = "";
        public int    AdbPort     { get; set; }

        // <-- 2. СЮДА ДОБАВЛЯЕМ АТРИБУТ СВЯЗИ С JSON
        [JsonPropertyName("AccSettings")]
        public TargetSize? Size { get; set; }
    }

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

    // Класс размеров окна
    public class TargetSize
    {
        public int TargetWidth  { get; set; }
        public int TargetHeight { get; set; }
    }

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

    // [ ] TODO 2026.05.30 Добавить параметр В космосе/В доке, статус безопастности, и подумать что еще нужно  
    public class AccountStateDto
    {
        public string AccountName      { get; set; } = "";
        public long Triggers           { get; set; }
        public double RuntimeSeconds   { get; set; }
        public string CurrentTask      { get; set; } = "";
        public IList<string> TaskQueue { get; set; } = [];
        public DateTime LastUpdate     { get; set; }
        public string EVESystem        { get; set; } = "";
        public string EVEShip          { get; set; } = "";
    }

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

    /// <summary>
    /// Класс управления конфигурацией всего бота.
    /// </summary>
    public static class ConfigManager
    {
        private const string ConfigPath = "config.json";

        // Кэшируем настройки сериализации один раз для всего приложения
        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region BotConfig Load


        /// <summary>
        /// Загружает конфигурацию бота из JSON-файла или создает конфигурацию по умолчанию, если файл отсутствует.
        /// </summary>
        /// <returns>Объект конфигурации <see cref="BotConfig"/>.</returns>
        public static BotConfig Load()
        {
            // Если файл конфигурации отсутствует, создаем и сохраняем дефолтный
            if (!File.Exists(ConfigPath))
            {
                BotConfig defaultConfig = CreateDefaultConfig();
                Save(defaultConfig);

                Logger.Log($"Создан файл конфигурации по умолчанию по пути '{ConfigPath}'.", LogType.Info);
                return defaultConfig;
            }

            try
            {
                string json = File.ReadAllText(ConfigPath);
                BotConfig? config = JsonSerializer.Deserialize<BotConfig>(json, _options);

                if (config == null)
                {
                    Logger.Log("Файл конфигурации пуст или поврежден. Инициализирован новый объект.", LogType.Warning);
                    return new BotConfig();
                }

                return config;
            }
            catch (Exception ex)
            {
                // Переведено на вашу новую систему логирования
                Logger.Log($"Не удалось прочитать или десериализовать файл конфигурации: {ex.Message}", LogType.Error);
                return new BotConfig();
            }
        }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region CreateDefaultConfig

        /// <summary>
        /// Генерирует дефолтную структуру конфигурации для первой сессии.
        /// </summary>
        private static BotConfig CreateDefaultConfig()
        {
            // Очищаем поток ввода консоли
            while (Console.KeyAvailable) Console.ReadKey(true);

            Console.ResetColor();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=== ПЕРВЫЙ ЗАПУСК: ИНТЕРАКТИВНАЯ НАСТРОЙКА БОТА ===");
            Console.ResetColor();

            // 1. Запрос имени персонажа
            string name = string.Empty;
            while (string.IsNullOrWhiteSpace(name))
            {
                Console.Write("Введите имя вашего персонажа (для логов): ");
                name = Console.ReadLine()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("⚠️ Имя не может быть пустым!");
                    Console.ResetColor();
                }
            }

            Console.WriteLine("\nСканирую запущенные окна эмуляторов...");

            // Получаем все окна и убираем очевидный мусор (проводник, саму консоль бота и т.д.)
            var allWindows = WindowEnumerator.GetVisibleWindowTitles()
                .Where(t => !t.Equals("Program Manager", StringComparison.OrdinalIgnoreCase) &&
                            !t.Equals("Settings", StringComparison.OrdinalIgnoreCase) &&
                            !t.Contains(AppDomain.CurrentDomain.FriendlyName))
                .Distinct()
                .ToList();

            string windowTitle = string.Empty;

            if (allWindows.Count == 0)
            {
                // Если окон вообще не нашли, откатываемся на ручной ввод
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("⚠️ Не удалось автоматически найти активные окна.");
                Console.ResetColor();

                while (string.IsNullOrWhiteSpace(windowTitle))
                {
                    Console.Write("Введите название окна эмулятора вручную: ");
                    windowTitle = Console.ReadLine()?.Trim() ?? string.Empty;
                }
            }
            else
            {
                // Выводим красивый нумерованный список окон
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("Найденные открытые окна:");
                for (int i = 0; i < allWindows.Count; i++)
                {
                    Console.WriteLine($"  [{i + 1}] {allWindows[i]}");
                }
                Console.ResetColor();

                int selectedIndex = -1;
                while (selectedIndex < 0 || selectedIndex >= allWindows.Count)
                {
                    Console.Write($"Выберите номер вашего эмулятора (1-{allWindows.Count}): ");
                    string input = Console.ReadLine() ?? string.Empty;

                    if (int.TryParse(input, out int num) && num >= 1 && num <= allWindows.Count)
                    {
                        selectedIndex = num - 1;
                        windowTitle = allWindows[selectedIndex];
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("⚠️ Неверный выбор! Введите число из списка.");
                        Console.ResetColor();
                    }
                }
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n✅ Профиль настроен! Персонаж: '{name}', Окно: '{windowTitle}'");
            Console.ResetColor();
            Console.WriteLine();

            return new BotConfig
            {
                Accounts =
                [
                    new AccSettings
                    {
                        Name = name,
                        WindowTitle = windowTitle,
                        Emulator = "BlueStacks",
                        Script = "LocalWatcher",
                        AdbPort = 5565,
                        Size = new TargetSize
                        {
                            TargetWidth = 1280,
                            TargetHeight = 720
                        }
                    }
                ]
            };
        }



#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region Save

        /// <summary>
        /// Сохраняет текущую конфигурацию бота в JSON-файл на диск.
        /// </summary>
        /// <param name="config">Объект configuration для сохранения.</param>
        public static void Save(BotConfig config)
        {
            if (config == null)
            {
                Logger.Log("Попытка сохранения пустого объекта конфигурации. Действие отменено.", LogType.Warning);
                return;
            }

            try
            {
                string json = JsonSerializer.Serialize(config, _options);
                File.WriteAllText(ConfigPath, json);

    #if DEBUG
                // Выводим информацию об успешном сохранении только в режиме отладки
                Logger.Log($"Конфигурация сохранена в файл '{ConfigPath}'.", LogType.Test);
    #endif
            }
            catch (Exception ex)
            {
                // Переведено на вашу единую систему логирования ошибок
                Logger.Log($"Не удалось сохранить конфигурацию в файл '{ConfigPath}': {ex.Message}", LogType.Error);
            }
        }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

    }

    public static partial class WindowEnumerator
    {
        // Описываем сигнатуру делегата для перечисления окон
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // 1. Импорт EnumWindows
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        // FIX ТУТ: Явно указываем EntryPoint = "GetWindowTextW" для Юникод-систем Windows
        [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int GetWindowText(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

        // 3. Импорт IsWindowVisible
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool IsWindowVisible(IntPtr hWnd);

        /// <summary>
        /// Возвращает список заголовков всех видимых окон в системе.
        /// </summary>
        public static List<string> GetVisibleWindowTitles()
        {
            List<string> titles = [];
            char[] buffer = new char[256];

            // Запуск перечисления окон
            EnumWindows((hWnd, lParam) =>
            {
                // Глушим предупреждение: явно показываем компилятору, что параметр проигнорирован намеренно
                _ = lParam;

                if (IsWindowVisible(hWnd))
                {
                    int length = GetWindowText(hWnd, buffer, buffer.Length);
                    if (length > 0)
                    {
                        string title = new string(buffer, 0, length).Trim();
                        if (!string.IsNullOrEmpty(title))
                        {
                            titles.Add(title);
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            return titles;
        }
    }

}