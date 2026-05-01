; ###############################################################################################################################
; #                                                                                                                             #
; # PROJECT........: NullSec Auto Mininer (Utility Library)                                                                     #
; # VERSION........: 1.0.0                                                                                                      #
; # BUILD..........: 2026.04.26                                                                                                 #
; # FILENAME.......: libUtility.au3                                                                                             #
; # GITHUB.........: https://github.com/Tatooine104/EVE-Echoes-Bot.git                                                          #
; # DESCRIPTION....: Библиотека общих и служебных функций бота.                                                                 #
; #                                                                                                                             #
; # FUNCTIONS......: _CheckAndActivateClient - Проверка существования и активация окна клиента.                                 #
; #                  _CW                     - Вывод текста в консоль с корректной кодировкой (UTF-8).                          #
; #                  _HumanSleep            - Рандомизированная пауза для имитации действий человека.                           #
; #                  _Log                   - Запись событий в файл, консоль и расширенный GUI лог.                             #
; #                  _SaveToIni             - Сохранение параметров в конфигурационный файл.                                    #
; #                  _SwitchToNextClient    - Переключение между несколькими окнами.                                            #
; #                                                                                                                             #
; ###############################################################################################################################

#include-once ; Добавить в первую строку файла библиотеки

; #FUNCTION# ====================================================================================================================
; Name...........: _SaveToIni
; Description....: Записывает значение в INI-файл конфигурации.
; Syntax.........: _SaveToIni($sSection, $sKey, $vValue[, $sFilePath = Default])
; Parameters ....: $sSection    - Название секции INI-файла.
;                  $sKey        - Название ключа.
;                  $vValue      - Значение для записи.
;                  $sFilePath   - [Optional] Путь к INI-файлу. Если Default, ищется глобальная $sIniPath 
;                                 или создается файл с именем скрипта в корне.
; Return values .: 1 - Успешно.
;                  0 - Ошибка записи (например, файл защищен от записи).
; Updated .......: 2026.04.25
; Version .......: 1.01
; Remarks .......: Автоматически определяет путь к конфигу, если он не передан явно.
; ===============================================================================================================================
Func _SaveToIni($sSection, $sKey, $vValue, $sFilePath = "data.ini")
    ; 1. Определяем путь к файлу
    If $sFilePath = Default Then
        ; Если есть глобальная переменная $sIniPath — берем её, иначе создаем рядом со скриптом
        If IsDeclared("sIniPath") Then
            $sFilePath = Eval("sIniPath")
        Else
            $sFilePath = @ScriptDir & "\" & StringTrimRight(@ScriptName, 4) & ".ini"
        EndIf
    EndIf

    ; 2. Запись данных
    Local $iResult = IniWrite($sFilePath, $sSection, $sKey, $vValue)
    
    Return $iResult
EndFunc   ;==>_SaveToIni

; #FUNCTION# ====================================================================================================================
; Name...........: _HumanSleep
; Description....: Выполняет рандомизированную паузу для имитации естественного поведения человека.
; Syntax.........: _HumanSleep($iMin = 100, $iMax = 800)
; Parameters ....: $iMin       - [Optional] Минимальное время ожидания в миллисекундах (по умолчанию 100).
;                  $iMax       - [Optional] Максимальное время ожидания в миллисекундах (по умолчанию 800).
; Return values .: Время выдержанной паузы в мс.
; Updated .......: 2026.04.25
; Version .......: 1.01
; Remarks .......: Если пауза превышает 1000 мс, автоматически отправляет запись в лог.
; ===============================================================================================================================
Func _HumanSleep($iMin = 100, $iMax = 800)

    ; Гарантируем, что входные данные — числа
    $iMin = Int($iMin)
    $iMax = Int($iMax)

    ; Генерируем случайное целое число в заданном диапазоне
    Local $iWait = Random($iMin, $iMax, 1)
    
    ; Логируем только значительные паузы (более 1 секунды)
    If $iWait > 1000 Then 
        _Log(StringFormat("Пауза: сек.", $iWait / 1000))
    EndIf
    
    Sleep($iWait)
    
    Return $iWait

