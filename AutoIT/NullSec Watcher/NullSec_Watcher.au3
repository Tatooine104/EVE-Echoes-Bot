; ###############################################################################################################################
; #                                                                                                                             #
; # PROJECT........: NullSec Watcher                                                                                            #
; # VERSION........: 1.0.0                                                                                                      #
; # DESCRIPTION....: Автономный модуль мониторинга локального чата и оповещения в альянс.                                       #
; #                                                                                                                             #
; ###############################################################################################################################

#include <WinAPIFiles.au3>
; Подключаем твои библиотеки (укажи правильные пути, если они в подпапках)
#include "..\Resource\libUtility.au3"
#include "..\Resource\libImageSearch.au3"
#include "..\Resource\libGUI.au3"

; 1. ИНИЦИАЛИЗАЦИЯ ПУТЕЙ
Global $sResourceDir = _WinAPI_GetFullPathName(@ScriptDir & "\..\Resource\") & "\"
Global $Debug = True
Global $IsSave = True ; Глобальная переменная для статуса безопасности
Global $aClients = ["(Client W.01)"] ; Массив с твоим окном
Global $iCurrentClient = 0

; Проверка ресурсов
If Not FileExists($sResourceDir) Then
    MsgBox(16, "Ошибка", "Папка ресурсов не найдена: " & $sResourceDir)
    Exit
EndIf

; 2. ЗАПУСК ИНТЕРФЕЙСА
_GUI_Init("Watcher Mode", 10, 10) ; Создаем окно в углу экрана

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
; Name...........: _AlliChatMessage
; Description....: Открывает чат альянса и отправляет сообщение разведки.
; Syntax.........: _AlliChatMessage($sCurrentClient)
; Parameters ....: $sCurrentClient - Заголовок текущего окна клиента.
; Return values .: True         - Сообщение успешно отправлено.
;                  False        - Ошибка на одном из этапов (с записью в лог).
; Updated .......: 2026.04.26
; Version .......: 1.13
; Remarks .......: Оптимизировано использование массива координат. Добавлена кнопка меню перед информированием.
; ===============================================================================================================================
Func _AlliChatMessage($sCurrentClient)
    Local $aCPos = _WinGetClientPos($sCurrentClient)
    If @error Then 
        _Log("_AlliChatMessage [" & $sCurrentClient & "]: Ошибка - не удалось получить координаты клиента.")
        Return False
    EndIf

    _Log("_AlliChatMessage [" & $sCurrentClient & "]: Подготовка к отправке разведданных...")

    ; 1. Нажимаем на иконку чата (нижний левый угол)
    _HumanSleep(200, 400)
    MouseClick("left", $aCPos[0] + 25, $aCPos[1] + 625, 1, 1) 
    _HumanSleep(800, 1200)

    ; 2. Проверяем статус чата
    Local $aArea[4]
    $aArea[0] = $aCPos[0] + 0
    $aArea[1] = $aCPos[1] + 30
    $aArea[2] = $aCPos[0] + 104
    $aArea[3] = $aCPos[1] + 720

    Local $x, $y
    Local $bIsChatOpen = _MyImageSearch("imgAllianceChat.bmp", $sResourceDir, $aArea, $x, $y, 100)
    Local $bIsChatActive = _MyImageSearch("imgAllianceChatActive.bmp", $sResourceDir, $aArea, $x, $y, 100)

    If $bIsChatOpen Or $bIsChatActive Then
        
        ; 3. Активируем вкладку, если она не активна
        If $bIsChatOpen And Not $bIsChatActive Then
            If Not _FindAndClick("imgAllianceChat.bmp", $sResourceDir, $aArea) Then 
                _Log("_AlliChatMessage [" & $sCurrentClient & "]: Ошибка - не удалось активировать вкладку.")
                Return False
            EndIf
            _HumanSleep(600, 900)
        EndIf

        ; 4. Нажимаем на кнопку меню чата (перед информированием)
        $aArea[0] = $aCPos[0] + 320
        $aArea[1] = $aCPos[1] + 650
        $aArea[2] = $aCPos[0] + 390
        $aArea[3] = $aCPos[1] + 720
        If Not _FindAndClick("imgChatMenu.bmp", $sResourceDir, $aArea) Then
            _Log("_AlliChatMessage [" & $sCurrentClient & "]: Ошибка - кнопка 'imgChatMenu.bmp' не найдена.")
            Return False
        EndIf
        _HumanSleep(400, 600)

        ; 5. Нажимаем кнопку "Информировать"
        $aArea[0] = $aCPos[0] + 1050
        $aArea[1] = $aCPos[1] + 630
        $aArea[2] = $aCPos[0] + 1280
        $aArea[3] = $aCPos[1] + 720
        If Not _FindAndClick("imgInformButton.bmp", $sResourceDir, $aArea) Then 
            _Log("_AlliChatMessage [" & $sCurrentClient & "]: Ошибка - кнопка 'imgInformButton.bmp' не найдена.")
            Return False
        EndIf
        _HumanSleep(500, 800)

        ; 6. Выбираем раздел "Разведка"
        $aArea[0] = $aCPos[0] + 5
        $aArea[1] = $aCPos[1] + 295
        $aArea[2] = $aCPos[0] + 200
        $aArea[3] = $aCPos[1] + 595
        If Not _FindAndClick("imgIntelligence.bmp", $sResourceDir, $aArea) Then 
            _Log("_AlliChatMessage [" & $sCurrentClient & "]: Ошибка - раздел 'imgIntelligence.bmp' не найден.")
            Return False
        EndIf
        _HumanSleep(500, 800)

        ; 7. Выбираем сообщение угрозы
        $aArea[0] = $aCPos[0] + 190
        $aArea[1] = $aCPos[1] + 400
        $aArea[2] = $aCPos[0] + 490
        $aArea[3] = $aCPos[1] + 550
        If Not _FindAndClick("imgWarningMessage.bmp", $sResourceDir, $aArea) Then 
            _Log("_AlliChatMessage [" & $sCurrentClient & "]: Ошибка - сообщение 'imgWarningMessage.bmp' не найдено.")
            Return False
        EndIf
        _HumanSleep(500, 800)

        ; 8. Нажимаем кнопку "Отправить"
        $aArea[0] = $aCPos[0] + 360
        $aArea[1] = $aCPos[1] + 200
        $aArea[2] = $aCPos[0] + 560
        $aArea[3] = $aCPos[1] + 300
        If Not _FindAndClick("imgSendButton.bmp", $sResourceDir, $aArea) Then 
            _Log("_AlliChatMessage [" & $sCurrentClient & "]: Ошибка - кнопка 'imgSendButton.bmp' не найдена.")
            Return False
        EndIf
        
        _Log("_AlliChatMessage [" & $sCurrentClient & "]: Отчет успешно отправлен.")
        Return True
    Else
        _Log("_AlliChatMessage [" & $sCurrentClient & "]: Ошибка - интерфейс чата не открылся.")
        Return False
    EndIf
EndFunc   ;==>_AlliChatMessage

_Log("Watcher запущен. Ожидание нажатия СТАРТ...")

; 3. ОСНОВНОЙ ЦИКЛ
While 1
    _GUI_Update() ; Обновляем время работы в GUI

    If $bIsRunning Then ; Если нажата кнопка "Старт" в GUI
        Local $sTarget = $aClients[$iCurrentClient]

        ; ШАГ 1: Проверка безопасности
        ; Передаем имя окна и индекс (0), результат запишется в $aIsSave (если массив) или $IsSave
        If Not _IsSafe($sTarget, $iCurrentClient) Then
            _Log("!!! ВНИМАНИЕ: Обнаружен нейтрал/враг в " & $sTarget)
            
            ; ШАГ 2: Отправка сообщения в чат Альянса
            If _AlliChatMessage($sTarget) Then
                _Log("Доклад в Альянс-чат отправлен успешно.")
            Else
                _Log("ОШИБКА: Не удалось отправить доклад.")
            EndIf
            
            ; Делаем паузу, чтобы не спамить чат каждую секунду
            _HumanSleep(30000, 60000) 
        Else
            ; Если все чисто, просто ждем перед следующей проверкой
            _HumanSleep(2000, 5000)
        EndIf
    Else
        Sleep(200) ; Бот на паузе
    EndIf
WEnd
