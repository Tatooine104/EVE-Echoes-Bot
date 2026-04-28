#include "..\Resource\libImageSearch.au3"
#include "..\Resource\libUtility.au3"
#include "..\Resource\libGUI.au3"

; Создаем чистый путь без всяких ".."
Global $sImagesDir = _WinAPI_GetFullPathName(@ScriptDir & "\..\Images") & "\"

; Убираем возможные двойные слеши, которые ломают проверку
$sImagesDir = StringReplace($sImagesDir, "\\", "\")

; ПРОВЕРКА: Ищем конкретный файл, который точно должен быть в папке
If Not FileExists($sImagesDir & "imgAllianceChat.bmp") Then
    MsgBox(16, "Критическая ошибка", "Бот ищет ресурсы здесь:" & @CRLF & $sImagesDir & @CRLF & @CRLF & "Но папка пуста или файла imgAllianceChat.bmp там нет.")
    Exit
EndIf

; ===============================================================================================================================
; FUNCTION.......: _IsSafe
; ===============================================================================================================================
Func _IsSafe($sDeviceID, $iClientIdx)
    ; Подключаем глобальные пути
    Global $sResourceDir, $sImagesDir, $aIsSave

    ; 1. Получаем скриншот
    Local $sFile = _Get_Screenshot_By_ID($sDeviceID)
    If $sFile = "" Then Return False

    ; 2. Настройки
    Local $aArea[4] = [0, 330, 400, 720] ; Область локала
    ; Маркеры УГРОЗ
    Local $aMarkers[3] = ["imgLocalStatCriminal.bmp", "imgLocalStatMinus.bmp", "imgLocalStatNeitral.bmp"]
    Local $x, $y
    Local $iTolerance = 255
    Local $bDangerFound = False ; Флаг обнаружения опасности

    _CW("--> [" & $sDeviceID & "] Проверка локала на угрозы..." & @CRLF)

    ; 3. Проверка маркеров
    For $i = 0 To 2
        ; Передаем координаты элементами массива!
        If _MyImageSearch($aMarkers[$i], $sImagesDir, $aArea, $x, $y, $iTolerance, $sFile) Then
            _CW("!!! НАЙДЕНА УГРОЗА: " & $aMarkers[$i] & @CRLF)
            $bDangerFound = True
            ExitLoop ; Если нашли хоть одного врага, дальше можно не искать
        EndIf
    Next

    ; 4. Чистим скриншот
    FileDelete($sFile)

    ; 5. Результат: Безопасно, если НЕ нашли ни одного врага
    Local $bStatus = Not $bDangerFound

    If IsDeclared("aIsSave") Then
        $aIsSave[$iClientIdx] = $bStatus
    EndIf

    If Not $bStatus Then
        _CW("!!! ВНИМАНИЕ: Система НЕБЕЗОПАСНА !!!" & @CRLF)
    Else
        _CW("+++ В системе чисто." & @CRLF)
    EndIf

    Return $bStatus
EndFunc


; ===============================================================================================================================
; FUNCTION.......: _AlliChatMessage
; ===============================================================================================================================
Func _AlliChatMessage($sDeviceID, $iClientIdx)

    Global $aIsSave, $aIntelSent

    ; Предотвращение ошибок обращения к необъявленным массивам
    If Not IsDeclared("aIsSave") Or Not IsDeclared("aIntelSent") Then Return False

    ; 1. Если безопасно — сбрасываем флаг и выходим
    If $aIsSave[$iClientIdx] = True Then
        $aIntelSent[$iClientIdx] = False 
        Return True
    EndIf

    ; 2. Если уже отправляли для этой угрозы — выходим
    If $aIntelSent[$iClientIdx] = True Then Return True 

    _CW("_AlliChatMessage [" & $sDeviceID & "]: Отправка разведданных..." & @CRLF)

    Local $sFile, $x, $y

    ; Открываем чат
    _ADB_Click($sDeviceID, 25, 625) 
    _HumanSleep(800, 1200)

    $sFile = _Get_Screenshot_By_ID($sDeviceID)
    If $sFile = "" Then Return False

    Local $aTabArea[4] = [0, 30, 104, 720]
    Local $bIsChatOpen = _MyImageSearch("imgAllianceChat.bmp", $sImagesDir, $aTabArea, $x, $y, 100, $sFile)
    Local $bIsChatActive = _MyImageSearch("imgAllianceChatActive.bmp", $sImagesDir, $aTabArea, $x, $y, 100, $sFile)

    If $bIsChatOpen Or $bIsChatActive Then
        ; Активация вкладки
        If $bIsChatOpen And Not $bIsChatActive Then
            _FindAndClick("imgAllianceChat.bmp", $sImagesDir, $aTabArea, $sDeviceID, $sFile)
            FileDelete($sFile)
            _HumanSleep(800, 1200)
            $sFile = _Get_Screenshot_By_ID($sDeviceID)
        EndIf

        ; Плюсик меню
        Local $aMenuArea[4] = [320, 650, 390, 720]
        If Not _FindAndClick("imgChatMenu.bmp", $sImagesDir, $aMenuArea, $sDeviceID, $sFile) Then Return _ErrCleanup($sFile)
        FileDelete($sFile)
        _HumanSleep(500, 800)

        ; Информировать
        $sFile = _Get_Screenshot_By_ID($sDeviceID)
        Local $aInfArea[4] = [1050, 630, 1280, 720]
        If Not _FindAndClick("imgInformButton.bmp", $sImagesDir, $aInfArea, $sDeviceID, $sFile) Then Return _ErrCleanup($sFile)
        FileDelete($sFile)
        _HumanSleep(600, 1000)

        ; Разведка
        $sFile = _Get_Screenshot_By_ID($sDeviceID)
        Local $aIntArea[4] = [5, 295, 200, 595]
        If Not _FindAndClick("imgIntelligence.bmp", $sImagesDir, $aIntArea, $sDeviceID, $sFile) Then Return _ErrCleanup($sFile)
        FileDelete($sFile)
        _HumanSleep(600, 1000)

        ; Угроза
        $sFile = _Get_Screenshot_By_ID($sDeviceID)
        Local $aWarnArea[4] = [190, 400, 490, 550]
        If Not _FindAndClick("imgWarningMessage.bmp", $sImagesDir, $aWarnArea, $sDeviceID, $sFile) Then Return _ErrCleanup($sFile)
        FileDelete($sFile)
        _HumanSleep(600, 1000)

        ; Отправить
        $sFile = _Get_Screenshot_By_ID($sDeviceID)
        Local $aSendArea[4] = [360, 200, 560, 300]
        If Not _FindAndClick("imgSendButton.bmp", $sImagesDir, $aSendArea, $sDeviceID, $sFile) Then Return _ErrCleanup($sFile)
        
        FileDelete($sFile)
        $aIntelSent[$iClientIdx] = True ; Флаг успеха
        _CW("+++ Отчет отправлен успешно!" & @CRLF)
        Return True
    EndIf

    FileDelete($sFile)
    Return False
EndFunc

; Вспомогательная функция для очистки при ошибке
Func _ErrCleanup($sFile)
    If FileExists($sFile) Then FileDelete($sFile)
    Return False
EndFunc
