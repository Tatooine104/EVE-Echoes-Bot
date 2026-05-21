using System.Text.Json;
using System.Text.Json.Serialization;

// ЗАМЕТКА: Убедитесь, что это пространство имен (namespace) совпадает с вашим основным файлом!
namespace EVEEchoesBot
{
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

        /// <summary>
        /// Загружает конфигурацию бота из JSON-файла или создает конфигурацию по умолчанию, если файл отсутствует.
        /// </summary>
        /// <returns>Объект конфигурации <see cref="BotConfig"/>.</returns>
        public static BotConfig Load()
        {
            if (!File.Exists(ConfigPath))
            {
                var defaultConfig = new BotConfig
                {
                    Accounts =
                    [
                        new WindowSettings
                        {
                            Name = "Miner_V04K0",
                            WindowTitle = "BlueStacks_EVE.01",
                            Script = "LocalWatcher",
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
                return JsonSerializer.Deserialize<BotConfig>(json, _options) ?? new BotConfig();
            }
            catch (Exception ex)
            {
                // Используем прямое обращение к методу логов основного класса
                Tools.ConsolePrint($"[Ошибка] Не удалось прочитать конфиг: {ex.Message}", ConsoleColor.Red);
                return new BotConfig();
            }
        }

        /// <summary>
        /// Сохраняет текущую конфигурацию бота в JSON-файл на диск.
        /// </summary>
        /// <param name="config">Объект конфигурации для сохранения.</param>
        public static void Save(BotConfig config)
        {
            try
            {
                string json = JsonSerializer.Serialize(config, _options);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Tools.ConsolePrint($"[Ошибка] Не удалось сохранить конфиг: {ex.Message}", ConsoleColor.Red);
            }
        }
    }

    /// <summary>
    /// Корневой объект конфигурационного файла JSON.
    /// </summary>
    public class BotConfig
    {
        public List<WindowSettings> Accounts { get; set; } = [];
    }

    /// <summary>
    /// Настройки для конкретного игрового аккаунта / окна эмулятора.
    /// </summary>
    public class WindowSettings
    {
        public string Name { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public string? Script { get; set; }

        // Полный путь к атрибуту больше не нужен, так как вверху файла добавлен System.Text.Json.Serialization
        [JsonPropertyName("WindowSettings")]
        public TargetSize? Size { get; set; }
    }

    /// <summary>
    /// Целевые размеры для подгонки рабочей области окна.
    /// </summary>
    public class TargetSize
    {
        public int TargetWidth { get; set; }
        public int TargetHeight { get; set; }
    }
}
