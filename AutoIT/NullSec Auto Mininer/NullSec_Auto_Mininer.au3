; ###############################################################################################################################
; #                                                                                                                             #
; # PROJECT........: NullSec Auto Mininer                                                                                       #
; # VERSION........: 1.0.0                                                                                                      #
; # BUILD..........: 2026.04.25                                                                                                 #
; # FILENAME.......: libImageSearch.au3                                                                                         #
; # GITHUB.........: https://github.com/Tatooine104/EVE-Echoes-Bot.git                                                          #
; # DESCRIPTION....: Автоматизированный комплекс управления добычей руды.                                                       #
; #                  - Поддержка многопоточности графического интерфейса (OnEventMode)                                          #
; #                  - Интеллектуальное распознавание образов (ImageSearch)                                                     #
; #                  - Сохранение прогресса в INI-конфигурацию                                                                  #
; #                                                                                                                             #
; ###############################################################################################################################
; #                                                                                                                             #
; # ТРЕБОВАНИЯ:                                                                                                                 #
; # - Разрешение игры : 1280 x 720                                                                                              #
; # - Android Эмулятор: Memu                                                                                                    #
; # -                 :                                                                                                         #
; #                                                                                                                             #
; ###############################################################################################################################
; #                                                                                                                             #
; # ПЕРСОНАЖ / КОРАБЛЬ:                                                                                                         #
; # -                                                                                                                           #
; #                                                                                                                             #
; #                                                                                                                             #                                                                                                                            #
; #                                                                                                                             #
; ###############################################################################################################################

#include <GUIConstantsEx.au3>    ; Содержит $GUI_EVENT_CLOSE
#include <WindowsConstants.au3>  ; Содержит $WS_EX_TOPMOST
#include <StaticConstants.au3>   ; Содержит константы для стилей текста (Label)
#include <libImageSearch.au3>    ; Содержит функции поиска изображений на экране
#include <libUtility.au3>        ; Содержит общий и служебные функции 
#include <libGUI.au3>            ; Подключаем вашу новую библиотеку интерфейса

; ===============================================================================================================================
; 1. РЕСУРСЫ И ИНИЦИАЛИЗАЦИЯ ПАПОК
; ===============================================================================================================================

; Создаем временную папку для ресурсов во временном каталоге системы
Global $sResourceDir = @TempDir & "\MyBotResources\"
DirCreate($sResourceDir)

; Вшиваем необходимые файлы внутрь EXE (распаковываются при запуске)
FileInstall("JetBrainsMono-Bold.ttf", $sResourceDir & "JetBrainsMono-Bold.ttf", 1)
FileInstall("ImageSearchDLL.dll", @SystemDir & "\ImageSearchDLL.dll", 1) 
FileInstall("imgUnDock.bmp", $sResourceDir & "imgUnDock.bmp", 1)
FileInstall("100PCargo.png", $sResourceDir & "100PCargo.png", 1)
FileInstall("EyeIcon.png", $sResourceDir & "EyeIcon.png", 1)

; !!! Сюда добавьте остальные FileInstall для всех ваших BMP/PNG файлов !!!

; Регистрация шрифта JetBrains Mono в системе на время работы скрипта
Global $hFontRes = DllCall("gdi32.dll", "int", "AddFontResourceEx", "str", $sResourceDir & "JetBrainsMono-Bold.ttf", "dword", 0x10, "int", 0)

; ===============================================================================================================================
; 2. НАСТРОЙКИ СКРИПТА (OPTIONS)
; ===============================================================================================================================

Opt("MouseCoordMode", 2)       ; Координаты мыши относительно клиентской области окна
Opt("PixelCoordMode", 2)       ; Координаты пикселей относительно клиентской области окна
Opt("SendKeyDownDelay", 50)    ; Задержка удержания клавиш (мс) для защиты от "проглатывания" игрой
Opt("GUIOnEventMode", 1)       ; Включение режима событий для работы интерфейса

; ===============================================================================================================================
; 3. ГЛОБАЛЬНЫЕ ПЕРЕМЕННЫЕ
; ===============================================================================================================================

; --- Пути и файлы ---
Global $sIniPath = @ScriptDir & "\settings.ini" ; Файл настроек рядом с исполняемым файлом

; --- Данные окна ---
Global $aClients = ["(Client W.01)"] ; Список окон через запятую
Global $sCurrentClient = $aClients[0]           ; Индекс окна, с которым работаем в данный момент

