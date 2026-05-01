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
                Console.WriteLine($"[Ошибка] {TargetWindow} не найден.");
                Console.ResetColor();
            }

            return hWnd;
        }

        static void ClickAt(IntPtr hWnd, int x, int y)
        {
            // Упаковываем координаты: Y в верхние 16 бит, X в нижние 16 бит
            IntPtr lParam = (IntPtr)((y << 16) | (x & 0xFFFF));
            // Посылаем сигнал "кнопка нажата" и "кнопка отпущена"
            PostMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)1, lParam);
            PostMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
            Console.WriteLine($"[Действие] Клик в координаты: {x}, {y}");
        }

    }
}
