using System.Runtime.InteropServices;
using System.Text;

// Замените "LowMiner" на пространство имен вашего проекта, если оно называется иначе
namespace EVEEchoesBot
{
    internal static partial class WinAPI
    {
        // === Системные структуры данных ===

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BITMAPINFOHEADER
        {
            internal uint biSize;
            internal int biWidth;
            internal int biHeight;
            internal ushort biPlanes;
            internal ushort biBitCount;
            internal uint biCompression;
            internal uint biSizeImage;
            internal int biXPelsPerMeter;
            internal int biYPelsPerMeter;
            internal uint biClrUsed;
            internal uint biClrImportant;
        }


        // === Размеры и поиск окон (User32 / DwmApi) ===

        /// <summary>
        /// Получает размеры внутренней рабочей области окна.
        /// </summary>
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        /// <summary>
        /// Находит дескриптор окна по имени класса или заголовку.
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        /// <summary>
        /// Изменяет позицию и размеры указанного окна.
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "MoveWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

        /// <summary>
        /// Получает габариты окна (включая невидимые области Aero/теней).
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "GetWindowRect")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        /// <summary>
        /// Получает реальные физические атрибуты окна (например, границы без учета теней).
        /// </summary>
        [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        internal static partial int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        /// <summary>
        /// Ищет дочернее окно внутри родительского по классу или имени.
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

        /// <summary>
        /// Получает имя системного класса указанного окна.
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int GetClassNameW(IntPtr hWnd, [Out] char[] lpClassName, int nMaxCount);

        internal static int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount)
        {
            if (lpClassName == null || nMaxCount <= 0) return 0;
            char[] buffer = new char[nMaxCount];
            int result = GetClassNameW(hWnd, buffer, nMaxCount);
            if (result > 0)
            {
                lpClassName.Clear();
                lpClassName.Append(buffer, 0, result);
            }
            return result;
        }


        // === Захват экрана и рендеринг (GDI / Контекст устройства) ===

        /// <summary>
        /// Получает контекст устройства (HDC) для всего экрана или конкретного окна.
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "GetDC")]
        internal static partial IntPtr GetDC(IntPtr hWnd);

        /// <summary>
        /// Освобождает контекст устройства (HDC), возвращая память операционной системе.
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "ReleaseDC")]
        internal static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        /// <summary>
        /// Создает контекст устройства в памяти (DC), совместимый с указанным устройством.
        /// </summary>
        [LibraryImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")]
        internal static partial IntPtr CreateCompatibleDC(IntPtr hdc);

        /// <summary>
        /// Создает растровое изображение (Bitmap), совместимое с указанным контекстом устройства.
        /// </summary>
        [LibraryImport("gdi32.dll", EntryPoint = "CreateCompatibleBitmap")]
        internal static partial IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

        /// <summary>
        /// Выбирает объект в указанный контекст устройства (HDC). Новый объект заменяет старый.
        /// </summary>
        [LibraryImport("gdi32.dll", EntryPoint = "SelectObject")]
        internal static partial IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        /// <summary>
        /// Копирует визуальное содержимое окна в указанный контекст устройства (HDC).
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "PrintWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        /// <summary>
        /// Получает биты указанного совместимого растрового изображения и копирует их в буфер.
        /// </summary>
        [LibraryImport("gdi32.dll", EntryPoint = "GetDIBits")]
        internal static partial int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, byte[] lpvBits, ref BITMAPINFOHEADER lpbmi, uint usage);

        /// <summary>
        /// Удаляет логическое перо, кисть, шрифт, растровое изображение или палитру, освобождая ресурсы.
        /// </summary>
        [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DeleteObject(IntPtr ho);

        /// <summary>
        /// Удаляет указанный контекст устройства (DC) из памяти.
        /// </summary>
        [LibraryImport("gdi32.dll", EntryPoint = "DeleteDC")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DeleteDC(IntPtr hdc);


        // === Фоновая отправка событий (Клик/Ввод) ===

        /// <summary>
        /// Помещает сообщение в очередь сообщений связанного с окном потока без ожидания ответа.
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