; --- Игровые параметры ---
Global $InSpace    = False                      ; Статус: в космосе или нет
Global $IsSave     = False                      ; Статус: безопасность (враги/щит)

; --- Статистика ---
; Загружаем сохраненный счетчик из INI (по умолчанию "0" если файл не найден)
Global $DeliveredCount = Int(IniRead($sIniPath, "Statistics", "OreCount", "0"))

; --- Оформление ---
Global $sFontFace = "JetBrains Mono"            ; Основной шрифт интерфейса

; --- Режим отладки ---
Global $Debug = false



; #FUNCTION# ====================================================================================================================
; Name...........: _Undock
; Description....: Выполняет выход со станции в космос, находя кнопку "Undock".
; Syntax.........: _Undock($sCurrentClient)
; Parameters ....: $sCurrentClient - Заголовок текущего окна, с которым работаем.
; Return values .: True         - Кнопка найдена и нажата.
;                  False        - Кнопка не найдена или возникла ошибка получения координат.
; Updated .......: 2026.04.26
; Version .......: 1.10
; Remarks .......: Функция больше не активирует окно сама, полагаясь на внешний цикл мультиокна.
; ===============================================================================================================================
Func _Undock($sCurrentClient)
    ; 1. Получаем координаты клиентской области (проверка существования окна вшита в _WinGetClientPos)
    Local $aCPos = _WinGetClientPos($sCurrentClient)
    If @error Then 
        _Log("_Undock: Ошибка - Не удалось получить координаты '" & $sCurrentClient & "'")
        Return False
    EndIf

    ; 2. Вычисляем область поиска
    Local $aArea[4]
    $aArea[0] = $aCPos[0] + 1060 ; X1
    $aArea[1] = $aCPos[1] + 230  ; Y1
    $aArea[2] = $aCPos[0] + 1280 ; X2
    $aArea[3] = $aCPos[1] + 300  ; Y2
    
    Local $x, $y

    ; 3. Поиск изображения кнопки Undock
    If _MyImageSearch("imgUnDock.bmp", $sResourceDir, $aArea, $x, $y, 100) = 1 Then
        _Log("_Undock [" & $sCurrentClient & "]: Кнопка найдена. Выходим.")
        _HumanSleep(200, 500)      
        Send("{SC016}") ; Клавиша "U"
        Return True
    EndIf

    _Log("_Undock [" & $sCurrentClient & "]: Кнопка не найдена.")
    Return False
EndFunc   ;==>_Undock



; #FUNCTION# ====================================================================================================================
; Name...........: IsCargoFull
; Description....: Проверяет заполнение грузового отсека (карго) по графическому индикатору 100%.
; Syntax.........: IsCargoFull($sCurrentClient)
; Parameters ....: $sCurrentClient - Заголовок текущего окна, с которым работаем.
; Return values .: True         - Индикатор полной загрузки найден.
;                  False        - Индикатор не найден или возникла ошибка клиента.
; Updated .......: 2026.04.26
; Version .......: 1.11
; Remarks .......: Предполагает, что окно уже активировано внешним циклом.
; ===============================================================================================================================
Func IsCargoFull($sCurrentClient)
    ; 1. Получаем координаты клиентской области
    Local $aCPos = _WinGetClientPos($sCurrentClient)
    If @error Then 
        _Log("IsCargoFull: Ошибка - Не удалось получить координаты '" & $sCurrentClient & "'")
        Return False
    EndIf

    ; 2. Вычисляем область поиска (Смещение: 0, 80, 60, 170)
    Local $aArea[4] ; Явно указываем размер, чтобы избежать ошибок записи
    $aArea[0] = $aCPos[0] + 0
    $aArea[1] = $aCPos[1] + 80
    $aArea[2] = $aCPos[0] + 60
    $aArea[3] = $aCPos[1] + 170

    _Log("IsCargoFull [" & $sCurrentClient & "]: Проверяем отсек...")

    Local $x, $y
    ; 3. Поиск изображения 100% карго
    If _MyImageSearch("imgCargoFull.bmp", $sResourceDir, $aArea, $x, $y, 100) = 1 Then
        _Log("IsCargoFull [" & $sCurrentClient & "]: Грузовой отсек ПОЛОН")
        Return True
    EndIf

    _Log("IsCargoFull [" & $sCurrentClient & "]: Еще есть место")
    Return False
