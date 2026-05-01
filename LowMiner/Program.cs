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

        const string TargetWindow = "(MEmu W.01)";

        static void Main()
        {
            Console.Title = "EVE Echoes Bot Controller";
            Console.WriteLine("=== EVE Echoes Bot Started ===");

            // const string TargetWindow = "(MEmu W.01)";

            IntPtr hWnd = GetWindow(TargetWindow);

            if (hWnd != IntPtr.Zero)
            {
                Console.WriteLine("Бот готов к работе. Окно захвачено.");
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
    }
}
