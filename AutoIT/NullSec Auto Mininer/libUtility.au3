; ###############################################################################################################################
; #                                                                                                                             #
; # PROJECT........: NullSec Auto Mininer (Utility Library)                                                                     #
; # VERSION........: 1.0.0                                                                                                      #
; # BUILD..........: 2026.04.25                                                                                                 #
; # FILENAME.......: libUtility.au3                                                                                             #
; # GITHUB.........: https://github.com/Tatooine104/EVE-Echoes-Bot.git                                                          #
; # DESCRIPTION....: Библиотека общих и служебных функций бота.                                                                 #
; #                                                                                                                             #
; # FUNCTIONS......: _Log                   - Запись событий в файл, консоль и GUI статус-бар.                                  #
; #                  _HumanSleep            - Рандомизированная пауза для имитации действий человека.                           #
; #                                                                                                                             #
; ###############################################################################################################################

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
; Name...........: _Log
; Description....: Записывает сообщение в файл лога, обновляет статус-бар GUI и выводит в консоль.
; Syntax.........: _Log($sText[, $bDebug = Default[, $hControlID = Default]])
; Parameters ....: $sText       - Сообщение для записи в лог.
;                  $bDebug      - [Optional] Принудительное включение/выключение вывода в консоль (True/False).
;                                 Если Default, используется глобальная переменная $Debug.
;                  $hControlID  - [Optional] ID элемента GUI (Label/Input) для вывода текста.
;                                 Если Default, ищется глобальная переменная $hStatusLabel.
; Return values .: 1 - Успешно.
;                  0 - Ошибка открытия файла лога.
; Updated .......: 2026.04.25
; Version .......: 1.05
; Remarks .......: Автоматически создает папку \Logs\. Безопасно работает в скриптах без GUI.
; ===============================================================================================================================
Func _Log($sText, $bDebug = Default, $hControlID = Default)
    ; 1. Определяем режим отладки (приоритет: параметр -> глобальная переменная -> False)
    If $bDebug = Default Then
        $bDebug = IsDeclared("Debug") ? Eval("Debug") : False
    EndIf

    ; 2. Подготовка путей и папок
    Local $sLogDir = @ScriptDir & "\Logs"
    If Not FileExists($sLogDir) Then DirCreate($sLogDir)

    Local $sLogFileName = StringTrimRight(@ScriptName, 4) & ".log"
    Local $sFullPath = $sLogDir & "\" & $sLogFileName

    ; 3. Формирование строки записи [ГГГГ.ММ.ДД ЧЧ:ММ:СС]
    Local $sLogEntry = StringFormat("[%s.%s.%s %s:%s:%s] -> %s", @YEAR, @MON, @MDAY, @HOUR, @MIN, @SEC, $sText)

    ; 4. Вывод в консоль Scite
    If $bDebug Then ConsoleWrite($sLogEntry & @CRLF)

    ; 5. Обновление GUI
    ; Если передан конкретный ID контрола — используем его
    If $hControlID <> Default And $hControlID <> 0 Then
        GUICtrlSetData($hControlID, $sText)
    ; Иначе ищем стандартную глобальную переменную hStatusLabel
    ElseIf IsDeclared("hStatusLabel") Then
        GUICtrlSetData(Eval("hStatusLabel"), $sText)
    EndIf

    ; 6. Запись в файл
    Local $hFile = FileOpen($sFullPath, 1 + 8) ; 1 = Append, 8 = Create directory structure
    If $hFile = -1 Then Return 0
    
    FileWriteLine($hFile, $sLogEntry)
    FileClose($hFile)

    Return 1
EndFunc   ;==>_Log



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
Func _SaveToIni($sSection, $sKey, $vValue, $sFilePath = Default)
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
