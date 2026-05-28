using System.Runtime.CompilerServices;

namespace EVEEchoesBot
{

    public static class Logger
    {

#if !DEBUG
        private const string LogFilePath = "EVE_Echoes_Bot_log.txt";
#endif

        /// <summary>
        /// Выводит форматированное сообщение в консоль и/или файл в зависимости от режима и типа.
        /// </summary>
        /// <param name="message">Текст сообщения.</param>
        /// <param name="type">Тип сообщения.</param>
        /// <param name="accountName">Имя активного аккаунта (по умолчанию System).</param>
        /// <param name="callerMethod">Автоматически заполняется компилятором.</param>
        public static void Log(
            string message,
            LogType type = LogType.Info,
            string? accountName = null,
            [System.Runtime.CompilerServices.CallerMemberName] string callerMethod = "")
        {
            string eveSystem = "Unknown";

            // 1. Потокобезопасный разбор имени аккаунта и игровой системы из сообщения
            if (string.IsNullOrEmpty(accountName))
            {
                // Если сообщение начинается с составного тега, например: "[EveAcc_1|Jita] Текст"
                if (message.StartsWith('[') && message.Contains(']'))
                {
                    int closeBracketIndex = message.IndexOf(']');
                    string rawTag = message[1..closeBracketIndex]; // Извлекаем "EveAcc_1|Jita"

                    // Очищаем текст сообщения от тега и пробелов по краям
                    message = message[(closeBracketIndex + 1)..].Trim();

                    // Проверяем, есть ли внутри разделитель '|'
                    if (rawTag.Contains('|'))
                    {
                        string[] parts = rawTag.Split('|');
                        accountName = parts[0]; // Имя бота (до черты)
                        eveSystem = parts[1];   // Имя системы (после черты)
                    }
                    else
                    {
                        accountName = rawTag;
                        eveSystem = "System"; // Если системы нет в теге
                    }
                }
                else
                {
                    accountName = "System";
                    eveSystem = "System";
                }
            }
            else
            {
                // Если accountName был явно передан в аргументы метода как "Имя|Система"
                if (accountName.Contains('|'))
                {
                    string[] parts = accountName.Split('|');
                    accountName = parts[0];
                    eveSystem = parts[1];
                }
            }

            // 2. Новый строгий порядок вывода по вашему запросу: 
            // Дата -> Вызвавший метод -> Аккаунт -> EVESystem -> Тип сообщения -> Текст
            string formattedMessage = $"[{DateTime.Now:HH:mm:ss}] [{callerMethod}] [{accountName}] [{eveSystem}] [{type.ToString().ToUpper()}]: {message}";
            ConsoleColor color = GetColorForType(type);

        #if DEBUG
            // Защищаем вывод в консоль от одновременной записи из разных параллельных потоков
            lock (Console.Out)
            {
                PrintToConsole(formattedMessage, color);
            }
        #else
            // Отправка логов в релизную систему обработки/файл
            RouteReleaseLog(formattedMessage, color, type);
        #endif
        }



#if !DEBUG
        private static void RouteReleaseLog(string message, ConsoleColor color, LogType type)
        {
            switch (type)
            {
                case LogType.Test: 
                    break;
                case LogType.Info:
                case LogType.Success:
                    PrintToConsole(message, color);
                    break;
                case LogType.Warning:
                case LogType.Error:
                    PrintToConsole(message, color);
                    AppendToFile(message);
                    break;
            }
        }
#endif

        private static void PrintToConsole(string message, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
        }

#if !DEBUG
        private static void AppendToFile(string message)
        {
            try 
            { 
                File.AppendAllText(LogFilePath, message + Environment.NewLine); 
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"[ERROR] Ошибка записи: {ex.Message}"); 
            }
        }
#endif

        private static ConsoleColor GetColorForType(LogType type)
        {
            return type switch
            {
                LogType.Info    => ConsoleColor.Gray,
                LogType.Success => ConsoleColor.Green,
                LogType.Warning => ConsoleColor.Yellow,
                LogType.Error   => ConsoleColor.Red,
                LogType.Test    => ConsoleColor.DarkBlue,
                _               => ConsoleColor.Gray
            };
        }
    }

    public enum LogType
    {
        Info,
        Success,
        Warning,
        Error,
        Test
    }

}
