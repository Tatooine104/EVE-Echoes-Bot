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
Global $aIsSave[UBound($aClients)]              ; Массив статусов безопасности для всех окон                      ; Статус: безопасность (враги/щит)

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
; Description....: Выполняет клик по цели и последующий варп с ожиданием прибытия.
; Syntax.........: _WarpTo($sCurrentClient, $sTargetName, $aTargetArea)
; Parameters ....: $sCurrentClient - Заголовок окна клиента.
;                  $sTargetName    - Название объекта для лога.
;                  $aTargetArea    - Массив [X1, Y1, X2, Y2] области/объекта, по которому нужно кликнуть для выбора.
; Return values .: True - Прибытие подтверждено, False - Ошибка на любом этапе.
; Updated .......: 2026.04.26
; Version .......: 1.16
; Remarks .......: Сначала выбирает цель по $aTargetArea, затем ищет кнопку варпа.
; ===============================================================================================================================
Func _WarpTo($sCurrentClient, $sTargetName, $aTargetArea)
    ; 1. Получаем базовые координаты клиента
    Local $aCPos = _WinGetClientPos($sCurrentClient)
    If @error Then Return False

    _Log("_WarpTo [" & $sCurrentClient & "]: Выбираем цель: " & $sTargetName)

    ; 2. Кликаем по самой цели (например, по строке в списке астероидов)
    ; Кликаем в центр переданной области $aTargetArea
    Local $iTargetX = $aTargetArea[0] + ($aTargetArea[2] - $aTargetArea[0]) / 2
    Local $iTargetY = $aTargetArea[1] + ($aTargetArea[3] - $aTargetArea[1]) / 2
    MouseClick("left", $iTargetX, $iTargetY, 1, 1)
    _HumanSleep(500, 800)

    ; 3. Область для поиска кнопки "Warp" (появляется после клика по цели)
    Local $aWarpBtnArea[4]
    $aWarpBtnArea[0] = $aCPos[0] + 700
    $aWarpBtnArea[1] = $aCPos[1] + 40
    $aWarpBtnArea[2] = $aCPos[0] + 1000
    $aWarpBtnArea[3] = $aCPos[1] + 700

    ; 4. Находим и кликаем кнопку варпа
    If _FindAndClick("warp.png", $sResourceDir, $aWarpBtnArea) Then
        _Log("_WarpTo [" & $sCurrentClient & "]: Кнопка нажата. Ожидаем разгон...")
        
        ; 5. Зона индикатора варпа
        Local $aWarpZone[4]
        $aWarpZone[0] = $aCPos[0] + 404
        $aWarpZone[1] = $aCPos[1] + 515
        $aWarpZone[2] = $aCPos[0] + 808
        $aWarpZone[3] = $aCPos[1] + 555
        
        Local $outX, $outY
        
        ; 6. Ждем индикатор начала варпа
        If _MyWaitForImageSearch("imgWarpTo.bmp", $sResourceDir, $aWarpZone, 30, $outX, $outY, 100) Then
            _Log("_WarpTo [" & $sCurrentClient & "]: В варпе. Ожидаем остановку...")
            
            ; 7. Ждем индикатор выхода из варпа (остановка корабля)
            If _MyWaitForImageSearch("imgShipStopping.bmp", $sResourceDir, $aWarpZone, 1200, $outX, $outY, 100) Then
                _Log("_WarpTo [" & $sCurrentClient & "]: Прибыли к " & $sTargetName)
                Return True
            Else
                _Log("_WarpTo [" & $sCurrentClient & "]: Ошибка - Тайм-аут завершения полета.")
                Return False
            EndIf
        Else
            _Log("_WarpTo [" & $sCurrentClient & "]: ПРЕДУПРЕЖДЕНИЕ - Варп не начался.")
            Return False
        EndIf
    Else
        _Log("_WarpTo [" & $sCurrentClient & "]: ОШИБКА - Кнопка варпа не найдена.")
        Return False
    EndIf
EndFunc   ;==>_WarpTo



