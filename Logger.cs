using System;
using System.IO;

namespace EVEEchoesBot
{
    public static class Logger
    {
        private const string LogFilePath = "EVE_Echoes_Bot_log.csv";

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region Log

        public static void Log(
            string message,
            LogType type = LogType.Info,
            string? accountName = null,
            [System.Runtime.CompilerServices.CallerMemberName] string callerMethod = "")
        {
            string eveSystem = "Unknown";
            string eveShip = "Unknown";
            string safeAccount = "System";

            // 1. Потокобезопасный и чистый разбор тегов [Имя|Система|Корабль]
            if (string.IsNullOrEmpty(accountName))
            {
                if (message.StartsWith('[') && message.Contains(']'))
                {
                    int closeBracketIndex = message.IndexOf(']');
                    string rawTag = message[1..closeBracketIndex];

                    message = message[(closeBracketIndex + 1)..].Trim();

                    if (rawTag.Contains('|'))
                    {
                        string[] parts = rawTag.Split('|');
                        safeAccount = parts[0].Trim();
                        eveSystem = parts.Length > 1 ? parts[1].Trim() : "Unknown";
                        eveShip = parts.Length > 2 ? parts[2].Trim() : "Unknown";
                    }
                    else
                    {
                        safeAccount = rawTag.Trim();
                        eveSystem = "Unknown";
                    }
                }
                else
                {
                    safeAccount = "System";
                    eveSystem = "System";
                }
            }
            else
            {
                if (accountName.Contains('|'))
                {
                    string[] parts = accountName.Split('|');
                    safeAccount = parts[0].Trim();
                    eveSystem = parts.Length > 1 ? parts[1].Trim() : "Unknown";
                    eveShip = parts.Length > 2 ? parts[2].Trim() : "Unknown";
                }
                else
                {
                    safeAccount = accountName.Trim();
                    eveSystem = "System";
                }
            }

            // 2. Иконки статуса с жесткой компенсацией ширины для Windows Console/Terminal
            // Добавлен явный пробел к каждому эмодзи, у предупреждения (Warning) — два пробела.
            string icon = type switch
            {
                LogType.Success => "✅",
                LogType.Warning => "⚠️ ",
                LogType.Error   => "🚨",
                LogType.Test    => "⚙️ ",
                _               => "🔹"
            };

            // 3. Форматируем дату по стандарту: ГГГГ.ММ.ДД чч:мм:сс
            string timestamp = DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss");

            // 4. Собираем строгий вид для вывода на ЭКРАН (пробел после {icon} убран, он уже внутри иконки)
            string botContext = safeAccount.Equals("System", StringComparison.OrdinalIgnoreCase) 
                ? "[SYSTEM]" 
                : $"[{safeAccount} | {eveSystem} | {eveShip}]";
                
            string consoleMessage = $"[{timestamp}] {Program._ProgVersion} {icon} {botContext} [{callerMethod}]: {message}";
            ConsoleColor color = GetColorForType(type);

            // 5. Потокобезопасная маршрутизация
            lock (Console.Out)
            {
                // В консоль пишем ВСЕГДА
                PrintToConsole(consoleMessage, color);

                // В файл пишем только важное. Параметры гарантированно не null.
                if (type == LogType.Warning || type == LogType.Error || type == LogType.Info)
                {
                    AppendToFile(timestamp, Program._ProgVersion, type.ToString(), safeAccount, eveSystem, eveShip, callerMethod, message);
                }
            }
        }


#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region PrintToConsole

        private static void PrintToConsole(string message, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
        }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region AppendToFile

        private static readonly System.Threading.Lock _fileLock = new();

        private static void AppendToFile(
            string timestamp,
            string progversion,
            string type,
            string? account,
            string? system,
            string? ship,
            string method,
            string message)
        {
            lock (_fileLock) // Защита от сбоев при одновременной записи из нескольких аккаунтов
            {
                try
                {
                    bool fileExists = File.Exists(LogFilePath);
                    string safeMessage = message.Replace(";", ",");
                    string csvLine = $"{timestamp};{progversion};{type};{account};{system};{ship};{method};{safeMessage}";

                    // Используем UTF8Encoding со специальным флагом 'true', который заставляет C# внедрить BOM-маркер
                    var utf8WithBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

                    if (!fileExists)
                    {
                        // Если файла нет, создаем его и сразу пишем шапку с BOM-маркером
                        string header = "Дата;Версия;Тип;Аккаунт;Система;Корабль;Метод;Сообщение" + Environment.NewLine;
                        File.WriteAllText(LogFilePath, header, utf8WithBom);
                    }

                    // Дописываем строку лога, строго сохраняя кодировку UTF-8 с BOM
                    File.AppendAllText(LogFilePath, csvLine + Environment.NewLine, utf8WithBom);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Ошибка записи в CSV: {ex.Message}");
                }
            }
        }


#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region GetColorForType

        private static ConsoleColor GetColorForType(LogType type)
        {
            return type switch
            {
                LogType.Info    => ConsoleColor.Gray,
                LogType.Success => ConsoleColor.Green,
                LogType.Warning => ConsoleColor.Yellow,
                LogType.Error   => ConsoleColor.Red,
                LogType.Test    => ConsoleColor.DarkGray,
                _               => ConsoleColor.Gray
            };
        }
    }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

#region LogType

    public enum LogType
    {
        Info,
        Success,
        Warning,
        Error,
        Test
    }

#endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +

}