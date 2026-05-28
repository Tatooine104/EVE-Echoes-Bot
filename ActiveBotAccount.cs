using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using static EVEEchoesBot.Program;
using static EVEEchoesBot.Tools;

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
            Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Поток запущен. Начинаю непрерывный мониторинг безопасности...", LogType.Info);
            
            // Принудительно стартуем с проверки безопасности
            CurrentTask = AccountTask.CheckSecurity;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    switch (CurrentTask)
                    {
                        case AccountTask.CheckSecurity:
                            // Проверяем безопасность локал-чата
                            bool isSystemPerfectlySafe = await CheckSecurityStatusAsync(token);

                            if (isSystemPerfectlySafe)
                            {
                                // Сработает ТОЛЬКО если найдены все 3 маркера (в системе только свои)
                                Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Локал-чат проверен. Всё спокойно.", LogType.Success);
                            }
                            else
                            {
                                // Сработает, если хотя бы один маркер пропал (найден минус или нейтрал)
                                Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] ОБНАРУЖЕНА УГРОЗА! Приостанавливаю мониторинг. Перехожу к оповещению альянса.", LogType.Error);
                                
                                // Инициализируем действие: переключаем стейт-машину на задачу отправки варнинга
                                // (Используем стейт CheckYourOwnState как триггер макроса оповещения)
                                CurrentTask = AccountTask.CheckYourOwnState; 
                            }
                            break;

                        case AccountTask.CheckYourOwnState:
                            // ВЫПОЛНЯЕМ ЦЕПОЧКУ КЛИКОВ ОПОВЕЩЕНИЯ В АЛЬЯНС-ЧАТ
                            // Ключевое слово 'await' заставит этот поток ЖДАТЬ (около 15 секунд), 
                            // пока бот полностью прокликает все меню. Проверка локала в этот момент полностью остановлена!
                            await RunAliChatWarningAsync(token);
                            
                            // После того, как варнинг успешно отправлен, возвращаем бота обратно на мониторинг локала
                            CurrentTask = AccountTask.CheckSecurity;
                            break;

                        default:
                            CurrentTask = AccountTask.CheckSecurity;
                            break;
                    }

                    // Частота опроса стейт-машины (каждые 5 секунд бот заходит в свой текущий case)
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Ошибка цикла мониторинга: {ex.Message}", LogType.Error);
                    await Task.Delay(5000, token);
                }
            }

            Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Поток мониторинга остановлен.", LogType.Info);
        }







