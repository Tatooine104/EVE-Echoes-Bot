using System.Runtime.InteropServices;
using System.Text;


namespace EVEEchoesBot.resources;


// [v] TODO 2026.05.30 Привести все тексты логгера к единому стилю
// [v] TODO 2026.06.01 разобраться всё ли из написанного тут актуально 

/// <summary>
/// Класс-оболочка для низкоуровневого взаимодействия с операционной системой Windows через native WinAPI (P/Invoke).
/// Использует современные механизмы Source Generation ([LibraryImport]) для оптимальной производительности.
/// </summary>
internal static partial class WinAPI
{

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    #region Systems Structures & Layouts

    /// <summary>
    /// Представляет двухмерную структуру координат точки (X, Y) на экране.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        /// <summary>Координата по горизонтальной оси X.</summary>
        public int X;
        /// <summary>Координата по вертикальной оси Y.</summary>
        public int Y;
    }

    /// <summary>
    /// Представляет прямоугольную область, заданную координатами левого верхнего и правого нижнего углов.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        /// <summary>Координата X левой границы.</summary>
        internal int Left;
        /// <summary>Координата Y верхней границы.</summary>
        internal int Top;
        /// <summary>Координата X правой границы.</summary>
        internal int Right;
        /// <summary>Координата Y нижней границы.</summary>
        internal int Bottom;
    }

    /// <summary>
    /// Содержит сведения о размерах и цветовом формате аппаратно-независимого растрового изображения (DIB).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        /// <summary>Размер структуры в байтах.</summary>
        internal uint biSize;
        /// <summary>Ширина растрового рисунка в пикселях.</summary>
        internal int biWidth;
        /// <summary>Высота растрового рисунка в пикселях. Отрицательное значение переворачивает массив осей корректно.</summary>
        internal int biHeight;
        /// <summary>Количество плоскостей целевого устройства (всегда 1).</summary>
        internal ushort biPlanes;
        /// <summary>Количество бит на пиксель (например, 32 для ARGB/BGRA формата).</summary>
        internal ushort biBitCount;
        /// <summary>Тип сжатия (0 — без сжатия BI_RGB).</summary>
        internal uint biCompression;
        /// <summary>Размер изображения в байтах.</summary>
        internal uint biSizeImage;
        /// <summary>Горизонтальное разрешение целевого устройства в пикселях на метр.</summary>
        internal int biXPelsPerMeter;
        /// <summary>Вертикальное разрешение целевого устройства в пикселях на метр.</summary>
        internal int biYPelsPerMeter;
        /// <summary>Количество индексов цвета в таблице (0 — по максимуму).</summary>
        internal uint biClrUsed;
        /// <summary>Количество индексов цвета, необходимых для отображения рисунка.</summary>
        internal uint biClrImportant;
    }

    /// <summary>
    /// Обобщенная структура, используемая методом <see cref="SendInput"/> для синтеза аппаратных системных событий (мышь, клавиатура).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        /// <summary>Тип аппаратного события (0 — INPUT_MOUSE).</summary>
        public uint type;
        /// <summary>Низкоуровневая структура параметров эмуляции мыши.</summary>
        public MOUSEINPUT mi;
    }

    /// <summary>
    /// Описывает параметры низкоуровневого эмулирования движения или нажатия кнопок мыши.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        /// <summary>Абсолютная координата или смещение по оси X.</summary>
        public int dx;
        /// <summary>Абсолютная координата или смещение по оси Y.</summary>
        public int dy;
        /// <summary>Дополнительные данные (например, шаги колесика прокрутки).</summary>
        public uint mouseData;
        /// <summary>Командные флаги действия (нажатие, перемещение, отпускание).</summary>
        public uint dwFlags;
        /// <summary>Временная метка события в миллисекундах (0 — системная по умолчанию).</summary>
        public uint time;
        /// <summary>Дополнительное 32- или 64-битное значение, связанное с событием.</summary>
        public IntPtr dwExtraInfo;
    }

    #endregion

    #region Kernel32 Imports

    /// <summary>
    /// Извлекает дескриптор окна (HWND), используемого текущим консольным процессом.
    /// Если приложение запущено без консоли (режим WinExe, системный трей), метод вернет <see cref="IntPtr.Zero"/>.
    /// </summary>
    /// <returns>Дескриптор окна консоли или <see cref="IntPtr.Zero"/>, если реальное окно консоли отсутствует.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "GetConsoleWindow")]
    public static partial IntPtr GetConsoleWindow();

    #endregion

    #region Windows Management API (User32 / DwmApi)

    /// <summary>
    /// Преобразует клиентские координаты (относительно окна) указанной точки в экранные координаты (глобальные).
    /// </summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    /// <summary>
    /// Синтезирует системные аппаратные события нажатия клавиш, движений мыши и кликов на уровне ядра.
    /// </summary>
    [LibraryImport("user32.dll")]
    internal static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    /// <summary>
    /// Выводит поток, создавший указанное окно, на передний план и активирует это окно.
    /// </summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// Перемещает курсор мыши в указанные глобальные экранные координаты.
    /// </summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetCursorPos(int X, int Y);

    /// <summary>
    /// Получает геометрические размеры внутренней чистой рабочей (клиентской) области окна.
    /// </summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// Находит дескриптор главного окна в ОС Windows по имени его системного класса или заголовку.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    /// <summary>
    /// Изменяет позицию, габариты и статус перерисовки указанного окна эмулятора.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "MoveWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

    /// <summary>
    /// Получает габариты границ окна (включая невидимые рамки Aero, размытия и теней).
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// Извлекает текущие физические атрибуты окна диспетчера окон рабочего стола (DWM), включая границы без учета теней.
    /// </summary>
    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    internal static partial int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    /// <summary>
    /// Выполняет циклический поиск дочернего окна внутри родительского по классу или имени.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    /// <summary>
    /// Внутренний импорт для безопасного считывания системного имени класса окна в массив символов.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetClassNameW(IntPtr hWnd, [Out] char[] lpClassName, int nMaxCount);

    /// <summary>
    /// Запрашивает имя класса окна и безопасно записывает его в динамический объект <see cref="StringBuilder"/>.
    /// </summary>
    /// <param name="hWnd">Дескриптор целевого окна.</param>
    /// <param name="lpClassName">Целевой буфер строк для записи.</param>
    /// <param name="nMaxCount">Максимальная длина буфера выделения.</param>
    /// <returns>Количество успешно скопированных символов.</returns>
    // BUG LOW - Ошибка компиляции (Missing Type / Namespace). Метод использует тип `StringBuilder`, но в директивах `using` файла `Program.cs` (где объявлен `partial class Program`) или этого файла отсутствует пространство имен `System.Text`. Если проект компилируется, значит `using System.Text` объявлен глобально в `GlobalUsings.cs`, иначе здесь упадет сборка.
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

    #endregion

    #region Screen Capture & Graphic Context (GDI / Gdi32)

    /// <summary>
    /// Получает контекст устройства (HDC) для вывода графики на экран или в окно.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "GetDC")]
    internal static partial IntPtr GetDC(IntPtr hWnd);

    /// <summary>
    /// Освобождает контекст устройства (HDC), возвращая оперативную память операционной системе.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "ReleaseDC")]
    internal static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    /// <summary>
    /// Создает пустой контекст устройства в памяти (DC), совместимый с указанным HDC.
    /// </summary>
    [LibraryImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")]
    internal static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    /// <summary>
    /// Создает аппаратно-совместимое растровое изображение (Bitmap) на основе контекста устройства.
    /// </summary>
    [LibraryImport("gdi32.dll", EntryPoint = "CreateCompatibleBitmap")]
    internal static partial IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    /// <summary>
    /// Выбирает объект (например, растровый массив или кисть) в указанный контекст устройства (HDC).
    /// </summary>
    /// <summary>
    /// Выбирает объект (например, растровый массив или кисть) в указанный контекст устройства (HDC).
    /// </summary>
    [LibraryImport("gdi32.dll", EntryPoint = "SelectObject")]
    internal static partial IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    /// <summary>
    /// Копирует визуальный графический буфер окна напрямую в указанный контекст памяти (HDC).
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "PrintWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    /// <summary>
    /// Извлекает массив пикселей из совместимого растра и копирует их в буфер обмена в заданном формате.
    /// </summary>
    [LibraryImport("gdi32.dll", EntryPoint = "GetDIBits")]
    internal static partial int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, byte[] lpvBits, ref BITMAPINFOHEADER lpbmi, uint usage);

    /// <summary>
    /// Удаляет логическое перо, кисть, шрифт или растровое изображение, принудительно очищая оперативную память GDI.
    /// </summary>
    [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(IntPtr ho);

    /// <summary>
    /// Удаляет созданный ранее контекст устройства (DC) из памяти.
    /// </summary>
    [LibraryImport("gdi32.dll", EntryPoint = "DeleteDC")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(IntPtr hdc);

    /// <summary>
    /// Отправляет указанное сообщение окну в асинхронном фоновом режиме без ожидания обработки потоком.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    #endregion

    #region WinAPI Architecture Constants

    /// <summary>Константа сообщения Windows: Нажатие левой кнопки мыши (Down).</summary>
    internal const uint WM_LBUTTONDOWN = 0x0201;

    /// <summary>Константа сообщения Windows: Отпускание левой кнопки мыши (Up).</summary>
    internal const uint WM_LBUTTONUP = 0x0202;

    /// <summary>Флаг интерфейса DWM для запроса точных физических границ окна без учета размытий рамок Aero в Windows 10/11.</summary>
    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    /// <summary>Флаг функции PrintWindow для принудительного рендеринга сложного контента игровых эмуляторов (DirectX/OpenGL/Vulkan).</summary>
    internal const uint PW_RENDERFULLCONTENT = 2;

    #endregion

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

    /// <summary>
    /// Автоматически находит дочернее окно ввода или рендеринга графики внутри главного окна эмулятора.
    /// Выполняет каскадный поиск по известным классам ("SubWin", "Form", "RenderWindow"), а в случае неудачи
    /// сканирует все дочерние окна на наличие сигнатур ("Render", "View", "Sub", "Qt") в именах системных классов.
    /// </summary>
    /// <param name="mainHWnd">Дескриптор (Handle) главного окна эмулятора.</param>
    /// <returns>Дескриптор <see cref="IntPtr"/> дочернего графического окна эмулятора. Если совпадений не найдено, возвращает исходный <paramref name="mainHWnd"/>.</returns>
    internal static IntPtr GetInputWindow(IntPtr mainHWnd)
    {
        if (mainHWnd == IntPtr.Zero) return IntPtr.Zero;

        // 1. Попытка быстрого поиска по жестко заданным системным классам популярных эмуляторов
        IntPtr child = FindWindowEx(mainHWnd, IntPtr.Zero, "SubWin", null);
        if (child != IntPtr.Zero) return child;

        child = FindWindowEx(mainHWnd, IntPtr.Zero, "Form", null);
        if (child != IntPtr.Zero) return child;

        child = FindWindowEx(mainHWnd, IntPtr.Zero, "RenderWindow", null);
        if (child != IntPtr.Zero) return child;

        // 2. Фолбек-система: динамическое сканирование всех дочерних окон через итератор
        IntPtr currentChild = FindWindowEx(mainHWnd, IntPtr.Zero, null, null);

        // Инициализируем StringBuilder начальным объемом, чтобы capacity не был равен по умолчанию 16
        System.Text.StringBuilder className = new(256);

        while (currentChild != IntPtr.Zero)
        {
            // Запрашиваем имя системного класса текущего дочернего окна
            GetClassName(currentChild, className, className.Capacity);
            string name = className.ToString();

            // Проверяем наличие ключевых сигнатур графических ядер (BlueStacks, LDPlayer, Nox и др.)
            if (!string.IsNullOrEmpty(name) &&
                (name.Contains("Render", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("View", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Sub", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Qt", StringComparison.OrdinalIgnoreCase)))
            {
                return currentChild;
            }

            // Переходим к следующему дочернему окну на том же уровне иерархии
            currentChild = FindWindowEx(mainHWnd, currentChild, null, null);
        }

        // Если специализированное графическое окно не найдено, возвращаем дескриптор главного окна
        return mainHWnd;
    }

    /// <summary>
    /// Переносит пиксели цветовых данных из исходного контекста устройства в целевой.
    /// Работает на уровне видеокарты, полностью исключая зависания PrintWindow в многопоточном режиме.
    /// </summary>
    // BUG LOW - Импорт метода BitBlt оформлен абсолютно верно. Мое предыдущее замечание о его отсутствии снимается, так как partial-структура класса воссоединила этот метод с вызовом внутри CaptureWindow. Ошибок здесь нет.
    [LibraryImport("gdi32.dll", EntryPoint = "BitBlt")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

// - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -

}