; #FUNCTION# ====================================================================================================================
; Name...........: _GoToRandomBelt
; Description....: Ищет пояса по приоритету с использованием уникальных областей поиска для каждого типа.
; Syntax.........: _GoToRandomBelt($sCurrentClient)
; Parameters ....: $sCurrentClient - Заголовок текущего окна клиента.
; Return values .: True         - Успешный варп к поясу.
;                  False        - Пояса не найдены или ошибка варпа.
; Updated .......: 2026.04.26
; Version .......: 1.31
; Remarks .......: Передает область найденного пояса в обновленную функцию _WarpTo.
; ===============================================================================================================================
Func _GoToRandomBelt($sCurrentClient)
    Local $aCPos = _WinGetClientPos($sCurrentClient)
    If @error Then Return False

    ; 1. Определяем типы поясов (имена файлов)
    Local $aBeltFiles[3] = ["imgBeltLarge.bmp", "imgBeltMedium.bmp", "imgBeltSmall.bmp"]

    ; 2. Определяем смещения областей поиска для каждого типа [X1, Y1, X2, Y2]
    Local $aOffsets[3][4] = [ _
        [967, 51, 995, 150], _  ; Область для Типа 1
        [967, 151, 995, 300], _ ; Область для Типа 2
        [967, 301, 995, 433]  _ ; Область для Типа 3
    ]

    Local $x, $y
    Local $aArea[4]       ; Рабочая область поиска
    Local $aTargetArea[4] ; Область найденной цели для передачи в _WarpTo

    ; 3. Перебираем типы по очереди
    For $i = 0 To 2
        _Log("_GoToRandomBelt [" & $sCurrentClient & "]: Проверка типа " & ($i + 1))

        ; Рассчитываем координаты области поиска
        $aArea[0] = $aCPos[0] + $aOffsets[$i][0]
        $aArea[1] = $aCPos[1] + $aOffsets[$i][1]
        $aArea[2] = $aCPos[0] + $aOffsets[$i][2]
        $aArea[3] = $aCPos[1] + $aOffsets[$i][3]

        ; Ищем пояс
        If _MyImageSearch($aBeltFiles[$i], $sResourceDir, $aArea, $x, $y, 100) Then
            _Log("_GoToRandomBelt [" & $sCurrentClient & "]: Найден пояс типа " & ($i + 1))
            
            ; Формируем область цели вокруг найденных координат (например, квадрат 20x20 пикселей)
            $aTargetArea[0] = $x - 10
            $aTargetArea[1] = $y - 10
            $aTargetArea[2] = $x + 10
            $aTargetArea[3] = $y + 10

            ; Вызываем варп, передавая имя окна, описание цели и область клика
            Return _WarpTo($sCurrentClient, "Пояс тип " & ($i + 1), $aTargetArea)
        EndIf
    Next

    _Log("_GoToRandomBelt [" & $sCurrentClient & "]: Доступные пояса не найдены.")
    Return False
EndFunc   ;==>_GoToRandomBelt



; #FUNCTION# ====================================================================================================================
; Name...........: _IsSafe
; Description....: Проверяет локальный чат конкретного окна и записывает статус в массив безопасности.
; Syntax.........: _IsSafe($sCurrentClient, $iClientIdx)
; Parameters ....: $sCurrentClient - Заголовок текущего окна клиента.
;                  $iClientIdx      - Индекс текущего клиента в массиве статусов.
; Return values .: True         - Безопасно.
;                  False        - Обнаружена угроза.
; Updated .......: 2026.04.26
; Version .......: 1.20
; Remarks .......: Результат записывается в Global $aIsSave[$iClientIdx].
; ===============================================================================================================================
Func _IsSafe($sCurrentClient, $iClientIdx)
    Local $aCPos = _WinGetClientPos($sCurrentClient)
    If @error Then Return False

    ; 1. Область локального чата
    Local $aArea[4]
    $aArea[0] = $aCPos[0] + 0
    $aArea[1] = $aCPos[1] + 330
    $aArea[2] = $aCPos[0] + 400
    $aArea[3] = $aCPos[1] + 720

    ; Ищем плохие стендинги: Криминал, Минус, Нейтрал
    Local $aMarkers[3] = ["imgLocalStatCriminal.bmp", "imgLocalStatMinus.bmp", "imgLocalStatNeitral.bmp"]
    Local $x, $y, $outX, $outY
    Local $iTolerance = 100
    Local $bStatus = True ; По умолчанию считаем, что безопасно

    _Log("_IsSafe [" & $sCurrentClient & "]: Проверка локала...")

    ; 2. Проверка маркеров угроз
    For $i = 0 To 2
        If _MyImageSearch($aMarkers[$i], $sResourceDir, $aArea, $x, $y, $iTolerance) Then
            
            ; 3. Проверка: есть ли рядом иконка "своего" (синий/зеленый плюс), перекрывающая угрозу
            ; (Например, если нейтарл на самом деле в твоем флоте)
            Local $aStatusArea[4]
            $aStatusArea[0] = $x + 10 
            $aStatusArea[1] = $y - 20 
            $aStatusArea[2] = $x + 60 
            $aStatusArea[3] = $y + 20 
            
            ; Если иконка подтверждения безопасности (imgLocalStatNull.bmp) НЕ найдена рядом с угрозой
            If Not _MyImageSearch("imgLocalStatNull.bmp", $sResourceDir, $aStatusArea, $outX, $outY, $iTolerance) Then
                _Log("_IsSafe [" & $sCurrentClient & "]: !!! ОБНАРУЖЕН ВРАГ !!!")
                $bStatus = False
                ExitLoop 
            EndIf
        EndIf
    Next

    ; 4. Записываем результат в массив статусов конкретного окна
    If IsDeclared("aIsSave") Then
        ; Используем Execute для записи в массив по индексу, так как Assign плохо работает с индексами массивов напрямую
        Execute('$aIsSave[' & $iClientIdx & '] = ' & ($bStatus ? 'True' : 'False'))
    EndIf

    Return $bStatus
