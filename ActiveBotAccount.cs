using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using static EVEEchoesBot.Program;

namespace EVEEchoesBot
{

    public class ActiveBotAccount
    {
        public WindowSettings Settings { get; }
        public IntPtr Hwnd { get; set; }

        public AccountTask CurrentTask { get; set; }

        private CancellationTokenSource? _accountCts;

        public ActiveBotAccount(WindowSettings settings)
        {
            Settings = settings;

            string firstTaskStr = settings.FirstTask ?? "Undocking";
            CurrentTask = firstTaskStr switch
            {
                "Undocking"         => AccountTask.Undocking,
                "GoToBelt"          => AccountTask.GoToBelt,
                "Mining"            => AccountTask.Mining,
                "GoToStation"       => AccountTask.GoToStation,
                "Unloading"         => AccountTask.Unloading,
                "CheckSecurity"     => AccountTask.CheckSecurity,
                "CheckYourOwnState" => AccountTask.CheckYourOwnState,
                _                   => AccountTask.CheckYourOwnState
            };
        }

        public void Start(CancellationToken globalToken)
        {
            _accountCts = CancellationTokenSource.CreateLinkedTokenSource(globalToken);

            // Передаем токен созданной связки вторым параметром в Task.Run
            Task.Run(() => RunLoopAsync(_accountCts.Token), _accountCts.Token);
        }

        public void Stop() => _accountCts?.Cancel();

private async Task RunLoopAsync(CancellationToken token)
{
    Logger.Log($"[{Settings.Name}] Поток запущен. Начинаю непрерывный мониторинг безопасности...", LogType.Info);

    // Принудительно выставляем стартовый стейт на проверку безопасности, так как в GameUi есть все элементы для этого
    CurrentTask = AccountTask.CheckSecurity;

    while (!token.IsCancellationRequested)
    {
        try
        {
            switch (CurrentTask)
            {
                case AccountTask.CheckSecurity:
                    // Вызываем асинхронную проверку безопасности для ЭТОГО бота
                    bool isSecurityOk = await CheckSecurityStatusAsync(token);

                    if (isSecurityOk)
                    {
                        // Если в системе чисто, бот пишет лог и уходит на короткую паузу
                        Logger.Log($"[{Settings.Name}] Локал-чат проверен. Всё спокойно.", LogType.Success);
                    }
                    else
                    {
                        // Если локал-чат не найден или в системе угроза (метод CheckSecurityStatusAsync 
                        // сам внутри вызовет RunAliChatWarningAsync, если увидит врагов)
                        Logger.Log($"[{Settings.Name}] Внимание: Требуется повторная проверка безопасности.", LogType.Warning);
                    }
                    break;

                default:
                    // Если вдруг у аккаунта включится стейт, для которого нет кнопок, 
                    // мы просто сбросим его обратно на проверку безопасности
                    CurrentTask = AccountTask.CheckSecurity;
                    break;
            }

            // Каждое окно проверяет свою игру раз в 5 секунд.
            // Потоки работают параллельно, не мешая друг другу!
            await Task.Delay(TimeSpan.FromSeconds(5), token);
        }
        catch (TaskCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            Logger.Log($"[{Settings.Name}] Ошибка цикла мониторинга: {ex.Message}", LogType.Error);
            await Task.Delay(5000, token);
        }
    }

    Logger.Log($"[{Settings.Name}] Поток мониторинга остановлен.", LogType.Info);
}



#region CheckSecurityStatus 


