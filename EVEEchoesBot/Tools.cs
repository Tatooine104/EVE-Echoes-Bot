namespace EVEEchoesBot
{

    public static class Tools
    {


        // --- Глобальные утилиты ---
        private static readonly Random _random = new();

        // Глобальный список аккаунтов, для которых размер окна уже был успешно подогнан
        public static readonly System.Collections.Generic.HashSet<string> _resizedAccounts = [];

        /// <summary>
        /// Выводит форматированное сообщение в консоль с указанным цветом.
        /// </summary>
        /// <param name="message">Текст сообщения.</param>
        /// <param name="color">Цвет текста (по умолчанию серый).</param>
        public static void ConsolePrint(string message, ConsoleColor color = ConsoleColor.Gray)
        {
            // TODO: Сделать разделение логики по флагу DEBUG: Консоль / Лог
            // TODO: Сделать метод логирования
            // TODO: Сделать типы: Info / Success / Warning / Error           
            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            Console.ResetColor();
        }

        /// <summary>
        /// Аппаратно эмулирует клик мыши на уровне драйвера Windows с помощью SendInput.
        /// Оптимизирован для молниеносного и чёткого нажатия.
        /// </summary>
        public static void SmartClick(IntPtr hWnd, int x, int y, int minSec = 1, int maxSec = 5, int offset = 10)
        {
            if (hWnd == IntPtr.Zero) return;

            // 1. Рандомная задержка перед действием (если параметры 0, выполнится мгновенно)
            if (minSec > 0 || maxSec > 0)
            {
                int initialDelay = GetRandomDelayMs(minSec, maxSec);
                Thread.Sleep(initialDelay);
            }

            // 2. Расчет координат внутри окна со случайным смещением
            int finalX = x + _random.Next(-offset, offset + 1);
            int finalY = y + _random.Next(-offset, offset + 1);

            // 3. Выводим окно эмулятора на передний план
            WinAPI.SetForegroundWindow(hWnd);

            // 4. Переводим относительные координаты окна в реальные экранные пиксели
            if (!WinAPI.GetWindowRect(hWnd, out WinAPI.RECT rect))
            {
                Tools.ConsolePrint("SmartClick | Ошибка: Не удалось получить координаты границ окна.", ConsoleColor.Red);
                return;
            }

            int screenX = rect.Left + finalX;
            int screenY = rect.Top + finalY;

            // 5. Мгновенно перемещаем курсор в точку клика
            WinAPI.SetCursorPos(screenX, screenY);

            // Константы для SendInput
            const uint INPUT_MOUSE = 0;
            const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
            const uint MOUSEEVENTF_LEFTUP = 0x0004;

            // СТРУКТУРА №1: Нажатие левой кнопки
            WinAPI.INPUT inputDown = new() { type = INPUT_MOUSE };
            inputDown.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;

            // СТРУКТУРА №2: Отпускание левой кнопки
            WinAPI.INPUT inputUp = new() { type = INPUT_MOUSE };
            inputUp.mi.dwFlags = MOUSEEVENTF_LEFTUP;

            // 6. ВЫПОЛНЯЕМ КЛИК: отправляем массивы из 1 элемента по отдельности
            // Шаг А: Мгновенно зажимаем кнопку
            WinAPI.SendInput(1, [inputDown], System.Runtime.InteropServices.Marshal.SizeOf<WinAPI.INPUT>());

            // Маленькая реалистичная пауза удержания кнопки (всего 20-40 миллисекунд), чтобы игра успела зафиксировать клик
            Thread.Sleep(_random.Next(20, 41));

            // Шаг Б: Мгновенно отпускаем кнопку
            WinAPI.SendInput(1, [inputUp], System.Runtime.InteropServices.Marshal.SizeOf<WinAPI.INPUT>());

#if DEBUG
            Tools.ConsolePrint($"SmartClick | [ДРАЙВЕР] Быстрый клик отправлен в ({screenX}, {screenY})", ConsoleColor.Yellow);
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
        /// Находит окно по настройкам из конфигурации и подгоняет его размеры только один раз за сессию.
        /// </summary>
        /// <param name="settings">Объект настроек окна.</param>
        /// <returns>Дескриптор окна (IntPtr.Zero, если окно не найдено).</returns>
        public static IntPtr GetWindow(WindowSettings settings)
        {
            // ИСПРАВЛЕНО: Добавлен префикс WinAPI к вызову FindWindow
            IntPtr hWnd = WinAPI.FindWindow(null, settings.WindowTitle);

            if (hWnd != IntPtr.Zero)
            {
                // 1. Проверяем наш глобальный массив (HashSet)
                if (_resizedAccounts.Contains(settings.AccountName))
                {
#if DEBUG
                    // В режиме отладки пишем, что аккаунт уже подгонялся ранее
                    Tools.ConsolePrint($"GetWindow | {settings.AccountName} | Окно уже подгонялось в этой сессии. Шаг пропущен.", ConsoleColor.DarkGray);
#endif
                    return hWnd; // МГНОВЕННЫЙ ВЫХОД, ПОЛНЫЙ ПРОПУСК ЛЮБЫХ ПРОВЕРОК И РЕСАЙЗОВ
                }

                if (settings.Size == null)
                {
                    Tools.ConsolePrint($"GetWindow | Ошибка: В конфиге аккаунта {settings.AccountName} отсутствует блок WindowSettings!", ConsoleColor.Red);
                    return hWnd;
                }

                int targetW = settings.Size.TargetWidth;
                int targetH = settings.Size.TargetHeight;

                // 2. Если аккаунта нет в списке, выполняем подгонку размера
                if (ResizeWindow(hWnd, targetW, targetH))
                {
                    Tools.ConsolePrint($"GetWindow | Аккаунт: {settings.AccountName} | Успех: Окно '{settings.WindowTitle}' подогнано под размер {targetW}x{targetH}", ConsoleColor.Green);

                    // Запоминаем аккаунт в глобальный список, чтобы больше никогда его не трогать
                    _resizedAccounts.Add(settings.AccountName);

                    // Даем окну 300 мс на применение изменений в Windows
                    Thread.Sleep(300);
                }
                else
                {
                    Tools.ConsolePrint($"GetWindow | Аккаунт: {settings.AccountName} | Ошибка: Не удалось изменить размер окна.", ConsoleColor.Red);
                }
            }
            else
            {
                Tools.ConsolePrint($"GetWindow | Ошибка: Окно '{settings.WindowTitle}' для аккаунта {settings.AccountName} не найдено.", ConsoleColor.Red);
            }

            return hWnd;
        }

        /// <summary>
        /// Изменяет размер окна BlueStacks так, чтобы его рабочая область стала заданной ширины и высоты.
        /// </summary>
        /// <param name="hWnd">Дескриптор окна эмулятора.</param>
        /// <param name="targetWidth">Целевая ширина рабочей области.</param>
        /// <param name="targetHeight">Целевая высота рабочей области.</param>
        /// <returns>True, если размер окна успешно изменен, иначе false.</returns>
        static bool ResizeWindow(IntPtr hWnd, int targetWidth, int targetHeight)
        {
            if (hWnd == IntPtr.Zero) return false;

            // ИСПРАВЛЕНО: Указываем структуру из класса WinAPI
            int structSize = System.Runtime.InteropServices.Marshal.SizeOf<WinAPI.RECT>();

            // ИСПРАВЛЕНО: Добавлен префикс WinAPI. и указана структура WinAPI.RECT
            if (WinAPI.DwmGetWindowAttribute(hWnd, WinAPI.DWMWA_EXTENDED_FRAME_BOUNDS, out WinAPI.RECT realWindowRect, structSize) == 0)
            {
                // Панели BlueStacks (верхний заголовок и правое тулбар-меню)
                const int bluestacksToolbarWidth = 33;
                const int bluestacksHeaderHeight = 33;

                // Обычные тонкие рамки изменения размера Windows 10/11
                const int windowsFrameWidth = 2;
                const int windowsFrameHeight = 2;

                // Рассчитываем итоговый полный габарит окна
                int finalWindowWidth = targetWidth + bluestacksToolbarWidth + windowsFrameWidth;
                int finalWindowHeight = targetHeight + bluestacksHeaderHeight + windowsFrameHeight;

                // ИСПРАВЛЕНО: Добавлен префикс WinAPI. для вызова MoveWindow
                // Меняем размер окна, оставляя его на прежних координатах X и Y
                return WinAPI.MoveWindow(hWnd, realWindowRect.Left, realWindowRect.Top, finalWindowWidth, finalWindowHeight, true);
            }

            return false;
        }

    }
}