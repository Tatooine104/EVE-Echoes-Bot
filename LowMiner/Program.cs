using System;
using System.Runtime.InteropServices;

namespace LowMiner
{

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    static partial class Program
    {

        // Для LibraryImport лучше использовать private, а для вызова снаружи — публичную обертку
        [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr FindWindow(
            string? lpClassName,
            string? lpWindowName);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // Константы для Windows сообщений
        const uint WM_LBUTTONDOWN = 0x0201; // Нажатие левой кнопки мыши
        const uint WM_LBUTTONUP = 0x0202;   // Отпускание левой кнопки мыши

        const string TargetWindow = "(MEmu W.01)";

        private static readonly Random _random = new();

        static void Main()
        {
            Console.Title = "EVE Echoes Bot Controller";
            Console.WriteLine("=== EVE Echoes Bot Started ===");

            // const string TargetWindow = "(MEmu W.01)";

            IntPtr hWnd = GetWindow(TargetWindow);

            if (hWnd != IntPtr.Zero)
            {
                Console.WriteLine("Бот готов. Выполняю тестовый клик через 2 секунды...");
                System.Threading.Thread.Sleep(2000); // Пауза, чтобы вы успели переключиться на эмулятор

                // Пример: клик в точку 100x100 внутри окна эмулятора
                ClickAt(hWnd, 40, 600);
            }

            Console.WriteLine("\nНажми любую клавишу для завершения...");
            Console.ReadKey();
        }

        static IntPtr GetWindow(string windowName)
        {
            // Теперь вызываем сгенерированный метод
            IntPtr hWnd = FindWindow(null, windowName);

            if (hWnd != IntPtr.Zero && GetWindowRect(hWnd, out RECT rect))
            {
                // Упрощенная интерполяция
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Успех] {TargetWindow} ({rect.Right - rect.Left}x{rect.Bottom - rect.Top})");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Ошибка] {TargetWindow} не найдено.");
                Console.ResetColor();
            }

            return hWnd;
        }

        /// <summary>
        /// Имитирует клик левой кнопкой мыши по заданным координатам в конкретном окне.
        /// </summary>
        /// <param name="hWnd">Дескриптор (Handle) окна эмулятора</param>
        /// <param name="x">Координата X</param>
        /// <param name="y">Координата Y</param>
        static void ClickAt(IntPtr hWnd, int x, int y)
        {
            // Упаковываем координаты: Y в верхние 16 бит, X в нижние 16 бит
            IntPtr lParam = (IntPtr)((y << 16) | (x & 0xFFFF));

            // Посылаем сигнал "кнопка нажата" (wParam 1 означает левую кнопку)
            PostMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)1, lParam);
            // Посылаем сигнал "кнопка отпущена"
            PostMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, lParam);

#if DEBUG
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Действие] Клик в координаты: {x}, {y}");
#endif
        }

        /// <summary>
        /// Генерирует случайную задержку в миллисекундах на основе заданного диапазона секунд.
        /// </summary>
        /// <param name="minSeconds">Минимальный порог задержки в секундах.</param>
        /// <param name="maxSeconds">Максимальный порог задержки в секундах.</param>
        /// <returns>Целое число миллисекунд для использования в Thread.Sleep.</returns>
        static int GetRandomDelayMs(int minSeconds = 1, int maxSeconds = 7)
        {
            // Рассчитываем случайное значение и переводим в миллисекунды
            return _random.Next(minSeconds, maxSeconds + 1) * 1000;
        }

        /// <summary>
        /// Выполняет имитацию человеческого клика: выдерживает паузу, 
        /// добавляет случайное смещение к координатам и нажимает кнопку мыши.
        /// </summary>
        /// <param name="hWnd">Дескриптор окна для отправки сообщения.</param>
        /// <param name="x">Базовая координата X.</param>
        /// <param name="y">Базовая координата Y.</param>
        /// <param name="minSec">Минимальная задержка перед кликом (сек).</param>
        /// <param name="maxSec">Максимальная задержка перед кликом (сек).</param>
        /// <param name="offset">Максимальное отклонение от точки в пикселях (в обе стороны).</param>
        static void SmartClick(IntPtr hWnd, int x, int y, int minSec = 1, int maxSec = 5, int offset = 10)
        {
            // 1. Рандомная задержка перед действием
            int initialDelay = GetRandomDelayMs(minSec, maxSec);
        #if DEBUG
            Console.WriteLine($"[SmartClick] Ожидание перед кликом: {initialDelay} мс...");
        #endif
            Thread.Sleep(initialDelay);

            // 2. Расчет координат с плавающим смещением
            // Используем offset для задания диапазона [-offset, +offset]
            int finalX = x + _random.Next(-offset, offset + 1);
            int finalY = y + _random.Next(-offset, offset + 1);

            // 3. Подготовка данных и отправка клика
            IntPtr lParam = (IntPtr)((finalY << 16) | (finalX & 0xFFFF));

            // Нажатие (wParam 1 = левая кнопка)
            PostMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)1, lParam);

            // Короткая пауза удержания кнопки (30-100 мс) для реалистичности
            Thread.Sleep(_random.Next(30, 101));

            // Отпускание
            PostMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, lParam);

        #if DEBUG
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Действие] Клик в ({finalX}, {finalY}) со смещением {offset}");
        #endif
        }

    }
}
