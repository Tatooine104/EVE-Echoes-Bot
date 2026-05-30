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

            // 1. Потокобезопасный разбор тегов [Имя|Система|Корабль]
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
                        accountName = parts[0];
                        eveSystem = parts.Length > 1 ? parts[1] : "Unknown";
                        eveShip = parts.Length > 2 ? parts[2] : "Unknown";
                    }
                    else
                    {
                        accountName = rawTag;
                        eveSystem = "Unknown";
                    }
                }
                else
                {
                    accountName = "System";
                    eveSystem = "System";
                }
            }
            else if (accountName.Contains('|'))
            {
                string[] parts = accountName.Split('|');
                accountName = parts[0];
                eveSystem = parts.Length > 1 ? parts[1] : "Unknown";
                eveShip = parts.Length > 2 ? parts[2] : "Unknown";
            }

// [ ] TODO 2026.05.30 Добавить иконку для ИНФО 

            // 2. Иконки статуса (Visual Anchors)
            string icon = type switch
            {
                LogType.Success => "✅",
                LogType.Warning => "⚠️",
                LogType.Error   => "🚨",
                LogType.Test    => "⚙️",
                _               => "🔹"
            };

            // 3. Форматируем дату по новому стандарту: ГГГГ.ММ.ДД чч:мм:сс
            string timestamp = DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss");

            // 4. Собираем строгий и чистый вид для вывода на ЭКРАН КОНСОЛИ
            string botContext = accountName == "System" ? "[SYSTEM]" : $"[{accountName} | {eveSystem} | {eveShip}]";
            string consoleMessage = $"[{timestamp}] {icon} {botContext} [{callerMethod}]: {message}";
            ConsoleColor color = GetColorForType(type);

            // 5. ВАША ЖЕСТКАЯ МАРШРУТИЗАЦИЯ
            lock (Console.Out)
            {
                // В консоль пишем ВСЕГДА и ВСЁ (включая Test)
                PrintToConsole(consoleMessage, color);

// [ ] TODO 2026.05.30 Добавить сохранения в файл ИНФО 

                // В файл CSV пишем ТОЛЬКО важное (Warning и Error)
                if (type == LogType.Warning || type == LogType.Error || type == LogType.Info)
                {
                    AppendToFile(timestamp, icon, accountName, eveSystem, eveShip, callerMethod, message);
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

        private static void AppendToFile(string timestamp, string icon, string account, string system, string ship, string method, string message)
        {
            lock (_fileLock) // Защита от сбоев при одновременной записи из нескольких аккаунтов
            {
                try
                {
                    bool fileExists = File.Exists(LogFilePath);
                    string safeMessage = message.Replace(";", ",");
                    string csvLine = $"{timestamp};{icon};{account};{system};{ship};{method};{safeMessage}";

                    // Используем UTF8Encoding со специальным флагом 'true', который заставляет C# внедрить BOM-маркер
                    var utf8WithBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

                    if (!fileExists)
                    {
                        // Если файла нет, создаем его и сразу пишем шапку с BOM-маркером
                        string header = "Дата;Статус;Аккаунт;Система;Корабль;Метод;Сообщение" + Environment.NewLine;
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