        private async Task<bool> CheckSecurityStatusAsync(CancellationToken token)
        {
            // Вместо Program._currentAccount или загрузки конфига используем данные ЭТОГО экземпляра (this)
            Logger.Log($"[{Settings.Name}] Запуск метода CheckSecurityStatus.", LogType.Test);

            if (Hwnd == IntPtr.Zero)
            {
                Logger.Log($"[{Settings.Name}] Ошибка CheckSecurityStatus: Окно целевой программы не найдено.", LogType.Error);
                // IsSave = false; // Если это общее свойство, управляйте им через экземпляр или класс настроек
                return false;
            }

            // Использование путей к шаблонам
            string pathImg1 = Path.Combine(TemplatesDir, "imgLocalChatHead.png");
            string pathImg2 = Path.Combine(TemplatesDir, "imgLocalChatIcon.png");

            // Области поиска
            Rect localRegion1 = new(5, 5, 500, 715);
            Rect localRegion2 = new(5, 650, 100, 120);

            string debugDir = Path.GetFullPath(Path.Combine(TemplatesDir, "..", "DebugScreenshots"));

            // Мы убрали внутренний while (!_cts.Token.IsCancellationRequested), 
            // так как этот метод вызывается внутри главного цикла RunLoopAsync каждого бота

            using Mat? screenshot = Tools.CaptureWindow(Hwnd);
            if (screenshot?.Empty() is not false || screenshot.Width <= 0 || screenshot.Height <= 0)
            {
                Logger.Log($"[{Settings.Name}] Ошибка CheckSecurityStatus: Не удалось сделать скриншот окна.", LogType.Error);
                return false;
            }

            Rect safeRegion1 = Tools.ClampRegion(localRegion1, screenshot.Width, screenshot.Height);
            Rect safeRegion2 = Tools.ClampRegion(localRegion2, screenshot.Width, screenshot.Height);

            if (safeRegion1.Width <= 0 || safeRegion1.Height <= 0 || safeRegion2.Width <= 0 || safeRegion2.Height <= 0)
            {
                Logger.Log($"[{Settings.Name}] Заданные Rect выходят за рамки окна! Размер скриншота: {screenshot.Width}x{screenshot.Height}", LogType.Error);
                return false;
            }

            // ==========================================
            // ЭТАП 1: Ищем Изображение 1 (Шапка чата)
            // ==========================================
            Point? foundImg1 = Tools.FindTemplateInRegion(screenshot, pathImg1, safeRegion1, 0.80);

            if (foundImg1.HasValue)
            {
        #if DEBUG
                try
                {
                    using Mat cropped = new(screenshot, safeRegion1);
                    Directory.CreateDirectory(debugDir);
                    Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatHead_FOUND.png"), cropped);
                }
                catch (Exception ex) { Logger.Log($"Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
        #endif

                // Важно: ваш метод RunLocalCheck должен поддерживать многопоточность (принимать hWnd или screenshot)
                if (RunLocalCheck(screenshot, safeRegion1)) return true;
                return true;
            }

        #if DEBUG
            try
            {
                using Mat cropped = new(screenshot, safeRegion1);
                Directory.CreateDirectory(debugDir);
                Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatHead_NOT_FOUND.png"), cropped);
            }
            catch (Exception ex) { Logger.Log($"Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
        #endif

            // ==========================================
            // ЭТАП 2: Ищем Изображение 2 (Иконка)
            // ==========================================
            Point? foundImg2 = Tools.FindTemplateInRegion(screenshot, pathImg2, safeRegion2, 0.80);

            if (foundImg2.HasValue)
            {
                Logger.Log($"[{Settings.Name}] {pathImg2} НАЙДЕНО. Выполняю клик.", LogType.Test);

        #if DEBUG
                try
                {
                    using Mat cropped = new(screenshot, safeRegion2);
                    Directory.CreateDirectory(debugDir);
                    Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatIcon_FOUND.png"), cropped);
                }
                catch (Exception ex) { Logger.Log($"Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
        #endif

                Tools.SmartClick(Hwnd, foundImg2.Value.X, foundImg2.Value.Y, minSec: 0, maxSec: 0, offset: 2);

                // Вместо Thread.Sleep используем неблокирующий Task.Delay
                await Task.Delay(3000, token);

                using Mat? freshScreenshot = Tools.CaptureWindow(Hwnd);
                if (freshScreenshot?.Empty() is not false) return true;

                Rect freshSafeRegion1 = Tools.ClampRegion(localRegion1, freshScreenshot.Width, freshScreenshot.Height);
                Point? retryImg1 = Tools.FindTemplateInRegion(freshScreenshot, pathImg1, freshSafeRegion1, 0.80);

                if (retryImg1.HasValue)
                {
                    if (RunLocalCheck(freshScreenshot, freshSafeRegion1)) return true;
                }
                else
                {
        #if DEBUG
                    try
                    {
                        using Mat cropped = new(freshScreenshot, freshSafeRegion1);
                        Directory.CreateDirectory(debugDir);
                        Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatHead_AFTER_CLICK_NOT_FOUND.png"), cropped);
                    }
                    catch (Exception ex) { Logger.Log($"Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
        #endif
                }

                return true;
            }

        #if DEBUG
            try
            {
                using Mat cropped = new(screenshot, safeRegion2);
                Directory.CreateDirectory(debugDir);
                Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatIcon_NOT_FOUND.png"), cropped);
            }
            catch (Exception ex) { Logger.Log($"Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
        #endif

            // Строка интерполяции исправлена с ${pathImg1} на правильную {Path.GetFileName(pathImg1)}
            Logger.Log($"[{Settings.Name}] Шаблоны чата не найдены. Безопасность под угрозой!", LogType.Error);
            return false;
        }

#endregion

#region RunLocalCheck

        private bool RunLocalCheck(Mat screenshot, Rect searchRegion)
        {
            Rect safeSearchRegion = Tools.ClampRegion(searchRegion, screenshot.Width, screenshot.Height);

            string[] templates = ["imgLocalCriminal.png", "imgLocalMinus.png", "imgLocalNeutral.png"];
            int foundCount = 0;

            foreach (string templateName in templates)
            {
                string fullTemplatePath = Path.Combine(Program.TemplatesDir, templateName);
                if (!File.Exists(fullTemplatePath)) continue;

                Point? foundPoint = Tools.FindTemplateInRegion(screenshot, fullTemplatePath, safeSearchRegion, 0.80);

                if (foundPoint.HasValue)
                {
                    foundCount++;
        #if DEBUG
                    try
                    {
                        using Mat croppedRegion = new(screenshot, safeSearchRegion);
                        string debugDir = Path.GetFullPath(Path.Combine(Program.TemplatesDir, "..", "DebugScreenshots"));
                        Directory.CreateDirectory(debugDir);
                        string debugPath = Path.Combine(debugDir, $"{Settings.Name}_{Path.GetFileNameWithoutExtension(templateName)}_FOUND.png");
                        Cv2.ImWrite(debugPath, croppedRegion);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[{Settings.Name}][Debug] Ошибка сохранения скриншота: {ex.Message}", LogType.Warning);
                    }
        #endif
                }
            }

            // СТРОГАЯ ПРОВЕРКА БЕЗОПАСНОСТИ:
            if (foundCount == 3)
            {
                // Все три маркера на месте (Criminal, Minus, Neutral) — значит в локале только свои, ПОСТОРОННИХ НЕТ
                this.IsSaveLocal = true;
                return true; // БЕЗОПАСНО
            }

            if (foundCount > 0 && foundCount < 3)
            {
                // Каких-то маркеров не хватает -> появились минуса или нейтралы!
                this.IsSaveLocal = false; // Триггерит панику и AliChatWarning внутри свойства IsSaveLocal
                return false; // НЕ БЕЗОПАСНО!
            }

            Logger.Log($"[{Settings.Name}] Ни один из трех шаблонов безопасности не найден на экране. Сброс...", LogType.Warning);
            return false; // Интерфейс не готов или закрыт
}


#endregion

#region AliChatWarning

private async Task RunAliChatWarningAsync(CancellationToken token)
{
    Logger.Log($"[{Settings.Name}] Запуск метода AliChatWarning.", LogType.Test);

    if (Hwnd == IntPtr.Zero)
    {
        Logger.Log($"[{Settings.Name}] Целевое окно программы не найдено. Прерывание цепочки!", LogType.Error);
        return;
    }

#if DEBUG
    Logger.Log($"[{Settings.Name}] Запуск цепочки кликов оповещения альянса...", LogType.Test);
#endif

    // 1. Первый клик: Открываем меню чатов
    Hwnd.ClickTo(GameUi.ChatsInterface);
    await Task.Delay(3000, token);

    using Mat? currentScreenshot = Tools.CaptureWindow(Hwnd);

    if (currentScreenshot?.Empty() is not false || currentScreenshot.Width <= 0 || currentScreenshot.Height <= 0)
    {
        Logger.Log($"[{Settings.Name}] Не удалось сделать свежий скриншот экрана эмулятора. Прерывание!", LogType.Error);
        return;
    }

    try
    {
        string imgPath1 = Path.Combine(Program.TemplatesDir, "imgAliChatENG.png");
        string imgPath2 = Path.Combine(Program.TemplatesDir, "imgAliChatRUS.png");

        if (!File.Exists(imgPath1) || !File.Exists(imgPath2))
        {
            Logger.Log($"[{Settings.Name}] Файлы шаблонов чата (ENG/RUS) отсутствуют на диске. Прерывание!", LogType.Error);
            return;
        }

        Rect searchRegion = new(5, 220, 300, 500);
        Rect safeSearchRegion = Tools.ClampRegion(searchRegion, currentScreenshot.Width, currentScreenshot.Height);

        Logger.Log($"[{Settings.Name}] Ищу маркеры языков меню чата...", LogType.Info);
        Point? foundEng = Tools.FindTemplateInRegion(currentScreenshot, imgPath1, safeSearchRegion, 0.85);
        Point? foundRus = Tools.FindTemplateInRegion(currentScreenshot, imgPath2, safeSearchRegion, 0.85);

        if (!foundEng.HasValue && !foundRus.HasValue)
        {
            Logger.Log($"[{Settings.Name}] Шаблоны чата не обнаружены. Интерфейс не готов. Прерывание!", LogType.Error);

#if DEBUG
            try
            {
                using Mat cropped = new(currentScreenshot, safeSearchRegion);
                string debugDir = Path.GetFullPath(Path.Combine(Program.TemplatesDir, "..", "DebugScreenshots"));
                Directory.CreateDirectory(debugDir);
                Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgAliChat_NOT_FOUND.png"), cropped);
            }
            catch (Exception ex) { Logger.Log($"[{Settings.Name}][Debug] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
#endif
            return;
        }

        Point targetPoint = new();

        if (foundEng.HasValue)
        {
            Logger.Log($"[{Settings.Name}] УСПЕХ: Найден ENG чат в точке X={foundEng.Value.X}, Y={foundEng.Value.Y}", LogType.Info);
            targetPoint = new Point(foundEng.Value.X, foundEng.Value.Y);
        }
        else if (foundRus.HasValue)
        {
            Logger.Log($"[{Settings.Name}] УСПЕХ: Найден RUS чат в точке X={foundRus.Value.X}, Y={foundRus.Value.Y}", LogType.Info);
            targetPoint = new Point(foundRus.Value.X, foundRus.Value.Y);
        }

        // 2. Второй клик: Активируем чат альянса по динамическим координатам в ФОНЕ
        Logger.Log($"[{Settings.Name}][Клик] Отправка фонового клика по динамическим координатам: X={targetPoint.X}, Y={targetPoint.Y}", LogType.Info);
        Tools.SmartClick(Hwnd, targetPoint.X, targetPoint.Y, minSec: 1, maxSec: 3, offset: 3);
    }
    catch (Exception ex)
    {
        Logger.Log($"[{Settings.Name}] Критический сбой анализа экрана: {ex.Message}", LogType.Error);
        return;
    }

    // Ждем выполнения анимаций интерфейса игры EVE Echoes
    await Task.Delay(1500, token);

    // 3. Третий клик: Открываем меню ввода чата
    Hwnd.ClickTo(GameUi.ChatInputMenu);
    await Task.Delay(1000, token);

    // 4. Четвертый клик: Открываем меню быстрых сообщений
    Hwnd.ClickTo(GameUi.ChatFastInput);
    await Task.Delay(1000, token);

    // 5. Пятый клик: Открываем вкладку данных разведки
    Hwnd.ClickTo(GameUi.ChatInform);
    await Task.Delay(1000, token);

    // 6. Шестой клик: Выбираем статус-сообщение "Scout"
    Hwnd.ClickTo(GameUi.ChatMessScout);
    await Task.Delay(1000, token);

    // 7. Седьмой клик: Закрываем область ввода
    Hwnd.ClickTo(GameUi.WindowCenter);
    await Task.Delay(1000, token);

    // 8. Восьмой клик: Нажимаем кнопку "Отправить"
    Hwnd.ClickTo(GameUi.ChatButtSend);
    await Task.Delay(1000, token);

    // 9. Девятый клик: Закрываем интерфейс чатов
    Hwnd.ClickTo(GameUi.WindowCenter);

    Logger.Log($"[{Settings.Name}] Цепочка кликов экстренного оповещения AliChatWarning успешно выполнена.", LogType.Success);
}

#endregion

    private bool? _isSaveLocal;

    public bool? IsSaveLocal
    {
        get => _isSaveLocal;
        set
        {
            if (_isSaveLocal == value) return;

            _isSaveLocal = value;

            if (_isSaveLocal is false)
            {
                Logger.Log($"[{Settings.Name}] ОПАСНОСТЬ!!! В системе посторонние!", LogType.Warning);

                // ИСПРАВЛЕНО: передаем правильное имя асинхронного метода 
                // и пробрасываем пустой токет отмены CancellationToken.None
                Task.Run(() => RunAliChatWarningAsync(CancellationToken.None));
            }
            else if (_isSaveLocal is true)
            {
                Logger.Log($"[{Settings.Name}] В системе нет посторонних.", LogType.Success);
            }
        }
    }


    }

    // Перечисление для задач (что делать боту дальше) 
    // [v] Продумать список возможных действий
    // [v] Добавить действие "Осмотреться"
    public enum AccountTask
    {
        Undocking,        // Выйти из дока
        GoToBelt,         // Отправиться в зону добычи
        Mining,           // Добывать руду
        GoToStation,      // Вернуться на станцию
        Unloading,        // Выгрузить руду на станцию
        CheckSecurity,    // Проверить статус безопастности
        CheckYourOwnState // Проверить текущее состояние
    }


}