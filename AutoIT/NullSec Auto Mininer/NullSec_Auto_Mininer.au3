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
#include <WinAPIFiles.au3>
#include "..\Resource\libImageSearch.au3"    ; Содержит функции поиска изображений на экране
#include "..\Resource\libUtility.au3"        ; Содержит общий и служебные функции 
#include "..\Resource\libGUI.au3"            ; Подключаем вашу новую библиотеку интерфейса
#include "..\Resource\libEVEEchoesFuncs.au3" ; 

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
Global $sImagesDir = _WinAPI_GetFullPathName(@ScriptDir & "\..\Images\") & "\"
Global $sResourceDir = _WinAPI_GetFullPathName(@ScriptDir & "\..\Resource\") & "\"

; Проверяем, видит ли скрипт папку
If Not FileExists($sResourceDir) Then 
    ; Сначала выводим сообщение пользователю
    MsgBox(16, "Критическая ошибка", "Папка с ресурсами не найдена!" & @CRLF & "Путь: " & $sResourceDir)
    
    ; Если лог уже может работать, записываем ошибку туда
    _Log("ОСТАНОВКА: Папка ресурсов не найдена по пути " & $sResourceDir)
    
    ; Прерываем выполнение
    Exit 
EndIf 

; Проверяем, видит ли скрипт папку
If Not FileExists($sImagesDir) Then 
    ; Сначала выводим сообщение пользователю
    MsgBox(16, "Критическая ошибка", "Папка с картинками не найдена!" & @CRLF & "Путь: " & $sImagesDir)
    
    ; Если лог уже может работать, записываем ошибку туда
    _Log("ОСТАНОВКА: Папка ресурсов не найдена по пути " & $sResourceDir)
    
    ; Прерываем выполнение
    Exit 
EndIf 

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



; ###############################################################################################################################
; # FUNCTION.......: _Undock
; # DESCRIPTION....: Выполняет выход со станции через ADB, используя заранее подготовленный скриншот.
; # PARAMETERS ....: $sDeviceID  - ID эмулятора (напр. "127.0.0.1:21503").
; #                  $sSourceBmp - Путь к актуальному скриншоту этого устройства.
; # RETURN.........: True - кнопка нажата, False - не найдена.
; ###############################################################################################################################
Func _Undock($sDeviceID, $sSourceBmp)
    ; 1. Задаем область поиска кнопки Undock (координаты внутри Android-экрана 1280x720)
    ; Кнопка обычно находится в правой части экрана
    Local $aArea[4] = [1060, 230, 1280, 300] 
    
    Local $x, $y

    ; 2. Поиск изображения кнопки (теперь передаем $sSourceBmp)
    ; $sResourceDir должна быть объявлена глобально
    If _MyImageSearch("imgUnDock.bmp", $sResourceDir, $aArea, $x, $y, 100, $sSourceBmp) = 1 Then
        _CW("_Undock [" & $sDeviceID & "]: Кнопка найдена. Выполняем андок." & @CRLF)
        
        ; 3. Пауза и фоновый клик по координатам кнопки
        _HumanSleep(200, 500)      
        _ADB_Click($sDeviceID, $x, $y) 
        
        Return True
    EndIf

    _CW("_Undock [" & $sDeviceID & "]: Кнопка не найдена." & @CRLF)
    Return False
EndFunc   ;==>_Undock




; ###############################################################################################################################
; # FUNCTION.......: IsCargoFull
; # DESCRIPTION....: Проверяет заполнение грузового отсека (карго) по заранее подготовленному скриншоту ADB.
; # PARAMETERS ....: $sDeviceID  - ID эмулятора (напр. "127.0.0.1:21503").
; #                  $sSourceBmp - Путь к актуальному скриншоту этого устройства.
; # RETURN.........: True - Карго заполнено, False - есть место или ошибка.
; ###############################################################################################################################
Func IsCargoFull($sDeviceID, $sSourceBmp)
    ; 1. Задаем область поиска индикатора 100% (фиксированные координаты для 1280x720)
    Local $aArea[4] = [0, 80, 60, 170]

    _CW("IsCargoFull [" & $sDeviceID & "]: Проверка индикатора заполнения..." & @CRLF)

    Local $x, $y
    ; 2. Поиск изображения индикатора 100% (imgCargoFull.bmp)
    ; Используем переданный скриншот $sSourceBmp
    If _MyImageSearch("imgCargoFull.bmp", $sResourceDir, $aArea, $x, $y, 100, $sSourceBmp) = 1 Then
        _CW("IsCargoFull [" & $sDeviceID & "]: [!] Грузовой отсек ПОЛОН" & @CRLF)
        Return True
    EndIf

    _CW("IsCargoFull [" & $sDeviceID & "]: Ок, место в трюме еще есть." & @CRLF)
    Return False
EndFunc   ;==>IsCargoFull


; ###############################################################################################################################
; # FUNCTION.......: _MoveCargo
; # DESCRIPTION....: Выполняет цикл перемещения руды из трюма на станцию через ADB (фоновые нажатия).
; # PARAMETERS ....: $sDeviceID - ID эмулятора (например, "127.0.0.1:21503").
; # RETURN.........: True - трюм пуст, False - ошибка выгрузки.
; ###############################################################################################################################
Func _MoveCargo($sDeviceID)
    _CW("_MoveCargo [" & $sDeviceID & "]: Начинаем цикл выгрузки..." & @CRLF)

    ; 1. ПОСЛЕДОВАТЕЛЬНОСТЬ КОМАНД (ADB Keyevents)
    ; 8 - '1' (Инвентарь), 7 - '0' (Трюм), 32 - 'D' (Выделить), 12 - '5' (Перенести), 14 - '7' (Ок)
    Local $aKeys[6] = [8, 8, 7, 32, 12, 14]
    
    For $i = 0 To UBound($aKeys) - 1
        RunWait('"' & $g_adbPath & '" -s ' & $sDeviceID & ' shell input keyevent ' & $aKeys[$i], "", @SW_HIDE)
        _HumanSleep(500, 800) ; Пауза для обработки команд игрой
    Next
    
    _HumanSleep(1500, 2000) ; Ждем завершения анимации переноса предметов

    ; Закрываем инвентарь (ESC = keyevent 111)
    RunWait('"' & $g_adbPath & '" -s ' & $sDeviceID & ' shell input keyevent 111', "", @SW_HIDE)
    _HumanSleep(500, 800)

    ; 2. ФИНАЛЬНАЯ ПРОВЕРКА (Поиск индикатора пустого трюма)
    Local $x, $y, $iMaxRetries = 3
    ; Фиксированная область индикатора (X1, Y1, X2, Y2)
    Local $aArea[4] = [0, 80, 60, 170]

    For $i = 1 To $iMaxRetries
        ; Снимаем свежий скриншот для проверки результата
        Local $sCheckFile = _Get_Screenshot_By_ID($sDeviceID)
        
        If $sCheckFile <> "" Then
            If _MyImageSearch("imgCargoEmpty.bmp", $sResourceDir, $aArea, $x, $y, 100, $sCheckFile) = 1 Then
                FileDelete($sCheckFile) ; Удаляем временный файл
                
                ; Обновление глобального счетчика выгрузок
                If IsDeclared("DeliveredCount") Then 
                    Assign("DeliveredCount", Eval("DeliveredCount") + 1, 2)
                EndIf
                
                _CW("_MoveCargo [" & $sDeviceID & "]: УСПЕХ. Трюм очищен." & @CRLF)
                Return True
            EndIf
            
            FileDelete($sCheckFile) ; Удаляем скриншот перед следующей попыткой
        EndIf

        _CW("_MoveCargo [" & $sDeviceID & "]: Ожидание очистки... Попытка " & $i & @CRLF)
        _HumanSleep(1000, 1500) 
    Next

    _CW("_MoveCargo [" & $sDeviceID & "]: ОШИБКА - Трюм не пуст." & @CRLF)
    Return False
EndFunc   ;==>_MoveCargo



; ###############################################################################################################################
; # FUNCTION.......: _OpenMenuIfNeed
; # DESCRIPTION....: Проверяет состояние меню и открывает его через ADB, если оно закрыто.
; # PARAMETERS ....: $sDeviceID - ID эмулятора.
; # RETURN.........: True - меню открыто, False - ошибка.
; ###############################################################################################################################
Func _OpenMenuIfNeed($sDeviceID)
    ; 1. Задаем фиксированную область поиска иконки "глаза" (для 1280x720)
    ; На основе смещения (1190, 370, 1260, 430)
    Local $aArea[4] = [1190, 370, 1260, 430]

    _CW("_OpenMenuIfNeed [" & $sDeviceID & "]: Проверка состояния меню..." & @CRLF)

    Local $x, $y
    Local $iMaxRetries = 3

    For $i = 1 To $iMaxRetries
        ; ШАГ А: Получаем свежий скриншот
        Local $sCurrentFile = _Get_Screenshot_By_ID($sDeviceID)
        If $sCurrentFile = "" Then ContinueLoop

        ; ШАГ Б: Проверяем наличие "глаза" (если 0 — значит меню уже развернуто)
        If _MyImageSearch("imgEyeIcon.bmp", $sResourceDir, $aArea, $x, $y, 100, $sCurrentFile) = 0 Then
            FileDelete($sCurrentFile) ; Чистим за собой
            _CW("_OpenMenuIfNeed [" & $sDeviceID & "]: Меню открыто." & @CRLF)
            Return True
        EndIf

        ; ШАГ В: Если меню закрыто, нажимаем клавишу (SC032 — это 'M' в EVE, ADB код 41)
        FileDelete($sCurrentFile) ; Удаляем скриншот перед нажатием
        _CW("_OpenMenuIfNeed [" & $sDeviceID & "]: Меню скрыто. Открываем (Попытка " & $i & ")" & @CRLF)
        
        _HumanSleep(200, 400)
        ; ADB Keyevent 41 соответствует клавише 'M'
        RunWait('"' & $g_adbPath & '" -s ' & $sDeviceID & ' shell input keyevent 41', "", @SW_HIDE)

        ; Ожидание анимации развертывания
        _HumanSleep(1000, 1500) 
    Next

    _CW("_OpenMenuIfNeed [" & $sDeviceID & "]: ОШИБКА - Не удалось развернуть меню." & @CRLF)
    Return False
EndFunc   ;==>_OpenMenuIfNeed




; ###############################################################################################################################
; # FUNCTION.......: _OpenBeltsList
; # DESCRIPTION....: Открывает список астероидных поясов через ADB и выбирает вкладку добычи.
; # PARAMETERS ....: $sDeviceID - ID эмулятора.
; #                  $bNeedToGo - если True, вызывает переход на пояс.
; # RETURN.........: True - список открыт, False - ошибка.
; ###############################################################################################################################
Func _OpenBeltsList($sDeviceID, $bNeedToGo)
    _CW("_OpenBeltsList [" & $sDeviceID & "]: Проверка списка астероидов..." & @CRLF)

    ; 1. Получаем базовый скриншот для первичной проверки
    Local $sFile = _Get_Screenshot_By_ID($sDeviceID)
    If $sFile = "" Then Return False

    Local $x, $y
    ; Local $aArea

    ; 2. Проверяем, не открыт ли список уже (MiningCurrent + SelectOre)
    Local $aArea[4] = [970, 0, 1104, 50] ; Область заголовка
    Local $bMiningCurrent = _MyImageSearch("imgMiningCurrent.bmp", $sResourceDir, $aArea, $x, $y, 100, $sFile)

    Local $aArea[4] = [1250, 50, 1280, 550] ; Область колонки выбора
    Local $bSelectOre = _MyImageSearch("imgSelectOreToMine.bmp", $sResourceDir, $aArea, $x, $y, 100, $sFile)

    If $bMiningCurrent = 1 And $bSelectOre = 1 Then
        _CW("_OpenBeltsList [" & $sDeviceID & "]: Список уже открыт." & @CRLF)
        FileDelete($sFile)
        If $bNeedToGo Then Return _GoToRandomBelt($sDeviceID) ; Функцию тоже нужно будет поправить под ADB
        Return True
    EndIf
    FileDelete($sFile) ; Удаляем старый скрин перед кликами

    ; 3. Открываем выпадающее меню
    _CW("_OpenBeltsList [" & $sDeviceID & "]: Открываем меню выбора..." & @CRLF)
    $sFile = _Get_Screenshot_By_ID($sDeviceID) ; Свежий скрин

    Local $aArea[4] = [970, 0, 1104, 50]
    
    If _FindAndClick("imgShowDropdownMy.bmp", $sResourceDir, $aArea, $sDeviceID, $sFile) Then
        FileDelete($sFile)
        _HumanSleep(600, 1000) ; Ждем раскрытия меню

        ; 4. Выбираем пункт добычи (в открывшемся списке)
        $sFile = _Get_Screenshot_By_ID($sDeviceID)
        Local $aArea[4] = [970, 0, 1104, 500]
        
        If _FindAndClick("imgMinigScreen.bmp", $sResourceDir, $aArea, $sDeviceID, $sFile) Then
            _CW("_OpenBeltsList [" & $sDeviceID & "]: Вкладка добычи выбрана." & @CRLF)
            FileDelete($sFile)
            _HumanSleep(800, 1200)

            ; 5. Финальный скриншот для проверки или перехода
            If $bNeedToGo Then Return _GoToRandomBelt($sDeviceID)
            Return True
        Else
            _CW("_OpenBeltsList [" & $sDeviceID & "]: Ошибка - вкладка не найдена." & @CRLF)
            FileDelete($sFile)
            Return False
        EndIf
    Else
        _CW("_OpenBeltsList [" & $sDeviceID & "]: Ошибка - кнопка меню не найдена." & @CRLF)
        FileDelete($sFile)
        Return False
    EndIf
EndFunc   ;==>_OpenBeltsList


; ###############################################################################################################################
; # FUNCTION.......: _WarpTo
; # DESCRIPTION....: Выбирает цель и выполняет варп через ADB с мониторингом состояния полета.
; # PARAMETERS ....: $sDeviceID   - ID эмулятора (127.0.0.1:21503).
; #                  $sTargetName - Название цели для лога.
; #                  $aTargetArea - Массив [X1, Y1, X2, Y2] координат цели внутри экрана 1280x720.
; # RETURN.........: True - прибыли, False - ошибка.
; ###############################################################################################################################
Func _WarpTo($sDeviceID, $sTargetName, $aTargetArea)
    _CW("_WarpTo [" & $sDeviceID & "]: Выбираем цель: " & $sTargetName & @CRLF)

    ; 1. Кликаем по цели в фоновом режиме (в центр области $aTargetArea)
    Local $iTargetX = $aTargetArea[0] + ($aTargetArea[2] - $aTargetArea[0]) / 2
    Local $iTargetY = $aTargetArea[1] + ($aTargetArea[3] - $aTargetArea[1]) / 2
    _ADB_Click($sDeviceID, $iTargetX, $iTargetY)
    _HumanSleep(600, 1000) ; Ждем появления кнопок действий

    ; 2. Область для поиска кнопки "Warp" (правая часть экрана)
    Local $aWarpBtnArea[4] = [700, 40, 1000, 700] 

    ; 3. Делаем скриншот для поиска кнопки варпа
    Local $sFile = _Get_Screenshot_By_ID($sDeviceID)
    If $sFile = "" Then Return False

    ; 4. Находим и нажимаем кнопку варпа
    If _FindAndClick("warp.png", $sResourceDir, $aWarpBtnArea, $sDeviceID, $sFile) Then
        FileDelete($sFile) ; Удаляем скрин после нажатия
        _CW("_WarpTo [" & $sDeviceID & "]: Кнопка нажата. Ждем входа в варп..." & @CRLF)
        
        ; 5. Координаты зоны индикаторов варпа (низ центральной части экрана)
        Local $aWarpZone = [404, 515, 808, 555] 
        Local $outX, $outY
        
        ; 6. Ждем индикатор начала варпа (используем обновленную функцию с $sDeviceID)
        ; Эта функция внутри себя будет делать и удалять скриншоты автоматически
        If _MyWaitForImageSearch("imgWarpTo.bmp", $sResourceDir, $aWarpZone, 30, $outX, $outY, 100, $sDeviceID) Then
            _CW("_WarpTo [" & $sDeviceID & "]: Корабль в варпе. Ожидаем прибытия..." & @CRLF)
            
            ; 7. Ждем индикатор выхода из варпа (остановка/появление интерфейса)
            ; Увеличиваем время ожидания для длинных перелетов
            If _MyWaitForImageSearch("imgShipStopping.bmp", $sResourceDir, $aWarpZone, 600, $outX, $outY, 100, $sDeviceID) Then
                _CW("_WarpTo [" & $sDeviceID & "]: Прибыли к " & $sTargetName & @CRLF)
                Return True
            Else
                _CW("_WarpTo [" & $sDeviceID & "]: Ошибка - Потеря визуального контроля при выходе." & @CRLF)
                Return False
            EndIf
        Else
            _CW("_WarpTo [" & $sDeviceID & "]: Варп не зафиксирован (возможно цель слишком близко)." & @CRLF)
            Return False
        EndIf
    Else
        FileDelete($sFile)
        _CW("_WarpTo [" & $sDeviceID & "]: ОШИБКА - Кнопка варпа не найдена." & @CRLF)
        Return False
    Endif
EndFunc   ;==>_WarpTo



; #FUNCTION# ====================================================================================================================
; Name...........: _GoToRandomBelt
; Description....: Ищет пояса по приоритету на ADB-скриншоте и инициирует варп.
; Syntax.........: _GoToRandomBelt($sDeviceID)
; Parameters ....: $sDeviceID - ID эмулятора (например, "127.0.0.1:21503").
; Return values .: True - Варп начат и успешно завершен, False - Пояса не найдены или ошибка.
; ===============================================================================================================================
Func _GoToRandomBelt($sDeviceID)
    _CW("_GoToRandomBelt [" & $sDeviceID & "]: Анализ доступных поясов..." & @CRLF)

    ; 1. Получаем скриншот (один для всех проверок в цикле)
    Local $sFile = _Get_Screenshot_By_ID($sDeviceID)
    If $sFile = "" Then Return False

    ; 2. База данных поясов: файлы и фиксированные области [X1, Y1, X2, Y2]
    Local $aBeltFiles[3] = ["imgBeltLarge.bmp", "imgBeltMedium.bmp", "imgBeltSmall.bmp"]
    Local $aOffsets[3][4] = [ _
        [967, 51, 995, 150], _  ; Large
        [967, 151, 995, 300], _ ; Medium
        [967, 301, 995, 433]  _ ; Small
    ]

    Local $x, $y
    Local $aTargetArea[4]

    ; 3. Перебор типов поясов
    For $i = 0 To 2
        ; Ищем пояс конкретного типа на текущем скриншоте
        If _MyImageSearch($aBeltFiles[$i], $sResourceDir, $aOffsets[$i], $x, $y, 100, $sFile) Then
            _CW("_GoToRandomBelt [" & $sDeviceID & "]: Обнаружен " & $aBeltFiles[$i] & @CRLF)
            
            ; Формируем область клика вокруг найденной иконки
            $aTargetArea[0] = $x - 10
            $aTargetArea[1] = $y - 10
            $aTargetArea[2] = $x + 10
            $aTargetArea[3] = $y + 10

            ; Удаляем скриншот перед уходом в длительную функцию варпа
            FileDelete($sFile)
            
            ; Вызываем обновленный _WarpTo (внутри него будут свои скриншоты для мониторинга полета)
            Return _WarpTo($sDeviceID, "Пояс приоритет " & ($i + 1), $aTargetArea)
        EndIf
    Next

    ; Если ничего не нашли, чистим за собой
    FileDelete($sFile)
    _CW("_GoToRandomBelt [" & $sDeviceID & "]: Подходящие пояса не найдены в списке." & @CRLF)
    Return False
EndFunc   ;==>_GoToRandomBelt


; ###############################################################################################################################
; #                                                                                                                             #
; # PROJECT........: EVE Echoes Bots                                                                                            #
; # VERSION........: 1.1.2                                                                                                      #
; # BUILD..........: 2026.04.27                                                                                                 #
; # FILENAME.......: ScreenshotMaker.au3                                                                                        #
; # GITHUB.........: https://github.com                                                          #
; # DESCRIPTION....: Автоматизированный комплекс управления добычей руды.                                                       #
; #                  - Перелет и стыковка со станцией через ADB.                                                                #
; #                  - Мониторинг состояния варпа и подтверждение дока.                                                         #
; #                                                                                                                             #
; ###############################################################################################################################

; #FUNCTION# ====================================================================================================================
; Name...........: _GoToStation
; Description....: Выполняет перелет и стыковку со станцией через ADB с проверкой результата по кнопке Undock.
; Syntax.........: _GoToStation($sDeviceID)
; Parameters ....: $sDeviceID - ID эмулятора (например, "127.0.0.1:21503").
; Return values .: True - Корабль в доке, False - Ошибка.
; ===============================================================================================================================
Func _GoToStation($sDeviceID)
    _CW("_GoToStation [" & $sDeviceID & "]: Возвращаемся на базу..." & @CRLF)

    Local $x, $y, $sFile
    ; Local $aArea

    ; 1. ВЫБОР ФИЛЬТРА СТАНЦИЙ
    $sFile = _Get_Screenshot_By_ID($sDeviceID)
    If $sFile = "" Then Return False

    Local $aArea[4] = [970, 1, 1010, 40]
    If Not _FindAndClick("imgShowDropdownMy.bmp", $sResourceDir, $aArea, $sDeviceID, $sFile) Then 
        FileDelete($sFile)
        Return False
    EndIf
    FileDelete($sFile)
    _HumanSleep(600, 1000)

    ; Выбираем пункт "Станции"
    $sFile = _Get_Screenshot_By_ID($sDeviceID)
    local $aArea[4] = [970, 50, 1220, 720]
    If Not _FindAndClick("imgStationFilter.bmp", $sResourceDir, $aArea, $sDeviceID, $sFile) Then
        FileDelete($sFile)
        Return False
    EndIf
    FileDelete($sFile)
    _HumanSleep(800, 1200)

    ; 2. ВЫБОР СТАНЦИИ В ГРИДЕ
    $sFile = _Get_Screenshot_By_ID($sDeviceID)
    Local $aArea[4] = [970, 50, 995, 433]
    If Not _MyImageSearch("imgStationLocation.bmp", $sResourceDir, $aArea, $x, $y, 100, $sFile) Then
        FileDelete($sFile)
        _CW("_GoToStation [" & $sDeviceID & "]: Станция не найдена в списке." & @CRLF)
        Return False
    EndIf
    _ADB_Click($sDeviceID, $x, $y)
    FileDelete($sFile)
    _HumanSleep(600, 1000)

    ; 3. НАЖАТИЕ КНОПКИ "DOCK"
    $sFile = _Get_Screenshot_By_ID($sDeviceID)
    Local $aDockArea[4] = [1060, 230, 1280, 300]
    If Not _FindAndClick("imgDockBtn.bmp", $sResourceDir, $aDockArea, $sDeviceID, $sFile) Then
        _CW("_GoToStation [" & $sDeviceID & "]: Кнопка 'Dock' не появилась." & @CRLF)
        FileDelete($sFile)
        Return False
    EndIf
    FileDelete($sFile)

    ; 4. МОНИТОРИНГ ПОЛЕТА И СТЫКОВКИ
    _CW("_GoToStation [" & $sDeviceID & "]: Ожидаем завершение стыковки..." & @CRLF)
    
    Local $aWarpZone[4] = [404, 515, 808, 555]
    Local $hTimer = TimerInit()
    
    While TimerDiff($hTimer) < 180000 ; Таймаут 3 минуты
        $sFile = _Get_Screenshot_By_ID($sDeviceID)
        If $sFile = "" Then 
            Sleep(1000)
            ContinueLoop
        EndIf

        ; Проверка 1: Мы уже в доке? (Ищем кнопку Undock)
        If _MyImageSearch("imgUnDock.bmp", $sResourceDir, $aDockArea, $x, $y, 100, $sFile) Then
            _CW("_GoToStation [" & $sDeviceID & "]: Успешно пристыкованы." & @CRLF)
            FileDelete($sFile)
            Return True
        EndIf

        ; Проверка 2: Мы еще летим?
        If _MyImageSearch("imgWarpTo.bmp", $sResourceDir, $aWarpZone, $x, $y, 100, $sFile) Then
            _CW("_GoToStation [" & $sDeviceID & "]: В варпе..." & @CRLF)
            FileDelete($sFile)
            ; Ждем выхода из варпа (функция сама делает/удаляет скрины)
            _MyWaitForImageSearch("imgShipStopping.bmp", $sResourceDir, $aWarpZone, 120, $x, $y, 100, $sDeviceID)
            ContinueLoop
        EndIf

        FileDelete($sFile)
        Sleep(2000) ; Интервал проверки состояния
    WEnd

    _CW("_GoToStation [" & $sDeviceID & "]: ОШИБКА - Превышено время ожидания дока." & @CRLF)
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

