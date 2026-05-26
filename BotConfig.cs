using System.Text.Json;
using System.Text.Json.Serialization;

// ЗАМЕТКА: Убедитесь, что это пространство имен (namespace) совпадает с вашим основным файлом!
namespace EVEEchoesBot
{

// [v] Проверить все методы и добавить новый метод Logger.Log() 

    // Главный класс конфигурации
    public class BotConfig
    {
        public List<WindowSettings> Accounts { get; set; } = [];
    }

    // Класс настроек конкретного аккаунта (ЗАМЕНИТЕ СВОЙ СТАРЫЙ НА ЭТОТ)
    public class WindowSettings
    {
        public string Name        { get; set; } = "";
        public string WindowTitle { get; set; } = "";
        public string Script      { get; set; } = "";
        public string FirstTask   { get; set; } = "";
        public string EVESystem   { get; set; } = "";
        public string EVEShip     { get; set; } = "";

        // <-- 2. СЮДА ДОБАВЛЯЕМ АТРИБУТ СВЯЗИ С JSON
        [JsonPropertyName("WindowSettings")]
        public TargetSize? Size { get; set; }
    }

    // Класс размеров окна
    public class TargetSize
    {
        public int TargetWidth  { get; set; }
        public int TargetHeight { get; set; }
    }

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

                Logger.Log($"Создан новый файл конфигурации по умолчанию: {ConfigPath}", LogType.Info);
                return defaultConfig;
            }

            try
            {
                string json = File.ReadAllText(ConfigPath);
                BotConfig? config = JsonSerializer.Deserialize<BotConfig>(json, _options);

                if (config == null)
                {
                    Logger.Log("Файл конфигурации пуст или поврежден. Создан пустой объект.", LogType.Warning);
                    return new BotConfig();
                }

                return config;
            }
            catch (Exception ex)
            {
                // Переведено на вашу новую систему логирования
                Logger.Log($"Не удалось прочитать или десериализовать конфиг: {ex.Message}", LogType.Error);
                return new BotConfig();
            }
        }

#endregion

#region CreateDefaultConfig

        /// <summary>
        /// Генерирует дефолтную структуру конфигурации для первой сессии.
        /// </summary>
        private static BotConfig CreateDefaultConfig()
        {
            return new BotConfig
            {
                Accounts =
                [
                    new WindowSettings
                    {
                        Name = "Somebody",
                        WindowTitle = "BlueStacks_EVE.01",
                        Script = "LocalWatcher",
                        FirstTask = "CheckSecurity",
                        EVESystem = "Jita",
                        EVEShip = "Covetor",
                        // ИСПРАВЛЕНО: возвращаем имя свойства Size. 
                        // Атрибут [JsonPropertyName] сам запишет его в JSON как "WindowSettings"
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

#region Save

        /// <summary>
        /// Сохраняет текущую конфигурацию бота в JSON-файл на диск.
        /// </summary>
        /// <param name="config">Объект configuration для сохранения.</param>
        public static void Save(BotConfig config)
        {
            if (config == null)
            {
                Logger.Log("Попытка сохранить пустой объект конфигурации (null). Действие отменено.", LogType.Warning);
                return;
            }

            try
            {
                string json = JsonSerializer.Serialize(config, _options);
                File.WriteAllText(ConfigPath, json);

    #if DEBUG
                // Выводим информацию об успешном сохранении только в режиме отладки
                Logger.Log($"Конфигурация успешно сохранена в файл: {ConfigPath}", LogType.Test);
    #endif
            }
            catch (Exception ex)
            {
                // Переведено на вашу единую систему логирования ошибок
                Logger.Log($"Не удалось сохранить конфигурацию в файл {ConfigPath}: {ex.Message}", LogType.Error);
            }
        }

#endregion

    }

}