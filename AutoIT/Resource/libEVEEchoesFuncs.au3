#include "..\Resource\libImageSearch.au3"    ; Содержит функции поиска изображений на экране
#include "..\Resource\libUtility.au3"        ; Содержит общий и служебные функции 
#include "..\Resource\libGUI.au3"            ; Подключаем вашу новую библиотеку интерфейса

; Создаем временную папку для ресурсов во временном каталоге системы
Global $sResourceDir = @TempDir & "\MyBotResources\"
DirCreate($sResourceDir)

; #FUNCTION# ====================================================================================================================
; Name...........: _IsSafe
; Description....: Проверяет локальный чат через ADB и обновляет глобальный статус безопасности окна.
; Syntax.........: _IsSafe($sDeviceID, $iClientIdx)
; Parameters ....: $sDeviceID  - ID эмулятора (например, "127.0.0.1:21503").
;                  $iClientIdx - Индекс текущего окна в глобальном массиве $aIsSave.
; Return values .: True - Врагов нет, False - Обнаружена угроза.
; ===============================================================================================================================
Func _IsSafe($sDeviceID, $iClientIdx)
    ; 1. Получаем скриншот для анализа
    Local $sFile = _Get_Screenshot_By_ID($sDeviceID)
    If $sFile = "" Then Return False

    ; 2. Область локального чата (фиксированные координаты для 1280x720)
    Local $aArea[4] = [0,330,400,7200] 

    ; Маркеры угроз: Криминал, Минус, Нейтрал
    Local $aMarkers[3] = ["imgLocalStatCriminal.bmp", "imgLocalStatMinus.bmp", "imgLocalStatNeitral.bmp"]
    Local $x, $y, $outX, $outY
    Local $iTolerance = 100
    Local $bStatus = True ; По умолчанию безопасно

    _CW("_IsSafe [" & $sDeviceID & "]: Мониторинг локала..." & @CRLF)

    ; 3. Проверка маркеров угроз на текущем скриншоте
    For $i = 0 To 2
        If _MyImageSearch($aMarkers[$i], $sResourceDir, $aArea, $x, $y, $iTolerance, $sFile) Then
            
            ; 4. Проверка на "дружелюбность" (ложная тревога: софлотовец или сокорп)
            ; Задаем область справа от найденного маркера
            Local $aStatusArea[4] = [$aArea[0] + 10, $aArea[0] + 20, $aArea[0] + 60, $aArea[0] + 20] 
            
            ; Если иконка "своего" (imgLocalStatNull.bmp) НЕ найдена рядом с маркером
            If Not _MyImageSearch("imgLocalStatNull.bmp", $sResourceDir, $aStatusArea, $outX, $outY, $iTolerance, $sFile) Then
                _CW("_IsSafe [" & $sDeviceID & "]: !!! ВНИМАНИЕ: ОБНАРУЖЕН ВРАГ !!!" & @CRLF)
                $bStatus = False
                ExitLoop 
            EndIf
        EndIf
    Next

    ; Удаляем проанализированный файл
    FileDelete($sFile)

    ; 5. Записываем результат в глобальный массив статусов
    If IsDeclared("aIsSave") Then
        ; Обновляем значение напрямую в глобальном массиве
        Global $aIsSave ; Обращаемся к объявленной ранее переменной
        $aIsSave[$iClientIdx] = $bStatus
    EndIf

    Return $bStatus
EndFunc   ;==>_IsSafe



