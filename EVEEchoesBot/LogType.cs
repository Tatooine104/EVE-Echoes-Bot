using System.Runtime.CompilerServices;

namespace EVEEchoesBot
{
    public enum LogType
    {
        Info,
        Success,
        Warning,
        Error,
        Test
    }

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
            string? accountName = null, // Меняем дефолт на null
            [CallerMemberName] string callerMethod = "")
        {
            // 1. Если имя не передано явно, пытаемся взять его из глобальной переменной
            if (string.IsNullOrEmpty(accountName))
            {
                // Проверяем, что объект _currentAccount не равен null
                // Замените .Name на реальное свойство вашего класса WindowSettings (например, .AccountName)
                accountName = Program._currentAccount?.Name ?? "System";
            }

            // 2. Строго по порядку: Дата -> Тип сообщения -> Вызвавший метод -> Аккаунт -> Текст
            string formattedMessage = $"[{DateTime.Now:HH:mm:ss}] [{accountName}] -> [{type.ToString().ToUpper()}] -> [{callerMethod}]: {message}";
            ConsoleColor color = GetColorForType(type);

        #if DEBUG
            PrintToConsole(formattedMessage, color);
        #else
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
}