EndFunc   ;==>_HumanSleep


; #FUNCTION# ====================================================================================================================
; Name ..........: _MEmu_SetSize
; Description ...: Устанавливает указанное разрешение для окна эмулятора по его хендлу.
; Syntax ........: _MEmu_SetSize($hWnd, $iWidth, $iHeight)
; Parameters ....: $hWnd   - Хендл (HWND) целевого окна.
;                  $iWidth  - Желаемая ширина окна.
;                  $iHeight - Желаемая высота окна.
; Return values .: Success: True. Failure: False, Sets @error = 1 (окно не найдено).
; Author ........: [Твое Имя]
; Remarks .......: Окно будет перемещено в координаты 0,0 для удобства, если нужно просто изменить размер — убери 0, 0.
; ===============================================================================================================================
Func _MEmu_SetSize($hWnd, $iWidth, $iHeight)

    If Not WinExists($hWnd) Then 
        _CW("_MEmu_SetSize Ошибка: Не найдено окно эмулятора")
        Return SetError(1, 0, False)
    EndIf

    If _SetInnerSize($hWnd, $iWidth, $iHeight) Then
        Return True
    Else
        _CW("_MEmu_SetSize Ошибка: Не удалось изменить размер окна")
        Return False
    EndIf

EndFunc   ;==>_MEmu_SetSize


; #FUNCTION# ====================================================================================================================
; Name ..........: _SetInnerSize
; Description ...: Устанавливает размер ВНУТРЕННЕЙ области окна (без учета рамок и заголовка).
; ===============================================================================================================================
Func _SetInnerSize($hWnd, $iWidth, $iHeight)

    Local $aPos, $aClient, $iDiffW, $iDiffH

    $aPos = WinGetPos($hWnd)      ; Получаем внешний размер окна
    $aClient = WinGetClientSize($hWnd) ; Получаем внутренний размер окна

    If Not IsArray($aPos) Or Not IsArray($aClient) Then 
        _CW("_SetInnerSize Ошибка: Не удалось изменить размер окна")
        Return False
    Else
        ; Считаем разницу (сколько занимают рамки и заголовок)
        $iDiffW = $aPos[2] - $aClient[0]
        $iDiffH = $aPos[3] - $aClient[1]
        
        ; Изменяем размер окна так, чтобы внутри получилось ровно $iWidth x $iHeight
        WinMove($hWnd, "", $aPos[0], $aPos[1], $iWidth + $iDiffW, $iHeight + $iDiffH)

        Return True
    EndIf

EndFunc

; #FUNCTION# ====================================================================================================================
; Name ..........: _Log
; Description ...: Логирует сообщение с меткой времени в консоль или в файл.
; Syntax ........: _Log($sText[, $lDebug = True[, $sFilePath = "log.txt"]])
; Parameters ....: $sText       - Текст сообщения для записи в лог.
;                  $lDebug      - [Опционально] Определяет способ вывода: True (по умолчанию) - консоль, False - файл.
;                  $sFilePath   - [Опционально] Путь к файлу лога. По умолчанию "log.txt" в папке скрипта.
; Return values .: None
; Author ........: [Твое Имя]
; Remarks .......: Для вывода в консоль используется вспомогательная функция _CW для корректной поддержки UTF-8.
; ===============================================================================================================================
Func _Log($sText, $lDebug = True, $sFilePath = "log.txt")
    ; Формируем строку лога: [ГГГГ.ММ.ДД ЧЧ:ММ:СС] Текст
    Local $sMsg = StringFormat("[%s.%s.%s %s:%s:%s] %s", @YEAR, @MON, @MDAY, @HOUR, @MIN, @SEC, $sText)

    If $lDebug Then
        ; Вывод в консоль (используем твою _CW, передаем строку как есть)
        _CW($sMsg)
    Else
        ; Запись в файл (1 = Append, 256 = Force UTF-8)
        Local $hFile = FileOpen($sFilePath, 1 + 256)
        If $hFile <> -1 Then
            FileWriteLine($hFile, $sMsg)
            FileClose($hFile)
        EndIf
    EndIf
