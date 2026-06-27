using System;
using System.IO;
using System.Threading; // Обязательно для System.Threading.Lock
using TesseractOCR; // Кроссплатформенный wrapper Tesseract для .NET 9+
using TesseractOCR.Enums;


namespace EVEEchoesBot.resources
{
    public sealed class OcrService : IDisposable
    {
        // 1. РЕАЛИЗАЦИЯ THREAD-SAFE SINGLETON (Конструктор делаем приватным)
        private static readonly Lazy<OcrService> _instance = new(() => new OcrService());
        public static OcrService Instance => _instance.Value;

        // 2. Новый объект блокировки из .NET 9 для защиты нативного движка Tesseract
        private readonly Lock _ocrLock = new();
        private readonly Engine _ocrEngine;

        private OcrService()
        {
            // Формируем путь к скопированной в билд папке ресурсов
            string tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources");

            if (!Directory.Exists(tessdataPath))
            {
                throw new DirectoryNotFoundException($"[OCR] Критическая ошибка: Папка с языковыми данными не найдена по пути: {tessdataPath}");
            }

            // Инициализация движка: только английский язык для максимальной скорости и точности
            _ocrEngine = new Engine(tessdataPath, "eng", EngineMode.Default);

            // =========================================================================
            // НАСТРОЙКА БЕЛОГО СПИСКА СИМВОЛОВ ДЛЯ ОПТИМИЗАЦИИ ШРИФТОВ EVE Echoes
            // =========================================================================
            // Разрешаем только английские буквы, цифры, дефис и косую черту '/' (для AU/s, m/s).
            _ocrEngine.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-/");
        }

        /// <summary>
        /// Потокобезопасно распознает текст на основе переданного массива байт (скриншота окна игры).
        /// </summary>
        /// <param name="imageBytes">Массив байт изображения.</param>
        /// <returns>Распознанная UTF-8 строка текста.</returns>
        public string RecognizeText(byte[] imageBytes)
        {
            // BUG HIGH - Потенциальный Hard Deadlock / Thread Pool Starvation (Голодание пула потоков) из-за нативной синхронной блокировки. Класс `Engine` из библиотеки TesseractOCR под капотом вызывает неуправляемые C++ библиотеки (Tesseract DLL). Вызов `_ocrEngine.Process(pixImage)` является тяжелой, чисто процессорной (CPU-bound) и полностью синхронной операцией, которая блокирует поток выполнения. Когда несколько параллельных ботов одновременно пытаются распознать текст на экране (например, сканируют овервью или ники в локале на первом же шаге сценария), они встают в жесткую очередь благодаря `lock (_ocrLock)`. Если один из вызовов нативного C++ метода зависает из-за некорректного формата переданного массива байт или внутреннего сбоя Tesseract, этот лок НИКОГДА не будет отпущен. Все остальные потоки ботов навсегда застрянут на входе в метод `RecognizeText`. Для предотвращения этого вызовы OCR должны быть изолированы, иметь строгие предварительные проверки валидности массива байт и, по-хорошему, выноситься из критических секций синхронизации дерева поведения.
            lock (_ocrLock)
            {
                try
                {
                    // Загружаем массив байт во внутреннюю структуру картинок библиотеки TesseractOCR
                    using var pixImage = TesseractOCR.Pix.Image.LoadFromMemory(imageBytes);

                    // Передаем созданный объект изображения в движок
                    using var page = _ocrEngine.Process(pixImage);

                    // Возвращаем распознанный текст, очищая его от мусорных пробелов
                    return page.Text?.Trim() ?? string.Empty;
                }
                catch (Exception ex)
                {
                    // Используем вывод в консоль из вашего исходного кода
                    Console.WriteLine($"[OCR ERROR] Ошибка обработки изображения Tesseract: {ex.Message}");
                    return string.Empty;
                }
            }
        }

        public void Dispose()
        {
            // Tesseract использует нативные C++ обертки, их ресурсы нужно освобождать строго вручную!
            _ocrEngine?.Dispose();
        }
    }
}