EndFunc   ;==>IsCargoFull




; #FUNCTION# ====================================================================================================================
; Name...........: _MoveCargo
; Description....: Выполняет цикл перемещения руды из трюма на станцию через горячие клавиши.
; Syntax.........: _MoveCargo($sCurrentClient)
; Parameters ....: $sCurrentClient - Заголовок текущего окна клиента.
; Return values .: True         - Выгрузка подтверждена (трюм пуст).
;                  False        - Ошибка выгрузки или трюм остался полным.
; Updated .......: 2026.04.26
; Version .......: 1.10
; Remarks .......: Использует серию Send команд и финальную проверку через imgCargoEmpty.bmp.
; ===============================================================================================================================
Func _MoveCargo($sCurrentClient)
    ; 1. Получаем координаты клиентской области
    Local $aCPos = _WinGetClientPos($sCurrentClient)
    If @error Then 
        _Log("_MoveCargo: Ошибка - Не удалось получить координаты '" & $sCurrentClient & "'")
        Return False
    EndIf

    _Log("_MoveCargo [" & $sCurrentClient & "]: Начинаем выгрузку...")

    ; 2. Последовательность горячих клавиш (Инвентарь -> Свернуть -> Трюм -> Выделить -> Переместить -> Подтвердить)
    _HumanSleep(200, 400)
    Send("{SC002}") ; "1" - Инвентарь
    _HumanSleep(300, 600)
    Send("{SC002}") ; "1" - Свернуть Станцию
    _HumanSleep(300, 600)
    Send("{SC00B}") ; "0" - Трюм для руды
    _HumanSleep(400, 700)
    Send("{SC020}") ; "D" - Выделить всё
    _HumanSleep(400, 700)
    Send("{SC006}") ; "5" - Переместить
    _HumanSleep(400, 700)
    Send("{SC008}") ; "7" - Подтвердить (в коде была опечатка SC007 - это "6", исправил на "7")
    
    _HumanSleep(1000, 1500) ; Ожидание завершения анимации

    Send("{SC001}") ; "Esc" - Закрыть инвентарь
    _HumanSleep(500, 800)

    ; 3. Финальная проверка пустого трюма
    Local $x, $y, $iMaxRetries = 3
    Local $aArea[4]
    $aArea = $aCPos + 0
    $aArea = $aCPos + 80
    $aArea = $aCPos + 60
    $aArea = $aCPos + 170

    For $i = 1 To $iMaxRetries
        If _MyImageSearch("imgCargoEmpty.bmp", $sResourceDir, $aArea, $x, $y, 100) = 1 Then
            ; Безопасно обновляем глобальный счетчик выгрузок
            If IsDeclared("DeliveredCount") Then 
                Assign("DeliveredCount", Eval("DeliveredCount") + 1, 2)
            EndIf
            
            _Log("_MoveCargo [" & $sCurrentClient & "]: Успешно выгружено.")
            Return True
        EndIf

        _Log("_MoveCargo [" & $sCurrentClient & "]: Ожидание очистки трюма... Попытка " & $i)
        _HumanSleep(800, 1200) 
    Next

    _Log("_MoveCargo [" & $sCurrentClient & "]: ОШИБКА - Трюм не очистился.")
    Return False
EndFunc   ;==>_MoveCargo