#region CheckSecurityStatus 


        private async Task<bool> CheckSecurityStatusAsync(CancellationToken token)
        {
            Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Запуск метода CheckSecurityStatus.", LogType.Test);

            if (Hwnd == IntPtr.Zero)
            {
                Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Ошибка CheckSecurityStatus: Окно целевой программы не найдено.", LogType.Error);
                return false;
            }

            // Использование путей к шаблонам (убедитесь, что Program.TemplatesDir доступен)
            string pathImg1 = Path.Combine(Program.TemplatesDir, "imgLocalChatHead.png");
            string pathImg2 = Path.Combine(Program.TemplatesDir, "imgLocalChatIcon.png");

            // Области поиска
            Rect localRegion1 = new(5, 5, 500, 720);
            Rect localRegion2 = new(5, 650, 100, 120);

            string debugDir = Path.GetFullPath(Path.Combine(Program.TemplatesDir, "..", "DebugScreenshots"));

            using Mat? screenshot = Tools.CaptureWindow(Hwnd);
            if (screenshot?.Empty() is not false || screenshot.Width <= 0 || screenshot.Height <= 0)
            {
                Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Ошибка CheckSecurityStatus: Не удалось сделать скриншот окна.", LogType.Error);
                return false;
            }

            Rect safeRegion1 = Tools.ClampRegion(localRegion1, screenshot.Width, screenshot.Height);
            Rect safeRegion2 = Tools.ClampRegion(localRegion2, screenshot.Width, screenshot.Height);

            if (safeRegion1.Width <= 0 || safeRegion1.Height <= 0 || safeRegion2.Width <= 0 || safeRegion2.Height <= 0)
            {
                Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Заданные Rect выходят за рамки окна! Размер скриншота: {screenshot.Width}x{screenshot.Height}", LogType.Error);
                return false;
            }

            // ==========================================
            // ЭТАП 1: Ищем Шапку чата (Развернун ли чат?)
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
                catch (Exception ex) { Logger.Log($"[{Settings.Name}|{Settings.EVESystem}][Debug] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
        #endif

                // ИСПРАВЛЕНО: Прямо возвращаем результат глубокой проверки (true если все 3 маркера на месте, false если меньше)
                return RunLocalCheck(screenshot, safeRegion1);
            }

        #if DEBUG
            try
            {
                using Mat cropped = new(screenshot, safeRegion1);
                Directory.CreateDirectory(debugDir);
                Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatHead_NOT_FOUND.png"), cropped);
            }
            catch (Exception ex) { Logger.Log($"[{Settings.Name}|{Settings.EVESystem}][Debug] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
        #endif

            // ==========================================
            // ЭТАП 2: Чат свернут, ищем Иконку для разворачивания
            // ==========================================
            Point? foundImg2 = Tools.FindTemplateInRegion(screenshot, pathImg2, safeRegion2, 0.80);

            if (foundImg2.HasValue)
            {
                Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Чат свернут. Найдена иконка {Path.GetFileName(pathImg2)}. Пробую развернуть...", LogType.Test);

        #if DEBUG
                try
                {
                    using Mat cropped = new(screenshot, safeRegion2);
                    Directory.CreateDirectory(debugDir);
                    Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatIcon_FOUND.png"), cropped);
                }
                catch (Exception ex) { Logger.Log($"[{Settings.Name}|{Settings.EVESystem}][Debug] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
        #endif

                // Кликаем по иконке через ADB, передавая порт этого бота
                Tools.SmartClick(foundImg2.Value.X, foundImg2.Value.Y, minSec: 0, maxSec: 0, offset: 2, adbPort: Settings.AdbPort);

                // Даем игре время проиграть анимацию развертывания чата
                await Task.Delay(3000, token);

                using Mat? freshScreenshot = Tools.CaptureWindow(Hwnd);
                if (freshScreenshot?.Empty() is not false) return false;

                Rect freshSafeRegion1 = Tools.ClampRegion(localRegion1, freshScreenshot.Width, freshScreenshot.Height);
                Point? retryImg1 = Tools.FindTemplateInRegion(freshScreenshot, pathImg1, freshSafeRegion1, 0.80);

                if (retryImg1.HasValue)
                {
                    // Чат успешно открылся, возвращаем РЕАЛЬНЫЙ результат проверки локала
                    return RunLocalCheck(freshScreenshot, freshSafeRegion1);
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
                    catch (Exception ex) { Logger.Log($"[{Settings.Name}|{Settings.EVESystem}][Debug] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
        #endif
                }

                // Если кликнули, но чат так и не открылся — система не безопасна
                return false;
            }

        #if DEBUG
            try
            {
                using Mat cropped = new(screenshot, safeRegion2);
                Directory.CreateDirectory(debugDir);
                Cv2.ImWrite(Path.Combine(debugDir, $"{Settings.Name}_imgLocalChatIcon_NOT_FOUND.png"), cropped);
            }
            catch (Exception ex) { Logger.Log($"[{Settings.Name}|{Settings.EVESystem}][Debug] Ошибка сохранения кадра: {ex.Message}", LogType.Warning); }
        #endif

            Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Шаблоны чата не найдены. Безопасность под угрозой!", LogType.Error);
            return false;
        }


#endregion

#region RunLocalCheck

        private bool RunLocalCheck(Mat screenshot, Rect searchRegion)
        {
            Rect safeSearchRegion = ClampRegion(searchRegion, screenshot.Width, screenshot.Height);

            // Список трех обязательных маркеров, которые должны быть ВСЕГДА для статуса "Безопасно"
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
                        Logger.Log($"[{Settings.Name}|{Settings.EVESystem}][Debug] Ошибка сохранения скриншота: {ex.Message}", LogType.Warning);
                    }
        #endif
                }
            }

            // 1. ИДЕАЛЬНАЯ БЕЗОПАСНОСТЬ: Найдены строго все 3 маркера
            if (foundCount == 3)
            {
                this.IsSaveLocal = true;
                return true; // БЕЗОПАСНО, в системе только свои
            }

            // 2. ОПАСНОСТЬ: Часть маркеров пропала (хотя бы один отсутствует, но интерфейс частично виден)
            if (foundCount > 0 && foundCount < 3)
            {
                this.IsSaveLocal = false; // Включает тревогу и ставит статус ОПАСНОСТЬ
                return false; // НЕ БЕЗОПАСНО, уходим в панику
            }

            // 3. СБОЙ ИНТЕРФЕЙСА: foundCount == 0 (Ни один маркер не найден, чат перекрылся или закрылся)
            Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Маркеры безопасности полностью пропали с экрана. Интерфейс не готов!", LogType.Warning);
            
            // При полном пропадании интерфейса мы тоже обязаны вернуть false, 
            // чтобы бот не думал, что всё безопасно, а повторил попытку анализа или развернул чат заново.
            return false; 
        }



#endregion

#region AliChatWarning

private async Task RunAliChatWarningAsync(CancellationToken token)
{
    Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Запуск метода AliChatWarning.", LogType.Test);

    if (Hwnd == IntPtr.Zero)
    {
        Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Целевое окно программы не найдено. Прерывание цепочки!", LogType.Error);
        return;
    }

#if DEBUG
    Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Запуск цепочки кликов оповещения альянса...", LogType.Test);
#endif

    // 1. Первый клик: Пробуем открыть меню чатов
    this.ClickTo(GameUi.ChatsInterface);
    await Task.Delay(3500, token); // Даем время на отрисовку интерфейса

    Mat? currentScreenshot = Tools.CaptureWindow(Hwnd);
    if (currentScreenshot?.Empty() is not false || currentScreenshot.Width <= 0 || currentScreenshot.Height <= 0)
    {
        Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Не удалось сделать свежий скриншот экрана эмулятора. Прерывание!", LogType.Error);
        currentScreenshot?.Dispose();
        return;
    }

    string imgPath1 = Path.Combine(Program.TemplatesDir, "imgAliChatENG.png");
    string imgPath2 = Path.Combine(Program.TemplatesDir, "imgAliChatRUS.png");

    if (!File.Exists(imgPath1) || !File.Exists(imgPath2))
    {
        Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Файлы шаблонов чата (ENG/RUS) отсутствуют на диске. Прерывание!", LogType.Error);
        currentScreenshot.Dispose();
        return;
    }

    Rect searchRegion = new(5, 220, 300, 500);
    Rect safeSearchRegion = Tools.ClampRegion(searchRegion, currentScreenshot.Width, currentScreenshot.Height);

    Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Ищу маркеры языков меню чата...", LogType.Info);
    Point? foundEng = Tools.FindTemplateInRegion(currentScreenshot, imgPath1, safeSearchRegion, 0.85);
    Point? foundRus = Tools.FindTemplateInRegion(currentScreenshot, imgPath2, safeSearchRegion, 0.85);

    // ==========================================
    // ПОПЫТКА №2: Если чат не открылся с первого раза
    // ==========================================
    if (!foundEng.HasValue && !foundRus.HasValue)
    {
        Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Чат не открылся с первой попытки. Пробую кликнуть повторно...", LogType.Warning);
        currentScreenshot.Dispose(); // Освобождаем старый неудачный кадр

        this.ClickTo(GameUi.ChatsInterface);
        await Task.Delay(4000, token); // Даем чуть больше времени на анимацию

        currentScreenshot = Tools.CaptureWindow(Hwnd);
        if (currentScreenshot?.Empty() is not false)
        {
            Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Не удалось сделать повторный скриншот. Прерывание!", LogType.Error);
            currentScreenshot?.Dispose();
            return;
        }

        foundEng = Tools.FindTemplateInRegion(currentScreenshot, imgPath1, safeSearchRegion, 0.85);
        foundRus = Tools.FindTemplateInRegion(currentScreenshot, imgPath2, safeSearchRegion, 0.85);
    }

    // Если даже после второго клика ничего не найдено — значит точно не совпадают координаты ChatsInterface
    if (!foundEng.HasValue && !foundRus.HasValue)
    {
        Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] КРИТ: Шаблоны чата не обнаружены даже после повторного клика. Проверьте координаты ChatsInterface в GameUi!", LogType.Error);

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
        currentScreenshot.Dispose();
        return;
    }

    try
    {
        Point targetPoint = new();

        if (foundEng.HasValue)
        {
            Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] УСПЕХ: Найден ENG чат в точке X={foundEng.Value.X}, Y={foundEng.Value.Y}", LogType.Info);
            targetPoint = new Point(foundEng.Value.X, foundEng.Value.Y);
        }
        else if (foundRus.HasValue)
        {
            Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] УСПЕХ: Найден RUS чат в точке X={foundRus.Value.X}, Y={foundRus.Value.Y}", LogType.Info);
            targetPoint = new Point(foundRus.Value.X, foundRus.Value.Y);
        }

        // 2. Второй клик: Кликаем по динамическим координатам через ADB, передавая порт этого бота
        Logger.Log($"[{Settings.Name}][Клик] Отправка фонового клика по динамическим координатам: X={targetPoint.X}, Y={targetPoint.Y}", LogType.Info);
        Tools.SmartClick(targetPoint.X, targetPoint.Y, minSec: 1, maxSec: 3, offset: 3, adbPort: Settings.AdbPort);
    }
    catch (Exception ex)
    {
        Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Критический сбой анализа экрана: {ex.Message}", LogType.Error);
        return;
    }
    finally
    {
        currentScreenshot.Dispose(); // Гарантированно очищаем память матрицы скриншота
    }

    // Ждем выполнения анимации выбора чата альянса
    await Task.Delay(2000, token);

    // 3. Третий клик: Открываем меню ввода чата
    this.ClickTo(GameUi.ChatInputMenu);
    await Task.Delay(1200, token);

    // 4. Четвертый клик: Открываем меню быстрых сообщений
    this.ClickTo(GameUi.ChatFastInput);
    await Task.Delay(1200, token);

    // 5. Пятый клик: Открываем вкладку данных разведки
    this.ClickTo(GameUi.ChatInform);
    await Task.Delay(1200, token);

    // 6. Шестой клик: Выбираем статус-сообщение "Scout"
    this.ClickTo(GameUi.ChatMessScout);
    await Task.Delay(1200, token);

    // 7. Седьмой клик: Закрываем область ввода
    this.ClickTo(GameUi.WindowCenter);
    await Task.Delay(1500, token);

    // 8. Восьмой клик: Нажимаем кнопку "Отправить"
    this.ClickTo(GameUi.ChatButtSend);
    await Task.Delay(2000, token);

    // 9. Девятый клик: Закрываем общий интерфейс чатов
    this.ClickTo(GameUi.WindowCenter);

    Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] Цепочка кликов экстренного оповещения AliChatWarning успешно выполнена.", LogType.Success);
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
                    Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] ОПАСНОСТЬ!!! В системе посторонние!", LogType.Warning);
                }
                else if (_isSaveLocal is true)
                {
                    Logger.Log($"[{Settings.Name}|{Settings.EVESystem}] В системе нет посторонних.", LogType.Success);
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