; #FUNCTION# ====================================================================================================================
; Name...........: _AlliChatMessage
; Description....: Открывает чат альянса и отправляет сообщение разведки через ADB.
; Syntax.........: _AlliChatMessage($sDeviceID)
; Parameters ....: $sDeviceID - ID эмулятора (например, "127.0.0.1:21503").
; Return values .: True         - Сообщение успешно отправлено.
;                  False        - Ошибка на одном из этапов.
; Updated .......: 2026.04.28
; Version .......: 1.14
; Remarks .......: Работает в фоновом режиме. Каждый шаг подтверждается новым скриншотом.
; ===============================================================================================================================
Func _AlliChatMessage($sDeviceID)
    _CW("_AlliChatMessage [" & $sDeviceID & "]: Подготовка к отправке разведданных...")

    Local $sFile, $aArea, $x, $y

    ; 1. Нажимаем на иконку чата (нижний левый угол)
    _ADB_Click($sDeviceID, 25, 625) 
    _HumanSleep(800, 1200)

    ; 2. Проверяем статус чата
    $sFile = _Get_Screenshot_By_ID($sDeviceID)
    If $sFile = "" Then Return False

    Local $aArea[4] = [0, 30, 104, 720] ; Левая панель вкладок
    Local $bIsChatOpen = _MyImageSearch("imgAllianceChat.bmp", $sResourceDir, $aArea, $x, $y, 100, $sFile)
    Local $bIsChatActive = _MyImageSearch("imgAllianceChatActive.bmp", $sResourceDir, $aArea, $x, $y, 100, $sFile)

    If $bIsChatOpen Or $bIsChatActive Then
        
        ; 3. Активируем вкладку, если она не активна
        If $bIsChatOpen And Not $bIsChatActive Then
            If Not _FindAndClick("imgAllianceChat.bmp", $sResourceDir, $aArea, $sDeviceID, $sFile) Then 
                _CW("_AlliChatMessage: Ошибка активации вкладки.")
                FileDelete($sFile)
                Return False
            EndIf
            FileDelete($sFile)
            _HumanSleep(600, 900)
            $sFile = _Get_Screenshot_By_ID($sDeviceID) ; Обновляем скрин
        EndIf

        ; 4. Нажимаем на кнопку меню чата (плюсик/меню)
        Local $aArea[4] = [320, 650, 390, 720]
        If Not _FindAndClick("imgChatMenu.bmp", $sResourceDir, $aArea, $sDeviceID, $sFile) Then
            _CW("_AlliChatMessage: Кнопка 'imgChatMenu.bmp' не найдена.")
            FileDelete($sFile)
            Return False
        EndIf
        FileDelete($sFile)
        _HumanSleep(400, 600)

        ; 5. Нажимаем кнопку "Информировать"
        $sFile = _Get_Screenshot_By_ID($sDeviceID)
        Local $aArea[4] = [1050, 630, 1280, 720]
        If Not _FindAndClick("imgInformButton.bmp", $sResourceDir, $aArea, $sDeviceID, $sFile) Then 
            _CW("_AlliChatMessage: Кнопка 'imgInformButton.bmp' не найдена.")
            FileDelete($sFile)
            Return False
        EndIf
        FileDelete($sFile)
        _HumanSleep(500, 800)

        ; 6. Выбираем раздел "Разведка"
        $sFile = _Get_Screenshot_By_ID($sDeviceID)
        Local $aArea[4] = [5, 295, 200, 595]
        If Not _FindAndClick("imgIntelligence.bmp", $sResourceDir, $aArea, $sDeviceID, $sFile) Then 
            _CW("_AlliChatMessage: Раздел 'imgIntelligence.bmp' не найден.")
            FileDelete($sFile)
            Return False
        EndIf
        FileDelete($sFile)
        _HumanSleep(500, 800)

        ; 7. Выбираем сообщение угрозы
        $sFile = _Get_Screenshot_By_ID($sDeviceID)
        Local $aArea[4] = [190, 400, 490, 550]
        If Not _FindAndClick("imgWarningMessage.bmp", $sResourceDir, $aArea, $sDeviceID, $sFile) Then 
            _CW("_AlliChatMessage: Сообщение 'imgWarningMessage.bmp' не найдено.")
            FileDelete($sFile)
            Return False
        Endif
        FileDelete($sFile)
        _HumanSleep(500, 800)

        ; 8. Нажимаем кнопку "Отправить"
        $sFile = _Get_Screenshot_By_ID($sDeviceID)
        Local $aArea[4] = [360, 200, 560, 300]
        If Not _FindAndClick("imgSendButton.bmp", $sResourceDir, $aArea, $sDeviceID, $sFile) Then 
            _CW("_AlliChatMessage: Кнопка 'imgSendButton.bmp' не найдена.")
            FileDelete($sFile)
            Return False
        EndIf
        FileDelete($sFile)
        
        _CW("_AlliChatMessage [" & $sDeviceID & "]: Отчет успешно отправлен.")
        Return True
    Else
        _CW("_AlliChatMessage [" & $sDeviceID & "]: Ошибка - интерфейс чата не открылся.")
        FileDelete($sFile)
        Return False
    EndIf
EndFunc   ;==>_AlliChatMessage