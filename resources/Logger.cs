using System;
using System.IO;

namespace EVEEchoesBot.resources;


// [ ] TODO 2026.06.01 Проанализировать файл лога и подумать насколько он информативный - может что-то еще требуется добавить. 

public static class Logger
{
    #region Static Fields & Consts

    /// <summary>
    /// Имя и путь к файлу журнала в формате CSV для записи критических и важных событий.
    /// </summary>
    private const string LogFilePath = "EVE_Echoes_Bot_log.csv";

    /// <summary>
    /// Кэшированная строковая репрезентация текущей версии сборки бота (например, v.1.02.003).
    /// Используется для вывода в заголовки логов при запуске приложения.
    /// </summary>
    private static readonly string _cachedVersion =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"v.{v.Major}.{v.Minor:D2}.{v.Build:D3}"
            : "v.0.00.000";

    /// <summary>
    /// Потокобезопасный кэш в оперативной памяти для хранения последних 13 сообщений для веб-панели.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _webLogsCache = new();

    #endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    /// <summary>
    /// Возвращает список последних 13 логов, отсортированных в порядке "самые свежие сверху" для веб-интерфейса.
    /// </summary>
    public static List<string> GetLastLogs()
    {
        // Разворачиваем очередь, чтобы новые записи шли первыми
        return [.. _webLogsCache.Reverse()];
    }


// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Log

    /// <summary>
    /// Основной метод логирования системы. Выполняет потокобезопасную маршрутизацию сообщений на экран и в файл.
    /// Автоматически извлекает контекст аккаунта, звездной системы и корабля из сообщения или параметра <paramref name="accountName"/>.
    /// </summary>
    /// <param name="message">Текст лог-сообщения.</param>
    /// <param name="type">Категория важности события (<see cref="LogType"/>). По умолчанию: <see cref="LogType.Info"/>.</param>
    /// <param name="accountName">Опциональный контекст аккаунта. Может содержать строку формата "Имя|Система|Корабль".</param>
    /// <param name="callerMethod">Имя метода, совершившего вызов логгера (заполняется автоматически компилятором).</param>
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

        // [ ] TODO 2026.06.01 Добавить тег взамен "Тест", который выводится в консоль но не логгируется в файл (ТЕСТ это только для отладки).
        // [ ] TODO 2026.06.01 Проверить все вызовы, разделить логику что логируется только в файл, что в консоль, а что только при отладке 
        
        // 2. Иконки статуса с жесткой компенсацией ширины для Windows Console/Terminal
        string icon = type switch
        {
            LogType.Success => "[ OK ]  ",
            LogType.Warning => "[WARN]  ",
            LogType.Error   => "[ERR!]  ",
            LogType.Test    => "[TEST]  ",
            _               => "[INFO]  "
        };

        // 3. Форматируем дату по стандарту: ГГГГ.ММ.ДД чч:мм:сс
        string timestamp = DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss");

        // 4. Собираем строгий вид для вывода на ЭКРАН (пробел после {icon} убран, он уже внутри иконки)
        string botContext = safeAccount.Equals("System", StringComparison.OrdinalIgnoreCase)
            ? "[SYSTEM]"
            : $"[{safeAccount} | {eveSystem} | {eveShip}]";

        string consoleMessage = $"[{timestamp}] {_cachedVersion} {icon} {botContext} [{callerMethod}]: {message}";
        ConsoleColor color = GetColorForType(type);

        // 5. Потокобезопасная маршрутизация
        lock (Console.Out)
        {
            // В консоль пишем ВСЕГДА
            PrintToConsole(consoleMessage, color);

            // В файл пишем только важное
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Info)
            {
                AppendToFile(timestamp, _cachedVersion, type.ToString(), safeAccount, eveSystem, eveShip, callerMethod, message);
            }

            // КОРРЕКЦИЯ ДЛЯ ВЕБ-ИНТЕРФЕЙСА:
            // Добавляем красивую отформатированную строку в кэш памяти для браузера
            string webFormattedMessage = $"[{DateTime.Now:HH:mm:ss}] {icon} {botContext} : {message}";
            _webLogsCache.Enqueue(webFormattedMessage);

            // Держим жесткий лимит строго в 13 строк, выбрасывая старое
            while (_webLogsCache.Count > 8)
            {
                _webLogsCache.TryDequeue(out _);
            }
        }
    }

    #endregion


// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region PrintToConsole

    /// <summary>
    /// Выводит форматированную текстовую строку в окно консоли заданным цветом, после чего восстанавливает исходную цветовую палитру терминала.
    /// </summary>
    /// <param name="message">Полностью сформированная текстовая строка для отображения.</param>
    /// <param name="color">Целевой цвет текста <see cref="ConsoleColor"/> (выбирается на основе категории события).</param>
    private static void PrintToConsole(string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    #endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region AppendToFile

    /// <summary>
    /// Специализированный потокобезопасный объект синхронизации для монопольного доступа к файлу лога.
    /// Предотвращает ошибки совместного доступа (IOException) при одновременной записи событий из разных потоков-аккаунтов.
    /// </summary>
    private static readonly System.Threading.Lock _fileLock = new();

    /// <summary>
    /// Выполняет атомарную запись структурированной строки события в лог-файл формата CSV.
    /// Автоматически экранирует разделители и гарантирует сохранение кодировки UTF-8 с BOM-маркером для корректного открытия в Excel.
    /// </summary>
    /// <param name="timestamp">Временная метка события в формате ГГГГ.ММ.ДД чч:мм:сс.</param>
    /// <param name="progversion">Текущая кэшированная версия сборки бота.</param>
    /// <param name="type">Строковое представление категории лога (LogType).</param>
    /// <param name="account">Имя игрового аккаунта-источника.</param>
    /// <param name="system">Звездная система EVE Echoes, в которой находится персонаж.</param>
    /// <param name="ship">Текущий корабль персонажа.</param>
    /// <param name="method">Имя метода, инициировавшего запись лога.</param>
    /// <param name="message">Текст информационного сообщения.</param>
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
        // Защита от сбоев при одновременной записи из нескольких параллельно работающих аккаунтов
        lock (_fileLock) 
        {
            try
            {
                bool fileExists = File.Exists(LogFilePath);
                
                // Защищаем структуру CSV от поломки разделителей, заменяя точки с запятой на запятые
                string safeMessage = message.Replace(";", ",");
                string csvLine = $"{timestamp};{progversion};{type};{account};{system};{ship};{method};{safeMessage}";

                // Создаем UTF8Encoding со специальным флагом 'true', который заставляет C# внедрить BOM-маркер для Excel
                var utf8WithBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

                if (!fileExists)
                {
                    // Если файла нет, инициализируем его структурированной шапкой таблицы с BOM-маркером
                    string header = "Дата;Версия;Тип;Аккаунт;Система;Корабль;Метод;Сообщение" + Environment.NewLine;
                    File.WriteAllText(LogFilePath, header, utf8WithBom);
                }

                // Дописываем новую строку лога, строго сохраняя кодировку UTF-8 с BOM
                File.AppendAllText(LogFilePath, csvLine + Environment.NewLine, utf8WithBom);
            }
            catch (Exception ex)
            {
                // Если запись в файл заблокирована на уровне ОС, выводим аварийный алерт напрямую на экран
                Console.WriteLine($"[ERROR] Ошибка записи в CSV: {ex.Message}");
            }
        }
    }

    #endregion


// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region GetColorForType

    /// <summary>
    /// Возвращает системный цвет консоли (ConsoleColor), соответствующий переданной категории события (LogType).
    /// Используется для цветового разделения информационных, отладочных и критических сообщений на экране.
    /// </summary>
    /// <param name="type">Категория важности события (<see cref="LogType"/>).</param>
    /// <returns>Цвет <see cref="ConsoleColor"/> для окрашивания текста в терминале.</returns>
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

    #endregion


}

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

#region LogType

/// <summary>
/// Категории важности и типов событий для системы логирования и маршрутизации отчетов.
/// Определяет цветовое форматирование в консоли и правила записи в файл CSV.
/// </summary>
public enum LogType
{
    /// <summary>
    /// Стандартные информационные сообщения о текущих штатных процессах работы бота/потока.
    /// </summary>
    Info,

    /// <summary>
    /// Сообщения об успешном завершении конкретной игровой задачи (например, успешный док, завершение варпа).
    /// </summary>
    Success,

    /// <summary>
    /// Важные технические или игровые события, требующие внимания (например, пустая очередь, старт/стоп систем).
    /// </summary>
    Warning,

    /// <summary>
    /// Критические сбои, ошибки WinAPI, ADB или исключения в коде, нарушающие логику сценария.
    /// </summary>
    Error,

    /// <summary>
    /// Отладочные сообщения и сырые аналитические данные, выводимые на экран только в режиме разработки.
    /// </summary>
    Test
}

#endregion


// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