; #FUNCTION# ====================================================================================================================
; Name...........: _OpenMenuIfNeed
; Description....: Проверяет состояние меню грида и открывает его, если оно закрыто (по отсутствию иконки "глаза").
; Syntax.........: _OpenMenuIfNeed($sCurrentClient)
; Parameters ....: $sCurrentClient - Заголовок текущего окна клиента.
; Return values .: True         - Меню открыто (или уже было открыто).
;                  False        - Не удалось открыть меню за отведенное количество попыток.
; Updated .......: 2026.04.26
; Version .......: 1.12
; Remarks .......: Использует проверку наличия imgEyeIcon.bmp и горячую клавишу SC032.
; ===============================================================================================================================
Func _OpenMenuIfNeed($sCurrentClient)

    ; 1. Получаем координаты клиентской области
    Local $aCPos = _WinGetClientPos($sCurrentClient)
    If @error Then 
        _Log("_OpenMenuIfNeed: Ошибка - Не удалось получить координаты '" & $sCurrentClient & "'")
        Return False
    EndIf

    ; 2. Вычисляем область поиска (Смещение: 1190, 370, 1260, 430)
    Local $aArea[4]
    $aArea[0] = $aCPos[0] + 1190
    $aArea[1] = $aCPos[1] + 370
    $aArea[2] = $aCPos[0] + 1260
    $aArea[3] = $aCPos[1] + 430

    _Log("_OpenMenuIfNeed [" & $sCurrentClient & "]: Проверяем состояние меню...")

    Local $x, $y
    Local $iMaxRetries = 3

    ; 3. Цикл попыток открытия меню
    For $i = 1 To $iMaxRetries
        ; Если иконка "глаза" НЕ найдена (результат 0), значит меню развернуто
        If _MyImageSearch("imgEyeIcon.bmp", $sResourceDir, $aArea, $x, $y, 100) = 0 Then
            _Log("_OpenMenuIfNeed [" & $sCurrentClient & "]: Меню открыто.")
            Return True
        EndIf

        _Log("_OpenMenuIfNeed [" & $sCurrentClient & "]: Меню закрыто. Попытка открытия " & $i)
        
        ; Нажимаем клавишу открытия (SC032)
        _HumanSleep()
        Send("{SC032}") 

        ; Ожидание анимации
        _HumanSleep(800, 1200) 
    Next

    _Log("_OpenMenuIfNeed [" & $sCurrentClient & "]: ОШИБКА - Меню не открылось.")
    Return False

EndFunc   ;==>_OpenMenuIfNeed



; #FUNCTION# ====================================================================================================================
; Name...........: _OpenBeltsList
; Description....: Проверяет наличие открытого списка астероидных поясов и открывает его при необходимости.
; Syntax.........: _OpenBeltsList($sCurrentClient, $bNeedToGo)
; Parameters ....: $sCurrentClient - Заголовок текущего окна клиента.
;                  $bNeedToGo       - Булево значение: если True, после открытия вызывает переход на случайный пояс.
; Return values .: True         - Список успешно открыт (или уже был открыт).
;                  False        - Ошибка на любом из этапов открытия.
; Updated .......: 2026.04.26
; Version .......: 1.12
; Remarks .......: Предполагает, что окно уже активировано внешним циклом. Использует _MyImageSearch и _FindAndClick.
; ===============================================================================================================================
Func _OpenBeltsList($sCurrentClient, $bNeedToGo)
    ; 1. Получаем координаты клиентской области
    Local $aCPos = _WinGetClientPos($sCurrentClient)
    If @error Then 
        _Log("_OpenBeltsList: Ошибка - Не удалось получить координаты '" & $sCurrentClient & "'")
        Return False
    EndIf

    _Log("_OpenBeltsList [" & $sCurrentClient & "]: Проверяем список астероидов...")

    ; 2. Проверяем, не открыт ли список уже
    Local $x, $y
    Local $aArea[4]

    ; --- Область: Mining Current ---
    $aArea[0] = $aCPos[0] + 970
    $aArea[1] = $aCPos[1] + 1
    $aArea[2] = $aCPos[0] + 1100
    $aArea[3] = $aCPos[1] + 50
    Local $bMiningCurrent = _MyImageSearch("imgMiningCurrent.bmp", $sResourceDir, $aArea, $x, $y, 100)

    ; --- Область: Select Ore ---
    $aArea[0] = $aCPos[0] + 970
    $aArea[1] = $aCPos[1] + 55
    $aArea[2] = $aCPos[0] + 1000
    $aArea[3] = $aCPos[1] + 720
    Local $bSelectOre = _MyImageSearch("imgSelectOreToMine.bmp", $sResourceDir, $aArea, $x, $y, 100)

    If $bMiningCurrent = 1 And $bSelectOre = 1 Then
        _Log("_OpenBeltsList [" & $sCurrentClient & "]: Список уже открыт.")
        If $bNeedToGo Then Return _GoToRandomBelt($sCurrentClient)
        Return True
    EndIf

    ; 3. Открываем выпадающее меню
    _Log("_OpenBeltsList [" & $sCurrentClient & "]: Открываем меню выбора...")
    $aArea[0] = $aCPos[0] + 970
    $aArea[1] = $aCPos[1] + 1
    $aArea[2] = $aCPos[0] + 1010
    $aArea[3] = $aCPos[1] + 40
    
    If _FindAndClick("imgShowDropdownMy.bmp", $sResourceDir, $aArea) Then
        _HumanSleep()

        ; 4. Выбираем пункт добычи
        $aArea[0] = $aCPos[0] + 970
        $aArea[1] = $aCPos[1] + 50
        $aArea[2] = $aCPos[0] + 1220
        $aArea[3] = $aCPos[1] + 720
        
        If _FindAndClick("imgMinigScreen.bmp", $sResourceDir, $aArea) Then
            _Log("_OpenBeltsList [" & $sCurrentClient & "]: Вкладка добычи выбрана.")
            _HumanSleep()

            ; 5. Дополнительный клик (управление списком)
            $aArea[0] = $aCPos[0] + 1220
            $aArea[1] = $aCPos[1] + 60
            $aArea[2] = $aCPos[0] + 1270
            $aArea[3] = $aCPos[1] + 530
            _FindAndClick("imgMinigScreen.bmp", $sResourceDir, $aArea)
            _HumanSleep()

            If $bNeedToGo Then Return _GoToRandomBelt($sCurrentClient)
            Return True
        Else
            _Log("_OpenBeltsList [" & $sCurrentClient & "]: Ошибка - вкладка не нажата.")
            Return False
        EndIf
    Else
        _Log("_OpenBeltsList [" & $sCurrentClient & "]: Ошибка - кнопка меню не найдена.")
        Return False
    EndIf