EndFunc   ;==>_Log

; #FUNCTION# ====================================================================================================================
; Name ..........: _GetHandleByTitle
; Description ...: Возвращает хендл конкретного окна по его точному названию.
; Syntax ........: _GetHandleByTitle($sTitle)
; Parameters ....: $sTitle - Полное или частичное имя окна (зависит от WinTitleMatchMode).
; Return values .: Success: hWnd окна. Failure: Sets @error = 1 и возвращает 0.
; ===============================================================================================================================
Func _GetHandleByTitle($sTitle)
    Local $hWnd = WinGetHandle($sTitle)
    
    If @error Then 
        _CW("_GetHandleByTitle Ошибка: Эмулятор не найден!")
        Return SetError(1, 0, 0)
    EndIf
    
    Return $hWnd
EndFunc   ;==>_GetHandleByTitle

; #FUNCTION# ====================================================================================================================
; Name ..........: _CW
; Description ...: Выводит текст в консоль с принудительной перекодировкой в UTF-8.
; Syntax ........: _CW($sText)
; Parameters ....: $sText - Строка текста для вывода.
; Return values .: None
; Author ........: Tatooine104
; Modified ......: 
; Remarks .......: Помогает избежать проблем с кодировкой (кракозябр) в консоли SciTE.
; Related .......: 
; Example .......: _CW("Привет, MEmu!")
; ===============================================================================================================================
Func _CW($sText)

    ; Преобразуем строку в бинарный вид (UTF-8) и обратно в строку для корректного вывода
    ConsoleWrite(BinaryToString(StringToBinary($sText & @CRLF, 4), 1))

EndFunc   ;==>_CW




; - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -
; - + - + - + - + -  |  Старые функции, требующие актуализации  | - + - + - + - + - + - + - + - + - + - + - 
; - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + - + -





#cs





#include <WinAPIProc.au3>
#include <WinAPIFiles.au3>
#include <WinAPISys.au3>
#include "..\Resource\libGUI.au3"

Global $g_adbPath = "C:\Program Files\Microvirt\MEmu\adb.exe"

; ===============================================================================================================================
; КЛИК ЧЕРЕЗ ADB
; ===============================================================================================================================
Func _ADB_Click($sDeviceID, $ix, $iy)
    _HumanSleep(50, 150)
    RunWait('"' & $g_adbPath & '" -s ' & $sDeviceID & ' shell input tap ' & $ix & ' ' & $iy, "", @SW_HIDE)
EndFunc

; #FUNCTION# ====================================================================================================================
; Name...........: _Util_PrepareClient
; Description....: Находит окно по имени процесса и принудительно устанавливает размер клиентской области.
; Syntax.........: _Util_PrepareClient($sProcessName, $iWidth = 1280, $iHeight = 720)
; Parameters ....: $sProcessName - Имя процесса (например, "MEmu.exe").
;                  $iWidth       - Нужная ширина клиентской части.
;                  $iHeight      - Нужная высота клиентской части.
; Return values .: Handle окна (HWND) при успехе, 0 при ошибке.
; Updated .......: 2026.04.26
; Version .......: 1.00
; Remarks .......: Автоматически корректирует внешние размеры окна, чтобы внутренняя часть соответствовала стандарту.
; ===============================================================================================================================
Func _Util_PrepareClient($sProcessName, $iWidth = 1280, $iHeight = 720)
    Local $hTarget = 0
    Local $aWinList = WinList()
    
    _Log(StringFormat("Поиск игрового окна процесса %s...", $sProcessName))

    For $i = 1 To $aWinList[0][0]
        If Not BitAND(WinGetState($aWinList[$i][1]), 2) Then ContinueLoop ; Пропускаем невидимые
        
        Local $iPid
        _WinAPI_GetWindowThreadProcessId($aWinList[$i][1], $iPid)
        
        If _Util_ProcessGetName($iPid) = $sProcessName Then
            Local $aSize = WinGetClientSize($aWinList[$i][1])
            If IsArray($aSize) And $aSize[0] > 600 Then ; Фильтр по минимальному размеру
                $hTarget = $aWinList[$i][1]
                
                _Log("Окно найдено: " & $aWinList[$i][0])
                
                ; Подгонка размера
                Local $aWinPos = WinGetPos($hTarget)
                Local $iDiffW = $aWinPos[2] - $aSize[0]
                Local $iDiffH = $aWinPos[3] - $aSize[1]
                
                WinMove($hTarget, "", $aWinPos[0], $aWinPos[1], $iWidth + $iDiffW, $iHeight + $iDiffH)
                Sleep(500)
                
                Local $aFinalSize = WinGetClientSize($hTarget)
                _Log(StringFormat("Размер скорректирован: %dx%d", $aFinalSize[0], $aFinalSize[1]))
                Return $hTarget
            EndIf
        EndIf
    Next

    _Log("ОШИБКА: Подходящее окно процесса " & $sProcessName & " не найдено.")
    Return 0
