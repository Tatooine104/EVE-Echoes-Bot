using System;
using System.IO;
using TesseractOCR; // Кроссплатформенный wrapper Tesseract для .NET 9+
using TesseractOCR.Enums;

namespace EVEEchoesBot.Services
{
    public class OcrService : IDisposable
    {
        private readonly Engine _ocrEngine;

        public OcrService()
        {
            // Формируем путь к скопированной в билд папке ресурсов
            string tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources");
            
            // Если вы внутри resources создали отдельную подпапку tessdata, то путь будет:
            // string tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"resources\tessdata");

            if (!Directory.Exists(tessdataPath))
            {
                throw new DirectoryNotFoundException($"[OCR] Критическая ошибка: Папка с языковыми данными не найдена по пути: {tessdataPath}");
            }

            // Инициализация движка: склеиваем языки через плюс, чтобы распознавать eng и rus параллельно
            _ocrEngine = new Engine(tessdataPath, "eng+rus", EngineMode.Default);
        }

        /// <summary>
        /// Распознает текст на основе переданного массива байт (скриншота окна игры).
        /// </summary>
        /// <param name="imageBytes">Массив байт изображения.</param>
        /// <returns>Распознанная UTF-8 строка текста.</returns>
        public string RecognizeText(byte[] imageBytes)
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
                // Используем ваш кастомный логгер
                Console.WriteLine($"[OCR ERROR] Ошибка обработки изображения Tesseract: {ex.Message}");
                return string.Empty;
            }
        }


        public void Dispose()
        {
            // Tesseract использует нативные C++ обертки, их ресурсы нужно освобождать строго вручную!
            _ocrEngine?.Dispose();
        }
    }
}