EndFunc   ;==>_OpenBeltsList



; #FUNCTION# ====================================================================================================================
; Name...........: _WarpTo
; Description....: Выполняет варп к выбранному объекту, находя и нажимая кнопку варпа.
; Syntax.........: _WarpTo($sCurrentClient, $sTargetName)
; Parameters ....: $sCurrentClient - Заголовок текущего окна клиента.
;                  $sTargetName    - Название объекта (используется для логирования).
; Return values .: True         - Кнопка варпа найдена и нажата.
;                  False        - Кнопка не найдена или ошибка координат.
; Updated .......: 2026.04.26
; Version .......: 1.12
; Remarks .......: Координаты кнопки рассчитываются относительно клиентской области окна.
; ===============================================================================================================================
Func _WarpTo($sCurrentClient, $sTargetName)
    ; 1. Получаем координаты клиентской области
    Local $aCPos = _WinGetClientPos($sCurrentClient)
    If @error Then 
        _Log("_WarpTo: Ошибка - Не удалось получить координаты '" & $sCurrentClient & "'")
        Return False
    EndIf

    ; 2. Вычисляем область поиска кнопки варпа (Смещение: 704, 40, 972, 562)
    Local $aArea[4]
    $aArea[0] = $aCPos[0] + 704
    $aArea[1] = $aCPos[1] + 40
    $aArea[2] = $aCPos[0] + 972
    $aArea[3] = $aCPos[1] + 562

    _Log("_WarpTo [" & $sCurrentClient & "]: Попытка варпа к: " & $sTargetName)

    ; 3. Поиск и клик по кнопке варпа (warp.png)
    If _FindAndClick("warp.png", $sResourceDir, $aArea) Then
        _Log("_WarpTo [" & $sCurrentClient & "]: Кнопка найдена. Разгон к " & $sTargetName)
        
        ; Имитируем человеческую паузу после клика
        ;_HumanSleep()
        ; Тут нужна логика проверки что варпанул
        ;

        Return True
    Else
        _Log("_WarpTo [" & $sCurrentClient & "]: ОШИБКА - Кнопка варпа не найдена.")
        Return False
    EndIf
EndFunc   ;==>_WarpTo


; - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - +


While 1

    _GUI_Update()

    If $bIsRunning Then
        ; 1. Выбираем текущее окно
        Local $sActiveTitle = $sCurrentClient
        
        ; 2. Выполняем логику 
        ; --- ЗДЕСЬ ВЕСЬ ТВОЙ КОД БОТА ---
        ;_DoMiningWork() 
        ; -------------------------------

        ; 3. Переключаемся на следующее окно для следующей итерации
        _SwitchToNextClient($aClients, $sCurrentClient)
        
        ; Небольшая пауза между окнами, чтобы система успела переключить фокус
        _HumanSleep(500, 900)
    Else
        Sleep(100)
    EndIf

WEnd