EndFunc   ;==>_Util_PrepareClient

; Вспомогательная внутренняя функция
Func _Util_ProcessGetName($iPid)
    Local $aProc = ProcessList()
    For $i = 1 To $aProc[0][0]
        If $aProc[$i][1] = $iPid Then Return $aProc[$i][0]
    Next
    Return ""
EndFunc   ;==>_Util_ProcessGetName

; #FUNCTION# ====================================================================================================================
; Name...........: _SwitchToNextClient
; Description....: Переключает контекст на следующее окно из переданного массива.
; Syntax.........: _SwitchToNextClient(ByRef $aWindowList, ByRef $iCurrentIdx)
; Parameters ....: $aWindowList - Массив с заголовками окон.
;                  $iCurrentIdx  - [ByRef] Переменная-индекс текущего активного окна.
; Return values .: True - Окно успешно активировано, False - Ошибка активации.
; Updated .......: 2026.04.26
; Version .......: 1.10
; Remarks .......: Автоматически зацикливает индекс при достижении конца массива.
; ===============================================================================================================================
Func _SwitchToNextClient(ByRef $aWindowList, ByRef $iCurrentIdx)
    
    If Not IsArray($aWindowList) Then Return False

    ; 1. Увеличиваем индекс и зацикливаем его
    $iCurrentIdx += 1
    If $iCurrentIdx >= UBound($aWindowList) Then $iCurrentIdx = 0
    
    Local $sTargetTitle = $aWindowList[$iCurrentIdx]
    _Log(">>> Смена фокуса: " & $sTargetTitle)
    
    ; 2. Активируем выбранное окно
    If _CheckAndActivateClient($sTargetTitle) Then
        ; Обновляем информацию в GUI, если переменная ClientName используется для отображения
        If IsDeclared("ClientName") Then Assign("ClientName", $sTargetTitle, 2)
        Return True
    EndIf
    
    Return False

EndFunc   ;==>_SwitchToNextClient


; #FUNCTION# ====================================================================================================================
; Name...........: _CheckAndActivateClient
; Description....: Проверяет существование окна и активирует его.
; Syntax.........: _CheckAndActivateClient($sTitle)
; Parameters ....: $sTitle      - Заголовок окна или его дескриптор (HWND).
; Return values .: True         - Окно успешно найдено и активировано.
;                  False        - Окно не найдено или не смогло стать активным.
; Updated .......: 2026.04.25
; Version .......: 1.02
; Remarks .......: Пытается записать дескриптор в глобальную переменную $hClient в основном скрипте.
; ===============================================================================================================================
Func _CheckAndActivateClient($sTitle)
    ; 1. Пытаемся получить дескриптор окна
    Local $hWnd = WinGetHandle($sTitle)
    If @error Then 
        _Log("_CheckAndActivateClient: Окно '" & $sTitle & "' не существует.")
        Return False
    EndIf

    ; 2. Записываем Handle в глобальную переменную основного скрипта по её имени
    ; Используем "hClient", так как она объявлена в главном файле
    If IsDeclared("hClient") Then 
        Assign("hClient", $hWnd, 2) ; Флаг 2 — принудительная запись в Global
    EndIf

    ; 3. Активируем окно
    WinActivate($hWnd)

    ; 4. Ждем активации (таймаут 1 секунда)
    If WinWaitActive($hWnd, "", 1) Then
        Return True
    Else
        _Log("_CheckAndActivateClient: Окно найдено, но не удалось сделать его активным.")
        Return False
    EndIf