EndFunc   ;==>_IsSafe


; #FUNCTION# ====================================================================================================================
; Name...........: _GoToStation
; Description....: Выполняет перелет и стыковку (док) со станцией.
; Syntax.........: _GoToStation($sCurrentClient)
; Parameters ....: $sCurrentClient - Заголовок текущего окна клиента.
; Return values .: True         - Корабль внутри станции.
;                  False        - Ошибка на любом из этапов.
; Updated .......: 2026.04.26
; Version .......: 1.10
; Remarks .......: Обрабатывает два сценария: прямой док (если рядом) или варп с последующим доком.
; ===============================================================================================================================
Func _GoToStation($sCurrentClient)
    Local $aCPos = _WinGetClientPos($sCurrentClient)
    If @error Then Return False

    _Log("_GoToStation [" & $sCurrentClient & "]: Возвращаемся на базу...")

    ; 1. Выбираем фильтр станций в гриде (Шаг 4 из _OpenBeltsList, но с иконкой станции)
    Local $aArea[4]
    $aArea[0] = $aCPos[0] + 1210
    $aArea[1] = $aCPos[1] + 50
    $aArea[2] = $aCPos[0] + 1280
    $aArea[3] = $aCPos[1] + 550
    
    If Not _FindAndClick("imgStationMarker.bmp", $sResourceDir, $aArea) Then Return False
    _HumanSleep(500, 800)

    ; Выбираем пункт "Станции" в выпадающем меню
    $aArea[0] = $aCPos[0] + 950
    $aArea[1] = $aCPos[1] + 50
    $aArea[2] = $aCPos[0] + 1210
    $aArea[3] = $aCPos[1] + 720
    
    Local $x = 0 
    Local $y = 0 

    If Not _MyImageSearch("imgStationMarker.bmp", $sResourceDir, $aArea, $x, $y, 100) Then
        _Log("_GoToStation [" & $sCurrentClient & "]: Станция не найдена в списке!")
        Return False
    EndIf
    
    ; Кликаем по найденной станции
    MouseClick("left", $x, $y, 1, 1)
    _HumanSleep(500, 800)

    ; 3. Ищем кнопку "Dock" (обычно там же, где кнопка Warp)
    Local $aDockBtnArea[4]
    $aDockBtnArea[0] = $aCPos[0] + 704
    $aDockBtnArea[1] = $aCPos[1] + 40
    $aDockBtnArea[2] = $aCPos[0] + 972
    $aDockBtnArea[3] = $aCPos[1] + 562

    If Not _FindAndClick("imgEnterToStation.bmp", $sResourceDir, $aDockBtnArea) Then
        _Log("_GoToStation [" & $sCurrentClient & "]: Кнопка 'Вход' не найдена.")
        Return False
    EndIf

    ; 4. Обработка ситуации: Варп или сразу Док
    _Log("_GoToStation [" & $sCurrentClient & "]: Команда принята. Ожидаем результат...")
    
    Local $aWarpZone[4]
    $aWarpZone[0] = $aCPos[0] + 404
    $aWarpZone[1] = $aCPos[1] + 515
    $aWarpZone[2] = $aCPos[0] + 808
    $aWarpZone[3] = $aCPos[1] + 555

    Local $outX, $outY
    Local $iTimer = TimerInit()
    
    ; Цикл ожидания: либо появится индикатор варпа, либо иконка "внутри станции"
    While TimerDiff($iTimer) < 180000 ; Общий таймаут 3 минуты
        ; А. Проверяем, не зашли ли мы уже на станцию (imgInsideStation.bmp - например, иконка ангара)
        If _MyImageSearch("imgUnDock.bmp", $sResourceDir, $aCPos, $outX, $outY, 100) Then
            _Log("_GoToStation [" & $sCurrentClient & "]: Мы внутри станции.")
            Return True
        EndIf

        ; Б. Проверяем, не находимся ли мы в варпе
        If _MyImageSearch("imgWarpTo.bmp", $sResourceDir, $aWarpZone, $outX, $outY, 100) Then
            _Log("_GoToStation [" & $sCurrentClient & "]: Летим к станции...")
            ; Если летим, ждем окончания (imgShipStopping.bmp)
            _MyWaitForImageSearch("imgShipStopping.bmp", $sResourceDir, $aWarpZone, 120, $outX, $outY, 100)
        EndIf

        Sleep(1000)
    WEnd

    _Log("_GoToStation [" & $sCurrentClient & "]: Ошибка - Время ожидания дока истекло.")
    Return False
EndFunc   ;==>_GoToStation


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