EndFunc   ;==>_CheckAndActivateClient




; #FUNCTION# ====================================================================================================================
; Name...........: _Log
; Description....: Записывает сообщение в лог-файл, консоль и обновляет GUI (включая список последних 5 событий).
; Syntax.........: _Log($sText[, $bDebug = Default[, $hControlID = Default]])
; Parameters ....: $sText       - Сообщение для записи в лог.
;                  $bDebug      - [Optional] Режим отладки (True/False). По умолчанию берется из $Debug.
;                  $hControlID  - [Optional] ID элемента для основного статуса. По умолчанию $hStatusLabel.
; Return values .: 1 - Успешно, 0 - Ошибка доступа к файлу.
; Updated .......: 2026.04.26
; Version .......: 1.06
; Remarks .......: Интегрирована функция _GUI_AddLog для циклического отображения событий в интерфейсе.
; ===============================================================================================================================
Func _Log($sText, $sDeviceID = "", $bDebug = Default, $hControlID = Default)
    ; 1. Подготовка глобальных ссылок
    Global $Debug, $hStatusLabel, $hGUI_Log

    ; 2. Определяем режим отладки
    If $bDebug = Default Then
        $bDebug = IsDeclared("Debug") ? $Debug : False
    EndIf

    ; 3. Формирование префикса устройства
    Local $sDevPrefix = ($sDeviceID <> "" ? "[" & $sDeviceID & "] " : "")
    
    ; 4. Подготовка путей
    Local $sLogDir = @ScriptDir & "\Logs"
    If Not FileExists($sLogDir) Then DirCreate($sLogDir)
    Local $sLogFileName = StringTrimRight(@ScriptName, 4) & ".log"
    Local $sFullPath = $sLogDir & "\" & $sLogFileName

    ; 5. Формирование строки записи [ЧЧ:ММ:СС]
    Local $sTime = StringFormat("%s:%s:%s", @HOUR, @MIN, @SEC)
    Local $sLogEntry = StringFormat("[%s] %s%s", $sTime, $sDevPrefix, $sText)

    ; 6. Вывод в консоль (через библиотечную функцию _CW)
    If $bDebug Then _CW($sLogEntry & @CRLF)

    ; 7. Обновление GUI
    ; Обновляем основной статус-бар (если передан ID или есть глобальный ярлык)
    If $hControlID <> Default And $hControlID <> 0 Then
        GUICtrlSetData($hControlID, $sText)
    ElseIf IsDeclared("hStatusLabel") Then
        GUICtrlSetData($hStatusLabel, $sText)
    EndIf

    ; Добавляем строку в список логов GUI (если библиотека GUI подключена)
    If IsDeclared("hGUI_Log") Then
        ; Используем внутреннюю функцию GUI для добавления строки в List/Edit
        ; Обновляем список последних 5 событий в GUI
        If IsDeclared("hGUI_Log") Then
            ; Проверяем, существует ли функция _GUI_AddLog
            Local $hFunc = IsFunc("_GUI_AddLog")
            If $hFunc Then _GUI_AddLog($sLogEntry)
        EndIf
    EndIf

    ; 8. Запись в файл (режим 1 + 8: Append + Create Dir)
    Local $hFile = FileOpen($sFullPath, 1 + 8) 
    If $hFile = -1 Then Return 0
    
    FileWriteLine($hFile, $sLogEntry)
    FileClose($hFile)

    Return 1
EndFunc   ;==>_Log
#ce
